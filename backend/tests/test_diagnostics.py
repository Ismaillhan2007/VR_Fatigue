"""Wire diagnostics are finite codes, isolated per request, and never echo payloads."""

import asyncio
import copy

import pytest
from google.genai.errors import ClientError, ServerError
from test_api import FakeAdapter, client_for, live_settings, settings
from test_api import body as body

from app.gemini import build_prompt
from app.models import AnalysisRequest
from app.service import AnalysisService

HEADER = "X-VR-Fallback-Reason"


@pytest.mark.parametrize("policy", [None, "local_only"])
def test_legacy_and_participant_checkins_never_use_cloud_even_with_synthetic_eyes(body, policy):
    if policy is None:
        body.pop("cloud_policy")
    else:
        body["cloud_policy"] = policy
    adapter = FakeAdapter()
    with client_for(live_settings(), adapter) as client:
        response = client.post("/api/v1/analyze", json=body)
        assert client.app.state.analysis_service.attempts == 0
        assert not client.app.state.analysis_service.cache
    assert response.status_code == 200
    assert response.headers[HEADER] == "local_only"
    assert response.json()["ai_source"] == "fallback"
    assert adapter.calls == 0
    with pytest.raises(ValueError):
        build_prompt(AnalysisRequest.model_validate(body), [])


@pytest.mark.parametrize("policy", [True, "participant_consent", None, 1])
def test_invalid_cloud_policy_rejected(body, policy):
    body["cloud_policy"] = policy
    with client_for() as client:
        assert client.post("/api/v1/analyze", json=body).status_code == 422


@pytest.mark.parametrize(
    ("config", "reason"),
    [
        (settings(), "gemini_disabled"),
        (live_settings(GEMINI_API_KEY=""), "gemini_not_configured"),
        (live_settings(gemini_max_attempts_per_day=0), "attempt_limit"),
        (live_settings(input_price_per_million="1"), "budget_limit"),
    ],
)
def test_configuration_and_limit_reasons_do_not_reserve_attempts(body, config, reason):
    adapter = FakeAdapter()
    with client_for(config, adapter) as client:
        response = client.post("/api/v1/analyze", json=body)
        assert client.app.state.analysis_service.attempts == 0
    assert response.headers[HEADER] == reason
    assert adapter.calls == 0


@pytest.mark.parametrize(
    ("behavior", "reason"), [("timeout", "timeout"), ("invalid", "invalid_output")]
)
def test_timeout_and_invalid_output_have_safe_diagnostics(body, behavior, reason):
    with client_for(live_settings(gemini_timeout_seconds=0.01), FakeAdapter(behavior)) as client:
        response = client.post("/api/v1/analyze", json=body)
    assert response.headers[HEADER] == reason
    assert response.json()["ai_source"] == "fallback"


@pytest.mark.parametrize(
    ("status", "reason"),
    [(403, "provider_error"), (429, "provider_rate_limited"), (503, "provider_unavailable")],
)
def test_provider_diagnostics_never_expose_exception_bodies(body, status, reason):
    class ErrorAdapter:
        async def generate(self, *_args):
            kind = ClientError if status < 500 else ServerError
            raise kind(status, {"error": {"message": "private-provider-marker"}})

    with client_for(live_settings(), ErrorAdapter()) as client:
        response = client.post("/api/v1/analyze", json=body)
    assert response.headers[HEADER] == reason
    assert "private-provider-marker" not in response.text + str(response.headers)


def test_live_and_cached_responses_have_no_fallback_header(body):
    with client_for(live_settings(), FakeAdapter()) as client:
        first = client.post("/api/v1/analyze", json=body)
        cached = client.post("/api/v1/analyze", json=body)
    assert first.json()["ai_source"] == "live"
    assert cached.json()["ai_source"] == "cached"
    assert HEADER not in first.headers and HEADER not in cached.headers


def test_request_outcomes_are_isolated_during_concurrent_analysis(body):
    async def run():
        adapter = FakeAdapter()
        service = AnalysisService(live_settings(), adapter)
        local = copy.deepcopy(body)
        local["cloud_policy"] = "local_only"
        outcomes = await asyncio.gather(
            service.analyze_with_diagnostics(AnalysisRequest.model_validate(local)),
            service.analyze_with_diagnostics(AnalysisRequest.model_validate(body)),
        )
        assert outcomes[0].fallback_reason == "local_only"
        assert outcomes[1].fallback_reason is None
        assert adapter.calls == 1

    asyncio.run(run())


@pytest.mark.parametrize("case", ["real", "recorded", "stop", "quality"])
def test_privacy_safety_and_quality_reasons_prevent_cloud_calls(body, case):
    expected = {
        "real": "non_synthetic_input",
        "recorded": "non_synthetic_input",
        "stop": "safety_stop",
        "quality": "insufficient_data",
    }[case]
    session = body["session_summary"]
    if case == "real":
        session.update(data_source="live", replay_origin=None)
    elif case == "recorded":
        session.update(data_source="replay", replay_origin="recorded")
    elif case == "stop":
        body["safety_notice"] = "stop_session"
    else:
        session["quality"]["status"] = "insufficient_data"
        session["strain_indicator"].update(level="unavailable", value=None)
        body["reconciliation"] = "unavailable"
    adapter = FakeAdapter()
    with client_for(live_settings(), adapter) as client:
        response = client.post("/api/v1/analyze", json=body)
    assert response.status_code == 200
    assert response.headers[HEADER] == expected
    assert adapter.calls == 0
