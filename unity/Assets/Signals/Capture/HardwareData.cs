using System;

namespace VRFatigue.Signals
{
    public sealed class HardwareReadiness
    {
        public bool Supported { get; private set; }
        public bool PermissionGranted { get; private set; }
        public bool Focused { get; private set; }
        public bool TrackingEnabled { get; private set; }
        public HardwareReadiness(bool supported, bool permissionGranted, bool focused, bool trackingEnabled)
        { Supported = supported; PermissionGranted = permissionGranted; Focused = focused; TrackingEnabled = trackingEnabled; }
    }

    public sealed class HardwareEyeFrame
    {
        public double TimestampSeconds { get; private set; }
        public double? HeadTimestampSeconds { get; private set; }
        public EyeReading Left { get; private set; }
        public EyeReading Right { get; private set; }
        public HeadPose Head { get; private set; }
        // Directions must already be head-local; head pose must be queried at the gaze timestamp.
        public HardwareEyeFrame(double timestampSeconds, EyeReading left, EyeReading right, HeadPose head, double? headTimestampSeconds)
        { TimestampSeconds = timestampSeconds; Left = left; Right = right; Head = head; HeadTimestampSeconds = headTimestampSeconds; }
    }

    public interface IHardwareEyeSource
    {
        string DataSource { get; } // Meta adapter: live. Test doubles: synthetic. Never inferred from availability.
        string TimestampSource { get; }
        string SdkPackageVersion { get; }
        HardwareReadiness Readiness { get; }
        bool TryGetClock(out double seconds);
        bool StartTracking();
        void StopTracking();
        bool TryReadFrame(out HardwareEyeFrame frame);
    }

    public sealed class HardwareCaptureOptions
    {
        public const string Version = "hardware-capture.v1";
        public long MaximumDurationUs { get; private set; }
        public int MaximumSamples { get; private set; }
        public int MaximumProbeEvents { get; private set; }
        public long HeadAlignmentToleranceUs { get; private set; }
        public HardwareCaptureOptions(long maximumDurationUs = 600000000, int maximumSamples = 100000,
            int maximumProbeEvents = 10000, long headAlignmentToleranceUs = 1000)
        {
            if (maximumDurationUs <= 0 || maximumDurationUs > 600000000 || maximumSamples < 2 || maximumSamples > 200000 ||
                maximumProbeEvents < 3 || maximumProbeEvents > 10000 || headAlignmentToleranceUs < 0 || headAlignmentToleranceUs > 10000)
                throw new ArgumentException("Invalid bounded hardware capture options.");
            MaximumDurationUs = maximumDurationUs; MaximumSamples = maximumSamples;
            MaximumProbeEvents = maximumProbeEvents; HeadAlignmentToleranceUs = headAlignmentToleranceUs;
        }
    }
}
