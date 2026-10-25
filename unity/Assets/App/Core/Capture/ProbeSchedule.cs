using System;
using System.Numerics;

namespace App.Core.Capture
{
    /// <summary>
    /// Fixed probe layout shared by the baseline and the loaded task (same conditions, honestly the same
    /// <see cref="LayoutId"/>): a head-locked target at <see cref="DistanceMetres"/> jumps every
    /// <see cref="TrialSeconds"/> through ten positions; consecutive positions are 10-30 degrees apart.
    /// Matches tools/generate_demo_session.py.
    /// </summary>
    public static class ProbeSchedule
    {
        public const string LayoutId = "probe_cycle_10_v1";
        public const double TrialSeconds = 2.0;
        public const double AcquisitionSeconds = 0.5;
        public const float DistanceMetres = 2f;

        /// <summary>(yaw, pitch) in degrees, head-local.</summary>
        public static readonly (float yaw, float pitch)[] Layout =
        {
            (0, 0), (-14, 6), (12, 8), (-6, -10), (18, -4), (-20, 2), (4, 12), (14, -12), (-12, -6), (10, 5),
        };

        public static Vector3 Direction(float yawDeg, float pitchDeg)
        {
            double y = yawDeg * Math.PI / 180, p = pitchDeg * Math.PI / 180;
            return Vector3.Normalize(new Vector3((float)(Math.Sin(y) * Math.Cos(p)), (float)Math.Sin(p), (float)(Math.Cos(y) * Math.Cos(p))));
        }

        /// <summary>Trial index active at <paramref name="elapsed"/> unpaused seconds after the phase start.</summary>
        public static int TrialIndex(double elapsed) => Math.Max(0, (int)Math.Floor(elapsed / TrialSeconds));

        /// <summary>Trial <paramref name="index"/> of a phase, starting at <paramref name="onset"/> on the frame clock.</summary>
        public static ProbeEvent Trial(string prefix, int index, double onset)
        {
            var (yaw, pitch) = Layout[index % Layout.Length];
            return new ProbeEvent
            {
                TrialId = $"{prefix}{index:000}",
                OnsetTime = onset,
                HoldStartTime = onset + AcquisitionSeconds,
                EndTime = onset + TrialSeconds,
                TargetDirection = Direction(yaw, pitch),
            };
        }
    }
}
