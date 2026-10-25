using System;
using System.Collections.Generic;
using App.Core.Capture;
using App.Core.Contracts;

namespace App.Capture
{
    /// <summary>
    /// PLACEHOLDER for the Quest Pro adapter (feat/quest-eye, Meta XR Core SDK OVREyeGaze). Not implemented: Start()
    /// throws, so EyeProviderFactory falls back to a labelled synthetic provider. See docs/ARCHITECTURE.md.
    /// </summary>
    public sealed class HardwareEyeProvider : IEyeTrackingProvider
    {
        public string Name => "Quest Pro eye tracking (not implemented)";
        public DataSource DataSource => DataSource.Live;
        public ReplayOrigin? ReplayOrigin => null;
        public Capabilities Capabilities { get; } = new Capabilities();
        public IReadOnlyList<UnavailableSignal> Unavailable { get; } = new List<UnavailableSignal>();
        public bool IsRunning => false;

        public void Start(double now) =>
            throw new NotImplementedException("HardwareEyeProvider arrives with feat/quest-eye");

        public void Stop() { }

        public int ReadSamples(List<GazeSample> buffer, double now) => 0;
    }

    public enum EyeSourceMode
    {
        Synthetic,
        Replay,
        Hardware,
    }

    public static class EyeProviderFactory
    {
        /// <summary>
        /// Creates and starts a provider. Any failure (missing replay file, unimplemented hardware) falls back to the
        /// synthetic provider; <paramref name="note"/> explains why so the UI can show it.
        /// </summary>
        public static IEyeTrackingProvider CreateAndStart(EyeSourceMode mode, double now, int seed, string replayPath,
            ReplayOrigin replayOrigin, out string note)
        {
            note = null;
            IEyeTrackingProvider provider;
            try
            {
                switch (mode)
                {
                    case EyeSourceMode.Replay:
                        provider = ReplayEyeProvider.FromFile(replayPath, replayOrigin);
                        break;
                    case EyeSourceMode.Hardware:
                        provider = new HardwareEyeProvider();
                        break;
                    default:
                        provider = new SyntheticEyeProvider(seed);
                        break;
                }
                provider.Start(now);
                return provider;
            }
            catch (Exception e)
            {
                note = $"{mode} unavailable ({e.GetType().Name}); using synthetic data";
                provider = new SyntheticEyeProvider(seed);
                provider.Start(now);
                return provider;
            }
        }
    }
}
