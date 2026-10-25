"""Manual synthetic smoke check; one attempt, explicit prices/budget, no secret output.

Run from backend with `python -m scripts.smoke_gemini --help`. Settings are temporary.
"""

import argparse
import asyncio
import copy
import json
import logging
from decimal import Decimal
from pathlib import Path

from jsonschema import Draft202012Validator
from pydantic import ValidationError
from referencing import Registry, Resource

from app.config import Settings
from app.gemini import GeminiAdapter
from app.models import AnalysisRequest
from app.service import AnalysisService


class ProbeAdapter(GeminiAdapter):
    def __init__(self, settings):
        super().__init__(settings)
        self.calls = 0
        self.failure = None

    async def generate(self, request, observations):
        self.calls += 1
        try:
            return await super().generate(request, observations)
        except Exception as exc:
            self.failure = {"type": type(exc).__name__, "status_code": getattr(exc, "code", None)}
            if isinstance(exc, ValidationError):
                self.failure["validation_errors"] = [
                    {"loc": list(error["loc"]), "type": error["type"]}
                    for error in exc.errors(include_input=False, include_context=False)
                ]
            raise


async def check(args):
    settings = Settings(
        gemini_enabled=True,
        GEMINI_MODEL=args.model,
        input_price_per_million=args.input_price,
        output_price_per_million=args.output_price,
        gemini_budget_usd=args.budget_usd,
        gemini_max_attempts_per_day=1,
        gemini_timeout_seconds=15,
    )
    root = Path(__file__).resolve().parents[2]
    body = {
        "schema_version": "1.0",
        "session_summary": json.loads(
            (root / "fixtures/session_synthetic_usable.json").read_text()
        ),
        "preferences": json.loads((root / "fixtures/preferences_default.json").read_text()),
        "reconciliation": "agree_high",
        "safety_notice": "none",
    }
    body["session_summary"]["self_report"]["free_text"] = None
    body["preferences"].update(animals_enabled=False, sound="silent", motion="none")
    adapter = ProbeAdapter(settings)
    service = AnalysisService(settings, adapter)
    first = await service.analyze(AnalysisRequest.model_validate(body))
    cached_body = copy.deepcopy(body)
    cached_body["session_summary"]["session_id"] = "00000000-0000-4000-8000-000000000003"
    cached = await service.analyze(AnalysisRequest.model_validate(cached_body))
    real_body = copy.deepcopy(body)
    real_body["session_summary"].update(data_source="live", replay_origin=None)
    real_body["session_summary"]["capabilities"]["native_eye_gaze"] = True
    real = await service.analyze(AnalysisRequest.model_validate(real_body))
    registry = Registry()
    for path in (root / "contracts/v1").glob("*.schema.json"):
        schema = json.loads(path.read_text())
        registry = registry.with_resource(schema["$id"], Resource.from_contents(schema))
    schema = json.loads((root / "contracts/v1/analysis_response.schema.json").read_text())
    validator = Draft202012Validator(schema, registry=registry)
    checks = {
        "live_generation": first.ai_source == "live",
        "cache_hit": cached.ai_source == "cached" and adapter.calls == 1,
        "cache_session_rebound": cached.session_id == cached_body["session_summary"]["session_id"],
        "real_data_stays_local": real.ai_source == "fallback",
        "shared_contracts": all(validator.is_valid(r.model_dump()) for r in (first, cached, real)),
        "preference_overrides": (
            first.scene_config.sound == "silent"
            and not first.scene_config.animals_enabled
            and first.scene_config.motion == "none"
        ),
    }
    print(
        json.dumps(
            {
                "model": settings.gemini_model,
                "checks": checks,
                "adapter_attempts": adapter.calls,
                "reserved_worst_case_usd": str(service.reserved_usd),
                "usage": adapter.last_usage,
                "provider_error": adapter.failure,
            },
            indent=2,
        )
    )
    return 0 if all(checks.values()) else 1


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model", required=True)
    parser.add_argument("--input-price", type=Decimal, required=True, help="USD per million tokens")
    parser.add_argument(
        "--output-price", type=Decimal, required=True, help="USD per million tokens"
    )
    parser.add_argument("--budget-usd", type=Decimal, required=True)
    args = parser.parse_args()
    # Third-party HTTP debug logs may contain authentication headers or request bodies.
    logging.disable(logging.CRITICAL)
    raise SystemExit(asyncio.run(check(args)))
