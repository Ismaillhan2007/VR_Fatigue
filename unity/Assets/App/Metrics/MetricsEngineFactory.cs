using App.Core.Metrics;

namespace App.Metrics
{
    /// <summary>
    /// Owned by the signals/metrics owner: returns the real engine (SignalsMetricsEngine bridges to Assets/Signals). The app only calls this factory, so swapping the
    /// engine needs no change outside App/Metrics.
    /// </summary>
    public static class MetricsEngineFactory
    {
        public static IMetricsEngine Create() => new SignalsMetricsEngine();
    }
}
