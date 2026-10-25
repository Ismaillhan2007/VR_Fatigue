using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using App.Core.Capture;
using App.Core.Contracts;

namespace App.Capture
{
    /// <summary>
    /// Replays an eye-session.v1 JSON file (with its probe trials) or a gaze CSV (format in docs/ARCHITECTURE.md) on the
    /// app clock. Labelled data_source = replay with the given origin; a synthetic file stays synthetic.
    /// <see cref="Speed"/> > 1 plays faster on the frame clock; sensor timestamps (and therefore metrics) are unchanged.
    /// The clock can be held between phases so nothing is skipped while the participant answers.
    /// </summary>
    public sealed class ReplayEyeProvider : IEyeTrackingProvider, IProbeSource, IHoldableProvider
    {
        public const string Header =
            "frame_time_s,sensor_time_s,valid,left_valid,right_valid,left_x,left_y,left_z,right_x,right_y,right_z,head_px,head_py,head_pz,head_qx,head_qy,head_qz,head_qw,confidence";

        readonly List<GazeSample> _rows;
        readonly List<ProbeEvent> _probes;
        readonly double _timelineStart, _sensorStart;
        double? _phaseEnd;
        double _start, _replayed, _lastNow;
        bool _held;
        int _cursor, _probeCursor;

        /// <summary>Replay seconds per frame-clock second.</summary>
        public double Speed { get; set; } = 1;

        public ReplayEyeProvider(IEnumerable<GazeSample> rows, ReplayOrigin origin, Capabilities capabilities = null,
            IEnumerable<ProbeEvent> probes = null, double? timelineStart = null)
        {
            _rows = new List<GazeSample>(rows);
            _probes = new List<ProbeEvent>(probes ?? Array.Empty<ProbeEvent>());
            _timelineStart = timelineStart ?? (_rows.Count == 0 ? 0 : _rows[0].FrameTime);
            _sensorStart = timelineStart ?? (_rows.Count == 0 ? 0 : _rows[0].SensorTimestamp);
            ReplayOrigin = origin;
            Capabilities = capabilities ?? new Capabilities { GazePose = true, ConfidenceInformative = false };
            if (origin == Core.Contracts.ReplayOrigin.Synthetic) Capabilities.NativeEyeGaze = false;
        }

        public static ReplayEyeProvider FromCsv(string csvText, ReplayOrigin origin) => new ReplayEyeProvider(Parse(csvText), origin);

        /// <summary>
        /// Loads a replay file: <c>.json</c> = eye-session.v1 (fixtures/replay, fixtures/metrics; parsed by VRFatigue.Signals),
        /// anything else = the CSV format above.
        /// </summary>
        public static ReplayEyeProvider FromFile(string path, ReplayOrigin origin) =>
            path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? FromEyeSessionJson(File.ReadAllText(path), origin)
                : FromCsv(File.ReadAllText(path), origin);

        /// <summary>
        /// eye-session.v1 document. A file that declares synthetic data stays labelled synthetic, whatever the caller asked.
        /// </summary>
        public static ReplayEyeProvider FromEyeSessionJson(string json, ReplayOrigin origin)
        {
            var session = VRFatigue.Signals.SignalJson.ParseSession(json);
            var replayInfo = session.Info.AsReplay();
            var actualOrigin = replayInfo.ReplayOrigin == "synthetic" ? Core.Contracts.ReplayOrigin.Synthetic : origin;
            var rows = new List<GazeSample>();
            foreach (var s in session.Samples)
            {
                var t = s.TimestampUs / 1e6;
                bool lv = s.Left != null && s.Left.IsUsable, rv = s.Right != null && s.Right.IsUsable;
                var left = lv ? V(s.Left.Direction.Value) : Vector3.UnitZ;
                var right = rv ? V(s.Right.Direction.Value) : Vector3.UnitZ;
                rows.Add(new GazeSample
                {
                    FrameTime = t,
                    SensorTimestamp = t,
                    Valid = lv || rv,
                    LeftValid = lv,
                    RightValid = rv,
                    LeftDirection = left,
                    RightDirection = right,
                    CyclopeanDirection = lv && rv ? Vector3.Normalize(left + right) : lv ? left : right,
                    HeadTracked = s.Head != null,
                    HeadPosition = s.Head == null ? Vector3.Zero : V(s.Head.PositionMetres),
                    HeadRotation = s.Head == null ? Quaternion.Identity
                        : new Quaternion((float)s.Head.RotationX, (float)s.Head.RotationY, (float)s.Head.RotationZ, (float)s.Head.RotationW),
                    Confidence = (float?)(s.Left?.Confidence ?? s.Right?.Confidence),
                });
            }
            var probes = new List<ProbeEvent>();
            foreach (var g in System.Linq.Enumerable.GroupBy(session.ProbeEvents, e => e.ProbeId))
            {
                double? onset = null, hold = null, end = null;
                var target = Vector3.UnitZ;
                foreach (var e in g)
                {
                    if (e.Kind == "target_onset") { onset = e.TimestampUs / 1e6; target = V(e.TargetDirection); }
                    else if (e.Kind == "hold_start") hold = e.TimestampUs / 1e6;
                    else if (e.Kind == "target_end") end = e.TimestampUs / 1e6;
                }
                if (onset.HasValue && hold.HasValue && end.HasValue)
                    probes.Add(new ProbeEvent { TrialId = g.Key, OnsetTime = onset.Value, HoldStartTime = hold.Value, EndTime = end.Value, TargetDirection = target });
            }
            probes.Sort((a, b) => a.OnsetTime.CompareTo(b.OnsetTime));
            var gaze = session.Info.Capabilities.Gaze.Available;
            return new ReplayEyeProvider(rows, actualOrigin, new Capabilities
            {
                GazePose = gaze,
                NativeEyeGaze = false,
                ConfidenceInformative = session.Info.Capabilities.ConfidenceInformative,
            }, probes, session.Info.StartTimestampUs / 1e6);
        }

        static Vector3 V(VRFatigue.Signals.Direction3 d) => new Vector3((float)d.X, (float)d.Y, (float)d.Z);

        public string Name => "replay file";
        public DataSource DataSource => DataSource.Replay;
        public ReplayOrigin? ReplayOrigin { get; }
        public Capabilities Capabilities { get; }
        public IReadOnlyList<UnavailableSignal> Unavailable { get; } = EyeSignals.NotExposed(Signal.Openness, Signal.Pupil, Signal.Blink);
        public bool IsRunning { get; private set; }
        public int RowCount => _rows.Count;

        public int ProbeCount => _probes.Count;

        public void Start(double now)
        {
            _start = _lastNow = now;
            _replayed = 0;
            _held = false;
            _phaseEnd = null;
            _cursor = _probeCursor = 0;
            IsRunning = true;
        }

        public void Stop() => IsRunning = false;

        /// <summary>Limit a capture phase before polling, so a slow render frame cannot consume the next phase.</summary>
        public void BeginPhase(double seconds, double now)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            Advance(now);
            _phaseEnd = _replayed + seconds;
        }

        /// <summary>Replay acquisition clock; keeps advancing through missing samples, holds between phases.</summary>
        public double SensorTime(double now) { Advance(now); return _sensorStart + _replayed; }

        public void SetHold(bool hold, double now)
        {
            Advance(now);
            _held = hold;
        }

        public int ReadSamples(List<GazeSample> buffer, double now)
        {
            if (!IsRunning || _rows.Count == 0) return 0;
            Advance(now);
            var t0 = _timelineStart;
            var added = 0;
            while (_cursor < _rows.Count && _rows[_cursor].FrameTime - t0 <= _replayed)
            {
                var s = _rows[_cursor++];
                s.FrameTime = ToFrameClock(s.FrameTime - t0);
                buffer.Add(s);
                added++;
            }
            if (_cursor >= _rows.Count) IsRunning = false;
            return added;
        }

        public int ReadProbes(List<ProbeEvent> buffer, double now)
        {
            if (_rows.Count == 0) return 0;
            Advance(now);
            var t0 = _timelineStart;
            var added = 0;
            while (_probeCursor < _probes.Count && _probes[_probeCursor].OnsetTime - t0 <= _replayed &&
                (!_phaseEnd.HasValue || _probes[_probeCursor].OnsetTime - t0 < _phaseEnd.Value))
            {
                var p = _probes[_probeCursor++];
                p.OnsetTime = ToFrameClock(p.OnsetTime - t0);
                p.HoldStartTime = ToFrameClock(p.HoldStartTime - t0);
                p.EndTime = ToFrameClock(p.EndTime - t0);
                buffer.Add(p);
                added++;
            }
            return added;
        }

        /// <summary>Replay seconds played so far (excludes held time); the app uses it as the phase clock.</summary>
        public double ReplayedSeconds(double now)
        {
            Advance(now);
            return _replayed;
        }

        void Advance(double now)
        {
            if (!_held && now > _lastNow) _replayed += (now - _lastNow) * Math.Max(0.01, Speed);
            if (_phaseEnd.HasValue) _replayed = Math.Min(_phaseEnd.Value, _replayed);
            _lastNow = now;
        }

        /// <summary>Approximate frame time of a replay offset (exact while not held; held spans shift later rows).</summary>
        double ToFrameClock(double replayOffset) => _lastNow - (_replayed - replayOffset) / Math.Max(0.01, Speed);

        /// <summary>Parses rows with the <see cref="Header"/> columns. Empty confidence = unavailable.</summary>
        public static List<GazeSample> Parse(string csvText)
        {
            var rows = new List<GazeSample>();
            var lines = (csvText ?? "").Split('\n');
            if (lines.Length == 0 || lines[0].Trim() != Header) throw new FormatException("unexpected replay CSV header");
            for (var i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0) continue;
                var f = line.Split(',');
                if (f.Length != 19) throw new FormatException($"line {i + 1}: expected 19 columns, got {f.Length}");
                var left = V(f, 5);
                var right = V(f, 8);
                bool lv = B(f[3]), rv = B(f[4]);
                var cyc = lv && rv ? Vector3.Normalize(left + right) : lv ? left : right;
                rows.Add(new GazeSample
                {
                    FrameTime = D(f[0]),
                    SensorTimestamp = D(f[1]),
                    Valid = B(f[2]),
                    LeftValid = lv,
                    RightValid = rv,
                    LeftDirection = left,
                    RightDirection = right,
                    CyclopeanDirection = cyc,
                    HeadTracked = true,
                    HeadPosition = V(f, 11),
                    HeadRotation = new Quaternion(F(f[14]), F(f[15]), F(f[16]), F(f[17])),
                    Confidence = string.IsNullOrEmpty(f[18]) ? (float?)null : F(f[18]),
                });
            }
            return rows;
        }

        static double D(string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        static bool B(string s) => s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase);
        static Vector3 V(string[] f, int i) => new Vector3(F(f[i]), F(f[i + 1]), F(f[i + 2]));
    }
}
