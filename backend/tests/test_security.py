import asyncio
import copy
import json
from types import SimpleNamespace

import pytest
from google.genai.errors import ClientError, ServerError
from test_api import FakeAdapter, client_for, live_settings, settings
from test_api import body as body

from app.fallback import local_scene
from app.models import AnalysisRequest
from app.service import AnalysisService


@pytest.mark.parametrize("authorization", [None, "Bearer incorrect", "Basic placeholder"])
def test_access_token_rejects_unauthorized_calls_before_generation(body, authorization):
    adapter = FakeAdapter()
    token = "backend-test-token-separate-from-provider-key"
    with client_for(live_settings(backend_access_token=token), adapter) as client:
        headers = {"Authorization": authorization} if authorization else {}
        response = client.post("/api/v1/analyze", json=body, headers=headers)
        assert client.app.state.analysis_service.attempts == 0
    assert response.status_code == 401
    assert response.headers["www-authenticate"] == "Bearer"
    assert token not in response.text
    assert adapter.calls == 0


def test_access_token_accepts_valid_client_and_health_stays_public(body):
    token = "backend-test-token-separate-from-provider-key"
    with client_for(settings(backend_access_token=token)) as client:
        response = client.post(
            "/api/v1/analyze", json=body, headers={"Authorization": f"Bearer {token}"}
        )
        assert client.get("/health").status_code == 200
        assert token not in client.get("/openapi.json").text
    assert response.status_code == 200


@pytest.mark.parametrize("length", ["20000", "1", None])
def test_actual_body_limit_cannot_be_bypassed_with_headers(body, length):
    adapter = FakeAdapter()
    headers = {"Content-Type": "application/json"}
    if length is not None:
        headers["Content-Length"] = length
    # The iterable sends a chunked body when Content-Length is omitted.
    chunks = iter([b" " * 9000, b" " * 9000])
    with client_for(live_settings(), adapter) as client:
        response = client.post("/api/v1/analyze", content=chunks, headers=headers)
    assert response.status_code == 413
    assert adapter.calls == 0


@pytest.mark.parametrize("length", ["-1", "not-a-number", "+1", "1_000", "1.0"])
def test_malformed_content_length_returns_safe_error(length):
    with client_for() as client:
        response = client.post("/api/v1/analyze", content=b"{}", headers={"Content-Length": length})
    assert response.status_code == 400


def test_validation_errors_do_not_echo_values_or_unknown_field_names(body):
    body["preferences"]["private-field-marker"] = "private-input-marker"
    body["session_summary"]["self_report"]["free_text"] = "private-text-marker" * 30
    with client_for() as client:
        response = client.post("/api/v1/analyze", json=body)
    assert response.status_code == 422
    assert all(
        marker not in response.text
        for marker in ("private-field-marker", "private-input-marker", "private-text-marker")
    )
    assert "<unknown_field>" in response.text


@pytest.mark.parametrize(
    "mutation",
    [
        "script",
        "raw_stream",
        "text_too_long",
        "duplicate_chips",
        "too_many_chips",
        "string_boolean",
        "negative_metric",
        "infinite_metric",
        "nan_scene_preference",
    ],
)
def test_hostile_request_is_rejected_without_provider_calls(body, mutation):
    if mutation == "script":
        body["preferences"]["script"] = "execute-untrusted-code"
    elif mutation == "raw_stream":
        body["session_summary"]["raw_frames"] = ["private-eye-frame"]
    elif mutation == "text_too_long":
        body["preferences"]["text"] = "x" * 301
    elif mutation == "duplicate_chips":
        body["session_summary"]["self_report"]["chips"] = ["want_quiet", "want_quiet"]
    elif mutation == "too_many_chips":
        body["session_summary"]["self_report"]["chips"] = [
            "tired_eyes",
            "tired_mind",
            "tense",
            "restless",
            "low_energy",
            "want_quiet",
            "want_dim",
        ]
    elif mutation == "string_boolean":
        body["preferences"]["animals_enabled"] = "true"
    elif mutation == "negative_metric":
        body["session_summary"]["metrics"]["probe_latency_ms"] = -1
    elif mutation == "infinite_metric":
        body["session_summary"]["metrics"]["probe_latency_ms"] = float("inf")
    else:
        body["preferences"]["motion"] = float("nan")
    adapter = FakeAdapter()
    with client_for(live_settings(), adapter) as client:
        response = client.post(
            "/api/v1/analyze",
            content=json.dumps(body),
            headers={"Content-Type": "application/json"},
        )
    assert response.status_code == 422
    assert adapter.calls == 0


@pytest.mark.parametrize(
    "mutation",
    ["script", "asset_url", "other_scene", "brightness", "motion_speed", "particles", "nonfinite"],
)
def test_hostile_model_settings_fall_back_without_executing_or_echoing(body, mutation):
    class HostileAdapter:
        async def generate(self, request, observations):
            scene = local_scene(request).model_dump()
            field, value = {
                "script": ("script", "private-provider-body-marker"),
                "asset_url": ("asset_url", "https://untrusted.invalid/asset"),
                "other_scene": ("scene_id", "untrusted_scene"),
                "brightness": ("brightness", 0.0),
                "motion_speed": ("motion_speed", 5.0),
                "particles": ("particles", ["untrusted_effect"]),
                "nonfinite": ("fog_density", float("nan")),
            }[mutation]
            scene[field] = value
            payload = {"observation_codes": [o.code for o in observations], "scene_config": scene}
            return SimpleNamespace(model_dump=lambda: payload)

    with client_for(live_settings(), HostileAdapter()) as client:
        response = client.post("/api/v1/analyze", json=body)
    assert response.status_code == 200
    result = response.json()
    assert result["ai_source"] == "fallback" and result["model_id"] is None
    assert result["scene_config"] == local_scene(AnalysisRequest.model_validate(body)).model_dump()
    assert "private-provider-body-marker" not in response.text
    assert "untrusted.invalid" not in response.text


@pytest.mark.parametrize("status", [400, 401, 403, 429, 500, 503])
def test_real_sdk_error_types_return_private_local_fallback(body, status, caplog):
    class ErrorAdapter:
        async def generate(self, *_args):
            kind = ClientError if status < 500 else ServerError
            raise kind(status, {"error": {"message": "private-provider-body-marker"}})

    with client_for(live_settings(), ErrorAdapter()) as client:
        response = client.post("/api/v1/analyze", json=body)
    assert response.status_code == 200
    assert response.json()["ai_source"] == "fallback"
    assert "private-provider-body-marker" not in response.text + caplog.text


def test_queued_request_deadline_includes_lock_wait_without_spending(body):
    async def check():
        adapter = FakeAdapter()
        service = AnalysisService(live_settings(gemini_timeout_seconds=0.01), adapter)
        await service.lock.acquire()
        try:
            # Outer timeout prevents a regression from hanging the test indefinitely.
            result = await asyncio.wait_for(
                service.analyze(AnalysisRequest.model_validate(body)), timeout=0.2
            )
        finally:
            service.lock.release()
        assert result.ai_source == "fallback"
        assert adapter.calls == 0 and service.attempts == 0
        # A timed-out waiter must not corrupt the lock or subsequent requests.
        result = await service.analyze(AnalysisRequest.model_validate(copy.deepcopy(body)))
        assert result.ai_source == "live"

    asyncio.run(check())
