using System.Collections.Generic;
using System.Numerics;

namespace App.Core.Capture
{
    /// <summary>Optional: a provider that brings its own probe schedule (replay files). The app then shows those targets.</summary>
    public interface IProbeSource
    {
        /// <summary>Appends probe trials whose onset is at or before <paramref name="now"/> and not yet returned.</summary>
        int ReadProbes(List<ProbeEvent> buffer, double now);
    }

    /// <summary>Optional: a provider whose clock can be held between phases (replay), so no data are skipped.</summary>
    public interface IHoldableProvider
    {
        void SetHold(bool hold, double now);
    }

    /// <summary>Optional: a synthetic provider that moves its gaze to the probe targets the app shows.</summary>
    public interface IProbeFollower
    {
        void ShowTarget(Vector3 headLocalDirection, double now);
        /// <summary>False during baseline, true during the loaded task (slightly slower, less accurate saccades).</summary>
        void SetLoaded(bool loaded);
    }
}
