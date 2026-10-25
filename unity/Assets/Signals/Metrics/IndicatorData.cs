using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace VRFatigue.Signals
{
    public sealed class IndicatorPolicy
    {
        public const string MethodVersion = "experimental-indicator.v1";
        public const long ExpectedWindowUs = 20000000;
        public const double ExpectedZClip = 4;
        public string Version { get; private set; }
        public double Alpha { get; private set; }
        public double NormalizationScale { get; private set; }
        public double LowThreshold { get; private set; }
        public double HighThreshold { get; private set; }
        public double ReleaseThreshold { get; private set; }
        public int RequiredHighWindows { get; private set; }
        public double TotalWeight { get; private set; }
        public ReadOnlyDictionary<string, double> Weights { get; private set; }
        public ReadOnlyDictionary<string, int> Directions { get; private set; }

        public IndicatorPolicy(string version = "indicator-policy.v1", double alpha = 0.3, double normalizationScale = 3,
            double lowThreshold = 0.35, double highThreshold = 0.6, double releaseThreshold = 0.4,
            int requiredHighWindows = 3, IDictionary<string, double> weights = null)
        {
            var defaults = new Dictionary<string, double> {
                ["gaze_speed_median_deg_s"] = 0, ["gaze_speed_p95_deg_s"] = 1,
                ["gaze_dispersion_deg"] = 0, ["head_speed_deg_s"] = 0,
                ["probe_latency_ms"] = 3, ["probe_rms_error_deg"] = 2, ["probe_miss_fraction"] = 2 };
            var directions = new Dictionary<string, int> {
                ["gaze_speed_median_deg_s"] = 0, ["gaze_speed_p95_deg_s"] = -1,
                ["gaze_dispersion_deg"] = 0, ["head_speed_deg_s"] = 0,
                ["probe_latency_ms"] = 1, ["probe_rms_error_deg"] = 1, ["probe_miss_fraction"] = 1 };
            var selected = weights == null ? defaults : new Dictionary<string, double>(weights);
            if (string.IsNullOrWhiteSpace(version) || !Finite(alpha, normalizationScale, lowThreshold, highThreshold, releaseThreshold) ||
                alpha <= 0 || alpha > 1 || normalizationScale <= 0 || lowThreshold <= 0 || highThreshold >= 1 || lowThreshold >= highThreshold ||
                releaseThreshold < 0 || releaseThreshold >= highThreshold || requiredHighWindows < 1 || requiredHighWindows > 1000 ||
                selected.Count != BaselinePolicy.MetricNames.Count || BaselinePolicy.MetricNames.Any(k => !selected.ContainsKey(k)) ||
                selected.Any(p => !Direction3.Finite(p.Value) || (directions[p.Key] == 0 ? p.Value != 0 : p.Value <= 0)))
                throw new ArgumentException("Invalid indicator policy. Four required signed metrics and three disabled context metrics are fixed in v1.");
            double total = selected.Values.Sum();
            if (!Direction3.Finite(total) || total <= 0) throw new ArgumentException("Indicator weight sum must be finite and positive.");
            Version = version; Alpha = alpha; NormalizationScale = normalizationScale;
            LowThreshold = lowThreshold; HighThreshold = highThreshold; ReleaseThreshold = releaseThreshold;
            RequiredHighWindows = requiredHighWindows; TotalWeight = total;
            Weights = new ReadOnlyDictionary<string, double>(selected);
            Directions = new ReadOnlyDictionary<string, int>(directions);
        }
        private static bool Finite(params double[] values) { return values.All(Direction3.Finite); }
    }

    public sealed class IndicatorState
    {
        public long StartUs { get; internal set; }
        public long EndUs { get; internal set; }
        public double? RawValue { get; internal set; }
        public double? Value { get; internal set; }
        public string Level { get; internal set; }
        public string PreviousLevel { get; internal set; }
        public bool LevelChanged { get { return Level != PreviousLevel; } }
        public bool NudgeActive { get; internal set; }
        public string NudgeEvent { get; internal set; }
        public int ConsecutiveHighWindows { get; internal set; }
        public bool StateReset { get; internal set; }
        public string ResetReason { get; internal set; }
        public string UnavailableReason { get; internal set; }
    }

    public sealed class IndicatorContribution
    {
        public double Weight { get; internal set; }
        public double NormalizedWeight { get; internal set; }
        public int Direction { get; internal set; }
        public string Status { get; internal set; }
        public double? ZScore { get; internal set; }
        public double? Contribution { get; internal set; }
        public string UnavailableReason { get; internal set; }
    }

    public sealed class IndicatorWindow
    {
        public int Index { get; internal set; }
        public double? SignedCombination { get; internal set; }
        public IndicatorState State { get; internal set; }
        public ReadOnlyCollection<string> MissingMetrics { get; internal set; }
        public ReadOnlyDictionary<string, IndicatorContribution> Contributions { get; internal set; }
    }

    public sealed class IndicatorResult
    {
        public bool Experimental { get { return true; } }
        public string Interpretation { get { return "baseline_deviation"; } }
        public IndicatorPolicy Policy { get; internal set; }
        public BaselineComparisonResult Comparison { get; internal set; }
        public ReadOnlyCollection<IndicatorWindow> Windows { get; internal set; }
    }
}
