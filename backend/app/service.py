import asyncio
import hashlib
import json
import time
from collections import OrderedDict
from dataclasses import dataclass
from datetime import datetime, timezone
from decimal import Decimal
from typing import Protocol

from google.genai.errors import APIError

from app.allowance import AllowanceLedger
from app.config import Settings
from app.fallback import constrain_scene, local_response, narrative_for, rationale_for
from app.gemini import MAX_INPUT_TOKENS, MAX_OUTPUT_TOKENS, PROMPT_VERSION, GeminiAdapter
from app.models import AnalysisRequest, AnalysisResponse, GeminiChoice, Observation


class Adapter(Protocol):
    async def generate(
        self, request: AnalysisRequest, observations: list[Observation]
    ) -> GeminiChoice: ...


@dataclass(frozen=True)
class AnalysisOutcome:
    response: AnalysisResponse
    fallback_reason: str | None = None


class AnalysisService:
    """Single-process demo service; conservative in-memory cost/attempt reservations."""

    def __init__(self, settings: Settings, adapter: Adapter | None = None):
        self.settings = settings
        self.adapter = adapter or GeminiAdapter(settings)
        self.lock = asyncio.Lock()
        self.day = ""
        self.attempts = 0
        self.reserved_usd = Decimal("0")
        self.cache: OrderedDict[str, tuple[float, AnalysisResponse]] = OrderedDict()
        self.ledger = AllowanceLedger(settings) if settings.gemini_ledger_path else None
        self.total_attempts = 0

    def attempt_cost(self) -> Decimal:
        return (
            MAX_INPUT_TOKENS * self.settings.input_price_per_million
            + MAX_OUTPUT_TOKENS * self.settings.output_price_per_million
        ) / Decimal("1000000")

    def readiness_reason(self) -> str | None:
        if not self.settings.allow_participant_cloud:
            return "participant_cloud_disabled"
        if not self.settings.gemini_enabled:
            return "gemini_disabled"
        if not self.settings.live_ready:
            return "gemini_not_configured"
        return self.allowance_reason()

    def allowance_reason(self) -> str | None:
        if self.ledger:
            try:
                return self.ledger.check(self.attempt_cost())
            except Exception:
                return "allowance_unavailable"
        total = self.settings.gemini_max_total_attempts
        if total is not None and self.total_attempts >= total:
            return "attempt_limit"
        day = datetime.now(timezone.utc).date().isoformat()
        if self.day == day and self.attempts >= self.settings.gemini_max_attempts_per_day:
            return "attempt_limit"
        if self.settings.gemini_max_attempts_per_day == 0:
            return "attempt_limit"
        if self.reserved_usd + self.attempt_cost() > self.settings.gemini_budget_usd:
            return "budget_limit"
        return None

    def reserve_attempt(self) -> str | None:
        if self.ledger:
            try:
                return self.ledger.check(self.attempt_cost(), reserve=True)
            except Exception:
                return "allowance_unavailable"
        denied = self.allowance_reason()
        if denied:
            return denied
        day = datetime.now(timezone.utc).date().isoformat()
        if day != self.day:
            self.day, self.attempts = day, 0
        if self.attempts >= self.settings.gemini_max_attempts_per_day:
            return "attempt_limit"
        cost = self.attempt_cost()
        if self.reserved_usd + cost > self.settings.gemini_budget_usd:
            return "budget_limit"
        # Keep the worst-case reservation even on failure; do not undercount retries/errors.
        self.attempts += 1
        self.total_attempts += 1
        self.reserved_usd += cost
        return None

    async def analyze(self, request: AnalysisRequest) -> AnalysisResponse:
        return (await self.analyze_with_diagnostics(request)).response

    async def analyze_with_diagnostics(self, request: AnalysisRequest) -> AnalysisOutcome:
        fallback = local_response(request)
        session = request.session_summary
        synthetic = session.data_source == "synthetic" or session.replay_origin == "synthetic"
        # Reasons are fixed codes, never exception text or participant input. Each outcome
        # belongs to its request; no shared last-error state can leak between callers.
        reason = None
        consented = request.cloud_policy == "consented_summary"
        if consented and not self.settings.allow_participant_cloud:
            reason = "participant_cloud_disabled"
        elif not synthetic and not consented:
            reason = "non_synthetic_input"
        elif request.cloud_policy != "synthetic_demo" and not consented:
            reason = "local_only"
        elif fallback.safety_notice == "stop_session":
            reason = "safety_stop"
        elif any(item.code == "data_quality_limited" for item in fallback.observations):
            reason = "insufficient_data"
        elif not self.settings.gemini_enabled:
            reason = "gemini_disabled"
        elif not self.settings.live_ready:
            reason = "gemini_not_configured"
        if reason:
            return AnalysisOutcome(fallback, reason)

        try:
            # Include lock/queue time, so concurrent requests cannot extend the deadline.
            async with asyncio.timeout(self.settings.gemini_timeout_seconds):
                return await self._generate_or_cache(request, fallback)
        except TimeoutError:
            return AnalysisOutcome(fallback, "timeout")

    async def _generate_or_cache(
        self, request: AnalysisRequest, fallback: AnalysisResponse
    ) -> AnalysisOutcome:
        session = request.session_summary
        # Only successful synthetic results are cached; never persist participant sessions.
        cacheable = request.cloud_policy == "synthetic_demo"
        key = None
        if cacheable:
            payload = request.model_dump()
            payload["session_summary"].pop("session_id")
            key = hashlib.sha256(json.dumps(payload, sort_keys=True).encode()).hexdigest()
        async with self.lock:
            cached = self.cache.get(key) if cacheable else None
            if cached and time.monotonic() - cached[0] < 300:
                self.cache.move_to_end(key)
                return AnalysisOutcome(
                    cached[1].model_copy(
                        deep=True, update={"session_id": session.session_id, "ai_source": "cached"}
                    )
                )
            denied = self.reserve_attempt()
            if denied:
                return AnalysisOutcome(fallback, denied)
            try:
                choice = await self.adapter.generate(request, fallback.observations)
                # Revalidate adapter output even when a mock/custom implementation is used.
                choice = GeminiChoice.model_validate(choice.model_dump())
                allowed = {item.code: item for item in fallback.observations}
                if not choice.observation_codes or any(
                    code not in allowed for code in choice.observation_codes
                ):
                    raise ValueError("Model selected unsupported observations")
                observations = [allowed[code] for code in dict.fromkeys(choice.observation_codes)]
                scene = constrain_scene(choice.scene_config, request)
                result = fallback.model_copy(
                    deep=True,
                    update={
                        "observations": observations,
                        "narrative": narrative_for(request, observations),
                        "scene_config": scene,
                        "scene_rationale": rationale_for(scene),
                        "ai_source": "live",
                        "model_id": self.settings.gemini_model,
                        "prompt_version": PROMPT_VERSION,
                    },
                )
                result = AnalysisResponse.model_validate(result.model_dump())
            except TimeoutError:
                return AnalysisOutcome(fallback, "timeout")
            except APIError as exc:
                reason = (
                    "provider_rate_limited"
                    if exc.code == 429
                    else "provider_unavailable"
                    if exc.code and exc.code >= 500
                    else "provider_error"
                )
                return AnalysisOutcome(fallback, reason)
            except ValueError:
                return AnalysisOutcome(fallback, "invalid_output")
            except Exception:
                # Never return SDK error bodies, API keys, prompts, or participant payloads.
                return AnalysisOutcome(fallback, "provider_error")
            if cacheable:
                self.cache[key] = (time.monotonic(), result.model_copy(deep=True))
                while len(self.cache) > 32:
                    self.cache.popitem(last=False)
            return AnalysisOutcome(result)
