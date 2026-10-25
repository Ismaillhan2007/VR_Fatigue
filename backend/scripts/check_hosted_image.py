"""Exercise the actual deployment image over HTTP with invented data only.

Requires Docker and the backend dev environment. Creates/removes its own containers;
the temporary token is supplied by environment name, never as a command argument.
"""

import argparse
import copy
import json
import os
import secrets
import subprocess
import sys
import time
from pathlib import Path

import httpx


def docker(*args, env=None, timeout=30):
    return subprocess.run(
        ["docker", *args], env=env, capture_output=True, text=True, timeout=timeout
    )


def check(image):
    root = Path(__file__).resolve().parents[2]
    name = "vr-fatigue-image-check-" + secrets.token_hex(6)
    token = secrets.token_urlsafe(32)
    env = {
        **os.environ,
        "VR_BACKEND_ACCESS_TOKEN": token,
        "VR_GEMINI_ENABLED": "false",
        "VR_RUNTIME_MODE": "local",  # The checkout-side probe, not the container.
    }
    checks = {}
    try:
        missing = docker(
            "run", "--rm", "--env", "VR_BACKEND_ACCESS_TOKEN=", image, env=env
        )
        checks["missing_token_refuses_startup"] = (
            missing.returncode != 0 and "required in hosted mode" in missing.stderr
        )
        started = docker(
            "run", "--detach", "--name", name, "--publish", "127.0.0.1::8127",
            "--env", "PORT=8127", "--env", "VR_BACKEND_ACCESS_TOKEN",
            "--read-only", "--tmpfs", "/tmp:rw,noexec,nosuid,size=16m",
            "--cap-drop", "ALL", "--security-opt", "no-new-privileges", image, env=env,
        )
        if started.returncode:
            raise RuntimeError("image_container_did_not_start")
        mapped = docker("port", name, "8127/tcp")
        if mapped.returncode:
            raise RuntimeError("image_port_not_published")
        address = mapped.stdout.strip().splitlines()[0]
        url = "http://" + address
        deadline = time.monotonic() + 30
        with httpx.Client(base_url=url, timeout=3, trust_env=False) as client:
            while True:
                try:
                    health = client.get("/health")
                    if health.status_code == 200:
                        break
                except httpx.HTTPError:
                    pass
                if time.monotonic() >= deadline:
                    raise RuntimeError("image_health_deadline")
                time.sleep(0.1)
            checks["platform_port_and_health"] = health.json()["gemini_enabled"] is False
            # Use the same standalone probe that teammates will run against HTTPS staging.
            probe = subprocess.run(
                [sys.executable, "-m", "scripts.probe_backend", "--base-url", url],
                cwd=root / "backend", env=env, capture_output=True, text=True, timeout=30,
            )
            checks["unity_wire_auth_200_401_422_and_provenance"] = (
                probe.returncode == 0 and json.loads(probe.stdout).get("ok") is True
            )
            headers = {"Authorization": f"Bearer {token}"}
            request = json.loads((root / "fixtures/analyze_request_synthetic.json").read_text())
            denied = []
            for source, origin in (("live", None), ("replay", "recorded")):
                body = copy.deepcopy(request)
                body["session_summary"].update(data_source=source, replay_origin=origin)
                denied.append(client.post("/api/v1/analyze", json=body, headers=headers))
            checks["participant_eye_input_rejected"] = all(r.status_code == 403 for r in denied)
            checks["token_not_in_responses_or_probe_output"] = token not in (
                health.text + probe.stdout + probe.stderr + "".join(r.text for r in denied)
            )
        inspected = docker(
            "exec", name, "python", "-c",
            "import importlib.util, os; from pathlib import Path; "
            "assert os.getuid() != 0; "
            "assert not list(Path('/app').rglob('.env*')); "
            "assert not Path('/app/tests').exists(); "
            "assert not Path('/app/fixtures').exists(); "
            "assert importlib.util.find_spec('pytest') is None",
        )
        checks["nonroot_image_excludes_dotenv_fixtures_and_dev_packages"] = (
            inspected.returncode == 0
        )
        # Execute the exact Docker health command, including its platform PORT lookup.
        health_command = docker("inspect", "--format", "{{json .Config.Healthcheck.Test}}", name)
        command = json.loads(health_command.stdout)
        checks["docker_healthcheck"] = (
            command[0] == "CMD" and docker("exec", name, *command[1:]).returncode == 0
        )
    except (OSError, RuntimeError, ValueError, httpx.HTTPError, subprocess.SubprocessError):
        # No Docker logs, commands, credential values or provider payloads in diagnostics.
        checks["image_smoke_completed"] = False
    finally:
        docker("rm", "--force", name)
    print(json.dumps({"checks": checks, "ok": bool(checks) and all(checks.values())}, indent=2))
    return 0 if checks and all(checks.values()) else 1


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", required=True)
    raise SystemExit(check(parser.parse_args().image))
