"""Hosted startup/auth/input policy without changing the v1 Unity contract."""

import copy

import pytest
from pydantic import ValidationError
from test_api import FakeAdapter, client_for, fixture, settings

from app.config import Settings
from app.main import create_app
from app.serve import main

TOKEN = "synthetic-hosting-test-token"


@pytest.mark.parametrize("token", ["", " ", "\t\n"])
def test_hosted_startup_refuses_missing_or_blank_access_token(token):
    with pytest.raises(RuntimeError, match="required in hosted mode"):
        create_app(settings(runtime_mode="hosted", backend_access_token=token))


def test_local_mode_still_supports_offline_unauthenticated_starter():
    with client_for(settings(runtime_mode="local", backend_access_token="")) as client:
        result = client.post("/api/v1/analyze", json=fixture("analyze_request_synthetic.json"))
    assert result.status_code == 200
    assert result.json()["ai_source"] == "fallback"


def test_invalid_runtime_mode_error_does_not_echo_environment_input():
    with pytest.raises(ValidationError) as error:
        Settings(_env_file=None, runtime_mode="private-invalid-environment-marker")
    assert "private-invalid-environment-marker" not in str(error.value)


@pytest.mark.parametrize("source,origin", [("synthetic", None), ("replay", "synthetic")])
def test_hosted_accepts_current_unity_contract_with_runtime_auth(source, origin):
    request = fixture("analyze_request_synthetic.json")
    request["session_summary"].update(data_source=source, replay_origin=origin)
    adapter = FakeAdapter()
    config = settings(runtime_mode="hosted", backend_access_token=TOKEN)
    with client_for(config, adapter) as client:
        assert client.get("/health").json()["gemini_enabled"] is False
        for headers in ({}, {"Authorization": "Bearer incorrect"}):
            denied = client.post("/api/v1/analyze", json=request, headers=headers)
            assert denied.status_code == 401
            assert TOKEN not in denied.text
        headers = {"Authorization": f"Bearer {TOKEN}"}
        result = client.post("/api/v1/analyze", json=request, headers=headers)
        assert result.status_code == 200
        response = result.json()
        for field in ("session_id", "data_source", "replay_origin", "quality"):
            assert response[field] == request["session_summary"][field]
        assert result.headers["X-VR-Fallback-Reason"] == "local_only"
        assert response["ai_source"] == "fallback" and response["model_id"] is None
        request["session_summary"]["duration_ms"] += 1
        invalid = client.post("/api/v1/analyze", json=request, headers=headers)
        assert invalid.status_code == 422
        assert "input" not in invalid.json()["detail"][0]
    assert adapter.calls == 0


@pytest.mark.parametrize("source,origin", [("live", None), ("replay", "recorded")])
def test_hosted_rejects_participant_eye_input_before_analysis(source, origin):
    request = fixture("analyze_request_synthetic.json")
    request["session_summary"].update(data_source=source, replay_origin=origin)
    config = settings(runtime_mode="hosted", backend_access_token=TOKEN)
    adapter = FakeAdapter()
    with client_for(config, adapter) as client:
        result = client.post(
            "/api/v1/analyze", json=request, headers={"Authorization": f"Bearer {TOKEN}"}
        )
        assert not client.app.state.analysis_service.cache
        assert client.app.state.analysis_service.attempts == 0
    assert result.status_code == 403
    assert result.json() == {
        "detail": "Participant summaries require explicit cloud consent and server enablement"
    }
    assert adapter.calls == 0


@pytest.mark.parametrize("source,origin", [("live", None), ("replay", "recorded")])
def test_local_participant_path_remains_usable_without_gemini(source, origin):
    request = copy.deepcopy(fixture("analyze_request_synthetic.json"))
    request["session_summary"].update(data_source=source, replay_origin=origin)
    with client_for(settings(runtime_mode="local", backend_access_token="")) as client:
        result = client.post("/api/v1/analyze", json=request)
    assert result.status_code == 200
    assert result.json()["data_source"] == source
    assert result.json()["ai_source"] == "fallback"


@pytest.mark.parametrize("port", [None, "8127"])
def test_container_entrypoint_uses_platform_port_and_one_worker(monkeypatch, port):
    if port is None:
        monkeypatch.delenv("PORT", raising=False)
    else:
        monkeypatch.setenv("PORT", port)
    # The entrypoint must not inherit a host's multi-worker setting.
    monkeypatch.setenv("WEB_CONCURRENCY", "8")
    calls = []
    monkeypatch.setattr("app.serve.uvicorn.run", lambda *a, **kw: calls.append((a, kw)))
    assert main() == 0
    assert calls == [
        (
            ("app.main:app",),
            {
                "host": "0.0.0.0",
                "port": int(port or 8000),
                "workers": 1,
                "access_log": False,
                "proxy_headers": False,
            },
        )
    ]


@pytest.mark.parametrize("port", ["", "0", "65536", "-1", "1.2", "８０００", "secret-marker"])
def test_invalid_platform_port_never_starts_or_echoes_its_value(monkeypatch, capsys, port):
    monkeypatch.setenv("PORT", port)
    monkeypatch.setattr("app.serve.uvicorn.run", lambda *a, **kw: pytest.fail("must not start"))
    assert main() == 1
    assert capsys.readouterr().err == "PORT must be an integer between 1 and 65535\n"
