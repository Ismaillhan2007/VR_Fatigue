using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VRFatigue.Signals
{
    internal sealed class HardwareCounters
    {
        internal int FramePolls, FramesReceived, ReadFailures, Duplicates, OutOfOrder, QuantizedCollisions;
        internal int PreStartFrames, FutureFrames, PostEndFrames, BadTimestamps, UnalignedHeads, ClockResets, ClockFailures;
        internal readonly List<long> DeltasUs = new List<long>();
        internal readonly List<double> LeftConfidence = new List<double>(), RightConfidence = new List<double>();
        internal void ObserveConfidence(EyeReading left, EyeReading right)
        { Observe(left, LeftConfidence); Observe(right, RightConfidence); }
        private static void Observe(EyeReading reading, List<double> values)
        {
            if (reading != null && reading.Confidence.HasValue && Direction3.Finite(reading.Confidence.Value) && reading.Confidence.Value >= 0 && reading.Confidence.Value <= 1)
                values.Add(reading.Confidence.Value);
        }
    }

    internal static class HardwareDiagnostics
    {
        internal static string Serialize(EyeSession session, HardwareCaptureOptions options, HardwareCounters count,
            string state, string reason, string errorType, string timestampSource, string sdk, bool consent, bool calibration)
        {
            var quality = QualityEvaluator.Evaluate(session.Info, session.Samples);
            var sorted = count.DeltasUs.OrderBy(x => x).ToArray();
            double? median = sorted.Length == 0 ? (double?)null : sorted.Length % 2 == 0 ?
                (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2000.0 : sorted[sorted.Length / 2] / 1000.0;
            return new JObject {
                ["schema_version"] = "hardware-diagnostics.v1", ["session_id"] = session.Info.SessionId,
                ["data_source"] = session.Info.DataSource, ["timestamp_source"] = timestampSource,
                ["timestamp_semantics"] = "runtime_reported_unverified", ["sdk_package_version"] = sdk,
                ["capture_state"] = state, ["status_reason"] = reason,
                ["source_error_type"] = errorType == null ? JValue.CreateNull() : new JValue(errorType),
                ["consent_confirmed"] = consent, ["calibration_confirmed_by_operator"] = calibration,
                ["duration_ms"] = session.Info.DurationUs / 1000.0,
                ["recording_period_complete"] = session.Info.DataSource == "live" && session.Info.DurationUs >= 60000000,
                ["headset_review_required"] = true,
                ["capture_policy"] = new JObject { ["version"] = HardwareCaptureOptions.Version,
                    ["maximum_duration_us"] = options.MaximumDurationUs, ["maximum_samples"] = options.MaximumSamples,
                    ["maximum_probe_events"] = options.MaximumProbeEvents, ["head_alignment_tolerance_us"] = options.HeadAlignmentToleranceUs },
                ["counts"] = new JObject { ["frame_polls"] = count.FramePolls, ["frames_received"] = count.FramesReceived,
                    ["read_failures"] = count.ReadFailures, ["accepted_samples"] = session.Samples.Count,
                    ["duplicate_timestamps"] = count.Duplicates, ["out_of_order_timestamps"] = count.OutOfOrder,
                    ["quantized_collisions"] = count.QuantizedCollisions, ["pre_start_frames"] = count.PreStartFrames,
                    ["future_frames"] = count.FutureFrames, ["post_end_frames"] = count.PostEndFrames, ["invalid_timestamps"] = count.BadTimestamps,
                    ["unaligned_head_frames"] = count.UnalignedHeads, ["clock_resets"] = count.ClockResets, ["clock_failures"] = count.ClockFailures,
                    ["dt_count"] = sorted.Length },
                ["repeated_fraction"] = count.FramesReceived == 0 ? 0 : (double)count.Duplicates / count.FramesReceived,
                ["median_dt_ms"] = median.HasValue ? new JValue(median.Value) : JValue.CreateNull(),
                ["dt_histogram"] = new JObject { ["le_5_ms"] = sorted.Count(t => t <= 5000), ["gt_5_le_15_ms"] = sorted.Count(t => t > 5000 && t <= 15000),
                    ["gt_15_le_25_ms"] = sorted.Count(t => t > 15000 && t <= 25000), ["gt_25_le_100_ms"] = sorted.Count(t => t > 25000 && t <= 100000),
                    ["gt_100_ms"] = sorted.Count(t => t > 100000) },
                ["confidence"] = new JObject { ["used_as_filter"] = false, ["left"] = Confidence(count.LeftConfidence), ["right"] = Confidence(count.RightConfidence) },
                ["quality_report"] = JObject.Parse(SignalJson.SerializeQuality(quality))
            }.ToString(Formatting.Indented);
        }
        private static JObject Confidence(List<double> values)
        {
            return new JObject { ["count"] = values.Count, ["minimum"] = values.Count == 0 ? JValue.CreateNull() : new JValue(values.Min()),
                ["maximum"] = values.Count == 0 ? JValue.CreateNull() : new JValue(values.Max()),
                ["observed_variation"] = values.Count == 0 ? JValue.CreateNull() : new JValue(values.Max() - values.Min()) };
        }
    }
}
