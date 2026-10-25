using System;
using System.Collections.Generic;
using System.Linq;
using App.Core.Capture;
using App.Core.Contracts;
using App.Core.Metrics;

namespace App.Metrics
{
    /// <summary>
    /// FALLBACK only (used if SignalsMetricsEngine throws). Real quality bookkeeping (coverage, repeated timestamps, gaps),
    /// but metrics/deviations are the fixed values of fixtures/session_synthetic_usable.json. Never use it for live data
    /// claims: the metric_version says "stub".
    /// </summary>
    public sealed class StubMetricsEngine : IMetricsEngine
    {
        public const string Version = "stub-0.1";
        public const string QualityPolicy = "quality-0.1";
        const double MinValidCoverage = 0.6;
        const double GapThresholdSeconds = 0.1;

        public string MetricVersion => Version;

        public SessionSummary Compute(PhaseRecording task, BaselineData baseline, MetricsContext context)
        {
            var q = QualityOf(task);
            var usable = q.Status == QualityStatus.Usable;
            var baselineSeconds = baseline?.Recording?.DurationSeconds ?? 0;
            var windows = (int)Math.Floor(baselineSeconds / BaselinePolicy.WindowSeconds);
            var baselineQuality = QualityOf(baseline?.Recording);
            var (lowConfidence, reason) = BaselinePolicy.Evaluate(baseline?.SelfReport, windows,
                baselineQuality.Status != QualityStatus.Usable);
            var baselinePresent = baselineSeconds > 0 && baselineQuality.ValidDurationMs > 0;
            var indicatorAvailable = usable && baselinePresent && windows >= BaselinePolicy.MinWindows;

            return new SessionSummary
            {
                SessionId = context.SessionId,
                DataSource = context.DataSource,
                ReplayOrigin = context.ReplayOrigin,
                DurationMs = q.ValidDurationMs + q.GapDurationMs,
                MetricVersion = Version,
                Capabilities = context.Capabilities,
                Quality = q,
                Baseline = new Baseline
                {
                    State = baselinePresent ? BaselineState.Present : BaselineState.Absent,
                    DurationMs = (long)Math.Round(baselineSeconds * 1000),
                    Windows = windows,
                    LowConfidence = lowConfidence,
                    LowConfidenceReason = reason,
                },
                Metrics = usable ? FixtureMetrics() : new MetricValues(),
                Deviations = usable && baselinePresent ? FixtureDeviations() : new MetricValues(),
                StrainIndicator = indicatorAvailable
                    ? new StrainIndicator { Level = StrainLevel.Moderate, Value = 0.52, MethodVersion = Version }
                    : new StrainIndicator { Level = StrainLevel.Unavailable, Value = null, MethodVersion = Version },
                SelfReport = context.PostTaskReport,
            }.WithUnavailable(context.Unavailable);
        }

        static Quality QualityOf(PhaseRecording r)
        {
            var samples = r?.Samples ?? new List<GazeSample>();
            var duration = r?.DurationSeconds ?? 0;
            var unique = new List<GazeSample>();
            var repeated = 0;
            double? last = null;
            foreach (var s in samples)
            {
                if (last.HasValue && s.SensorTimestamp <= last.Value) { repeated++; continue; }
                last = s.SensorTimestamp;
                unique.Add(s);
            }

            double valid = 0, gaps = 0, maxGap = 0;
            var dts = new List<double>();
            for (var i = 1; i < unique.Count; i++)
            {
                var dt = unique[i].SensorTimestamp - unique[i - 1].SensorTimestamp;
                dts.Add(dt);
                if (dt > GapThresholdSeconds || !unique[i].Valid || !unique[i - 1].Valid)
                {
                    gaps += dt;
                    maxGap = Math.Max(maxGap, dt);
                }
                else valid += dt;
            }
            valid = Math.Min(valid, duration);
            var coverage = duration > 0 ? Math.Min(1, valid / duration) : 0;
            dts.Sort();
            return new Quality
            {
                Status = coverage >= MinValidCoverage ? QualityStatus.Usable : QualityStatus.InsufficientData,
                ValidCoverage = Math.Round(coverage, 4),
                ValidDurationMs = (long)Math.Round(valid * 1000),
                GapDurationMs = Math.Max(0, (long)Math.Round(duration * 1000) - (long)Math.Round(valid * 1000)),
                MaxGapMs = (long)Math.Round(maxGap * 1000),
                RepeatedSampleFraction = samples.Count > 0 ? Math.Round((double)repeated / samples.Count, 4) : 0,
                MedianDtMs = dts.Count > 0 ? Math.Round(dts[dts.Count / 2] * 1000, 2) : (double?)null,
                PolicyVersion = QualityPolicy,
            };
        }

        static MetricValues FixtureMetrics() => new MetricValues
        {
            GazeSpeedMedianDegS = 18.4, GazeSpeedP95DegS = 212.0, GazeDispersionDeg = 6.1, ProbeLatencyMs = 285.0,
            ProbeRmsErrorDeg = 1.9, ProbeMissFraction = 0.08, HeadSpeedDegS = 9.7,
        };

        static MetricValues FixtureDeviations() => new MetricValues
        {
            GazeSpeedMedianDegS = -0.4, GazeSpeedP95DegS = -0.9, GazeDispersionDeg = 0.3, ProbeLatencyMs = 1.6,
            ProbeRmsErrorDeg = 1.1, ProbeMissFraction = 1.4, HeadSpeedDegS = 0.6,
        };
    }

    static class SummaryExtensions
    {
        public static SessionSummary WithUnavailable(this SessionSummary s, IReadOnlyList<UnavailableSignal> unavailable)
        {
            if (unavailable != null) s.Quality.Unavailable = unavailable.Take(7).ToList();
            if (s.Quality.Status == QualityStatus.InsufficientData && s.Quality.Unavailable.All(u => u.Signal != Signal.Gaze))
                s.Quality.Unavailable.Add(new UnavailableSignal { Signal = Signal.Gaze, Reason = UnavailableReason.TrackingLost });
            return s;
        }
    }
}
