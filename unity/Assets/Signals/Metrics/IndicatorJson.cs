using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VRFatigue.Signals
{
    public static class IndicatorJson
    {
        public static string Serialize(IndicatorResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            var policy = result.Policy;
            var terms = new JObject();
            foreach (string key in BaselinePolicy.MetricNames)
                terms[key] = new JObject { ["weight"] = policy.Weights[key], ["direction"] = policy.Directions[key] };
            return new JObject {
                ["schema_version"] = "signal-indicator.v1", ["method_version"] = IndicatorPolicy.MethodVersion,
                ["experimental"] = true, ["interpretation"] = "baseline_deviation",
                ["session_id"] = result.Comparison.Baseline.Info.SessionId,
                ["data_source"] = result.Comparison.Baseline.Info.DataSource,
                ["replay_origin"] = Text(result.Comparison.Baseline.Info.ReplayOrigin),
                ["baseline_low_confidence"] = result.Comparison.Baseline.LowConfidence,
                ["baseline_low_confidence_reasons"] = new JArray(result.Comparison.Baseline.LowConfidenceReasons),
                ["policy"] = new JObject {
                    ["version"] = policy.Version, ["expected_window_us"] = IndicatorPolicy.ExpectedWindowUs,
                    ["expected_z_clip"] = IndicatorPolicy.ExpectedZClip, ["alpha"] = policy.Alpha,
                    ["normalization_scale"] = policy.NormalizationScale, ["low_threshold"] = policy.LowThreshold,
                    ["high_threshold"] = policy.HighThreshold, ["release_threshold"] = policy.ReleaseThreshold,
                    ["required_high_windows"] = policy.RequiredHighWindows, ["total_weight"] = policy.TotalWeight, ["terms"] = terms },
                ["baseline_comparison"] = JObject.Parse(BaselineJson.Serialize(result.Comparison)),
                ["windows"] = new JArray(result.Windows.Select(Window))
            }.ToString(Formatting.Indented);
        }

        private static JObject Window(IndicatorWindow point)
        {
            var terms = new JObject();
            foreach (var pair in point.Contributions)
            {
                var t = pair.Value;
                terms[pair.Key] = new JObject { ["weight"] = t.Weight, ["normalized_weight"] = t.NormalizedWeight,
                    ["direction"] = t.Direction, ["status"] = t.Status, ["z_score"] = Number(t.ZScore),
                    ["contribution"] = Number(t.Contribution), ["unavailable_reason"] = Text(t.UnavailableReason) };
            }
            var s = point.State;
            return new JObject {
                ["index"] = point.Index, ["start_timestamp_us"] = s.StartUs, ["end_timestamp_us"] = s.EndUs,
                ["signed_combination"] = Number(point.SignedCombination), ["raw_value"] = Number(s.RawValue), ["value"] = Number(s.Value),
                ["level"] = s.Level, ["previous_level"] = s.PreviousLevel, ["level_changed"] = s.LevelChanged,
                ["nudge_active"] = s.NudgeActive, ["nudge_event"] = s.NudgeEvent, ["consecutive_high_windows"] = s.ConsecutiveHighWindows,
                ["state_reset"] = s.StateReset, ["reset_reason"] = Text(s.ResetReason),
                ["unavailable_reason"] = Text(s.UnavailableReason), ["missing_metrics"] = new JArray(point.MissingMetrics), ["contributions"] = terms };
        }
        private static JToken Number(double? value) { return value.HasValue ? new JValue(value.Value) : JValue.CreateNull(); }
        private static JToken Text(string value) { return value == null ? JValue.CreateNull() : new JValue(value); }
    }
}
