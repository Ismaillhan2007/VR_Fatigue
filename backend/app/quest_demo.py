"""Launch the authenticated Quest LAN backend using an explicitly configured allowance."""

import argparse
from pathlib import Path

import uvicorn

from app.config import Settings
from app.main import create_app


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--env-file", type=Path, default=Path(__file__).resolve().parents[1] / ".env"
    )
    parser.add_argument("--port", type=int, default=8147)
    args = parser.parse_args()
    settings = Settings(_env_file=args.env_file)
    if not settings.backend_access_token.get_secret_value().strip():
        parser.error("Set VR_BACKEND_ACCESS_TOKEN in the private env file")
    if not settings.gemini_ledger_path or settings.gemini_max_total_attempts is None:
        parser.error("Set a persistent VR_GEMINI_LEDGER_PATH and VR_GEMINI_MAX_TOTAL_ATTEMPTS")
    app = create_app(settings)
    reason = app.state.analysis_service.readiness_reason()
    if reason:
        parser.error("Gemini is not ready: " + reason)
    print(f"Quest backend ready on port {args.port}. Use this laptop's LAN IP on the headset.")
    print("Consent is required for each session. Readiness does not make a paid provider call.")
    uvicorn.run(app, host="0.0.0.0", port=args.port, access_log=False, proxy_headers=False)


if __name__ == "__main__":
    main()
