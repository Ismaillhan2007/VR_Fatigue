using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace VRFatigue.Signals
{
    public static class IndicatorEvaluator
    {
        public static IndicatorResult Evaluate(BaselineComparisonResult comparison, IndicatorPolicy policy = null)
        {
            if (comparison == null) throw new ArgumentNullException(nameof(comparison));
            policy = policy ?? new IndicatorPolicy();
            var machine = new IndicatorStateMachine(policy);
            var result = new List<IndicatorWindow>();
            foreach (var window in comparison.Windows)
            {
                var missing = new List<string>();
                var contributions = new Dictionary<string, IndicatorContribution>();
                foreach (string key in BaselinePolicy.MetricNames)
                {
                    var deviation = window.Deviations[key];
                    var term = new IndicatorContribution { Weight = policy.Weights[key], Direction = policy.Directions[key],
                        NormalizedWeight = policy.Weights[key] / policy.TotalWeight, ZScore = deviation.ZScore };
                    contributions[key] = term;
                    if (term.Weight == 0) { term.Status = "disabled"; term.Contribution = 0; continue; }
                    if (!deviation.ZScore.HasValue || !Direction3.Finite(deviation.ZScore.Value) || Math.Abs(deviation.ZScore.Value) > IndicatorPolicy.ExpectedZClip)
                    {
                        term.Status = "unavailable"; term.UnavailableReason = deviation.UnavailableReason ?? "invalid_deviation";
                        missing.Add(key); continue;
                    }
                    term.Status = "available";
                    // Normalize weights before multiplication to avoid overflowing a large custom weight.
                    term.Contribution = term.NormalizedWeight * term.Direction * deviation.ZScore.Value;
                }
                var baseline = comparison.Baseline;
                string unavailable = baseline.Status != "ready" ? "baseline_unavailable" :
                    baseline.Policy.WindowDurationUs != IndicatorPolicy.ExpectedWindowUs || baseline.Policy.ZClip != IndicatorPolicy.ExpectedZClip ? "incompatible_baseline_policy" :
                    baseline.Phase.ConditionsId == null || comparison.Phase.ConditionsId == null ? "conditions_unconfirmed" :
                    baseline.Phase.ConditionsId != comparison.Phase.ConditionsId ? "conditions_mismatch" :
                    !window.Complete ? "partial_window" : !window.Eligible || window.Measurements.Quality.Status != "usable" ? "insufficient_window_data" :
                    new[] { "probe_latency_ms", "probe_rms_error_deg", "probe_miss_fraction" }.Any(k => !baseline.References[k].Median.HasValue) ? "baseline_probes_unavailable" :
                    window.Measurements.Probes.Count == 0 ? "probes_unavailable" :
                    missing.Count > 0 ? "required_deviation_unavailable" : null;
                double? combined = unavailable == null ? (double?)Math.Max(-IndicatorPolicy.ExpectedZClip,
                    Math.Min(IndicatorPolicy.ExpectedZClip, contributions.Values.Sum(c => c.Contribution.Value))) : null;
                double? raw = combined.HasValue ? (double?)Math.Max(0, Math.Min(1, combined.Value / policy.NormalizationScale)) : null;
                result.Add(new IndicatorWindow { Index = window.Index, SignedCombination = combined,
                    State = machine.Advance(window.StartUs, window.EndUs, raw, unavailable), MissingMetrics = missing.AsReadOnly(),
                    Contributions = new ReadOnlyDictionary<string, IndicatorContribution>(contributions) });
            }
            return new IndicatorResult { Policy = policy, Comparison = comparison, Windows = result.AsReadOnly() };
        }
    }
}
