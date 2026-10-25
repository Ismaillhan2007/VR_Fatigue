using System.Collections.Generic;
using App.Core.Contracts;

namespace App.Core.Capture
{
    /// <summary>
    /// Seam for every eye-data source (synthetic, replay, Quest Pro hardware). Providers report what they really offer:
    /// only a hardware provider reading per-eye gaze may set <see cref="Capabilities.NativeEyeGaze"/>.
    /// </summary>
    public interface IEyeTrackingProvider
    {
        /// <summary>Short human-readable name shown in the provenance label.</summary>
        string Name { get; }
        DataSource DataSource { get; }
        /// <summary>Non-null only for replay data.</summary>
        ReplayOrigin? ReplayOrigin { get; }
        Capabilities Capabilities { get; }
        /// <summary>Signals this provider cannot offer, with reasons (copied into SessionSummary.quality.unavailable).</summary>
        IReadOnlyList<UnavailableSignal> Unavailable { get; }
        bool IsRunning { get; }

        /// <summary>Begins producing samples. <paramref name="now"/> is the app clock in seconds.</summary>
        void Start(double now);
        void Stop();

        /// <summary>Appends samples produced since the previous call up to <paramref name="now"/>; returns how many were added.</summary>
        int ReadSamples(List<GazeSample> buffer, double now);
    }
}
