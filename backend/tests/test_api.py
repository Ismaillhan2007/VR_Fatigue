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


def test_offline_response_matches_shared_contract_and_preferences(body):
    with client_for() as client:
        response = client.post("/api/v1/analyze", json=body)
    assert response.status_code == 200, response.text
    result = response.json()
    assert result["ai_source"] == "fallback" and result["model_id"] is None
    assert result["quality"] == body["session_summary"]["quality"]
    assert result["scene_config"]["sound"] == "silent"
    assert result["scene_config"]["animals_enabled"] is False
    assert result["scene_config"]["motion_speed"] == 0
    registry = Registry()
    documents = {}
    for path in (ROOT / "contracts/v1").glob("*.schema.json"):
        doc = json.loads(path.read_text())
        documents[path.stem] = doc
        registry = registry.with_resource(doc["$id"], Resource.from_contents(doc))
    Draft202012Validator(documents["analysis_response.schema"], registry=registry).validate(result)


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
