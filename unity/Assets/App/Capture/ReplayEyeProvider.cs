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
    /// Replays a gaze CSV (format in docs/ARCHITECTURE.md) on the app clock. Labelled data_source = replay with the
    /// given origin. STUB: minimal parser for feat/replay-metrics to extend; real recordings stay out of Git.
    /// </summary>
    public sealed class ReplayEyeProvider : IEyeTrackingProvider
    {
        public const string Header =
            "frame_time_s,sensor_time_s,valid,left_valid,right_valid,left_x,left_y,left_z,right_x,right_y,right_z,head_px,head_py,head_pz,head_qx,head_qy,head_qz,head_qw,confidence";

        readonly List<GazeSample> _rows;
        double _start;
        int _cursor;

        public ReplayEyeProvider(IEnumerable<GazeSample> rows, ReplayOrigin origin, Capabilities capabilities = null)
        {
            _rows = new List<GazeSample>(rows);
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
                    HeadPosition = s.Head == null ? Vector3.Zero : V(s.Head.PositionMetres),
                    HeadRotation = s.Head == null ? Quaternion.Identity
                        : new Quaternion((float)s.Head.RotationX, (float)s.Head.RotationY, (float)s.Head.RotationZ, (float)s.Head.RotationW),
                    Confidence = (float?)(s.Left?.Confidence ?? s.Right?.Confidence),
                });
            }
            var gaze = session.Info.Capabilities.Gaze.Available;
            return new ReplayEyeProvider(rows, actualOrigin, new Capabilities
            {
                GazePose = gaze,
                NativeEyeGaze = false,
                ConfidenceInformative = session.Info.Capabilities.ConfidenceInformative,
            });
        }

        static Vector3 V(VRFatigue.Signals.Direction3 d) => new Vector3((float)d.X, (float)d.Y, (float)d.Z);

        public string Name => "replay file";
        public DataSource DataSource => DataSource.Replay;
        public ReplayOrigin? ReplayOrigin { get; }
        public Capabilities Capabilities { get; }
        public IReadOnlyList<UnavailableSignal> Unavailable { get; } = EyeSignals.NotExposed(Signal.Openness, Signal.Pupil, Signal.Blink);
        public bool IsRunning { get; private set; }
        public int RowCount => _rows.Count;

        public void Start(double now)
        {
            _start = now;
            _cursor = 0;
            IsRunning = true;
        }

        public void Stop() => IsRunning = false;

        public int ReadSamples(List<GazeSample> buffer, double now)
        {
            if (!IsRunning || _rows.Count == 0) return 0;
            var t0 = _rows[0].FrameTime;
            var added = 0;
            while (_cursor < _rows.Count && _rows[_cursor].FrameTime - t0 <= now - _start)
            {
                var s = _rows[_cursor++];
                s.FrameTime = _start + (s.FrameTime - t0);
                buffer.Add(s);
                added++;
            }
            if (_cursor >= _rows.Count) IsRunning = false;
            return added;
        }

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
