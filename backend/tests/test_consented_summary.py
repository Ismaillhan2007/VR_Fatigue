"""Invented fixtures test explicit consent without sending participant data."""

import asyncio
import json

import pytest
from fastapi.testclient import TestClient
from test_api import FakeAdapter, fixture, live_settings

from app.fallback import local_response
from app.gemini import build_prompt
from app.main import create_app
from app.models import AnalysisRequest
from app.service import AnalysisService


def test_participant_cloud_server_requires_authentication_configuration():
    with pytest.raises(RuntimeError):
        create_app(live_settings(allow_participant_cloud=True))


@pytest.mark.parametrize(
    "source,origin", [("live", None), ("replay", "recorded"), ("synthetic", None)]
)
def test_consented_summaries_require_both_server_and_request_opt_in_and_are_not_cached(
    source, origin
):
    body = fixture("analyze_request_consented_summary.json")
    body["session_summary"].update(data_source=source, replay_origin=origin)
    request = AnalysisRequest.model_validate(body)
    blocked_adapter = FakeAdapter()
    blocked = AnalysisService(live_settings(), blocked_adapter)
    result = asyncio.run(blocked.analyze_with_diagnostics(request))
    assert result.fallback_reason == "participant_cloud_disabled"
    assert blocked_adapter.calls == 0
    adapter = FakeAdapter()
    service = AnalysisService(live_settings(allow_participant_cloud=True), adapter)
    for _ in range(2):
        result = asyncio.run(service.analyze_with_diagnostics(request))
        assert result.response.ai_source == "live"
        assert result.response.data_source == source
        assert result.response.replay_origin == origin
        assert result.response.session_id == request.session_summary.session_id
    assert adapter.calls == 2
    assert not service.cache
    local = request.model_copy(update={"cloud_policy": "local_only"})
    assert asyncio.run(service.analyze(local)).ai_source == "fallback"
    assert adapter.calls == 2


def test_hosted_accepts_consent_only_with_server_flag_auth_and_valid_contract():
    body = fixture("analyze_request_consented_summary.json")
    body["session_summary"].update(data_source="live", replay_origin=None)
    config = live_settings(
        runtime_mode="hosted",
        backend_access_token="invented-consent-token",
        allow_participant_cloud=True,
    )
    adapter = FakeAdapter()
    with TestClient(create_app(config, adapter)) as client:
        assert client.post("/api/v1/analyze", json=body).status_code == 401
        result = client.post(
            "/api/v1/analyze", json=body, headers={"Authorization": "Bearer invented-consent-token"}
        )
        assert result.status_code == 200
        assert result.json()["ai_source"] == "live"
        from app.models import AnalysisResponse

        AnalysisResponse.model_validate(result.json())
        body["session_summary"]["self_report"]["free_text"] = "invented-private-marker"
        assert (
            client.post(
                "/api/v1/analyze",
                json=body,
                headers={"Authorization": "Bearer invented-consent-token"},
            ).status_code
            == 422
        )
    assert adapter.calls == 1


def test_consented_prompt_contains_only_aggregates_and_ratings_without_identity_or_raw_streams():
    body = fixture("analyze_request_consented_summary.json")
    body["session_summary"].update(data_source="live", replay_origin=None)
    request = AnalysisRequest.model_validate(body)
    prompt = build_prompt(request, local_response(request).observations)
    data = json.loads(prompt.split("\n", 1)[1])
    assert data["input_is_synthetic"] is False
    assert data["self_report"]["eye_strain"] == request.session_summary.self_report.eye_strain
    assert request.session_summary.session_id not in prompt
    assert not {"session_id", "free_text", "samples", "timestamps", "device_id"} & data.keys()
    assert "free_text" not in data["self_report"]
