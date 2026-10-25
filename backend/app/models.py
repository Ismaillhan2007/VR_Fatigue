"""Typed HTTP models matching contracts/v1, with additional semantic checks."""

from typing import Annotated, Literal

from pydantic import BaseModel, ConfigDict, Field, model_validator

Version = Literal["1.0"]
Unit = Annotated[float, Field(ge=0, le=1)]
Nonnegative = Annotated[float, Field(ge=0)]
NonnegativeInt = Annotated[int, Field(ge=0)]
Deviation = Annotated[float, Field(ge=-4, le=4)]
SessionId = Annotated[
    str, Field(pattern=r"^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")
]
DataSource = Literal["live", "replay", "synthetic"]
ReplayOrigin = Literal["recorded", "synthetic"] | None
Reconciliation = Literal["agree_high", "agree_low", "words_only", "signals_only", "unavailable"]
SafetyNotice = Literal["none", "take_break", "stop_session"]
MetricName = Literal[
    "gaze_speed_median_deg_s",
    "gaze_speed_p95_deg_s",
    "gaze_dispersion_deg",
    "probe_latency_ms",
    "probe_rms_error_deg",
    "probe_miss_fraction",
    "head_speed_deg_s",
]
ObservationCode = Literal[
    "gaze_speed_changed",
    "gaze_spread_changed",
    "probe_slower",
    "probe_less_accurate",
    "probe_more_misses",
    "head_more_active",
    "within_baseline",
    "no_baseline",
    "data_quality_limited",
]
Chip = Literal[
    "tired_eyes",
    "tired_mind",
    "tense",
    "restless",
    "overstimulated",
    "low_energy",
    "want_quiet",
    "want_nature",
    "want_dim",
    "want_movement",
]


class ContractModel(BaseModel):
    model_config = ConfigDict(extra="forbid", strict=True, allow_inf_nan=False)


class Capabilities(ContractModel):
    gaze_pose: bool
    native_eye_gaze: bool
    openness: bool
    pupil: bool
    confidence_informative: bool


class UnavailableSignal(ContractModel):
    signal: Literal["openness", "pupil", "blink", "gaze", "probe", "head_pose"]
    reason: Literal[
        "not_exposed_by_runtime",
        "permission_denied",
        "tracking_lost",
        "calibration_missing",
        "not_collected",
    ]


class Quality(ContractModel):
    status: Literal["usable", "insufficient_data"]
    valid_coverage: Unit
    valid_duration_ms: NonnegativeInt
    gap_duration_ms: NonnegativeInt
    max_gap_ms: NonnegativeInt
    repeated_sample_fraction: Unit
    median_dt_ms: Nonnegative | None
    unavailable: Annotated[list[UnavailableSignal], Field(max_length=8)]
    policy_version: Annotated[str, Field(max_length=32)]


class Baseline(ContractModel):
    state: Literal["absent", "present"]
    duration_ms: NonnegativeInt
    windows: NonnegativeInt
    low_confidence: bool
    low_confidence_reason: Literal["self_reported_tired", "too_short", "poor_quality"] | None


class Metrics(ContractModel):
    gaze_speed_median_deg_s: Nonnegative | None
    gaze_speed_p95_deg_s: Nonnegative | None
    gaze_dispersion_deg: Nonnegative | None
    probe_latency_ms: Nonnegative | None
    probe_rms_error_deg: Nonnegative | None
    probe_miss_fraction: Unit | None
    head_speed_deg_s: Nonnegative | None


class Deviations(ContractModel):
    gaze_speed_median_deg_s: Deviation | None
    gaze_speed_p95_deg_s: Deviation | None
    gaze_dispersion_deg: Deviation | None
    probe_latency_ms: Deviation | None
    probe_rms_error_deg: Deviation | None
    probe_miss_fraction: Deviation | None
    head_speed_deg_s: Deviation | None


class StrainIndicator(ContractModel):
    kind: Literal["experimental_baseline_deviation"]
    level: Literal["low", "moderate", "high", "unavailable"]
    value: Unit | None
    method_version: Annotated[str, Field(max_length=32)]

    @model_validator(mode="after")
    def consistent_value(self):
        if (self.level == "unavailable") != (self.value is None):
            raise ValueError("An unavailable indicator must have a null value, and vice versa")
        return self


class SelfReport(ContractModel):
    schema_version: Version
    phase: Literal["baseline", "pre_task", "post_task", "post_scene"]
    eye_strain: Annotated[int, Field(ge=1, le=7)]
    mental_fatigue: Annotated[int, Field(ge=1, le=7)]
    chips: Annotated[list[Chip], Field(max_length=6)]
    free_text: Annotated[str, Field(max_length=300)] | None

    @model_validator(mode="after")
    def unique_chips(self):
        if len(self.chips) != len(set(self.chips)):
            raise ValueError("chips must be unique")
        return self


class SessionSummary(ContractModel):
    schema_version: Version
    session_id: SessionId
    data_source: DataSource
    replay_origin: ReplayOrigin
    duration_ms: NonnegativeInt
    metric_version: Annotated[str, Field(max_length=32)]
    capabilities: Capabilities
    quality: Quality
    baseline: Baseline
    metrics: Metrics
    deviations: Deviations
    strain_indicator: StrainIndicator
    self_report: SelfReport | None

    @model_validator(mode="after")
    def semantic_rules(self):
        if (self.data_source == "replay") != (self.replay_origin is not None):
            raise ValueError("replay_origin is required only for replay")
        synthetic = self.data_source == "synthetic" or self.replay_origin == "synthetic"
        if synthetic and self.capabilities.native_eye_gaze:
            raise ValueError("Synthetic input cannot claim native eye gaze")
        unavailable = (
            self.quality.status == "insufficient_data"
            or self.baseline.state == "absent"
            or self.baseline.windows < 6
            or any(
                getattr(self.metrics, name) is None
                for name in ("probe_latency_ms", "probe_rms_error_deg", "probe_miss_fraction")
            )
        )
        if unavailable and self.strain_indicator.level != "unavailable":
            raise ValueError(
                "Insufficient data, baseline, or probes requires unavailable indicator"
            )
        if self.quality.valid_duration_ms + self.quality.gap_duration_ms != self.duration_ms:
            raise ValueError("Valid and gap durations must sum to session duration")
        return self


class Preferences(ContractModel):
    schema_version: Version
    theme: Literal["forest"]
    animals_enabled: bool
    time_of_day: Literal["day", "sunset"]
    sound: Literal["forest", "silent"]
    motion: Literal["none", "gentle"]
    text: Annotated[str, Field(max_length=300)] | None


class SoundLayers(ContractModel):
    wind: Unit
    birds: Unit
    water: Unit
    pad: Unit


class Orb(ContractModel):
    enabled: bool
    breath_seconds: Annotated[float, Field(ge=4, le=8)]


class SceneConfig(ContractModel):
    schema_version: Version
    scene_id: Literal["forest_v1"]
    animals_enabled: bool
    time_of_day: Literal["day", "sunset"]
    light_warmth: Unit
    brightness: Annotated[float, Field(ge=0.4, le=1)]
    fog_density: Annotated[float, Field(ge=0, le=0.6)]
    sound: Literal["forest", "silent"]
    master_volume: Unit
    sound_layers: SoundLayers
    motion: Literal["none", "gentle"]
    motion_speed: Annotated[float, Field(ge=0, le=0.5)]
    particles: Annotated[list[Literal["fireflies", "pollen"]], Field(max_length=2)]
    orb: Orb

    @model_validator(mode="after")
    def unique_particles(self):
        if len(self.particles) != len(set(self.particles)):
            raise ValueError("particles must be unique")
        return self


class Observation(ContractModel):
    code: ObservationCode
    metric: MetricName | None


class AnalysisRequest(ContractModel):
    schema_version: Version
    session_summary: SessionSummary
    # Null when the participant let the app choose (contracts/v1/analyze_request.schema.json).
    preferences: Preferences | None
    reconciliation: Reconciliation
    safety_notice: SafetyNotice

    @model_validator(mode="after")
    def unavailable_reconciliation(self):
        unavailable = self.session_summary.strain_indicator.level == "unavailable"
        if unavailable and self.reconciliation != "unavailable":
            raise ValueError("Unavailable eye indicator requires unavailable reconciliation")
        return self


class AnalysisResponse(ContractModel):
    schema_version: Version
    session_id: SessionId
    data_source: DataSource
    replay_origin: ReplayOrigin
    quality: Quality
    reconciliation: Reconciliation
    observations: Annotated[list[Observation], Field(max_length=6)]
    narrative: Annotated[str, Field(max_length=600)]
    limitations: Annotated[
        list[Annotated[str, Field(max_length=200)]], Field(min_length=1, max_length=5)
    ]
    safety_notice: SafetyNotice
    scene_rationale: Annotated[str, Field(max_length=200)]
    scene_config: SceneConfig
    ai_source: Literal["live", "cached", "fallback"]
    model_id: Annotated[str, Field(max_length=64)] | None
    prompt_version: Annotated[str, Field(max_length=32)]


class GeminiChoice(ContractModel):
    """The model selects vetted explanation codes, never unrestricted medical prose."""

    observation_codes: Annotated[list[ObservationCode], Field(max_length=6)]
    scene_config: SceneConfig
