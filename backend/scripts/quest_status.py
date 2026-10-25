"""Check Quest backend/auth/remaining allowance without a paid request or report upload."""

import argparse
import json
from pathlib import Path

import httpx

from app.config import Settings


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base-url", default="http://127.0.0.1:8147")
    parser.add_argument(
        "--env-file", type=Path, default=Path(__file__).resolve().parents[1] / ".env"
    )
    args = parser.parse_args()
    settings = Settings(_env_file=args.env_file)
    token = settings.backend_access_token.get_secret_value()
    try:
        with httpx.Client(timeout=5, trust_env=False) as client:
            response = client.get(
                args.base_url.rstrip("/") + "/api/v1/readiness",
                headers={"Authorization": "Bearer " + token},
            )
        if response.status_code != 200:
            print(json.dumps({"ready": False, "http_status": response.status_code}))
            return 1
        body = response.json()
        # Finite fields only; do not echo URLs, token, arbitrary server bodies or errors.
        reason = body.get("reason")
        allowed = {
            None, "gemini_disabled", "gemini_not_configured", "participant_cloud_disabled",
            "attempt_limit", "budget_limit", "allowance_unavailable",
        }
        ready = body.get("ready_for_consented_summary") is True
        print(json.dumps({
            "ready": ready, "reason": reason if reason in allowed else "unknown",
            "persistent_allowance": body.get("persistent_allowance") is True,
            "provider_verified_by_this_check": False,
        }))
        return 0 if ready else 1
    except (httpx.HTTPError, ValueError, TypeError):
        print(json.dumps({"ready": False, "reason": "connection_or_response_failed"}))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
