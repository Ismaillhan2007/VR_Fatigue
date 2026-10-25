using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VRFatigue.Signals
{
    public static class BaselineJson
    {
        public static BaselinePlan ParsePlan(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            try
            {
                var root = SignalJson.Fields(SignalJson.ReadObject(json), "schema_version", "session_id", "baseline", "comparison");
                if (SignalJson.Text(root["schema_version"]) != "baseline-plan.v1") throw new FormatException("Unsupported baseline plan version.");
                var baseline = SignalJson.Fields(root["baseline"], "start_timestamp_us", "end_timestamp_us", "conditions_id", "self_report");
                var comparison = SignalJson.Fields(root["comparison"], "start_timestamp_us", "end_timestamp_us", "conditions_id");
                BaselineSelfReport ratings = null;
                if (baseline["self_report"].Type != JTokenType.Null)
                {
                    var r = SignalJson.Fields(baseline["self_report"], "eye_strain", "mental_fatigue");
                    long eye = SignalJson.Integer(r["eye_strain"]), mind = SignalJson.Integer(r["mental_fatigue"]);
                    if (eye < 1 || eye > 7 || mind < 1 || mind > 7) throw new FormatException("Ratings must be 1..7.");
                    ratings = new BaselineSelfReport((int)eye, (int)mind);
                }
                return new BaselinePlan(SignalJson.Text(root["session_id"]), Phase(baseline), Phase(comparison), ratings);
            }
            catch (Exception error) when (error is JsonException || error is ArgumentException || error is OverflowException)
            { throw new FormatException("Invalid baseline-plan.v1 document: " + error.Message, error); }
        }

        public static string Serialize(BaselineComparisonResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            var baseline = result.Baseline;
            var sessionQuality = JObject.Parse(SignalJson.SerializeQuality(result.SessionQuality));
            var metricPolicy = new JObject {
                ["version"] = baseline.MetricPolicy.Version, ["gaze_eye"] = baseline.MetricPolicy.GazeEye,
                ["smoothing_window"] = baseline.MetricPolicy.SmoothingWindow, ["smoothing_order"] = 2,
                ["probe_tolerance_deg"] = baseline.MetricPolicy.ProbeToleranceDeg,
                ["acquisition_deadline_us"] = baseline.MetricPolicy.AcquisitionDeadlineUs,
                ["minimum_hold_coverage"] = baseline.MetricPolicy.MinimumHoldCoverage,
                ["minimum_hold_duration_us"] = baseline.MetricPolicy.MinimumHoldDurationUs,
                ["minimum_target_separation_deg"] = baseline.MetricPolicy.MinimumTargetSeparationDeg };
            var floors = new JObject(); var references = new JObject();
            foreach (string key in BaselinePolicy.MetricNames)
            {
                floors[key] = baseline.Policy.Floors[key];
                var r = baseline.References[key];
                references[key] = new JObject { ["median"] = Number(r.Median), ["mad"] = Number(r.Mad),
                    ["scale"] = Number(r.Scale), ["floor"] = r.Floor, ["usable_window_count"] = r.UsableWindowCount,
                    ["unavailable_reason"] = Text(r.UnavailableReason) };
            }
            var baselineJson = PhaseJson(baseline.Phase);
            baselineJson["self_report"] = baseline.SelfReport == null ? JValue.CreateNull() : (JToken)new JObject {
                ["eye_strain"] = baseline.SelfReport.EyeStrain, ["mental_fatigue"] = baseline.SelfReport.MentalFatigue };
            baselineJson["status"] = baseline.Status; baselineJson["low_confidence"] = baseline.LowConfidence;
            baselineJson["low_confidence_reasons"] = new JArray(baseline.LowConfidenceReasons);
            baselineJson["complete_window_count"] = baseline.Windows.Count(w => w.Complete);
            baselineJson["usable_window_count"] = baseline.Windows.Count(w => w.Eligible);
            baselineJson["selected_eye_valid_duration_ms"] = baseline.Windows.Sum(w => w.Measurements.GazeValidDurationUs) / 1000.0;
            baselineJson["references"] = references;
            baselineJson["windows"] = new JArray(baseline.Windows.Select(w => WindowJson(w, false)));
            var comparison = PhaseJson(result.Phase);
            comparison["status"] = result.Status; comparison["unavailable_reason"] = Text(result.UnavailableReason);
            comparison["windows"] = new JArray(result.Windows.Select(w => WindowJson(w, true)));
            return new JObject {
                ["schema_version"] = "baseline-comparison.v1", ["method_version"] = BaselinePolicy.MethodVersion,
                ["metric_version"] = MetricPolicy.AlgorithmVersion, ["session_id"] = baseline.Info.SessionId,
                ["data_source"] = baseline.Info.DataSource, ["replay_origin"] = Text(baseline.Info.ReplayOrigin),
                ["coordinate_frame"] = "head_local", ["reference_kind"] = "session_start", ["selected_eye"] = baseline.SelectedEye,
                ["baseline_policy"] = new JObject { ["version"] = baseline.Policy.Version,
                    ["window_duration_us"] = baseline.Policy.WindowDurationUs, ["minimum_usable_windows"] = baseline.Policy.MinimumWindows,
                    ["maximum_windows_per_phase"] = BaselinePolicy.MaximumWindowsPerPhase,
                    ["mad_multiplier"] = BaselinePolicy.MadMultiplier, ["z_clip"] = baseline.Policy.ZClip, ["metric_floors"] = floors },
                ["quality_policy"] = sessionQuality["policy"].DeepClone(), ["metric_policy"] = metricPolicy,
                ["session_quality"] = sessionQuality, ["baseline"] = baselineJson, ["comparison"] = comparison
            }.ToString(Formatting.Indented);
        }

        private static AssessmentPhase Phase(JObject value)
        { return new AssessmentPhase(SignalJson.Integer(value["start_timestamp_us"]), SignalJson.Integer(value["end_timestamp_us"]), SignalJson.NullableText(value["conditions_id"])); }
        private static JObject PhaseJson(AssessmentPhase phase)
        { return new JObject { ["start_timestamp_us"] = phase.StartUs, ["end_timestamp_us"] = phase.EndUs, ["conditions_id"] = Text(phase.ConditionsId) }; }
        private static JObject WindowJson(MeasurementWindow window, bool includeDeviations)
        {
            var result = new JObject { ["index"] = window.Index, ["start_timestamp_us"] = window.StartUs, ["end_timestamp_us"] = window.EndUs,
                ["complete"] = window.Complete, ["eligible"] = window.Eligible, ["excluded_boundary_probe_count"] = window.BoundaryProbeCount,
                ["measurements"] = JObject.Parse(MetricsJson.Serialize(window.Measurements)) };
            if (includeDeviations)
            {
                var deviations = new JObject();
                foreach (var pair in window.Deviations)
                {
                    var d = pair.Value;
                    deviations[pair.Key] = new JObject { ["value"] = Number(d.Value), ["delta"] = Number(d.Delta),
                        ["z_score"] = Number(d.ZScore), ["clipped"] = d.Clipped.HasValue ? new JValue(d.Clipped.Value) : JValue.CreateNull(),
                        ["unavailable_reason"] = Text(d.UnavailableReason) };
                }
                result["deviations"] = deviations;
            }
            return result;
        }
        private static JToken Number(double? value) { return value.HasValue ? new JValue(value.Value) : JValue.CreateNull(); }
        private static JToken Text(string value) { return value == null ? JValue.CreateNull() : new JValue(value); }
    }
}
