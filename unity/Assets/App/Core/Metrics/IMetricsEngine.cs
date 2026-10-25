using System.Collections.Generic;
using App.Core.Capture;
using App.Core.Contracts;

namespace App.Core.Metrics
{
    /// <summary>Samples and probes captured during one phase (baseline or loaded task).</summary>
    public sealed class PhaseRecording
    {
        public readonly List<GazeSample> Samples = new List<GazeSample>();
        public readonly List<ProbeEvent> Probes = new List<ProbeEvent>();
        public double StartTime;
        public double EndTime;

        public double DurationSeconds => EndTime > StartTime ? EndTime - StartTime : 0;

        public void Clear()
        {
            Samples.Clear();
            Probes.Clear();
            StartTime = EndTime = 0;
        }
    }

    /// <summary>The personal reference: baseline recording plus the self-report taken at baseline.</summary>
    public sealed class BaselineData
    {
        public PhaseRecording Recording = new PhaseRecording();
        public SelfReport SelfReport;
    }

    /// <summary>Provenance and context the engine must copy into the summary unchanged.</summary>
    public sealed class MetricsContext
    {
        public string SessionId;
        public DataSource DataSource;
        public ReplayOrigin? ReplayOrigin;
        public Capabilities Capabilities;
        public IReadOnlyList<UnavailableSignal> Unavailable;
        /// <summary>Post-task self-report embedded into SessionSummary.self_report (may be null).</summary>
        public SelfReport PostTaskReport;
    }

    /// <summary>
    /// Seam owned by feat/replay-metrics. Deterministic: the same inputs give the same summary, without any AI.
    /// Must apply <see cref="BaselinePolicy"/>, keep unavailable metrics null (never 0), and emit
    /// strain_indicator level "unavailable" for insufficient data. Plug in through App.Metrics.MetricsEngineFactory.
    /// </summary>
    public interface IMetricsEngine
    {
        string MetricVersion { get; }
        SessionSummary Compute(PhaseRecording task, BaselineData baseline, MetricsContext context);
    }
}
