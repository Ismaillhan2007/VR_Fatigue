using System;
using System.Collections.Generic;
using System.Numerics;
using App.Core.Capture;
using App.Core.Contracts;
using App.Core.Metrics;
using Sig = VRFatigue.Signals;
using Newtonsoft.Json.Linq;

namespace App.Capture
{
    /// <summary>App adapter over the existing validated native stream. SDK composition is supplied by App.Quest.</summary>
    public sealed class HardwareEyeProvider : IEyeTrackingProvider, IDisposable
    {
        readonly Sig.IHardwareEyeSource _source;
        readonly Sig.HardwareEyeProvider _capture;
        readonly Func<bool> _requestPermission, _beginRendering;
        readonly Action _endRendering;
        readonly Action<bool> _setConsent;
        readonly Action _release;
        bool _consent, _calibrated, _disposed, _lockHeld;
        double _phaseStart, _phaseEnd;
        bool _phaseActive;
        int _receivedAtPhaseStart, _duplicatesAtPhaseStart;
        public string Name => DataSource == DataSource.Live ? "Quest Pro native eyes" : "Synthetic native-adapter test";
        public DataSource DataSource => _source.DataSource == "live" ? DataSource.Live : DataSource.Synthetic;
        public ReplayOrigin? ReplayOrigin => null;
        public Capabilities Capabilities { get; } = new Capabilities();
        public IReadOnlyList<UnavailableSignal> Unavailable { get; } = new[]
        {
            new UnavailableSignal { Signal = Signal.Openness, Reason = UnavailableReason.NotExposedByRuntime },
            new UnavailableSignal { Signal = Signal.Pupil, Reason = UnavailableReason.NotExposedByRuntime },
            new UnavailableSignal { Signal = Signal.Blink, Reason = UnavailableReason.NotExposedByRuntime },
        };
        public bool IsRunning => !_disposed && _capture.State == "running";
        public bool Completed { get; private set; }
        public string Status => _capture.StatusReason;
        public bool CalibrationConfirmed => _calibrated;
        public bool ConsentGranted => _consent;
        public string ReadinessDescription
        {
            get
            {
                var ready = _source.Readiness;
                return $"Native support: {ready.Supported}; eye permission: {ready.PermissionGranted}; focus: {ready.Focused}. "
                    + $"Calibration confirmed by operator: {_calibrated}. ";
            }
        }
        public string AggregateDiagnostics => _disposed ? null : _capture.DiagnosticsJson();
        public string DiagnosticSummary
        {
            get
            {
                if (_disposed) return null;
                var report = JObject.Parse(AggregateDiagnostics); var counts = report["counts"];
                string Value(JToken value) => value == null || value.Type == JTokenType.Null ? "unavailable" : value.ToString();
                return "Local aggregate diagnostic (native timestamp semantics still require device verification):\n"
                    + "Duration ms: " + Value(report["duration_ms"]) + "; unique samples: " + Value(counts["accepted_samples"])
                    + "; median dt ms: " + Value(report["median_dt_ms"]) + "; repeated fraction: " + Value(report["repeated_fraction"])
                    + "\nInvalid gaze: " + Value(report["quality_report"]?["quality"]?["counts"]?["invalid_gaze"])
                    + "; unaligned heads: " + Value(counts["unaligned_head_frames"]) + "; longest gap ms: " + Value(report["quality_report"]?["quality"]?["longest_gap_ms"])
                    + "\nTimestamp rejects (bad / out of order / future / pre-start): " + Value(counts["invalid_timestamps"]) + " / "
                    + Value(counts["out_of_order_timestamps"]) + " / " + Value(counts["future_frames"]) + " / " + Value(counts["pre_start_frames"])
                    + "\nIntervals histogram: " + report["dt_histogram"]?.ToString(Newtonsoft.Json.Formatting.None)
                    + "\nPupil, openness and blinks: not exposed. Calibration: operator confirmation only. No clinical accuracy claim.";
            }
        }

        public HardwareEyeProvider(Sig.IHardwareEyeSource source, Func<bool> requestPermission,
            Func<bool> beginRendering, Action endRendering, Action<bool> setConsent, Action release = null)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _capture = new Sig.HardwareEyeProvider(source);
            _requestPermission = requestPermission; _beginRendering = beginRendering;
            _endRendering = endRendering; _setConsent = setConsent; _release = release;
        }

        public bool RequestPermissionAfterConsent()
        {
            ThrowIfDisposed(); _consent = true; _setConsent?.Invoke(true);
            return _requestPermission?.Invoke() == true; // OS response remains asynchronous.
        }
        public void ConfirmCalibration(bool confirmed) { ThrowIfDisposed(); _calibrated = confirmed; }
        public void Start(double now)
        {
            ThrowIfDisposed(); Completed = false;
            if (!_capture.Start(_consent, _calibrated)) throw new InvalidOperationException(Status);
            try
            {
                if (_beginRendering?.Invoke() != true) throw new InvalidOperationException("rendering_not_ready");
                _lockHeld = true;
            }
            catch
            {
                _capture.Abort("rendering_not_ready"); _endRendering?.Invoke();
                throw new InvalidOperationException(Status);
            }
            Capabilities.GazePose = true; Capabilities.NativeEyeGaze = DataSource == DataSource.Live;
        }

        public double AcquisitionTime()
        {
            ThrowIfDisposed();
            if (!_capture.TryGetCaptureTime(out var us)) throw new InvalidOperationException(Status);
            return us / 1e6;
        }

        public void BeginPhase(PhaseRecording phase, double now, double seconds)
        {
            _phaseStart = AcquisitionTime(); _phaseEnd = _phaseStart + seconds; _phaseActive = true;
            _receivedAtPhaseStart = _capture.FramesReceived; _duplicatesAtPhaseStart = _capture.DuplicateFrames;
            phase.StartTime = now; phase.SensorStartTime = _phaseStart; phase.SensorClockRate = 1;
        }
        public void EndPhase(PhaseRecording phase)
        {
            phase.SensorEndTime = Math.Min(_phaseEnd, AcquisitionTime());
            phase.NativeFramesReceived = _capture.FramesReceived - _receivedAtPhaseStart;
            phase.NativeDuplicateFrames = _capture.DuplicateFrames - _duplicatesAtPhaseStart;
            _phaseActive = false;
        }
        public int ReadSamples(List<GazeSample> buffer, double now)
        {
            ThrowIfDisposed();
            var batch = _capture.ReadAvailable(_capture.ElapsedUs);
            if (!IsRunning && !Completed) { ReleaseRendering(); return 0; }
            var count = 0;
            if (!_phaseActive) return count; // Poll between phases for faults, retaining no app-owned rows.
            foreach (var sample in batch.Samples)
            {
                var stamp = sample.TimestampUs / 1e6;
                if (stamp < _phaseStart || stamp > _phaseEnd) continue;
                var left = Direction(sample.Left); var right = Direction(sample.Right);
                var gaze = sample.Left.IsUsable && sample.Right.IsUsable ? left + right : sample.Left.IsUsable ? left : right;
                buffer.Add(new GazeSample
                {
                    FrameTime = now, SensorTimestamp = stamp, Valid = sample.HasUsableGaze,
                    LeftValid = sample.Left.IsUsable, RightValid = sample.Right.IsUsable,
                    LeftDirection = left, RightDirection = right,
                    CyclopeanDirection = gaze.LengthSquared() > 0 ? Vector3.Normalize(gaze) : Vector3.Zero,
                    HeadTracked = sample.Head != null,
                    HeadPosition = sample.Head == null ? Vector3.Zero : new Vector3((float)sample.Head.PositionMetres.X, (float)sample.Head.PositionMetres.Y, (float)sample.Head.PositionMetres.Z),
                    HeadRotation = sample.Head == null ? Quaternion.Identity : new Quaternion((float)sample.Head.RotationX,
                        (float)sample.Head.RotationY, (float)sample.Head.RotationZ, (float)sample.Head.RotationW),
                    Confidence = sample.Left.Confidence.HasValue ? (float?)sample.Left.Confidence.Value : sample.Right.Confidence.HasValue ? (float?)sample.Right.Confidence.Value : null,
                });
                count++;
            }
            return count;
        }

        public double StampProbe(string id, string kind, Vector3 direction)
        {
            if (!_capture.RecordProbe(id, kind, new Sig.Direction3(direction.X, direction.Y, direction.Z)))
                throw new InvalidOperationException(Status);
            return _capture.ElapsedUs / 1e6;
        }

        public void CompleteAssessment()
        {
            ThrowIfDisposed(); _capture.Finish();
            Completed = _capture.State == "stopped" && _capture.StatusReason == "completed" && _capture.SourceErrorType == null;
            ReleaseRendering();
            if (!Completed) throw new InvalidOperationException(Status);
        }
        public void Abort(string reason) { if (!_disposed) { _capture.Abort(reason); ReleaseRendering(); } }
        public void Stop() => Dispose(); // Exit/withdrawal must destroy the native buffers, not just stop polling.
        public void Dispose()
        {
            if (_disposed) return;
            // Destroying the Unity owner can synchronously invoke OnDisable/OnDestroy. Mark disposed first.
            _disposed = true; _consent = _calibrated = _phaseActive = false;
            ReleaseRendering(); _setConsent?.Invoke(false); _capture.Dispose(); _release?.Invoke();
        }
        void ReleaseRendering() { if (_lockHeld) { _lockHeld = false; _endRendering?.Invoke(); } }
        void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(HardwareEyeProvider)); }
        static Vector3 Direction(Sig.EyeReading eye) => eye?.IsUsable == true
            ? new Vector3((float)eye.Direction.Value.X, (float)eye.Direction.Value.Y, (float)eye.Direction.Value.Z) : Vector3.Zero;
    }

    public enum EyeSourceMode { Synthetic, Replay, Hardware }

    public static class EyeProviderFactory
    {
        /// <summary>Registered by the Android composition; no Unity/SDK reference enters the capture assembly.</summary>
        public static Func<HardwareEyeProvider> HardwareFactory { get; set; }
        public static HardwareEyeProvider CreateHardware() => HardwareFactory?.Invoke()
            ?? throw new InvalidOperationException("quest_standalone_required");

        public static IEyeTrackingProvider CreateAndStart(EyeSourceMode mode, double now, int seed, string replayPath,
            ReplayOrigin replayOrigin, out string note, double replaySpeed = 1)
        {
            note = null;
            if (mode == EyeSourceMode.Hardware)
                throw new InvalidOperationException("Native eyes require explicit permission and calibration preparation");
            try
            {
                IEyeTrackingProvider provider;
                if (mode == EyeSourceMode.Replay)
                {
                    provider = ReplayEyeProvider.FromFile(replayPath, replayOrigin);
                    ((ReplayEyeProvider)provider).Speed = replaySpeed;
                }
                else provider = new SyntheticEyeProvider(seed);
                provider.Start(now); return provider;
            }
            catch (Exception error)
            {
                note = $"{mode} unavailable ({error.GetType().Name}); using synthetic data";
                var provider = new SyntheticEyeProvider(seed); provider.Start(now); return provider;
            }
        }
    }
}
