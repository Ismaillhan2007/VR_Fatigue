using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VRFatigue.Signals
{
    public static class MetricsJson
    {
        public static string Serialize(MetricsResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            var quality = JObject.Parse(SignalJson.SerializeQuality(result.Quality));
            var metrics = new JObject();
            var reasons = new JObject();
            foreach (var item in result.Metrics)
            { metrics[item.Key] = Number(item.Value.Value); reasons[item.Key] = Text(item.Value.UnavailableReason); }
            var probes = new JArray(result.Probes.Select(p => new JObject
            {
                ["probe_id"] = p.ProbeId, ["outcome"] = p.Outcome,
                ["latency_ms"] = Number(p.LatencyMs), ["acquisition_unavailable_reason"] = Text(p.AcquisitionUnavailableReason),
                ["hold_rms_error_deg"] = Number(p.HoldRmsErrorDeg), ["hold_unavailable_reason"] = Text(p.HoldUnavailableReason),
                ["acquisition_window_ms"] = Number(p.AcquisitionWindowMs), ["acquisition_valid_duration_ms"] = Number(p.AcquisitionValidDurationMs),
                ["hold_duration_ms"] = Number(p.HoldDurationMs), ["hold_valid_duration_ms"] = Number(p.HoldValidDurationMs)
            }));
            return new JObject
            {
                ["schema_version"] = "signal-metrics.v1", ["metric_version"] = MetricPolicy.AlgorithmVersion,
                ["session_id"] = result.Quality.Info.SessionId, ["data_source"] = result.Quality.Info.DataSource,
                ["replay_origin"] = Text(result.Quality.Info.ReplayOrigin), ["coordinate_frame"] = "head_local",
                ["selected_eye"] = result.SelectedEye,
                ["duration_ms"] = result.Quality.Info.DurationUs / 1000.0,
                ["quality_policy"] = quality["policy"], ["quality"] = quality["quality"], ["capabilities"] = quality["capabilities"],
                ["metric_policy"] = new JObject
                {
                    ["version"] = result.Policy.Version, ["gaze_eye"] = result.Policy.GazeEye,
                    ["smoothing_window"] = result.Policy.SmoothingWindow, ["smoothing_order"] = 2,
                    ["probe_tolerance_deg"] = result.Policy.ProbeToleranceDeg,
                    ["acquisition_deadline_us"] = result.Policy.AcquisitionDeadlineUs,
                    ["minimum_hold_coverage"] = result.Policy.MinimumHoldCoverage,
                    ["minimum_hold_duration_us"] = result.Policy.MinimumHoldDurationUs,
                    ["minimum_target_separation_deg"] = result.Policy.MinimumTargetSeparationDeg
                },
                ["metrics"] = metrics, ["unavailable_reasons"] = reasons,
                ["support"] = new JObject
                {
                    ["gaze_valid_duration_ms"] = result.GazeValidDurationUs / 1000.0,
                    ["gaze_speed_duration_ms"] = result.GazeSpeedDurationUs / 1000.0,
                    ["gaze_speed_interval_count"] = result.GazeSpeedIntervalCount,
                    ["head_speed_duration_ms"] = result.HeadSpeedDurationUs / 1000.0,
                    ["head_speed_interval_count"] = result.HeadSpeedIntervalCount,
                    ["probe_count"] = result.Probes.Count,
                    ["acquired_probe_count"] = result.Probes.Count(p => p.Outcome == "acquired"),
                    ["missed_probe_count"] = result.Probes.Count(p => p.Outcome == "missed"),
                    ["excluded_probe_count"] = result.Probes.Count(p => p.Outcome == "unavailable")
                },
                ["probes"] = probes
            }.ToString(Formatting.Indented);
        }
        private static JToken Number(double? value) { return value.HasValue ? new JValue(value.Value) : JValue.CreateNull(); }
        private static JToken Text(string value) { return value == null ? JValue.CreateNull() : new JValue(value); }
    }
}
