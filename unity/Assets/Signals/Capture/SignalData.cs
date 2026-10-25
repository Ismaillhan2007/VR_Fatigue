using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace VRFatigue.Signals
{
    public struct Direction3
    {
        public readonly double X, Y, Z;
        public Direction3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public bool IsUsable { get { return Finite(X) && Finite(Y) && Finite(Z) && (X != 0 || Y != 0 || Z != 0); } }
        internal static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }

    public sealed class HeadPose
    {
        public Direction3 PositionMetres { get; private set; }
        public double RotationX { get; private set; }
        public double RotationY { get; private set; }
        public double RotationZ { get; private set; }
        public double RotationW { get; private set; }
        public HeadPose(Direction3 position, double x, double y, double z, double w)
        { PositionMetres = position; RotationX = x; RotationY = y; RotationZ = z; RotationW = w; }
    }

    public sealed class EyeReading
    {
        public bool Valid { get; private set; }
        public Direction3? Direction { get; private set; }
        public double? Confidence { get; private set; }
        // Confidence is retained for diagnostics, never used as a quality gate.
        public bool IsUsable { get { return Valid && Direction.HasValue && Direction.Value.IsUsable; } }
        public EyeReading(bool valid, Direction3? direction, double? confidence = null)
        { Valid = valid; Direction = direction; Confidence = confidence; }
    }

    public sealed class EyeSample
    {
        public long TimestampUs { get; private set; }
        public EyeReading Left { get; private set; }
        public EyeReading Right { get; private set; }
        public HeadPose Head { get; private set; }
        public bool HasUsableGaze { get { return (Left != null && Left.IsUsable) || (Right != null && Right.IsUsable); } }
        public EyeSample(long timestampUs, EyeReading left, EyeReading right, HeadPose head = null)
        { TimestampUs = timestampUs; Left = left; Right = right; Head = head; }
    }

    public sealed class ProbeEvent
    {
        public long TimestampUs { get; private set; }
        public string ProbeId { get; private set; }
        public string Kind { get; private set; }
        public Direction3 TargetDirection { get; private set; }
        public ProbeEvent(long timestampUs, string probeId, string kind, Direction3 targetDirection)
        {
            if (string.IsNullOrWhiteSpace(probeId) || probeId.Length > 64) throw new ArgumentException("Invalid probe ID.");
            if (kind != "target_onset" && kind != "hold_start" && kind != "target_end") throw new ArgumentException("Invalid probe event kind.");
            if (!targetDirection.IsUsable) throw new ArgumentException("Invalid probe direction.");
            TimestampUs = timestampUs; ProbeId = probeId; Kind = kind; TargetDirection = targetDirection;
        }
    }

    public sealed class SignalAvailability
    {
        public bool Available { get; private set; }
        public string Reason { get; private set; }
        public SignalAvailability(bool available, string reason)
        {
            if (available ? reason != null : string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("Available signals require a null reason; unavailable signals require a reason.");
            Available = available; Reason = reason;
        }
    }

    public sealed class EyeCapabilities
    {
        public SignalAvailability Gaze { get; private set; }
        public SignalAvailability PupilDiameter { get; private set; }
        public SignalAvailability EyelidOpenness { get; private set; }
        public bool ConfidenceInformative { get; private set; }
        public EyeCapabilities(SignalAvailability gaze, SignalAvailability pupilDiameter, SignalAvailability eyelidOpenness, bool confidenceInformative)
        {
            Gaze = gaze ?? throw new ArgumentNullException(nameof(gaze));
            PupilDiameter = pupilDiameter ?? throw new ArgumentNullException(nameof(pupilDiameter));
            EyelidOpenness = eyelidOpenness ?? throw new ArgumentNullException(nameof(eyelidOpenness));
            ConfidenceInformative = confidenceInformative;
        }
    }

    public sealed class SessionInfo
    {
        public const string SchemaVersion = "eye-session.v1";
        public string SessionId { get; private set; }
        public string DataSource { get; private set; }
        public string ReplayOrigin { get; private set; }
        public long StartTimestampUs { get; private set; }
        public long EndTimestampUs { get; private set; }
        public long DurationUs { get { return EndTimestampUs - StartTimestampUs; } }
        public EyeCapabilities Capabilities { get; private set; }
        public SessionInfo(string sessionId, string dataSource, string replayOrigin, long startTimestampUs, long endTimestampUs, EyeCapabilities capabilities)
        {
            Guid parsed;
            if (!Guid.TryParseExact(sessionId, "D", out parsed)) throw new ArgumentException("session_id must be a UUID.");
            if (dataSource != "live" && dataSource != "replay" && dataSource != "synthetic") throw new ArgumentException("Invalid data source.");
            if (dataSource == "replay" ? replayOrigin != "synthetic" && replayOrigin != "recorded" : replayOrigin != null)
                throw new ArgumentException("replay_origin is required only for replay.");
            if (startTimestampUs < 0 || endTimestampUs < startTimestampUs) throw new ArgumentException("Invalid session bounds.");
            SessionId = sessionId; DataSource = dataSource; ReplayOrigin = replayOrigin;
            StartTimestampUs = startTimestampUs; EndTimestampUs = endTimestampUs;
            Capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        }
        public SessionInfo AsReplay()
        {
            return new SessionInfo(SessionId, "replay", DataSource == "synthetic" ? "synthetic" : ReplayOrigin ?? "recorded",
                StartTimestampUs, EndTimestampUs, Capabilities);
        }
    }

    public sealed class EyeSession
    {
        public SessionInfo Info { get; private set; }
        public ReadOnlyCollection<EyeSample> Samples { get; private set; }
        public ReadOnlyCollection<ProbeEvent> ProbeEvents { get; private set; }
        public EyeSession(SessionInfo info, IEnumerable<EyeSample> samples, IEnumerable<ProbeEvent> probeEvents)
        {
            Info = info ?? throw new ArgumentNullException(nameof(info));
            Samples = Array.AsReadOnly((samples ?? throw new ArgumentNullException(nameof(samples))).ToArray());
            ProbeEvents = Array.AsReadOnly((probeEvents ?? throw new ArgumentNullException(nameof(probeEvents))).ToArray());
            if (Samples.Any(s => s == null) || ProbeEvents.Any(p => p == null)) throw new ArgumentException("Null stream entries are not allowed.");
            long previous = -1;
            foreach (var item in ProbeEvents)
            {
                if (item.TimestampUs < previous || item.TimestampUs < info.StartTimestampUs || item.TimestampUs > info.EndTimestampUs)
                    throw new ArgumentException("Probe events must be chronological and within session bounds.");
                previous = item.TimestampUs;
            }
        }
    }
}
