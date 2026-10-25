"""Check a receiver over real HTTP using synthetic input, with cloud calls disabled.

Run on either laptop. The optional token comes from VR_BACKEND_ACCESS_TOKEN or
backend/.env, never a command-line argument. No payloads or secrets are printed.
"""

import argparse
import json
from pathlib import Path

import httpx
from pydantic import ValidationError

from app.config import Settings
from app.models import AnalysisRequest, AnalysisResponse


def check(args):
    path = args.request_file or (
        Path(__file__).resolve().parents[2] / "fixtures/analyze_request_synthetic.json"
    )
    try:
        body = json.loads(path.read_text())
        # Probing must never consume a model attempt, even with a custom request file.
        body["cloud_policy"] = "local_only"
        request = AnalysisRequest.model_validate(body)
        session = request.session_summary
        if session.data_source != "synthetic" and session.replay_origin != "synthetic":
            raise ValueError("Probe requires invented data")
    except (OSError, ValueError, ValidationError):
        print(json.dumps({"ok": False, "error": "invalid_synthetic_probe_request"}))
        return 1
    token = Settings().backend_access_token.get_secret_value()
    headers = {"Authorization": f"Bearer {token}"} if token else {}
    checks = {}
    try:
        with httpx.Client(
            base_url=args.base_url.rstrip("/"), timeout=20, trust_env=False
        ) as client:
            checks["health"] = client.get("/health").status_code == 200
            response = client.post("/api/v1/analyze", json=request.model_dump(), headers=headers)
            checks["http_200"] = response.status_code == 200
            if response.status_code != 200:
                print(json.dumps({"checks": checks, "status": response.status_code}))
                return 1
            result = AnalysisResponse.model_validate(response.json())
            checks["session_preserved"] = result.session_id == session.session_id
            checks["provenance_preserved"] = (
                result.data_source == session.data_source
                and result.replay_origin == session.replay_origin
                and result.quality == session.quality
            )
            checks["local_only"] = result.ai_source == "fallback" and result.model_id is None
            checks["diagnostic_header"] = (
                response.headers.get("X-VR-Fallback-Reason") == "local_only"
            )
            if token:
                checks["missing_token_401"] = (
                    client.post("/api/v1/analyze", json=request.model_dump()).status_code == 401
                )
                checks["wrong_token_401"] = (
                    client.post(
                        "/api/v1/analyze",
                        json=request.model_dump(),
                        headers={"Authorization": "Bearer deliberately-wrong-probe-token"},
                    ).status_code
                    == 401
                )
            invalid = request.model_dump()
            invalid["session_summary"]["duration_ms"] += 1
            checks["semantic_validation_422"] = (
                client.post("/api/v1/analyze", json=invalid, headers=headers).status_code == 422
            )
    except httpx.HTTPError:
        print(json.dumps({"checks": checks, "ok": False, "error": "connection_or_timeout"}))
        return 1
    except (ValueError, ValidationError):
        print(json.dumps({"checks": checks, "ok": False, "error": "invalid_backend_response"}))
        return 1
    print(json.dumps({"checks": checks, "ok": all(checks.values())}, indent=2))
    return 0 if all(checks.values()) else 1


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base-url", default="http://127.0.0.1:8000")
    parser.add_argument("--request-file", type=Path)
    raise SystemExit(check(parser.parse_args()))
