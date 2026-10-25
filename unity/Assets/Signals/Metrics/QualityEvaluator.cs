using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace VRFatigue.Signals
{
    public sealed class QualityPolicy
    {
        public string Version { get; private set; }
        public long MaximumGapUs { get; private set; }
        public long MinimumValidDurationUs { get; private set; }
        public double MinimumCoverage { get; private set; }
        public int MinimumValidSamples { get; private set; }
        public static QualityPolicy PrototypeV1 { get { return new QualityPolicy("quality-policy.v1", 100000, 1000000, 0.8, 2); } }
        public QualityPolicy(string version, long maximumGapUs, long minimumValidDurationUs, double minimumCoverage, int minimumValidSamples)
        {
            if (string.IsNullOrWhiteSpace(version) || maximumGapUs <= 0 || minimumValidDurationUs <= 0 ||
                !Direction3.Finite(minimumCoverage) || minimumCoverage <= 0 || minimumCoverage > 1 || minimumValidSamples < 2)
                throw new ArgumentException("Invalid quality policy.");
            Version = version; MaximumGapUs = maximumGapUs; MinimumValidDurationUs = minimumValidDurationUs;
            MinimumCoverage = minimumCoverage; MinimumValidSamples = minimumValidSamples;
        }
    }

    public sealed class ValidInterval
    {
        public long StartUs { get; private set; }
        public long EndUs { get; private set; }
        internal ValidInterval(long startUs, long endUs) { StartUs = startUs; EndUs = endUs; }
    }

    public sealed class QualityResult
    {
        public SessionInfo Info { get; internal set; }
        public QualityPolicy Policy { get; internal set; }
        public string Status { get; internal set; }
        public double ValidCoverage { get; internal set; }
        public long ValidDurationUs { get; internal set; }
        public long GapDurationUs { get; internal set; }
        public long LongestGapUs { get; internal set; }
        public int InputSamples { get; internal set; }
        public int AcceptedSamples { get; internal set; }
        public int ValidGazeSamples { get; internal set; }
        public int InvalidGazeSamples { get; internal set; }
        public int DuplicateTimestamps { get; internal set; }
        public int OutOfOrderTimestamps { get; internal set; }
        public int OutOfBoundsTimestamps { get; internal set; }
        public ReadOnlyCollection<string> Reasons { get; internal set; }
        // Retain invalid rows as barriers: later metrics must never bridge them.
        public ReadOnlyCollection<EyeSample> AcceptedStream { get; internal set; }
        public ReadOnlyCollection<ValidInterval> ValidIntervals { get; internal set; }
    }

    public static class QualityEvaluator
    {
        public static QualityResult Evaluate(SessionInfo info, IEnumerable<EyeSample> samples, QualityPolicy policy = null)
        {
            if (info == null || samples == null) throw new ArgumentNullException();
            policy = policy ?? QualityPolicy.PrototypeV1;
            var result = new QualityResult { Info = info, Policy = policy };
            var accepted = new List<EyeSample>();
            var intervals = new List<ValidInterval>();
            var seen = new HashSet<long>();
            EyeSample previous = null;
            bool previousUsable = false;
            foreach (var sample in samples)
            {
                if (sample == null) throw new ArgumentException("Null sample.");
                result.InputSamples++;
                if (sample.TimestampUs < info.StartTimestampUs || sample.TimestampUs > info.EndTimestampUs)
                { result.OutOfBoundsTimestamps++; continue; }
                if (!seen.Add(sample.TimestampUs)) { result.DuplicateTimestamps++; continue; }
                if (previous != null && sample.TimestampUs < previous.TimestampUs) { result.OutOfOrderTimestamps++; continue; }
                accepted.Add(sample);
                bool usable = info.Capabilities.Gaze.Available && sample.HasUsableGaze;
                if (usable) result.ValidGazeSamples++; else result.InvalidGazeSamples++;
                if (previous != null && previousUsable && usable && sample.TimestampUs - previous.TimestampUs <= policy.MaximumGapUs)
                {
                    long start = previous.TimestampUs, end = sample.TimestampUs;
                    result.ValidDurationUs += end - start;
                    if (intervals.Count > 0 && intervals[intervals.Count - 1].EndUs == start)
                    {
                        start = intervals[intervals.Count - 1].StartUs;
                        intervals.RemoveAt(intervals.Count - 1);
                    }
                    intervals.Add(new ValidInterval(start, end));
                }
                previous = sample; previousUsable = usable;
            }
            result.AcceptedSamples = accepted.Count;
            result.AcceptedStream = accepted.AsReadOnly();
            result.ValidIntervals = intervals.AsReadOnly();
            result.GapDurationUs = info.DurationUs - result.ValidDurationUs;
            result.ValidCoverage = info.DurationUs == 0 ? 0 : (double)result.ValidDurationUs / info.DurationUs;
            long cursor = info.StartTimestampUs;
            foreach (var interval in intervals)
            { result.LongestGapUs = Math.Max(result.LongestGapUs, interval.StartUs - cursor); cursor = interval.EndUs; }
            result.LongestGapUs = Math.Max(result.LongestGapUs, info.EndTimestampUs - cursor);
            var reasons = new List<string>();
            if (!info.Capabilities.Gaze.Available) reasons.Add("gaze_unavailable");
            if (info.DurationUs == 0) reasons.Add("zero_duration");
            if (result.InputSamples == 0) reasons.Add("no_samples");
            if (result.ValidGazeSamples < policy.MinimumValidSamples) reasons.Add("too_few_valid_samples");
            if (result.ValidDurationUs < policy.MinimumValidDurationUs) reasons.Add("too_short");
            if (result.ValidCoverage < policy.MinimumCoverage) reasons.Add("low_coverage");
            result.Reasons = reasons.AsReadOnly();
            result.Status = reasons.Count == 0 ? "usable" : "insufficient_data";
            return result;
        }
    }
}
