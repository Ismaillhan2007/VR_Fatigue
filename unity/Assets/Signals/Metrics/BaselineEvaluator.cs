using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace VRFatigue.Signals
{
    public static class BaselineEvaluator
    {
        public static BaselineComparisonResult Evaluate(EyeSession session, BaselinePlan plan, BaselinePolicy policy = null,
            QualityPolicy qualityPolicy = null, MetricPolicy metricPolicy = null)
        {
            if (session == null || plan == null) throw new ArgumentNullException();
            if (session.Info.SessionId != plan.SessionId) throw new ArgumentException("Plan belongs to a different session.");
            var baseline = Build(session, plan.Baseline, plan.SelfReport, policy, qualityPolicy, metricPolicy);
            return Compare(baseline, session, plan.Comparison);
        }

        public static BaselineResult Build(EyeSession session, AssessmentPhase phase, BaselineSelfReport selfReport = null,
            BaselinePolicy policy = null, QualityPolicy qualityPolicy = null, MetricPolicy metricPolicy = null)
        {
            if (session == null || phase == null) throw new ArgumentNullException();
            policy = policy ?? new BaselinePolicy(); qualityPolicy = qualityPolicy ?? QualityPolicy.PrototypeV1;
            metricPolicy = metricPolicy ?? new MetricPolicy();
            ValidatePhase(session.Info, phase, policy);
            var clean = QualityEvaluator.Evaluate(session.Info, session.Samples, qualityPolicy);
            var phaseQuality = QualityEvaluator.Evaluate(Bounds(session.Info, phase.StartUs, phase.EndUs),
                clean.AcceptedStream.Where(s => s.TimestampUs >= phase.StartUs && s.TimestampUs < phase.EndUs), qualityPolicy);
            string eye = MetricsEvaluator.ChooseEye(phaseQuality, metricPolicy);
            var pinned = metricPolicy.WithGazeEye(eye);
            var windows = Windows(session, clean, phase, policy, qualityPolicy, pinned);
            var references = new Dictionary<string, MetricReference>();
            int eligible = windows.Count(w => w.Eligible);
            foreach (string key in BaselinePolicy.MetricNames)
            {
                var values = windows.Where(w => w.Eligible && w.Measurements.Metrics[key].Value.HasValue)
                    .Select(w => w.Measurements.Metrics[key].Value.Value).ToArray();
                var reference = new MetricReference { Floor = policy.Floors[key], UsableWindowCount = values.Length };
                references[key] = reference;
                if (values.Length < policy.MinimumWindows) { reference.UnavailableReason = "insufficient_metric_windows"; continue; }
                double median = SignalMath.Median(values);
                double mad = SignalMath.Median(values.Select(v => Math.Abs(v - median)));
                reference.Median = median; reference.Mad = mad;
                reference.Scale = Math.Max(BaselinePolicy.MadMultiplier * mad, reference.Floor);
            }
            var reasons = new List<string>();
            if (windows.Count(w => w.Complete) < policy.MinimumWindows) reasons.Add("too_short");
            else if (eligible < policy.MinimumWindows) reasons.Add("insufficient_usable_windows");
            bool hasGazeReference = BaselinePolicy.MetricNames.Take(3).Any(k => references[k].Median.HasValue);
            if (eligible >= policy.MinimumWindows && !hasGazeReference) reasons.Add("no_reference_metrics");
            string status = eligible >= policy.MinimumWindows && hasGazeReference ? "ready" : "insufficient_data";
            if (selfReport == null) reasons.Add("self_report_missing");
            else if (Math.Max(selfReport.EyeStrain, selfReport.MentalFatigue) >= 5) reasons.Add("self_reported_tired");
            if (phase.ConditionsId == null) reasons.Add("conditions_unconfirmed");
            return new BaselineResult {
                Info = session.Info, Phase = phase, SelfReport = selfReport, Policy = policy, QualityPolicy = qualityPolicy,
                MetricPolicy = pinned, SelectedEye = eye, Status = status, LowConfidence = reasons.Count > 0,
                LowConfidenceReasons = reasons.AsReadOnly(), Windows = windows.AsReadOnly(),
                References = new ReadOnlyDictionary<string, MetricReference>(references) };
        }

        public static BaselineComparisonResult Compare(BaselineResult baseline, EyeSession session, AssessmentPhase phase)
        {
            if (baseline == null || session == null || phase == null) throw new ArgumentNullException();
            if (baseline.Info.SessionId != session.Info.SessionId || baseline.Info.DataSource != session.Info.DataSource ||
                baseline.Info.ReplayOrigin != session.Info.ReplayOrigin || baseline.Info.StartTimestampUs != session.Info.StartTimestampUs ||
                !SameCapabilities(baseline.Info.Capabilities, session.Info.Capabilities))
                throw new ArgumentException("Comparison must use the same session, provenance, clock origin and capabilities.");
            if (phase.StartUs < baseline.Phase.EndUs) throw new ArgumentException("Comparison must follow the frozen baseline.");
            ValidatePhase(session.Info, phase, baseline.Policy);
            var clean = QualityEvaluator.Evaluate(session.Info, session.Samples, baseline.QualityPolicy);
            var windows = Windows(session, clean, phase, baseline.Policy, baseline.QualityPolicy, baseline.MetricPolicy);
            string reason = baseline.Status != "ready" ? "baseline_unavailable" :
                baseline.Phase.ConditionsId == null || phase.ConditionsId == null ? "conditions_unconfirmed" :
                baseline.Phase.ConditionsId != phase.ConditionsId ? "conditions_mismatch" : null;
            foreach (var window in windows)
            {
                var deviations = new Dictionary<string, MetricDeviation>();
                foreach (string key in BaselinePolicy.MetricNames)
                {
                    var reading = window.Measurements.Metrics[key]; var reference = baseline.References[key];
                    var deviation = new MetricDeviation { Value = reading.Value };
                    deviations[key] = deviation;
                    string unavailable = reason ?? (!window.Complete ? "partial_window" : !window.Eligible ? "insufficient_window_data" :
                        !reference.Median.HasValue ? "baseline_metric_unavailable" : reading.UnavailableReason);
                    if (unavailable != null) { deviation.UnavailableReason = unavailable; continue; }
                    double delta = reading.Value.Value - reference.Median.Value;
                    // Compare before division: tiny positive floors must not produce Infinity.
                    bool clipped = Math.Abs(delta) > baseline.Policy.ZClip * reference.Scale.Value;
                    deviation.Delta = delta; deviation.Clipped = clipped;
                    deviation.ZScore = clipped ? (delta < 0 ? -baseline.Policy.ZClip : baseline.Policy.ZClip) : delta / reference.Scale.Value;
                }
                window.Deviations = new ReadOnlyDictionary<string, MetricDeviation>(deviations);
            }
            if (reason == null && !windows.Any(w => w.Deviations.Values.Any(d => d.ZScore.HasValue))) reason = "no_comparable_windows";
            return new BaselineComparisonResult { Baseline = baseline, Phase = phase, SessionQuality = clean,
                Status = reason == null ? "comparable" : "not_comparable", UnavailableReason = reason, Windows = windows.AsReadOnly() };
        }

        private static List<MeasurementWindow> Windows(EyeSession session, QualityResult clean, AssessmentPhase phase,
            BaselinePolicy policy, QualityPolicy qualityPolicy, MetricPolicy metricPolicy)
        {
            var result = new List<MeasurementWindow>();
            var groups = session.ProbeEvents.GroupBy(p => p.ProbeId).Select(g => g.ToArray()).ToArray();
            var errors = ProbeEvaluator.ValidationErrors(session.ProbeEvents.ToArray(), metricPolicy);
            for (long start = phase.StartUs; start < phase.EndUs;)
            {
                long end = start + Math.Min(policy.WindowDurationUs, phase.EndUs - start);
                // Half-open sample windows avoid using the first sample of the next phase.
                var rows = clean.AcceptedStream.Where(s => s.TimestampUs >= start && s.TimestampUs < end).ToArray();
                var selected = groups.Where(g => g.All(p => p.TimestampUs >= start && p.TimestampUs <= end) && g.Any(p => p.TimestampUs < end)).ToArray();
                int boundary = groups.Count(g => g.Min(p => p.TimestampUs) < end && g.Max(p => p.TimestampUs) > start && !selected.Contains(g));
                var events = selected.SelectMany(g => g).OrderBy(p => p.TimestampUs).ToArray();
                var quality = QualityEvaluator.Evaluate(Bounds(session.Info, start, end), rows, qualityPolicy);
                var measurements = MetricsEvaluator.EvaluateWithProbeContext(quality, events, metricPolicy, errors);
                bool complete = end - start == policy.WindowDurationUs;
                bool eligible = complete && quality.Status == "usable" && measurements.GazeValidDurationUs >= qualityPolicy.MinimumValidDurationUs &&
                    (double)measurements.GazeValidDurationUs / (end - start) >= qualityPolicy.MinimumCoverage;
                result.Add(new MeasurementWindow { Index = result.Count, StartUs = start, EndUs = end,
                    Complete = complete, Eligible = eligible, BoundaryProbeCount = boundary, Measurements = measurements });
                start = end;
            }
            return result;
        }

        private static SessionInfo Bounds(SessionInfo info, long start, long end)
        { return new SessionInfo(info.SessionId, info.DataSource, info.ReplayOrigin, start, end, info.Capabilities); }
        private static void ValidatePhase(SessionInfo info, AssessmentPhase phase, BaselinePolicy policy)
        {
            if (phase.StartUs < info.StartTimestampUs || phase.EndUs > info.EndTimestampUs) throw new ArgumentException("Phase is outside the captured session.");
            long duration = phase.EndUs - phase.StartUs;
            if (duration / policy.WindowDurationUs + (duration % policy.WindowDurationUs == 0 ? 0 : 1) > BaselinePolicy.MaximumWindowsPerPhase)
                throw new ArgumentException("Too many windows in phase.");
        }
        private static bool SameCapabilities(EyeCapabilities a, EyeCapabilities b)
        {
            return a.Gaze.Available == b.Gaze.Available && a.Gaze.Reason == b.Gaze.Reason &&
                a.PupilDiameter.Available == b.PupilDiameter.Available && a.PupilDiameter.Reason == b.PupilDiameter.Reason &&
                a.EyelidOpenness.Available == b.EyelidOpenness.Available && a.EyelidOpenness.Reason == b.EyelidOpenness.Reason &&
                a.ConfidenceInformative == b.ConfidenceInformative;
        }
    }
}
