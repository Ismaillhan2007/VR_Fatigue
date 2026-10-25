using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace VRFatigue.Signals
{
    public sealed class BaselinePolicy
    {
        public const string MethodVersion = "baseline-method.v1";
        public const double MadMultiplier = 1.4826;
        public const int MaximumWindowsPerPhase = 1000;
        public static readonly ReadOnlyCollection<string> MetricNames = Array.AsReadOnly(new[] {
            "gaze_speed_median_deg_s", "gaze_speed_p95_deg_s", "gaze_dispersion_deg", "head_speed_deg_s",
            "probe_latency_ms", "probe_rms_error_deg", "probe_miss_fraction" });
        public string Version { get; private set; }
        public long WindowDurationUs { get; private set; }
        public int MinimumWindows { get; private set; }
        public double ZClip { get; private set; }
        public ReadOnlyDictionary<string, double> Floors { get; private set; }
        public BaselinePolicy(string version = "baseline-policy.v1", long windowDurationUs = 20000000,
            int minimumWindows = 6, double zClip = 4, IDictionary<string, double> floors = null)
        {
            var defaults = new Dictionary<string, double> {
                ["gaze_speed_median_deg_s"] = 1, ["gaze_speed_p95_deg_s"] = 1,
                ["gaze_dispersion_deg"] = 0.1, ["head_speed_deg_s"] = 1,
                ["probe_latency_ms"] = 10, ["probe_rms_error_deg"] = 0.1, ["probe_miss_fraction"] = 0.05 };
            var selected = floors == null ? defaults : new Dictionary<string, double>(floors);
            if (string.IsNullOrWhiteSpace(version) || windowDurationUs <= 0 || minimumWindows < 6 || minimumWindows > MaximumWindowsPerPhase ||
                !Direction3.Finite(zClip) || zClip <= 0 || selected.Count != MetricNames.Count ||
                MetricNames.Any(k => !selected.ContainsKey(k)) || selected.Values.Any(v => !Direction3.Finite(v) || v <= 0))
                throw new ArgumentException("Invalid baseline policy.");
            Version = version; WindowDurationUs = windowDurationUs; MinimumWindows = minimumWindows; ZClip = zClip;
            Floors = new ReadOnlyDictionary<string, double>(selected);
        }
    }

    public sealed class BaselineSelfReport
    {
        public int EyeStrain { get; private set; }
        public int MentalFatigue { get; private set; }
        public BaselineSelfReport(int eyeStrain, int mentalFatigue)
        {
            if (eyeStrain < 1 || eyeStrain > 7 || mentalFatigue < 1 || mentalFatigue > 7) throw new ArgumentException("Ratings must be 1..7.");
            EyeStrain = eyeStrain; MentalFatigue = mentalFatigue;
        }
    }

    public sealed class AssessmentPhase
    {
        public long StartUs { get; private set; }
        public long EndUs { get; private set; }
        public string ConditionsId { get; private set; }
        public AssessmentPhase(long startUs, long endUs, string conditionsId)
        {
            if (startUs < 0 || endUs < startUs || (conditionsId != null &&
                (string.IsNullOrWhiteSpace(conditionsId) || conditionsId.Length > 64 || conditionsId.Any(c => !(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') && c != '_' && c != '-' && c != '.'))))
                throw new ArgumentException("Invalid phase bounds or conditions identifier.");
            StartUs = startUs; EndUs = endUs; ConditionsId = conditionsId;
        }
    }

    public sealed class BaselinePlan
    {
        public string SessionId { get; private set; }
        public AssessmentPhase Baseline { get; private set; }
        public AssessmentPhase Comparison { get; private set; }
        public BaselineSelfReport SelfReport { get; private set; }
        public BaselinePlan(string sessionId, AssessmentPhase baseline, AssessmentPhase comparison, BaselineSelfReport selfReport)
        {
            Guid parsed;
            if (!Guid.TryParseExact(sessionId, "D", out parsed)) throw new ArgumentException("session_id must be a UUID.");
            Baseline = baseline ?? throw new ArgumentNullException(nameof(baseline));
            Comparison = comparison ?? throw new ArgumentNullException(nameof(comparison));
            if (comparison.StartUs < baseline.EndUs) throw new ArgumentException("Comparison must follow baseline without overlap.");
            SessionId = sessionId; SelfReport = selfReport;
        }
    }

    public sealed class MetricReference
    {
        public double? Median { get; internal set; }
        public double? Mad { get; internal set; }
        public double? Scale { get; internal set; }
        public double Floor { get; internal set; }
        public int UsableWindowCount { get; internal set; }
        public string UnavailableReason { get; internal set; }
    }

    public sealed class MetricDeviation
    {
        public double? Value { get; internal set; }
        public double? Delta { get; internal set; }
        public double? ZScore { get; internal set; }
        public bool? Clipped { get; internal set; }
        public string UnavailableReason { get; internal set; }
    }

    public sealed class MeasurementWindow
    {
        public int Index { get; internal set; }
        public long StartUs { get; internal set; }
        public long EndUs { get; internal set; }
        public bool Complete { get; internal set; }
        public bool Eligible { get; internal set; }
        public int BoundaryProbeCount { get; internal set; }
        public MetricsResult Measurements { get; internal set; }
        public ReadOnlyDictionary<string, MetricDeviation> Deviations { get; internal set; }
    }

    public sealed class BaselineResult
    {
        public SessionInfo Info { get; internal set; }
        public AssessmentPhase Phase { get; internal set; }
        public BaselineSelfReport SelfReport { get; internal set; }
        public BaselinePolicy Policy { get; internal set; }
        public QualityPolicy QualityPolicy { get; internal set; }
        public MetricPolicy MetricPolicy { get; internal set; }
        public string SelectedEye { get; internal set; }
        public string Status { get; internal set; }
        public bool LowConfidence { get; internal set; }
        public ReadOnlyCollection<string> LowConfidenceReasons { get; internal set; }
        public ReadOnlyCollection<MeasurementWindow> Windows { get; internal set; }
        public ReadOnlyDictionary<string, MetricReference> References { get; internal set; }
    }

    public sealed class BaselineComparisonResult
    {
        public BaselineResult Baseline { get; internal set; }
        public AssessmentPhase Phase { get; internal set; }
        public QualityResult SessionQuality { get; internal set; }
        public string Status { get; internal set; }
        public string UnavailableReason { get; internal set; }
        public ReadOnlyCollection<MeasurementWindow> Windows { get; internal set; }
    }
}
