import json
from pathlib import Path

import pytest
from jsonschema import Draft202012Validator
from referencing import Registry, Resource
from test_api import FakeAdapter, client_for, fixture, live_settings

from app.models import AnalysisRequest

ROOT = Path(__file__).resolve().parents[2]
CASES = json.loads((ROOT / "fixtures/integration/scene_rules.json").read_text())


def schema_validator(name):
    registry = Registry()
    for path in (ROOT / "contracts/v1").glob("*.schema.json"):
        document = json.loads(path.read_text())
        registry = registry.with_resource(document["$id"], Resource.from_contents(document))
    schema = json.loads((ROOT / f"contracts/v1/{name}.schema.json").read_text())
    return Draft202012Validator(schema, registry=registry)


@pytest.mark.parametrize("preferences", [None, fixture("preferences_default.json")])
def test_actual_unity_request_is_accepted_and_response_matches_contract(preferences):
    body = fixture("analyze_request_synthetic.json")
    body["preferences"] = preferences
    schema_validator("analyze_request").validate(body)
    with client_for() as client:
        response = client.post("/api/v1/analyze", json=body)
    assert response.status_code == 200, response.text
    schema_validator("analysis_response").validate(response.json())
    assert response.json()["session_id"] == body["session_summary"]["session_id"]


def test_synthetic_gemini_path_and_cache_accept_null_preferences():
    body = fixture("analyze_request_synthetic.json")
    body["cloud_policy"] = "synthetic_demo"
    body["preferences"] = None
    adapter = FakeAdapter()
    with client_for(live_settings(), adapter) as client:
        first = client.post("/api/v1/analyze", json=body)
        second = client.post("/api/v1/analyze", json=body)
    assert first.status_code == second.status_code == 200
    assert first.json()["ai_source"] == "live"
    assert second.json()["ai_source"] == "cached"
    assert adapter.calls == 1


@pytest.mark.parametrize("field", ["self_report", "preferences"])
@pytest.mark.parametrize("text", ["", "private text stays on device"])
def test_wire_contract_rejects_free_text_before_any_cloud_call(field, text):
    body = fixture("analyze_request_synthetic.json")
    if field == "self_report":
        body["session_summary"]["self_report"]["free_text"] = text
    else:
        body["preferences"]["text"] = text
    assert not schema_validator("analyze_request").is_valid(body)
    adapter = FakeAdapter()
    with client_for(live_settings(), adapter) as client:
        response = client.post("/api/v1/analyze", json=body)
    assert response.status_code == 422
    assert "private text" not in response.text
    assert adapter.calls == 0


def assert_subset(actual, expected):
    if isinstance(expected, dict):
        for key, value in expected.items():
            assert_subset(actual[key], value)
    elif isinstance(expected, (int, float)) and not isinstance(expected, bool):
        assert actual == pytest.approx(expected)
    else:
        assert actual == expected


@pytest.mark.parametrize("case", CASES, ids=lambda case: case["name"])
def test_shared_unity_and_python_scene_rules(case):
    body = fixture("analyze_request_synthetic.json")
    body["session_summary"]["self_report"]["chips"] = case["chips"]
    body["preferences"] = case["preferences"]
    body["safety_notice"] = case["safety_notice"]
    with client_for() as client:
        response = client.post("/api/v1/analyze", json=body)
    assert response.status_code == 200, response.text
    result = response.json()
    schema_validator("analysis_response").validate(result)
    expected = fixture(case["expected_fixture"]) if "expected_fixture" in case else case["expected"]
    assert_subset(result["scene_config"], expected)


def test_task_duration_contract_keeps_baseline_separate():
    body = fixture("analyze_request_synthetic.json")
    session = body["session_summary"]
    session["duration_ms"] = 20000
    session["baseline"].update(duration_ms=20000, windows=1)
    session["quality"].update(valid_duration_ms=19000, gap_duration_ms=1000)
    session["strain_indicator"].update(level="unavailable", value=None)
    body["reconciliation"] = "unavailable"
    assert AnalysisRequest.model_validate(body).session_summary.duration_ms == 20000
    with client_for() as client:
        response = client.post("/api/v1/analyze", json=body)
    assert response.status_code == 200


def test_unity_request_with_authentication_preserves_local_fallback():
    token = "test-only-backend-token"
    body = fixture("analyze_request_synthetic.json")
    body["preferences"] = None
    with client_for(live_settings(backend_access_token=token), FakeAdapter()) as client:
        assert client.post("/api/v1/analyze", json=body).status_code == 401
        response = client.post(
            "/api/v1/analyze", json=body, headers={"Authorization": f"Bearer {token}"}
        )
    assert response.status_code == 200
    schema_validator("analysis_response").validate(response.json())
