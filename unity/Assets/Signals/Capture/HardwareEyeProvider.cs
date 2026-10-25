using System;
using System.Collections.Generic;

namespace VRFatigue.Signals
{
    public sealed class HardwareEyeProvider : IEyeTrackingProvider, IDisposable
    {
        private readonly IHardwareEyeSource source;
        private readonly HardwareCaptureOptions options;
        private readonly string dataSource, timestampSource, sdkVersion;
        private readonly List<EyeSample> samples = new List<EyeSample>();
        private readonly List<ProbeEvent> probes = new List<ProbeEvent>();
        private readonly List<ProbeEvent> pendingProbes = new List<ProbeEvent>();
        private double origin, lastClock;
        private double? lastFrameTime;
        private long lastCallerElapsed = -1, elapsedUs;
        private bool consent, calibrated;
        private HardwareCounters counters = new HardwareCounters();
        public SessionInfo Info { get; private set; }
        public string State { get; private set; }
        public string StatusReason { get; private set; }
        public string SourceErrorType { get; private set; }
        public long ElapsedUs { get { return elapsedUs; } }
        public bool IsComplete { get { return State == "stopped" || State == "unavailable" || State == "faulted" || State == "disposed"; } }

        public HardwareEyeProvider(IHardwareEyeSource source, HardwareCaptureOptions options = null)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            if (source.DataSource != "live" && source.DataSource != "synthetic") throw new ArgumentException("Hardware source must explicitly declare live or synthetic provenance.");
            if (string.IsNullOrWhiteSpace(source.TimestampSource) || string.IsNullOrWhiteSpace(source.SdkPackageVersion)) throw new ArgumentException("Source metadata is required.");
            this.options = options ?? new HardwareCaptureOptions();
            dataSource = source.DataSource; timestampSource = source.TimestampSource; sdkVersion = source.SdkPackageVersion;
            ClearSession();
        }

        public bool Start(bool participantConsent, bool calibrationConfirmed)
        {
            ThrowIfDisposed();
            if (State == "running") throw new InvalidOperationException("Capture is already running.");
            ClearSession(); consent = participantConsent; calibrated = calibrationConfirmed;
            if (!consent) return Unavailable("consent_required");
            if (!calibrated) return Unavailable("calibration_confirmation_required");
            try
            {
                var ready = source.Readiness;
                if (!ready.Supported) return Unavailable("hardware_unsupported");
                if (!ready.PermissionGranted) return Unavailable("permission_required");
                if (!ready.Focused) return Unavailable("focus_required");
                if (!source.StartTracking() || !source.Readiness.TrackingEnabled) { StopSource(); return Unavailable("tracking_start_failed"); }
                double now;
                if (!source.TryGetClock(out now) || !Direction3.Finite(now) || now < 0) { StopSource(); return Unavailable("runtime_clock_unavailable"); }
                origin = lastClock = now;
                State = "running"; StatusReason = "waiting_for_frame";
                Info = Metadata(Info.SessionId, options.MaximumDurationUs, true, null);
                return true;
            }
            catch (Exception error) { SourceFault(error); return false; }
        }

        public EyeTrackingBatch ReadAvailable(long callerElapsedUs)
        {
            ThrowIfDisposed();
            if (callerElapsedUs < 0 || callerElapsedUs < lastCallerElapsed) throw new ArgumentOutOfRangeException(nameof(callerElapsedUs));
            lastCallerElapsed = callerElapsedUs;
            var batch = new List<EyeSample>();
            if (State == "running")
            {
                try
                {
                    var ready = source.Readiness;
                    if (!ready.Supported) Abort("hardware_unavailable");
                    else if (!ready.PermissionGranted) Abort("permission_revoked");
                    else if (!ready.Focused) Abort("focus_lost");
                    else if (!ready.TrackingEnabled) Abort("tracking_disabled");
                    else if (UpdateClock())
                    {
                        if (elapsedUs >= options.MaximumDurationUs) StopAtCurrentTime("duration_limit");
                        else
                        {
                            counters.FramePolls++;
                            HardwareEyeFrame frame;
                            bool received;
                            try { received = source.TryReadFrame(out frame); }
                            catch { counters.ReadFailures++; throw; }
                            if (!received || frame == null) { counters.ReadFailures++; StatusReason = "frame_unavailable"; }
                            else
                            {
                                counters.FramesReceived++;
                                // The frame may arrive after the first clock query. Never call that a future sample.
                                if (UpdateClock())
                                {
                                    Accept(frame, batch);
                                    if (State == "running" && elapsedUs >= options.MaximumDurationUs) StopAtCurrentTime("duration_limit");
                                }
                            }
                        }
                    }
                }
                catch (Exception error) { SourceFault(error); }
            }
            var events = new List<ProbeEvent>(pendingProbes); pendingProbes.Clear();
            return new EyeTrackingBatch(batch, events);
        }

        public bool RecordProbe(string id, string kind, Direction3 headLocalTargetDirection)
        {
            ThrowIfDisposed();
            // Validate caller input separately from source failures.
            new ProbeEvent(0, id, kind, headLocalTargetDirection);
            if (State != "running") return false;
            try
            {
                if (!UpdateClock()) return false;
                if (elapsedUs >= options.MaximumDurationUs) { StopAtCurrentTime("duration_limit"); return false; }
                if (probes.Count >= options.MaximumProbeEvents) { StopAtCurrentTime("probe_buffer_limit"); return false; }
                var item = new ProbeEvent(elapsedUs, id, kind, headLocalTargetDirection);
                probes.Add(item); pendingProbes.Add(item); return true;
            }
            catch (Exception error) { SourceFault(error); return false; }
        }

        public EyeSession Finish()
        {
            ThrowIfDisposed();
            if (State == "idle") throw new InvalidOperationException("Start capture before finishing it.");
            if (State == "running")
            {
                try { if (UpdateClock()) StopAtCurrentTime("completed"); }
                catch (Exception error) { SourceFault(error); }
            }
            return new EyeSession(Metadata(Info.SessionId, elapsedUs, Info.Capabilities.Gaze.Available, Info.Capabilities.Gaze.Reason), samples, probes);
        }

        public void Abort(string reason)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Abort reason is required.");
            if (State == "running")
            {
                try { UpdateClock(); } catch (Exception error) { SourceErrorType = error.GetType().Name; }
                if (State == "running") { State = "faulted"; StatusReason = reason; SetFinalBounds(); StopSource(); }
            }
        }

        public string DiagnosticsJson()
        {
            var session = new EyeSession(Metadata(Info.SessionId, elapsedUs, Info.Capabilities.Gaze.Available, Info.Capabilities.Gaze.Reason), samples, probes);
            return HardwareDiagnostics.Serialize(session, options, counters, State, StatusReason, SourceErrorType,
                timestampSource, sdkVersion, consent, calibrated);
        }

        public void Reset()
        {
            ThrowIfDisposed(); StopSource(); ClearSession();
        }
        public void WithdrawConsent()
        {
            ThrowIfDisposed(); StopSource(); ClearSession();
            Unavailable("consent_withdrawn");
        }
        public void Dispose()
        {
            if (State == "disposed") return;
            StopSource(); ClearSession(); State = "disposed"; StatusReason = "disposed";
        }

        private bool UpdateClock()
        {
            double now;
            if (!source.TryGetClock(out now) || !Direction3.Finite(now) || now < 0)
            { counters.ClockFailures++; State = "faulted"; StatusReason = "runtime_clock_unavailable"; SetFinalBounds(); StopSource(); return false; }
            if (now < lastClock)
            { counters.ClockResets++; State = "faulted"; StatusReason = "runtime_clock_reset"; SetFinalBounds(); StopSource(); return false; }
            lastClock = now;
            elapsedUs = RelativeTime(now);
            return true;
        }

        private void Accept(HardwareEyeFrame frame, List<EyeSample> batch)
        {
            double stamp = frame.TimestampSeconds;
            if (!Direction3.Finite(stamp) || stamp < 0) { counters.BadTimestamps++; StatusReason = "invalid_timestamp"; return; }
            if (stamp < origin) { counters.PreStartFrames++; StatusReason = "pre_start_frame"; return; }
            if (stamp > lastClock) { counters.FutureFrames++; StatusReason = "future_timestamp"; return; }
            if ((stamp - origin) * 1000000 > options.MaximumDurationUs)
            { counters.PostEndFrames++; StatusReason = "post_end_frame"; return; }
            if (lastFrameTime.HasValue && stamp == lastFrameTime.Value) { counters.Duplicates++; StatusReason = "duplicate_timestamp"; return; }
            if (lastFrameTime.HasValue && stamp < lastFrameTime.Value) { counters.OutOfOrder++; StatusReason = "out_of_order_timestamp"; return; }
            long timeUs = (long)Math.Round((stamp - origin) * 1000000, MidpointRounding.AwayFromZero);
            if (samples.Count > 0 && timeUs <= samples[samples.Count - 1].TimestampUs)
            { counters.QuantizedCollisions++; lastFrameTime = stamp; StatusReason = "quantized_timestamp_collision"; return; }
            if (samples.Count >= options.MaximumSamples) { StopAtCurrentTime("sample_buffer_limit"); return; }
            double[] quaternion;
            bool aligned = frame.HeadTimestampSeconds.HasValue && Direction3.Finite(frame.HeadTimestampSeconds.Value) &&
                Math.Abs(frame.HeadTimestampSeconds.Value - stamp) * 1000000 <= options.HeadAlignmentToleranceUs &&
                SignalMath.Quaternion(frame.Head, out quaternion);
            if (!aligned) counters.UnalignedHeads++;
            var left = aligned ? Sanitized(frame.Left) : Invalid(frame.Left);
            var right = aligned ? Sanitized(frame.Right) : Invalid(frame.Right);
            var sample = new EyeSample(timeUs, left, right, aligned ? frame.Head : null);
            if (samples.Count > 0) counters.DeltasUs.Add(timeUs - samples[samples.Count - 1].TimestampUs);
            counters.ObserveConfidence(frame.Left, frame.Right);
            samples.Add(sample); batch.Add(sample); lastFrameTime = stamp;
            StatusReason = sample.HasUsableGaze ? "capturing" : "gaze_invalid";
        }

        private static EyeReading Sanitized(EyeReading eye)
        {
            if (eye == null || !eye.IsUsable) return Invalid(eye);
            Direction3 direction; SignalMath.Normalize(eye.Direction.Value, out direction);
            return new EyeReading(true, direction, Confidence(eye));
        }
        private static EyeReading Invalid(EyeReading eye) { return new EyeReading(false, null, Confidence(eye)); }
        private static double? Confidence(EyeReading eye)
        { return eye != null && eye.Confidence.HasValue && Direction3.Finite(eye.Confidence.Value) && eye.Confidence.Value >= 0 && eye.Confidence.Value <= 1 ? eye.Confidence : null; }
        private long RelativeTime(double seconds)
        {
            double relative = (seconds - origin) * 1000000;
            return relative >= options.MaximumDurationUs ? options.MaximumDurationUs : (long)Math.Round(relative, MidpointRounding.AwayFromZero);
        }
        private void StopAtCurrentTime(string reason) { State = "stopped"; StatusReason = reason; SetFinalBounds(); StopSource(); }
        private void SetFinalBounds() { Info = Metadata(Info.SessionId, elapsedUs, Info.Capabilities.Gaze.Available, Info.Capabilities.Gaze.Reason); }
        private void SourceFault(Exception error)
        { SourceErrorType = error.GetType().Name; State = "faulted"; StatusReason = "source_error"; SetFinalBounds(); StopSource(); }
        private bool Unavailable(string reason)
        { State = "unavailable"; StatusReason = reason; Info = Metadata(Info.SessionId, 0, false, reason); return false; }
        private void StopSource()
        { try { source.StopTracking(); } catch (Exception error) { SourceErrorType = error.GetType().Name; } }
        private void ClearSession()
        {
            samples.Clear(); probes.Clear(); pendingProbes.Clear(); counters = new HardwareCounters();
            origin = lastClock = 0; lastFrameTime = null; lastCallerElapsed = -1; elapsedUs = 0;
            consent = calibrated = false; SourceErrorType = null;
            State = "idle"; StatusReason = "not_started";
            Info = Metadata(Guid.NewGuid().ToString("D"), 0, false, "not_started");
        }
        private SessionInfo Metadata(string id, long endUs, bool available, string reason)
        {
            return new SessionInfo(id, dataSource, null, 0, endUs,
                new EyeCapabilities(new SignalAvailability(available, reason), new SignalAvailability(false, "not_exposed_by_runtime"),
                    new SignalAvailability(false, "not_exposed_by_runtime"), false));
        }
        private void ThrowIfDisposed() { if (State == "disposed") throw new ObjectDisposedException(nameof(HardwareEyeProvider)); }
    }
}
