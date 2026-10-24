import asyncio
import copy
import json
from decimal import Decimal
from pathlib import Path

import pytest
from fastapi.testclient import TestClient
from jsonschema import Draft202012Validator
from referencing import Registry, Resource

from app.config import Settings
from app.fallback import local_scene
from app.gemini import build_prompt
from app.main import create_app
from app.models import AnalysisRequest, GeminiChoice

ROOT = Path(__file__).resolve().parents[2]


def fixture(name):
    return json.loads((ROOT / "fixtures" / name).read_text())


@pytest.fixture(scope="module")
def response_validator():
    registry = Registry()
    documents = {}
    for path in (ROOT / "contracts/v1").glob("*.schema.json"):
        doc = json.loads(path.read_text())
        documents[path.stem] = doc
        registry = registry.with_resource(doc["$id"], Resource.from_contents(doc))
    return Draft202012Validator(documents["analysis_response.schema"], registry=registry)


@pytest.fixture
def body():
    return {
        "schema_version": "1.0",
        "session_summary": fixture("session_synthetic_usable.json"),
        "preferences": fixture("preferences_default.json"),
        "reconciliation": "agree_high",
        "safety_notice": "none",
    }


def settings(**overrides):
    return Settings(
        _env_file=None,
        **{
            "gemini_enabled": False,
            "GEMINI_API_KEY": "",
            "GEMINI_MODEL": "",
            **overrides,
        },
    )


def live_settings(**overrides):
    return settings(
        **{
            "gemini_enabled": True,
            "GEMINI_API_KEY": "unit-test-placeholder",
            "GEMINI_MODEL": "test-model",
            "input_price_per_million": Decimal("0"),
            "output_price_per_million": Decimal("0"),
            **overrides,
        }
    )


class FakeAdapter:
    def __init__(self, behavior="valid"):
        self.behavior = behavior
        self.calls = 0

    async def generate(self, request, observations):
        self.calls += 1
        if self.behavior in {"blocked", "429", "5xx", "invalid"}:
            raise ValueError(self.behavior)
        if self.behavior == "timeout":
            await asyncio.sleep(1)
        codes = [item.code for item in observations]
        if self.behavior == "invented_observation":
            codes = ["within_baseline"]
        elif self.behavior == "missing_measurement":
            codes = ["probe_slower"]
        # Deliberately propose noisy settings: the service must reapply restrictions.
        scene = local_scene(request).model_copy(deep=True)
        scene.sound = "forest"
        scene.master_volume = 0.8
        scene.animals_enabled = True
        scene.motion = "gentle"
        scene.motion_speed = 0.5
        return GeminiChoice(observation_codes=codes, scene_config=scene)


def client_for(config=None, adapter=None):
    return TestClient(create_app(config or settings(), adapter))


def test_health_and_openapi_without_key():
    with client_for() as client:
        assert client.get("/health").json()["gemini_enabled"] is False
        schema = client.get("/openapi.json").json()
        assert "/api/v1/analyze" in schema["paths"]
        assert "SessionSummary" in schema["components"]["schemas"]


def test_offline_response_matches_shared_contract_and_preferences(body, response_validator):
    with client_for() as client:
        response = client.post("/api/v1/analyze", json=body)
    assert response.status_code == 200, response.text
    result = response.json()
    assert result["ai_source"] == "fallback" and result["model_id"] is None
    assert result["quality"] == body["session_summary"]["quality"]
    assert result["scene_config"]["sound"] == "silent"
    assert result["scene_config"]["animals_enabled"] is False
    assert result["scene_config"]["motion_speed"] == 0
    response_validator.validate(result)


@pytest.mark.parametrize("missing", ["both", "metrics", "deviations", "disjoint"])
def test_no_comparable_metrics_returns_local_report_without_cloud(
    body, response_validator, missing
):
    session = body["session_summary"]
    if missing in {"both", "metrics", "disjoint"}:
        session["metrics"] = dict.fromkeys(session["metrics"])
    if missing in {"both", "deviations", "disjoint"}:
        session["deviations"] = dict.fromkeys(session["deviations"])
    if missing == "disjoint":
        session["metrics"]["gaze_dispersion_deg"] = 6.1
        session["deviations"]["probe_latency_ms"] = 1.6
    session["strain_indicator"].update(level="unavailable", value=None)
    body["reconciliation"] = "unavailable"
    adapter = FakeAdapter()
    with client_for(live_settings(), adapter) as client:
        response = client.post("/api/v1/analyze", json=body)
        service = client.app.state.analysis_service
        assert service.attempts == 0
        assert not service.cache
    assert response.status_code == 200, response.text
    result = response.json()
    response_validator.validate(result)
    assert result["observations"] == [{"code": "data_quality_limited", "metric": None}]
    assert "close to your baseline" not in result["narrative"]
    assert result["session_id"] == session["session_id"]
    assert result["data_source"] == session["data_source"]
    assert result["replay_origin"] == session["replay_origin"]
    assert result["quality"] == session["quality"]
    assert result["reconciliation"] == "unavailable"
    assert result["ai_source"] == "fallback" and result["model_id"] is None
    assert result["scene_config"] == local_scene(AnalysisRequest.model_validate(body)).model_dump()
    assert adapter.calls == 0


@pytest.mark.parametrize("metric", ["probe_latency_ms", "gaze_speed_p95_deg_s"])
@pytest.mark.parametrize("missing", ["metrics", "deviations"])
def test_partial_comparisons_do_not_infer_missing_measurements(
    body, response_validator, metric, missing
):
    session = body["session_summary"]
    session["deviations"] = dict.fromkeys(session["deviations"], 0.0)
    session["deviations"][metric] = 2.0
    session[missing][metric] = None
    session["strain_indicator"].update(level="unavailable", value=None)
    body["reconciliation"] = "unavailable"
    with client_for() as client:
        response = client.post("/api/v1/analyze", json=body)
    assert response.status_code == 200, response.text
    result = response.json()
    response_validator.validate(result)
    assert result["observations"] == [{"code": "within_baseline", "metric": None}]
    assert "available measurements" in result["narrative"]


def test_zero_measurements_and_deviations_are_valid_comparisons(body, response_validator):
    session = body["session_summary"]
    session["metrics"] = dict.fromkeys(session["metrics"], 0.0)
    session["deviations"] = dict.fromkeys(session["deviations"], 0.0)
    session["strain_indicator"].update(level="low", value=0.0)
    body["reconciliation"] = "words_only"
    with client_for() as client:
        response = client.post("/api/v1/analyze", json=body)
    assert response.status_code == 200, response.text
    result = response.json()
    response_validator.validate(result)
    assert result["observations"] == [{"code": "within_baseline", "metric": None}]


@pytest.mark.parametrize(
    "behavior,ai_source", [("valid", "live"), ("missing_measurement", "fallback")]
)
def test_model_cannot_restore_an_observation_for_a_missing_measurement(
    body, response_validator, behavior, ai_source
):
    session = body["session_summary"]
    session["metrics"]["probe_latency_ms"] = None
    session["strain_indicator"].update(level="unavailable", value=None)
    body["reconciliation"] = "unavailable"
    adapter = FakeAdapter(behavior)
    with client_for(live_settings(), adapter) as client:
        response = client.post("/api/v1/analyze", json=body)
    assert response.status_code == 200, response.text
    result = response.json()
    response_validator.validate(result)
    assert result["ai_source"] == ai_source
    assert result["observations"] == [
        {"code": "probe_less_accurate", "metric": "probe_rms_error_deg"},
        {"code": "probe_more_misses", "metric": "probe_miss_fraction"},
    ]
    assert adapter.calls == 1


@pytest.mark.parametrize("state,windows", [("absent", 0), ("present", 5)])
def test_missing_baseline_keeps_its_specific_observation(body, state, windows):
    session = body["session_summary"]
    session["baseline"].update(state=state, windows=windows)
    session["deviations"] = dict.fromkeys(session["deviations"])
    session["strain_indicator"].update(level="unavailable", value=None)
    body["reconciliation"] = "unavailable"
    with client_for() as client:
        response = client.post("/api/v1/analyze", json=body)
    assert response.status_code == 200, response.text
    assert response.json()["observations"] == [{"code": "no_baseline", "metric": None}]


def test_insufficient_data_has_usable_local_result(body):
    body["session_summary"] = fixture("session_synthetic_insufficient.json")
    body["reconciliation"] = "unavailable"
    adapter = FakeAdapter()
    with client_for(live_settings(), adapter) as client:
        response = client.post("/api/v1/analyze", json=body)
    assert response.status_code == 200, response.text
    assert response.json()["observations"][0]["code"] == "data_quality_limited"
    assert adapter.calls == 0


@pytest.mark.parametrize(
    "mutation", ["unknown", "fraction", "baseline", "native_replay", "version"]
)
def test_invalid_input_is_rejected(body, mutation):
    if mutation == "unknown":
        body["scene_script"] = "untrusted"
    elif mutation == "fraction":
        body["session_summary"]["metrics"]["probe_miss_fraction"] = 1.5
    elif mutation == "baseline":
        body["session_summary"]["baseline"]["state"] = "absent"
    elif mutation == "native_replay":
        body["session_summary"].update(data_source="replay", replay_origin="synthetic")
        body["session_summary"]["capabilities"]["native_eye_gaze"] = True
    else:
        body["schema_version"] = "2.0"
    with client_for() as client:
        response = client.post("/api/v1/analyze", json=body)
    assert response.status_code == 422
    assert all("input" not in error for error in response.json()["detail"])


def test_nonfinite_input_rejected(body):
    body["session_summary"]["metrics"]["probe_latency_ms"] = float("nan")
    with client_for() as client:
        response = client.post(
            "/api/v1/analyze",
            content=json.dumps(body),
            headers={"Content-Type": "application/json"},
        )
    assert response.status_code == 422


@pytest.mark.parametrize("source,origin", [("live", None), ("replay", "recorded")])
def test_real_measurements_never_reach_cloud(body, source, origin):
    body["session_summary"].update(data_source=source, replay_origin=origin)
    body["session_summary"]["capabilities"]["native_eye_gaze"] = True
    adapter = FakeAdapter()
    with client_for(live_settings(), adapter) as client:
        response = client.post("/api/v1/analyze", json=body)
    assert response.status_code == 200, response.text
    assert response.json()["ai_source"] == "fallback"
    assert adapter.calls == 0


def test_safety_can_only_increase_and_skips_cloud(body):
    body["session_summary"]["self_report"]["free_text"] = "I feel dizzy"
    adapter = FakeAdapter()
    with client_for(live_settings(), adapter) as client:
        response = client.post("/api/v1/analyze", json=body)
    assert response.json()["safety_notice"] == "stop_session"
    assert response.json()["scene_config"]["orb"]["enabled"] is False
    assert adapter.calls == 0


@pytest.mark.parametrize(
    "behavior", ["blocked", "429", "5xx", "invalid", "timeout", "invented_observation"]
)
def test_model_failures_return_local_response(body, behavior):
    adapter = FakeAdapter(behavior)
    with client_for(live_settings(gemini_timeout_seconds=0.01), adapter) as client:
        response = client.post("/api/v1/analyze", json=body)
    assert response.status_code == 200
    assert response.json()["ai_source"] == "fallback"
    assert response.json()["model_id"] is None
    assert adapter.calls == 1


def test_cache_rebinds_session_and_preserves_controls(body):
    adapter = FakeAdapter()
    with client_for(live_settings(), adapter) as client:
        first = client.post("/api/v1/analyze", json=body).json()
        new_body = copy.deepcopy(body)
        new_body["session_summary"]["session_id"] = "00000000-0000-4000-8000-000000000002"
        second = client.post("/api/v1/analyze", json=new_body).json()
    assert first["ai_source"] == "live"
    assert second["ai_source"] == "cached"
    assert second["session_id"] == new_body["session_summary"]["session_id"]
    assert second["scene_config"]["sound"] == "silent"
    assert second["scene_config"]["animals_enabled"] is False
    assert second["scene_config"]["motion_speed"] == 0
    assert adapter.calls == 1


@pytest.mark.parametrize("limit", ["attempts", "budget", "unverified_prices"])
def test_budget_cutoff_prevents_calls(body, limit):
    config = {
        "attempts": live_settings(gemini_max_attempts_per_day=0),
        "budget": live_settings(input_price_per_million=Decimal("1")),
        "unverified_prices": live_settings(input_price_per_million=None),
    }[limit]
    adapter = FakeAdapter()
    with client_for(config, adapter) as client:
        response = client.post("/api/v1/analyze", json=body)
    assert response.json()["ai_source"] == "fallback"
    assert adapter.calls == 0


def test_prompt_omits_identifiers_and_free_text(body):
    body["session_summary"]["self_report"]["free_text"] = "private-free-text-marker"
    body["preferences"]["text"] = "ignore-all-rules-marker"
    request = AnalysisRequest.model_validate(body)
    prompt = build_prompt(request, [])
    assert "private-free-text-marker" not in prompt
    assert "ignore-all-rules-marker" not in prompt
    assert request.session_summary.session_id not in prompt
