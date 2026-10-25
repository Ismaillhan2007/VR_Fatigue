using System.Runtime.Serialization;

namespace App.Core.Contracts
{
    // String values match contracts/v1 exactly. Unknown strings and integer values are rejected by ContractJson.

    public enum DataSource
    {
        [EnumMember(Value = "live")] Live,
        [EnumMember(Value = "replay")] Replay,
        [EnumMember(Value = "synthetic")] Synthetic,
    }

    public enum ReplayOrigin
    {
        [EnumMember(Value = "recorded")] Recorded,
        [EnumMember(Value = "synthetic")] Synthetic,
    }

    public enum QualityStatus
    {
        [EnumMember(Value = "usable")] Usable,
        [EnumMember(Value = "insufficient_data")] InsufficientData,
    }

    public enum Signal
    {
        [EnumMember(Value = "openness")] Openness,
        [EnumMember(Value = "pupil")] Pupil,
        [EnumMember(Value = "blink")] Blink,
        [EnumMember(Value = "gaze")] Gaze,
        [EnumMember(Value = "probe")] Probe,
        [EnumMember(Value = "head_pose")] HeadPose,
    }

    public enum UnavailableReason
    {
        [EnumMember(Value = "not_exposed_by_runtime")] NotExposedByRuntime,
        [EnumMember(Value = "permission_denied")] PermissionDenied,
        [EnumMember(Value = "tracking_lost")] TrackingLost,
        [EnumMember(Value = "calibration_missing")] CalibrationMissing,
        [EnumMember(Value = "not_collected")] NotCollected,
    }

    public enum BaselineState
    {
        [EnumMember(Value = "absent")] Absent,
        [EnumMember(Value = "present")] Present,
    }

    public enum LowConfidenceReason
    {
        [EnumMember(Value = "self_reported_tired")] SelfReportedTired,
        [EnumMember(Value = "too_short")] TooShort,
        [EnumMember(Value = "poor_quality")] PoorQuality,
    }

    public enum StrainLevel
    {
        [EnumMember(Value = "low")] Low,
        [EnumMember(Value = "moderate")] Moderate,
        [EnumMember(Value = "high")] High,
        [EnumMember(Value = "unavailable")] Unavailable,
    }

    public enum ReportPhase
    {
        [EnumMember(Value = "baseline")] Baseline,
        [EnumMember(Value = "pre_task")] PreTask,
        [EnumMember(Value = "post_task")] PostTask,
        [EnumMember(Value = "post_scene")] PostScene,
    }

    public enum Chip
    {
        [EnumMember(Value = "tired_eyes")] TiredEyes,
        [EnumMember(Value = "tired_mind")] TiredMind,
        [EnumMember(Value = "tense")] Tense,
        [EnumMember(Value = "restless")] Restless,
        [EnumMember(Value = "overstimulated")] Overstimulated,
        [EnumMember(Value = "low_energy")] LowEnergy,
        [EnumMember(Value = "want_quiet")] WantQuiet,
        [EnumMember(Value = "want_nature")] WantNature,
        [EnumMember(Value = "want_dim")] WantDim,
        [EnumMember(Value = "want_movement")] WantMovement,
    }

    public enum Theme
    {
        [EnumMember(Value = "forest")] Forest,
    }

    public enum TimeOfDay
    {
        [EnumMember(Value = "day")] Day,
        [EnumMember(Value = "sunset")] Sunset,
    }

    public enum SoundMode
    {
        [EnumMember(Value = "forest")] Forest,
        [EnumMember(Value = "silent")] Silent,
    }

    public enum MotionMode
    {
        [EnumMember(Value = "none")] None,
        [EnumMember(Value = "gentle")] Gentle,
    }

    public enum Particle
    {
        [EnumMember(Value = "fireflies")] Fireflies,
        [EnumMember(Value = "pollen")] Pollen,
    }

    public enum Reconciliation
    {
        [EnumMember(Value = "agree_high")] AgreeHigh,
        [EnumMember(Value = "agree_low")] AgreeLow,
        [EnumMember(Value = "words_only")] WordsOnly,
        [EnumMember(Value = "signals_only")] SignalsOnly,
        [EnumMember(Value = "unavailable")] Unavailable,
    }

    public enum ObservationCode
    {
        [EnumMember(Value = "gaze_speed_changed")] GazeSpeedChanged,
        [EnumMember(Value = "gaze_spread_changed")] GazeSpreadChanged,
        [EnumMember(Value = "probe_slower")] ProbeSlower,
        [EnumMember(Value = "probe_less_accurate")] ProbeLessAccurate,
        [EnumMember(Value = "probe_more_misses")] ProbeMoreMisses,
        [EnumMember(Value = "head_more_active")] HeadMoreActive,
        [EnumMember(Value = "within_baseline")] WithinBaseline,
        [EnumMember(Value = "no_baseline")] NoBaseline,
        [EnumMember(Value = "data_quality_limited")] DataQualityLimited,
    }

    public enum MetricName
    {
        [EnumMember(Value = "gaze_speed_median_deg_s")] GazeSpeedMedianDegS,
        [EnumMember(Value = "gaze_speed_p95_deg_s")] GazeSpeedP95DegS,
        [EnumMember(Value = "gaze_dispersion_deg")] GazeDispersionDeg,
        [EnumMember(Value = "probe_latency_ms")] ProbeLatencyMs,
        [EnumMember(Value = "probe_rms_error_deg")] ProbeRmsErrorDeg,
        [EnumMember(Value = "probe_miss_fraction")] ProbeMissFraction,
        [EnumMember(Value = "head_speed_deg_s")] HeadSpeedDegS,
    }

    /// <summary>Ordered by severity: the AI may raise it, never lower it.</summary>
    public enum SafetyNotice
    {
        [EnumMember(Value = "none")] None,
        [EnumMember(Value = "take_break")] TakeBreak,
        [EnumMember(Value = "stop_session")] StopSession,
    }

    public enum AiSource
    {
        [EnumMember(Value = "live")] Live,
        [EnumMember(Value = "cached")] Cached,
        [EnumMember(Value = "fallback")] Fallback,
    }
}
