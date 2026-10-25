using System;
using System.Collections.Generic;
using System.Linq;
using App.Core.Contracts;
using App.Core.Metrics;
using Sig = VRFatigue.Signals;
using AppCapture = App.Core.Capture;

namespace App.Metrics
{
    /// <summary>
    /// Bridge from the application seam to the VRFatigue.Signals module (feat/signals-metrics-quest-pro): converts
    /// the captured baseline + task samples into one eye-session on the sensor clock, runs quality, metrics, baseline
    /// comparison and the experimental indicator there, and maps the results onto the v1 SessionSummary.
    /// The maths lives in Assets/Signals; this class only translates.
    /// </summary>
    public sealed class SignalsMetricsEngine : IMetricsEngine
    {
        /// <summary>Baseline and task share one placeholder canvas, so they share one conditions id.</summary>
        public const string ConditionsId = "assessment_canvas_v1";

        public string MetricVersion => Sig.MetricPolicy.AlgorithmVersion;

        public SessionSummary Compute(PhaseRecording task, BaselineData baseline, MetricsContext context)
        {
            var baselineSamples = baseline?.Recording?.Samples ?? new List<AppCapture.GazeSample>();
            var taskSamples = task?.Samples ?? new List<AppCapture.GazeSample>();
            var all = baselineSamples.Concat(taskSamples).ToList();
            if (all.Count == 0) return Empty(context, baseline);

            // One clock for both phases: sensor timestamps relative to the first baseline-or-task row.
            var origin = all[0].SensorTimestamp;
            long Us(double seconds) => (long)Math.Round((seconds - origin) * 1e6, MidpointRounding.AwayFromZero);
            var samples = all.Select(s => ToEyeSample(s, Us(s.SensorTimestamp))).ToList();
            var endUs = Math.Max(0, samples.Max(s => s.TimestampUs));
            var baselineEndUs = taskSamples.Count > 0 ? Math.Max(0, Math.Min(endUs, Us(taskSamples[0].SensorTimestamp))) : endUs;

            var info = new Sig.SessionInfo(context.SessionId, Wire(context.DataSource), Wire(context.ReplayOrigin), 0, endUs, Capabilities(context));
            var probes = Probes(baseline?.Recording, all, origin, endUs).Concat(Probes(task, all, origin, endUs)).OrderBy(p => p.TimestampUs).ToList();
            var session = new Sig.EyeSession(info, samples, probes);

            var report = baseline?.SelfReport;
            var plan = new Sig.BaselinePlan(context.SessionId,
                new Sig.AssessmentPhase(0, baselineEndUs, ConditionsId),
                new Sig.AssessmentPhase(baselineEndUs, endUs, ConditionsId),
                report == null ? null : new Sig.BaselineSelfReport(report.EyeStrain, report.MentalFatigue));
            var comparison = Sig.BaselineEvaluator.Evaluate(session, plan);
            var indicator = Sig.IndicatorEvaluator.Evaluate(comparison);

            var clean = comparison.SessionQuality;
            var taskInfo = new Sig.SessionInfo(info.SessionId, info.DataSource, info.ReplayOrigin, baselineEndUs, endUs, info.Capabilities);
            var taskQuality = Sig.QualityEvaluator.Evaluate(taskInfo,
                clean.AcceptedStream.Where(s => s.TimestampUs >= baselineEndUs), clean.Policy);
            var taskProbes = probes.Where(p => p.TimestampUs >= baselineEndUs);
            var taskMetrics = Sig.MetricsEvaluator.Evaluate(taskQuality, taskProbes, comparison.Baseline.MetricPolicy);

            var usable = clean.Status == "usable" && taskQuality.Status == "usable";
            var summary = new SessionSummary
            {
                SessionId = context.SessionId,
                DataSource = context.DataSource,
                ReplayOrigin = context.ReplayOrigin,
                MetricVersion = MetricVersion,
                Capabilities = context.Capabilities,
                Quality = QualityOf(clean, usable, context, probes.Count > 0),
                Baseline = BaselineOf(comparison.Baseline, baselineEndUs),
                Metrics = Values(name => usable ? taskMetrics.Metrics[name].Value : null),
                Deviations = Values(name => MedianZ(comparison, name)),
                SelfReport = context.PostTaskReport,
            };
            summary.DurationMs = summary.Quality.ValidDurationMs + summary.Quality.GapDurationMs;
            summary.StrainIndicator = IndicatorOf(indicator, summary, usable);
            return summary;
        }

        // ----- mapping -----

        static Sig.EyeSample ToEyeSample(AppCapture.GazeSample s, long timestampUs)
        {
            double? confidence = s.Confidence.HasValue ? (double?)Math.Max(0, Math.Min(1, s.Confidence.Value)) : null;
            Sig.EyeReading Eye(bool valid, System.Numerics.Vector3 d) => valid && s.Valid
                ? new Sig.EyeReading(true, new Sig.Direction3(d.X, d.Y, d.Z), confidence)
                : new Sig.EyeReading(false, null, confidence);
            var q = s.HeadRotation;
            var head = new Sig.HeadPose(new Sig.Direction3(s.HeadPosition.X, s.HeadPosition.Y, s.HeadPosition.Z), q.X, q.Y, q.Z, q.W);
            return new Sig.EyeSample(timestampUs, Eye(s.LeftValid, s.LeftDirection), Eye(s.RightValid, s.RightDirection), head);
        }

        /// <summary>App probes use the frame clock; shift them onto the sensor clock with the first sample's offset.</summary>
        static IEnumerable<Sig.ProbeEvent> Probes(PhaseRecording r, List<AppCapture.GazeSample> all, double origin, long endUs)
        {
            if (r == null || r.Probes.Count == 0) yield break;
            var offset = all[0].FrameTime - all[0].SensorTimestamp;
            var n = 0;
            foreach (var p in r.Probes)
            {
                var target = new Sig.Direction3(p.TargetDirection.X, p.TargetDirection.Y, p.TargetDirection.Z);
                long At(double frameTime) => Math.Max(0, Math.Min(endUs, (long)Math.Round((frameTime - offset - origin) * 1e6)));
                var id = $"probe_{r.StartTime:0}_{n++}";
                yield return new Sig.ProbeEvent(At(p.OnsetTime), id, "target_onset", target);
                yield return new Sig.ProbeEvent(At(p.OnsetTime + 1.0), id, "hold_start", target);
                yield return new Sig.ProbeEvent(At(p.OnsetTime + 1.0 + p.HoldSeconds), id, "target_end", target);
            }
        }

        static Sig.EyeCapabilities Capabilities(MetricsContext c)
        {
            var gaze = c.Capabilities != null && c.Capabilities.GazePose
                ? new Sig.SignalAvailability(true, null)
                : new Sig.SignalAvailability(false, "not_exposed_by_runtime");
            var notExposed = new Sig.SignalAvailability(false, "not_exposed_by_runtime");
            return new Sig.EyeCapabilities(gaze, notExposed, notExposed, false);
        }

        static Quality QualityOf(Sig.QualityResult q, bool usable, MetricsContext c, bool hadProbes)
        {
            var durationMs = (long)Math.Round(q.Info.DurationUs / 1000.0);
            var validMs = Math.Min(durationMs, (long)Math.Round(q.ValidDurationUs / 1000.0));
            var deltas = new List<double>();
            for (var i = 1; i < q.AcceptedStream.Count; i++)
                deltas.Add((q.AcceptedStream[i].TimestampUs - q.AcceptedStream[i - 1].TimestampUs) / 1000.0);
            deltas.Sort();

            var unavailable = new List<UnavailableSignal>(c.Unavailable ?? Array.Empty<UnavailableSignal>());
            if (!hadProbes) unavailable.Add(new UnavailableSignal { Signal = Signal.Probe, Reason = UnavailableReason.NotCollected });
            if (!usable) unavailable.Add(new UnavailableSignal { Signal = Signal.Gaze, Reason = UnavailableReason.TrackingLost });
            return new Quality
            {
                Status = usable ? QualityStatus.Usable : QualityStatus.InsufficientData,
                ValidCoverage = Math.Round(Math.Max(0, Math.Min(1, q.ValidCoverage)), 4),
                ValidDurationMs = validMs,
                GapDurationMs = durationMs - validMs,
                MaxGapMs = (long)Math.Round(q.LongestGapUs / 1000.0),
                RepeatedSampleFraction = q.InputSamples == 0 ? 0 : Math.Round((double)q.DuplicateTimestamps / q.InputSamples, 4),
                MedianDtMs = deltas.Count == 0 ? (double?)null : Math.Round(deltas[deltas.Count / 2], 3),
                Unavailable = unavailable.GroupBy(u => u.Signal).Select(g => g.First()).Take(8).ToList(),
                PolicyVersion = q.Policy.Version,
            };
        }

        static Baseline BaselineOf(Sig.BaselineResult b, long baselineEndUs)
        {
            var reasons = b.LowConfidenceReasons;
            LowConfidenceReason? reason = reasons.Contains("self_reported_tired") ? LowConfidenceReason.SelfReportedTired
                : reasons.Contains("too_short") ? LowConfidenceReason.TooShort
                : b.LowConfidence ? LowConfidenceReason.PoorQuality : (LowConfidenceReason?)null;
            return new Baseline
            {
                State = baselineEndUs > 0 && b.Windows.Count > 0 ? BaselineState.Present : BaselineState.Absent,
                DurationMs = (long)Math.Round(baselineEndUs / 1000.0),
                Windows = b.Windows.Count(w => w.Eligible),
                LowConfidence = b.LowConfidence,
                LowConfidenceReason = reason,
            };
        }

        /// <summary>Median of the comparison windows' robust z-scores (already clipped to +/-4 by the module).</summary>
        static double? MedianZ(Sig.BaselineComparisonResult c, string name)
        {
            var z = c.Windows.Where(w => w.Deviations != null && w.Deviations[name].ZScore.HasValue)
                .Select(w => w.Deviations[name].ZScore.Value).OrderBy(v => v).ToList();
            if (z.Count == 0) return null;
            var m = z.Count % 2 == 1 ? z[z.Count / 2] : (z[z.Count / 2 - 1] + z[z.Count / 2]) / 2;
            return Math.Round(Math.Max(-4, Math.Min(4, m)), 4);
        }

        static StrainIndicator IndicatorOf(Sig.IndicatorResult r, SessionSummary s, bool usable)
        {
            var last = r.Windows.LastOrDefault()?.State;
            var probesMeasured = s.Metrics.ProbeLatencyMs.HasValue && s.Metrics.ProbeRmsErrorDeg.HasValue && s.Metrics.ProbeMissFraction.HasValue;
            var available = usable && last?.Value != null && s.Baseline.Windows >= BaselinePolicy.MinWindows && probesMeasured;
            return new StrainIndicator
            {
                Level = available ? Level(last.Level) : StrainLevel.Unavailable,
                Value = available ? Math.Round(last.Value.Value, 4) : (double?)null,
                MethodVersion = Sig.IndicatorPolicy.MethodVersion,
            };
        }

        static StrainLevel Level(string level) =>
            level == "low" ? StrainLevel.Low : level == "moderate" ? StrainLevel.Moderate : level == "high" ? StrainLevel.High : StrainLevel.Unavailable;

        static MetricValues Values(Func<string, double?> get)
        {
            double? R(string n) { var v = get(n); return v.HasValue ? Math.Round(v.Value, 4) : (double?)null; }
            return new MetricValues
            {
                GazeSpeedMedianDegS = R("gaze_speed_median_deg_s"),
                GazeSpeedP95DegS = R("gaze_speed_p95_deg_s"),
                GazeDispersionDeg = R("gaze_dispersion_deg"),
                ProbeLatencyMs = R("probe_latency_ms"),
                ProbeRmsErrorDeg = R("probe_rms_error_deg"),
                ProbeMissFraction = R("probe_miss_fraction"),
                HeadSpeedDegS = R("head_speed_deg_s"),
            };
        }

        static SessionSummary Empty(MetricsContext c, BaselineData baseline) => new SessionSummary
        {
            SessionId = c.SessionId,
            DataSource = c.DataSource,
            ReplayOrigin = c.ReplayOrigin,
            DurationMs = 0,
            MetricVersion = Sig.MetricPolicy.AlgorithmVersion,
            Capabilities = c.Capabilities,
            Quality = new Quality
            {
                Status = QualityStatus.InsufficientData,
                PolicyVersion = Sig.QualityPolicy.PrototypeV1.Version,
                Unavailable = new List<UnavailableSignal> { new UnavailableSignal { Signal = Signal.Gaze, Reason = UnavailableReason.TrackingLost } },
            },
            Baseline = new Baseline
            {
                State = BaselineState.Absent,
                LowConfidence = true,
                LowConfidenceReason = BaselinePolicy.SelfReportedTired(baseline?.SelfReport) ? LowConfidenceReason.SelfReportedTired : LowConfidenceReason.TooShort,
            },
            StrainIndicator = new StrainIndicator { MethodVersion = Sig.IndicatorPolicy.MethodVersion },
            SelfReport = c.PostTaskReport,
        };

        static string Wire(DataSource d) => d == DataSource.Live ? "live" : d == DataSource.Replay ? "replay" : "synthetic";
        static string Wire(ReplayOrigin? o) => o == null ? null : o == ReplayOrigin.Recorded ? "recorded" : "synthetic";
    }
}
