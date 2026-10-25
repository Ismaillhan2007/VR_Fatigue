using System.Collections.Generic;
using Newtonsoft.Json;

namespace App.Core.Contracts
{
    /// <summary>contracts/v1/session_summary.schema.json. Aggregates only: no raw samples, identity or eye imagery.</summary>
    public sealed class SessionSummary : IContract
    {
        [JsonProperty("schema_version", Required = Required.Always)] public string SchemaVersion = Problems.SchemaVersion;
        /// <summary>Random UUIDv4 per session; never derived from a person, device or account.</summary>
        [JsonProperty("session_id", Required = Required.Always)] public string SessionId;
        [JsonProperty("data_source", Required = Required.Always)] public DataSource DataSource;
        [JsonProperty("replay_origin", Required = Required.AllowNull)] public ReplayOrigin? ReplayOrigin;
        [JsonProperty("duration_ms", Required = Required.Always)] public long DurationMs;
        [JsonProperty("metric_version", Required = Required.Always)] public string MetricVersion;
        [JsonProperty("capabilities", Required = Required.Always)] public Capabilities Capabilities = new Capabilities();
        [JsonProperty("quality", Required = Required.Always)] public Quality Quality = new Quality();
        [JsonProperty("baseline", Required = Required.Always)] public Baseline Baseline = new Baseline();
        /// <summary>Null means unavailable, never zero.</summary>
        [JsonProperty("metrics", Required = Required.Always)] public MetricValues Metrics = new MetricValues();
        /// <summary>Robust z versus the personal baseline, clamped to +/-4.</summary>
        [JsonProperty("deviations", Required = Required.Always)] public MetricValues Deviations = new MetricValues();
        [JsonProperty("strain_indicator", Required = Required.Always)] public StrainIndicator StrainIndicator = new StrainIndicator();
        [JsonProperty("self_report", Required = Required.AllowNull)] public SelfReport SelfReport;

        public void Validate(Problems p, string path)
        {
            p.Version(path, SchemaVersion);
            p.Uuid(path + ".session_id", SessionId);
            p.Defined(path + ".data_source", DataSource);
            p.Defined(path + ".replay_origin", ReplayOrigin);
            p.Min(path + ".duration_ms", DurationMs, 0);
            p.Required(path + ".metric_version", MetricVersion);
            p.MaxLength(path + ".metric_version", MetricVersion, 32);
            p.Nested(path + ".capabilities", Capabilities);
            p.Nested(path + ".quality", Quality);
            p.Nested(path + ".baseline", Baseline);
            if (Metrics == null) p.Add(path + ".metrics", "is required");
            else Metrics.Validate(p, path + ".metrics", 0, double.MaxValue);
            if (Deviations == null) p.Add(path + ".deviations", "is required");
            else Deviations.Validate(p, path + ".deviations", -4, 4);
            p.Nested(path + ".strain_indicator", StrainIndicator);
            if (SelfReport != null) SelfReport.Validate(p, path + ".self_report");

            Provenance.CheckReplayOrigin(p, path, DataSource, ReplayOrigin);
            if (Quality != null && StrainIndicator != null && Quality.Status == QualityStatus.InsufficientData
                && (StrainIndicator.Level != StrainLevel.Unavailable || StrainIndicator.Value != null))
                p.Add(path + ".strain_indicator", "insufficient_data requires level 'unavailable' and value null");
            if (DataSource == DataSource.Synthetic && Capabilities != null && Capabilities.NativeEyeGaze)
                p.Add(path + ".capabilities.native_eye_gaze", "synthetic data cannot claim native eye gaze");
        }
    }

    /// <summary>What the active provider really offers. Unsupported signals stay false and their metrics null.</summary>
    public sealed class Capabilities : IContract
    {
        [JsonProperty("gaze_pose", Required = Required.Always)] public bool GazePose;
        /// <summary>True only for per-eye gaze from the headset. Head gaze, controller ray and synthetic gaze are false.</summary>
        [JsonProperty("native_eye_gaze", Required = Required.Always)] public bool NativeEyeGaze;
        [JsonProperty("openness", Required = Required.Always)] public bool Openness;
        [JsonProperty("pupil", Required = Required.Always)] public bool Pupil;
        /// <summary>False when the runtime reports constant confidence (Quest Pro). Never filter on it then.</summary>
        [JsonProperty("confidence_informative", Required = Required.Always)] public bool ConfidenceInformative;

        public void Validate(Problems p, string path) { }
    }

    public sealed class Quality : IContract
    {
        [JsonProperty("status", Required = Required.Always)] public QualityStatus Status;
        [JsonProperty("valid_coverage", Required = Required.Always)] public double ValidCoverage;
        [JsonProperty("valid_duration_ms", Required = Required.Always)] public long ValidDurationMs;
        [JsonProperty("gap_duration_ms", Required = Required.Always)] public long GapDurationMs;
        [JsonProperty("max_gap_ms", Required = Required.Always)] public long MaxGapMs;
        [JsonProperty("repeated_sample_fraction", Required = Required.Always)] public double RepeatedSampleFraction;
        [JsonProperty("median_dt_ms", Required = Required.AllowNull)] public double? MedianDtMs;
        [JsonProperty("unavailable", Required = Required.Always)] public List<UnavailableSignal> Unavailable = new List<UnavailableSignal>();
        [JsonProperty("policy_version", Required = Required.Always)] public string PolicyVersion;

        public void Validate(Problems p, string path)
        {
            p.Defined(path + ".status", Status);
            p.Range(path + ".valid_coverage", ValidCoverage, 0, 1);
            p.Min(path + ".valid_duration_ms", ValidDurationMs, 0);
            p.Min(path + ".gap_duration_ms", GapDurationMs, 0);
            p.Min(path + ".max_gap_ms", MaxGapMs, 0);
            p.Range(path + ".repeated_sample_fraction", RepeatedSampleFraction, 0, 1);
            p.Range(path + ".median_dt_ms", MedianDtMs, 0, double.MaxValue);
            p.List(path + ".unavailable", Unavailable, 0, 8, unique: false);
            if (Unavailable != null) for (var i = 0; i < Unavailable.Count; i++) p.Nested($"{path}.unavailable[{i}]", Unavailable[i]);
            p.Required(path + ".policy_version", PolicyVersion);
            p.MaxLength(path + ".policy_version", PolicyVersion, 32);
        }
    }

    public sealed class UnavailableSignal : IContract
    {
        [JsonProperty("signal", Required = Required.Always)] public Signal Signal;
        [JsonProperty("reason", Required = Required.Always)] public UnavailableReason Reason;

        public void Validate(Problems p, string path)
        {
            p.Defined(path + ".signal", Signal);
            p.Defined(path + ".reason", Reason);
        }
    }

    public sealed class Baseline : IContract
    {
        [JsonProperty("state", Required = Required.Always)] public BaselineState State;
        [JsonProperty("duration_ms", Required = Required.Always)] public long DurationMs;
        [JsonProperty("windows", Required = Required.Always)] public int Windows;
        [JsonProperty("low_confidence", Required = Required.Always)] public bool LowConfidence;
        [JsonProperty("low_confidence_reason", Required = Required.AllowNull)] public LowConfidenceReason? LowConfidenceReason;

        public void Validate(Problems p, string path)
        {
            p.Defined(path + ".state", State);
            p.Min(path + ".duration_ms", DurationMs, 0);
            p.Min(path + ".windows", Windows, 0);
            p.Defined(path + ".low_confidence_reason", LowConfidenceReason);
        }
    }

    /// <summary>Shared shape of <c>metrics</c> and <c>deviations</c>. Null = unavailable.</summary>
    public sealed class MetricValues
    {
        [JsonProperty("gaze_speed_median_deg_s", Required = Required.AllowNull)] public double? GazeSpeedMedianDegS;
        [JsonProperty("gaze_speed_p95_deg_s", Required = Required.AllowNull)] public double? GazeSpeedP95DegS;
        [JsonProperty("gaze_dispersion_deg", Required = Required.AllowNull)] public double? GazeDispersionDeg;
        [JsonProperty("probe_latency_ms", Required = Required.AllowNull)] public double? ProbeLatencyMs;
        [JsonProperty("probe_rms_error_deg", Required = Required.AllowNull)] public double? ProbeRmsErrorDeg;
        [JsonProperty("probe_miss_fraction", Required = Required.AllowNull)] public double? ProbeMissFraction;
        [JsonProperty("head_speed_deg_s", Required = Required.AllowNull)] public double? HeadSpeedDegS;

        public double? Get(MetricName m)
        {
            switch (m)
            {
                case MetricName.GazeSpeedMedianDegS: return GazeSpeedMedianDegS;
                case MetricName.GazeSpeedP95DegS: return GazeSpeedP95DegS;
                case MetricName.GazeDispersionDeg: return GazeDispersionDeg;
                case MetricName.ProbeLatencyMs: return ProbeLatencyMs;
                case MetricName.ProbeRmsErrorDeg: return ProbeRmsErrorDeg;
                case MetricName.ProbeMissFraction: return ProbeMissFraction;
                case MetricName.HeadSpeedDegS: return HeadSpeedDegS;
                default: return null;
            }
        }

        public void Validate(Problems p, string path, double min, double max)
        {
            p.Range(path + ".gaze_speed_median_deg_s", GazeSpeedMedianDegS, min, max);
            p.Range(path + ".gaze_speed_p95_deg_s", GazeSpeedP95DegS, min, max);
            p.Range(path + ".gaze_dispersion_deg", GazeDispersionDeg, min, max);
            p.Range(path + ".probe_latency_ms", ProbeLatencyMs, min, max);
            p.Range(path + ".probe_rms_error_deg", ProbeRmsErrorDeg, min, max);
            p.Range(path + ".probe_miss_fraction", ProbeMissFraction, min, max);
            p.Range(path + ".head_speed_deg_s", HeadSpeedDegS, min, max);
        }
    }

    /// <summary>
    /// Experimental heuristic over per-metric baseline deviations. NOT a validated fatigue measure and NOT a diagnosis;
    /// label it "experimental" wherever shown.
    /// </summary>
    public sealed class StrainIndicator : IContract
    {
        public const string ExperimentalKind = "experimental_baseline_deviation";

        [JsonProperty("kind", Required = Required.Always)] public string Kind = ExperimentalKind;
        [JsonProperty("level", Required = Required.Always)] public StrainLevel Level = StrainLevel.Unavailable;
        [JsonProperty("value", Required = Required.AllowNull)] public double? Value;
        [JsonProperty("method_version", Required = Required.Always)] public string MethodVersion;

        public void Validate(Problems p, string path)
        {
            p.Const(path + ".kind", Kind, ExperimentalKind);
            p.Defined(path + ".level", Level);
            p.Range(path + ".value", Value, 0, 1);
            p.Required(path + ".method_version", MethodVersion);
            p.MaxLength(path + ".method_version", MethodVersion, 32);
        }
    }

    static class Provenance
    {
        /// <summary>replay_origin is required for replay data and must be null otherwise.</summary>
        public static void CheckReplayOrigin(Problems p, string path, DataSource source, ReplayOrigin? origin)
        {
            if (source == DataSource.Replay && origin == null) p.Add(path + ".replay_origin", "required for replay data");
            if (source != DataSource.Replay && origin != null) p.Add(path + ".replay_origin", "must be null unless data_source is replay");
        }
    }
}
