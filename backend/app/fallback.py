"""Deterministic explanation and scene mapping. No cloud dependency."""

import re

from app.models import (
    AnalysisRequest,
    AnalysisResponse,
    Observation,
    SafetyNotice,
    SceneConfig,
)

OBSERVATION_TEXT = {
    "gaze_speed_changed": "Gaze movement speed changed from your baseline.",
    "gaze_spread_changed": "Gaze spread changed from your baseline.",
    "probe_slower": "Target acquisition was slower than your baseline.",
    "probe_less_accurate": "Target-following error increased from your baseline.",
    "probe_more_misses": "More targets were missed than in your baseline.",
    "head_more_active": "Head movement increased from your baseline.",
    "within_baseline": "The available measurements were close to your baseline.",
    "no_baseline": "No usable personal baseline was supplied.",
    "data_quality_limited": "Eye measurements are insufficient for a baseline comparison.",
}


def observations_for(request: AnalysisRequest) -> list[Observation]:
    session = request.session_summary
    if session.quality.status == "insufficient_data":
        return [Observation(code="data_quality_limited", metric=None)]
    if session.baseline.state == "absent" or session.baseline.windows < 6:
        return [Observation(code="no_baseline", metric=None)]
    # Initial experimental display threshold; not a clinical or fatigue threshold.
    result = []
    has_comparison = False
    for metric, code, directional in (
        ("probe_latency_ms", "probe_slower", True),
        ("probe_rms_error_deg", "probe_less_accurate", True),
        ("probe_miss_fraction", "probe_more_misses", True),
        ("gaze_speed_p95_deg_s", "gaze_speed_changed", False),
        ("gaze_dispersion_deg", "gaze_spread_changed", False),
        ("head_speed_deg_s", "head_more_active", True),
    ):
        value = getattr(session.deviations, metric)
        # A deviation alone cannot substantiate a claim about a missing measurement.
        if getattr(session.metrics, metric) is None or value is None:
            continue
        has_comparison = True
        changed = value >= 1 if directional else abs(value) >= 1
        if changed:
            result.append(Observation(code=code, metric=metric))
    if not has_comparison:
        return [Observation(code="data_quality_limited", metric=None)]
    return result or [Observation(code="within_baseline", metric=None)]


def safety_for(request: AnalysisRequest) -> SafetyNotice:
    # Only raise the caller's explicit notice. These are conservative demo keywords.
    report = request.session_summary.self_report
    prefs_text = request.preferences.text if request.preferences else None
    text = " ".join(filter(None, [report.free_text if report else None, prefs_text]))
    if re.search(r"\b(dizziness|dizzy|nausea|nauseous|pain)\b", text, re.IGNORECASE):
        return "stop_session"
    return request.safety_notice


def constrain_scene(scene: SceneConfig, request: AnalysisRequest) -> SceneConfig:
    """Apply choices again after AI. Quiet/no motion/no animals cannot be reversed."""
    scene = scene.model_copy(deep=True)
    prefs = request.preferences
    report = request.session_summary.self_report
    chips = set(report.chips if report else [])
    # Without explicit preferences the scene's own values stand; chips and safety still apply.
    if prefs is not None:
        scene.time_of_day = prefs.time_of_day
    animals = prefs.animals_enabled if prefs is not None else scene.animals_enabled
    scene.animals_enabled = animals and "overstimulated" not in chips
    silent = (prefs is not None and prefs.sound == "silent") or scene.sound == "silent"
    scene.sound = "silent" if silent or "want_quiet" in chips else "forest"
    if scene.sound == "silent":
        scene.master_volume = 0
        scene.sound_layers.wind = 0
        scene.sound_layers.birds = 0
        scene.sound_layers.water = 0
        scene.sound_layers.pad = 0
    no_motion = (
        (prefs is not None and prefs.motion == "none")
        or scene.motion == "none"
        or bool(chips & {"tired_eyes", "overstimulated"})
    )
    scene.motion = "none" if no_motion else "gentle"
    if no_motion:
        scene.motion_speed = 0
    if "overstimulated" in chips:
        scene.particles = []
    if safety_for(request) == "stop_session":
        scene.motion = "none"
        scene.motion_speed = 0
        scene.animals_enabled = False
        scene.particles = []
        scene.orb.enabled = False
    return SceneConfig.model_validate(scene.model_dump())


def local_scene(request: AnalysisRequest) -> SceneConfig:
    scene = SceneConfig(
        schema_version="1.0",
        scene_id="forest_v1",
        animals_enabled=True,
        time_of_day="sunset",
        light_warmth=0.7,
        brightness=0.8,
        fog_density=0.25,
        sound="forest",
        master_volume=0.6,
        sound_layers={"wind": 0.2, "birds": 0.3, "water": 0.4, "pad": 0.3},
        motion="gentle",
        motion_speed=0.2,
        particles=["fireflies"],
        orb={"enabled": False, "breath_seconds": 6.0},
    )
    report = request.session_summary.self_report
    chips = set(report.chips if report else [])
    if "tired_eyes" in chips:
        scene.brightness = 0.65
        scene.light_warmth = 0.85
        scene.fog_density = 0.35
    if "want_dim" in chips:
        scene.brightness = 0.5
    if chips & {"tense", "restless"}:
        scene.orb.enabled = True
    if "want_nature" in chips:
        scene.sound_layers.birds = 0.5
        scene.sound_layers.water = 0.6
    if "low_energy" in chips:
        scene.sound_layers.birds = min(1.0, scene.sound_layers.birds + 0.2)
    if "want_movement" in chips:
        scene.motion_speed = 0.3
    if "overstimulated" in chips:
        scene.sound_layers.wind = 0
        scene.sound_layers.birds = 0
        scene.sound_layers.water = 0
    return constrain_scene(scene, request)


def narrative_for(request: AnalysisRequest, observations: list[Observation]) -> str:
    text = " ".join(OBSERVATION_TEXT[item.code] for item in observations[:3])
    report = request.session_summary.self_report
    if report:
        text += (
            f" You rated eye strain {report.eye_strain}/7"
            f" and mental fatigue {report.mental_fatigue}/7."
        )
    text += " This is an experimental wellness summary, not validated fatigue detection."
    return text[:600]


def rationale_for(scene: SceneConfig) -> str:
    return (
        f"{scene.time_of_day.title()} forest; "
        f"{'muted' if scene.sound == 'silent' else 'ambient sound'}; "
        f"{'no' if scene.motion == 'none' else 'gentle'} motion; "
        f"{'squirrels enabled' if scene.animals_enabled else 'no animals'}. "
        "You can adjust these settings."
    )


def local_response(request: AnalysisRequest) -> AnalysisResponse:
    session = request.session_summary
    observations = observations_for(request)
    scene = local_scene(request)
    limitations = [
        "Experimental within-session comparison, not a medical assessment.",
        f"Input source: {session.data_source}; unavailable measurements are not inferred.",
    ]
    if session.baseline.low_confidence:
        limitations.append("The baseline has low confidence; it may not reflect a rested state.")
    return AnalysisResponse(
        schema_version="1.0",
        session_id=session.session_id,
        data_source=session.data_source,
        replay_origin=session.replay_origin,
        quality=session.quality.model_copy(deep=True),
        reconciliation=request.reconciliation,
        observations=observations,
        narrative=narrative_for(request, observations),
        limitations=limitations,
        safety_notice=safety_for(request),
        scene_rationale=rationale_for(scene),
        scene_config=scene,
        ai_source="fallback",
        model_id=None,
        prompt_version="local-0.2",
    )
