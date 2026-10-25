using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace VRFatigue.Signals
{
    public static class MetricsEvaluator
    {
        public static MetricsResult Evaluate(QualityResult quality, IEnumerable<ProbeEvent> probeEvents, MetricPolicy policy = null)
        { return EvaluateWithProbeContext(quality, probeEvents, policy ?? new MetricPolicy(), null); }

        internal static MetricsResult EvaluateWithProbeContext(QualityResult quality, IEnumerable<ProbeEvent> probeEvents,
            MetricPolicy policy, IDictionary<string, string> probeErrors)
        {
            if (quality == null || probeEvents == null) throw new ArgumentNullException();
            policy = policy ?? new MetricPolicy();
            var events = probeEvents.ToArray();
            long previous = -1;
            foreach (var p in events)
            {
                if (p == null || p.TimestampUs < previous || p.TimestampUs < quality.Info.StartTimestampUs || p.TimestampUs > quality.Info.EndTimestampUs)
                    throw new ArgumentException("Probe events must be chronological and within session bounds.");
                previous = p.TimestampUs;
            }
            var left = Segments(quality, true);
            var right = Segments(quality, false);
            string eye = policy.GazeEye == "auto" ? (Duration(left) >= Duration(right) ? "left" : "right") : policy.GazeEye;
            var segments = eye == "left" ? left : right;
            var result = new MetricsResult { Quality = quality, Policy = policy, SelectedEye = eye, GazeValidDurationUs = Duration(segments) };
            var readings = new Dictionary<string, MetricReading>();
            string gazeReason = quality.Status != "usable" ? "insufficient_data" :
                result.GazeValidDurationUs == 0 ? "no_continuous_gaze" :
                (double)result.GazeValidDurationUs / quality.Info.DurationUs < quality.Policy.MinimumCoverage ? "insufficient_eye_coverage" :
                result.GazeValidDurationUs < quality.Policy.MinimumValidDurationUs ? "insufficient_eye_duration" : null;
            var speeds = new List<WeightedValue>();
            if (gazeReason == null)
            {
                foreach (var segment in segments.Where(s => s.Count >= policy.SmoothingWindow))
                {
                    var smoothed = Enumerable.Range(0, segment.Count).Select(i => SignalMath.Smooth(segment, i, policy.SmoothingWindow)).ToArray();
                    for (int i = 1; i < segment.Count; i++)
                    {
                        if (!smoothed[i - 1].HasValue || !smoothed[i].HasValue) continue;
                        long dt = segment[i].Time - segment[i - 1].Time;
                        speeds.Add(new WeightedValue(SignalMath.Angle(smoothed[i - 1].Value, smoothed[i].Value) * 1000000 / dt, dt));
                    }
                }
            }
            result.GazeSpeedDurationUs = speeds.Sum(s => s.DurationUs);
            result.GazeSpeedIntervalCount = speeds.Count;
            string speedReason = gazeReason ?? (speeds.Count == 0 ? "insufficient_smoothing_support" :
                (double)result.GazeSpeedDurationUs / quality.Info.DurationUs < quality.Policy.MinimumCoverage ? "insufficient_smoothing_coverage" :
                result.GazeSpeedDurationUs < quality.Policy.MinimumValidDurationUs ? "insufficient_smoothing_duration" : null);
            readings["gaze_speed_median_deg_s"] = speedReason == null ? MetricReading.Measured(SignalMath.Quantile(speeds, 0.5)) : MetricReading.Unavailable(speedReason);
            readings["gaze_speed_p95_deg_s"] = speedReason == null ? MetricReading.Measured(SignalMath.Quantile(speeds, 0.95)) : MetricReading.Unavailable(speedReason);
            readings["gaze_dispersion_deg"] = gazeReason == null ? Dispersion(segments, result.GazeValidDurationUs) : MetricReading.Unavailable(gazeReason);

            var head = new List<WeightedValue>();
            if (quality.Status == "usable")
            for (int i = 1; i < quality.AcceptedStream.Count; i++)
            {
                var a = quality.AcceptedStream[i - 1]; var b = quality.AcceptedStream[i];
                long dt = b.TimestampUs - a.TimestampUs;
                double[] qa, qb;
                if (dt > quality.Policy.MaximumGapUs || !SignalMath.Quaternion(a.Head, out qa) || !SignalMath.Quaternion(b.Head, out qb)) continue;
                head.Add(new WeightedValue(SignalMath.HeadAngle(qa, qb) * 1000000 / dt, dt));
            }
            result.HeadSpeedDurationUs = head.Sum(s => s.DurationUs); result.HeadSpeedIntervalCount = head.Count;
            string headReason = quality.Status != "usable" ? "insufficient_data" : head.Count == 0 ? "no_head_pose_intervals" :
                (double)result.HeadSpeedDurationUs / quality.Info.DurationUs < quality.Policy.MinimumCoverage ? "insufficient_head_coverage" :
                result.HeadSpeedDurationUs < quality.Policy.MinimumValidDurationUs ? "insufficient_head_duration" : null;
            readings["head_speed_deg_s"] = headReason == null ? MetricReading.Measured(SignalMath.Quantile(head, 0.5)) : MetricReading.Unavailable(headReason);

            var probes = ProbeEvaluator.Evaluate(events, segments, policy, gazeReason, probeErrors);
            result.Probes = probes.AsReadOnly();
            var acquired = probes.Where(p => p.Outcome == "acquired").ToArray();
            int evaluable = probes.Count(p => p.Outcome != "unavailable"), missed = probes.Count(p => p.Outcome == "missed");
            string noProbe = gazeReason ?? (probes.Count == 0 ? "no_probes" : "no_evaluable_probes");
            readings["probe_latency_ms"] = acquired.Length == 0 ? MetricReading.Unavailable(gazeReason ?? (probes.Count == 0 ? "no_probes" : "no_acquired_probes")) :
                MetricReading.Measured(SignalMath.Median(acquired.Select(p => p.LatencyMs.Value)));
            readings["probe_miss_fraction"] = evaluable == 0 ? MetricReading.Unavailable(noProbe) : MetricReading.Measured((double)missed / evaluable);
            var holds = probes.Where(p => p.HoldRmsErrorDeg.HasValue).ToArray();
            readings["probe_rms_error_deg"] = holds.Length == 0 ? MetricReading.Unavailable(gazeReason ?? (probes.Count == 0 ? "no_probes" : "no_usable_hold")) :
                MetricReading.Measured(Math.Sqrt(holds.Sum(p => p.HoldSquaredErrorIntegral) / holds.Sum(p => (double)p.HoldValidDurationUs)));
            result.Metrics = new ReadOnlyDictionary<string, MetricReading>(readings);
            return result;
        }

        internal static string ChooseEye(QualityResult quality, MetricPolicy policy)
        { return policy.GazeEye == "auto" ? (Duration(Segments(quality, true)) >= Duration(Segments(quality, false)) ? "left" : "right") : policy.GazeEye; }

        private static List<List<GazePoint>> Segments(QualityResult quality, bool left)
        {
            var segments = new List<List<GazePoint>>();
            List<GazePoint> current = null;
            foreach (var sample in quality.AcceptedStream)
            {
                var eye = left ? sample.Left : sample.Right;
                Direction3 normalized = new Direction3();
                if (!quality.Info.Capabilities.Gaze.Available || eye == null || !eye.Valid || !eye.Direction.HasValue || !SignalMath.Normalize(eye.Direction.Value, out normalized))
                { current = null; continue; }
                if (current == null || sample.TimestampUs - current[current.Count - 1].Time > quality.Policy.MaximumGapUs)
                { current = new List<GazePoint>(); segments.Add(current); }
                current.Add(new GazePoint(sample.TimestampUs, normalized));
            }
            return segments;
        }
        private static long Duration(IEnumerable<List<GazePoint>> segments) { return segments.Sum(s => s[s.Count - 1].Time - s[0].Time); }
        private static MetricReading Dispersion(List<List<GazePoint>> segments, long duration)
        {
            double x = 0, y = 0, z = 0;
            foreach (var segment in segments)
            for (int i = 1; i < segment.Count; i++)
            {
                var a = segment[i - 1]; var b = segment[i];
                double weight = (double)(b.Time - a.Time) / duration / 2;
                x += (a.Direction.X + b.Direction.X) * weight;
                y += (a.Direction.Y + b.Direction.Y) * weight;
                z += (a.Direction.Z + b.Direction.Z) * weight;
            }
            if (x * x + y * y + z * z < 1e-18) return MetricReading.Unavailable("ambiguous_mean_direction");
            Direction3 mean;
            SignalMath.Normalize(new Direction3(x, y, z), out mean);
            double squared = 0;
            foreach (var segment in segments)
            for (int i = 1; i < segment.Count; i++)
            {
                double a = SignalMath.Angle(segment[i - 1].Direction, mean), b = SignalMath.Angle(segment[i].Direction, mean);
                squared += (a * a + b * b) / 2 * ((double)(segment[i].Time - segment[i - 1].Time) / duration);
            }
            return MetricReading.Measured(Math.Sqrt(squared));
        }
    }
}
