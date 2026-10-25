using System;
using System.Collections.Generic;
using System.Linq;

namespace VRFatigue.Signals
{
    internal static class ProbeEvaluator
    {
        private sealed class Definition
        {
            internal string Id, Error;
            internal long Onset, Hold, End;
            internal Direction3 Target;
        }

        private static List<Definition> Definitions(ProbeEvent[] events, MetricPolicy policy)
        {
            var definitions = new List<Definition>();
            foreach (var group in events.GroupBy(p => p.ProbeId))
            {
                var items = group.ToArray();
                var d = new Definition { Id = group.Key };
                definitions.Add(d);
                if (items.Length != 3 || items[0].Kind != "target_onset" || items[1].Kind != "hold_start" || items[2].Kind != "target_end" ||
                    items[0].TimestampUs >= items[1].TimestampUs || items[1].TimestampUs >= items[2].TimestampUs)
                { d.Error = "invalid_probe_sequence"; continue; }
                d.Onset = items[0].TimestampUs; d.Hold = items[1].TimestampUs; d.End = items[2].TimestampUs;
                SignalMath.Normalize(items[0].TargetDirection, out d.Target);
                foreach (var item in items.Skip(1))
                {
                    Direction3 target;
                    SignalMath.Normalize(item.TargetDirection, out target);
                    if (SignalMath.Angle(d.Target, target) > 1e-6) d.Error = "moving_target_unsupported";
                }
            }
            var valid = definitions.Where(d => d.Error == null).OrderBy(d => d.Onset).ToArray();
            for (int i = 0; i < valid.Length; i++)
            for (int j = i + 1; j < valid.Length && valid[j].Onset < valid[i].End; j++)
            { valid[i].Error = "overlapping_probes"; valid[j].Error = "overlapping_probes"; }
            for (int i = 1; i < valid.Length; i++)
                if (valid[i].Error == null && SignalMath.Angle(valid[i - 1].Target, valid[i].Target) + 1e-9 < policy.MinimumTargetSeparationDeg)
                    valid[i].Error = "targets_too_close";

            return definitions;
        }

        internal static Dictionary<string, string> ValidationErrors(ProbeEvent[] events, MetricPolicy policy)
        { return Definitions(events, policy).Where(d => d.Error != null).ToDictionary(d => d.Id, d => d.Error); }

        internal static List<ProbeMeasurement> Evaluate(ProbeEvent[] events, List<List<GazePoint>> segments,
            MetricPolicy policy, string gazeReason, IDictionary<string, string> contextErrors = null)
        {
            var results = new List<ProbeMeasurement>();
            var definitions = Definitions(events, policy);
            foreach (var d in definitions)
            {
                var result = new ProbeMeasurement { ProbeId = d.Id, Outcome = "unavailable" };
                results.Add(result);
                string context = null;
                if (contextErrors != null) contextErrors.TryGetValue(d.Id, out context);
                string unavailable = d.Error ?? context ?? gazeReason;
                if (unavailable != null)
                { result.AcquisitionUnavailableReason = unavailable; result.HoldUnavailableReason = unavailable; continue; }
                long window = Math.Min(policy.AcquisitionDeadlineUs, d.End - d.Onset);
                long deadline = d.Onset + window;
                var firstHit = segments.SelectMany(s => s).FirstOrDefault(p => p.Time >= d.Onset && p.Time <= deadline &&
                    SignalMath.Angle(p.Direction, d.Target) <= policy.ProbeToleranceDeg + 1e-9);
                long searchEnd = firstHit == null ? deadline : firstHit.Time;
                long observed = Coverage(segments, d.Onset, searchEnd);
                result.AcquisitionWindowMs = (searchEnd - d.Onset) / 1000.0;
                result.AcquisitionValidDurationMs = observed / 1000.0;
                if (observed != searchEnd - d.Onset)
                    result.AcquisitionUnavailableReason = "acquisition_tracking_loss";
                else if (firstHit != null)
                { result.Outcome = "acquired"; result.LatencyMs = (firstHit.Time - d.Onset) / 1000.0; }
                else if (window < policy.AcquisitionDeadlineUs)
                    result.AcquisitionUnavailableReason = "probe_too_short";
                else result.Outcome = "missed";

                long holdDuration = d.End - d.Hold;
                result.HoldDurationMs = holdDuration / 1000.0;
                result.HoldValidDurationUs = Coverage(segments, d.Hold, d.End);
                result.HoldValidDurationMs = result.HoldValidDurationUs / 1000.0;
                if (result.HoldValidDurationUs < policy.MinimumHoldDurationUs)
                    result.HoldUnavailableReason = "hold_too_short";
                else if ((double)result.HoldValidDurationUs / holdDuration < policy.MinimumHoldCoverage)
                    result.HoldUnavailableReason = "insufficient_hold_coverage";
                else
                {
                    // Trapezoidal integral of squared observed angular errors; clip within valid intervals only.
                    double integral = 0;
                    foreach (var segment in segments)
                    for (int i = 1; i < segment.Count; i++)
                    {
                        var a = segment[i - 1]; var b = segment[i];
                        long start = Math.Max(d.Hold, a.Time), end = Math.Min(d.End, b.Time);
                        if (end <= start) continue;
                        double ea = SignalMath.Angle(a.Direction, d.Target), eb = SignalMath.Angle(b.Direction, d.Target);
                        double squaredA = ea * ea, squaredB = eb * eb;
                        double atStart = squaredA + (squaredB - squaredA) * ((double)(start - a.Time) / (b.Time - a.Time));
                        double atEnd = squaredA + (squaredB - squaredA) * ((double)(end - a.Time) / (b.Time - a.Time));
                        integral += (atStart + atEnd) / 2 * (end - start);
                    }
                    result.HoldSquaredErrorIntegral = integral;
                    result.HoldRmsErrorDeg = Math.Sqrt(integral / result.HoldValidDurationUs);
                }
            }
            return results;
        }

        private static long Coverage(List<List<GazePoint>> segments, long start, long end)
        {
            long duration = 0;
            foreach (var segment in segments)
            {
                long a = Math.Max(start, segment[0].Time), b = Math.Min(end, segment[segment.Count - 1].Time);
                if (b > a) duration += b - a;
            }
            return duration;
        }
    }
}
