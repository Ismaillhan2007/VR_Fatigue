using App.Core.Metrics;

namespace App.Metrics
{
    /// <summary>
    /// Owned by feat/replay-metrics: return the real engine here. The app only calls this factory, so swapping the
    /// engine needs no change outside App/Metrics.
    /// </summary>
    public static class MetricsEngineFactory
    {
        public static IMetricsEngine Create() => new StubMetricsEngine();
    }
}
