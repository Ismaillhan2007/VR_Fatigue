"""Real loopback HTTP checks; provider calls are disabled, including with a local .env."""

import json
import os
import socket
import subprocess
import sys
import threading
import time
from contextlib import contextmanager

import httpx
import pytest
import uvicorn
from test_api import settings

from app.main import create_app


@contextmanager
def receiver(token=""):
    app = create_app(settings(backend_access_token=token))
    with socket.socket() as sock:
        sock.bind(("127.0.0.1", 0))
        sock.listen(16)
        port = sock.getsockname()[1]
        server = uvicorn.Server(uvicorn.Config(app, log_level="error", access_log=False))
        thread = threading.Thread(target=server.run, kwargs={"sockets": [sock]}, daemon=True)
        thread.start()
        try:
            deadline = time.monotonic() + 5
            while not server.started:
                if not thread.is_alive() or time.monotonic() >= deadline:
                    raise RuntimeError("Test receiver did not start")
                time.sleep(0.01)
            yield f"http://127.0.0.1:{port}"
        finally:
            server.should_exit = True
            thread.join(timeout=5)
            assert not thread.is_alive(), "Test receiver failed to stop"


@pytest.mark.parametrize("token", ["", "test-only-wire-token"])
def test_real_http_receiver_accepts_current_unity_fixture_and_enforces_auth(token):
    from test_api import fixture

    request = fixture("analyze_request_synthetic.json")
    with receiver(token) as url, httpx.Client(base_url=url, timeout=5, trust_env=False) as client:
        assert client.get("/health").status_code == 200
        headers = {"Authorization": f"Bearer {token}"} if token else {}
        response = client.post("/api/v1/analyze", json=request, headers=headers)
        assert response.status_code == 200
        assert response.headers["X-VR-Fallback-Reason"] == "local_only"
        assert response.json()["session_id"] == request["session_summary"]["session_id"]
        assert response.json()["ai_source"] == "fallback"
        if token:
            for bad_headers in ({}, {"Authorization": "Bearer wrong-test-token"}):
                denied = client.post("/api/v1/analyze", json=request, headers=bad_headers)
                assert denied.status_code == 401
                assert "X-VR-Fallback-Reason" not in denied.headers
        request["session_summary"]["duration_ms"] += 1
        invalid = client.post("/api/v1/analyze", json=request, headers=headers)
        assert invalid.status_code == 422
        assert "input" not in invalid.json()["detail"][0]


def test_receiver_probe_cli_uses_runtime_token_without_printing_it():
    token = "probe-cli-test-token"
    with receiver(token) as url:
        result = subprocess.run(
            [sys.executable, "-m", "scripts.probe_backend", "--base-url", url],
            env={**os.environ, "VR_BACKEND_ACCESS_TOKEN": token},
            capture_output=True,
            text=True,
            timeout=15,
        )
    assert result.returncode == 0, result.stdout + result.stderr
    assert json.loads(result.stdout)["ok"] is True
    assert token not in result.stdout + result.stderr
