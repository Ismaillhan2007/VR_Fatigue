import json
import os
import subprocess
import sys

import httpx
import pytest
from test_api import settings

from scripts import quest_setup

TOKEN = "invented-private-setup-token"


def body():
    return {"backend_url": "http://100.64.127.140:8147", "backend_token": TOKEN,
            "local_development": True}


def test_export_is_private_and_only_contains_backend_connection(tmp_path, monkeypatch, capsys):
    target = tmp_path / "setup.json"
    monkeypatch.setattr(sys, "argv", ["setup", "--export", str(target),
                                     "--backend-url", body()["backend_url"]])
    monkeypatch.setattr(quest_setup, "Settings", lambda **_: settings(backend_access_token=TOKEN))
    monkeypatch.setattr(quest_setup, "preflight", lambda _: None)
    assert quest_setup.main() == 0
    assert json.loads(target.read_text()) == body()
    if os.name != "nt":
        assert target.stat().st_mode & 0o777 == 0o600
    assert TOKEN not in capsys.readouterr().out


def test_install_sends_secret_via_stdin_only_and_uses_private_app_storage(monkeypatch):
    calls = []

    def run(args, **kwargs):
        calls.append((args, kwargs))
        return subprocess.CompletedProcess(args, 0, stdout=b"", stderr=b"")

    monkeypatch.setattr(quest_setup.subprocess, "run", run)
    quest_setup.install_config("adb", "invented-serial", body())
    args, kwargs = calls[0]
    assert TOKEN not in str(args)
    assert "run-as com.vrfatigue.prototype" in args[-1]
    assert "files/backend-connection.json" in args[-1]
    assert "umask 077" in args[-1]
    assert json.loads(kwargs["input"]) == body()


def test_backend_preflight_only_checks_authenticated_readiness(monkeypatch):
    calls = []

    def handler(request):
        calls.append(request)
        return httpx.Response(200, json={"ready_for_consented_summary": True})

    original = httpx.Client
    monkeypatch.setattr(quest_setup.httpx, "Client", lambda **kw:
                        original(transport=httpx.MockTransport(handler), **kw))
    quest_setup.preflight(body())
    assert len(calls) == 1
    assert calls[0].method == "GET"
    assert calls[0].url.path == "/api/v1/readiness"
    assert calls[0].headers["Authorization"] == "Bearer " + TOKEN


@pytest.mark.parametrize("change", [
    {"local_development": False}, {"cloud_consent": True}, {"GEMINI_API_KEY": TOKEN},
    {"backend_token": ""}, {"backend_url": "http://8.8.8.8:8147"},
    {"backend_url": "https://user:secret@example.com"},
])
def test_unsafe_or_consent_bearing_files_are_rejected(change):
    with pytest.raises(ValueError, match="invalid_setup"):
        quest_setup.validate({**body(), **change})


def test_unauthorized_device_cannot_receive_config(monkeypatch):
    monkeypatch.setattr(quest_setup, "adb_run", lambda *_:
                        b"List of devices attached\ninvented-serial\tunauthorized\n")
    with pytest.raises(ValueError, match="connect_one_authorized_quest"):
        quest_setup.device("adb")


def test_failed_setup_never_prints_child_errors_or_credentials(tmp_path, monkeypatch, capsys):
    target = tmp_path / "setup.json"
    target.write_text(json.dumps(body()))
    monkeypatch.setattr(sys, "argv", ["setup", "--install", str(target), "--adb", "adb"])
    monkeypatch.setattr(quest_setup, "device", lambda *_: "invented-serial")
    monkeypatch.setattr(quest_setup, "preflight", lambda _: None)

    def fail(*_):
        raise ValueError("private-provider-error-" + TOKEN)

    monkeypatch.setattr(quest_setup, "install_config", fail)
    assert quest_setup.main() == 1
    output = capsys.readouterr().out
    assert TOKEN not in output
    assert "private-provider-error" not in output
