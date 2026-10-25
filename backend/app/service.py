import asyncio
import hashlib
import json
import time
from collections import OrderedDict
from datetime import datetime, timezone
from decimal import Decimal
from typing import Protocol

from app.config import Settings
from app.fallback import constrain_scene, local_response, narrative_for, rationale_for
from app.gemini import MAX_INPUT_TOKENS, MAX_OUTPUT_TOKENS, PROMPT_VERSION, GeminiAdapter
from app.models import AnalysisRequest, AnalysisResponse, GeminiChoice, Observation


class Adapter(Protocol):
    async def generate(
        self, request: AnalysisRequest, observations: list[Observation]
    ) -> GeminiChoice: ...


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

    def reserve_attempt(self) -> bool:
        day = datetime.now(timezone.utc).date().isoformat()
        if day != self.day:
            self.day, self.attempts = day, 0
        if self.attempts >= self.settings.gemini_max_attempts_per_day:
            return False
        cost = (
            MAX_INPUT_TOKENS * self.settings.input_price_per_million
            + MAX_OUTPUT_TOKENS * self.settings.output_price_per_million
        ) / Decimal("1000000")
        if self.reserved_usd + cost > self.settings.gemini_budget_usd:
            return False
        # Keep the worst-case reservation even on failure; do not undercount retries/errors.
        self.attempts += 1
        self.reserved_usd += cost
        return True

    async def analyze(self, request: AnalysisRequest) -> AnalysisResponse:
        fallback = local_response(request)
        session = request.session_summary
        synthetic = session.data_source == "synthetic" or session.replay_origin == "synthetic"
        if (
            not synthetic
            or not self.settings.live_ready
            or fallback.safety_notice == "stop_session"
            # Usable capture quality does not guarantee any comparable measurements.
            or any(item.code == "data_quality_limited" for item in fallback.observations)
        ):
            return fallback

        try:
            # Include lock/queue time, so concurrent requests cannot extend the deadline.
            async with asyncio.timeout(self.settings.gemini_timeout_seconds):
                return await self._generate_or_cache(request, fallback)
        except TimeoutError:
            return fallback

    async def _generate_or_cache(
        self, request: AnalysisRequest, fallback: AnalysisResponse
    ) -> AnalysisResponse:
        session = request.session_summary
        # Only successful synthetic results are cached; never persist participant sessions.
        payload = request.model_dump()
        payload["session_summary"].pop("session_id")
        key = hashlib.sha256(json.dumps(payload, sort_keys=True).encode()).hexdigest()
        async with self.lock:
            cached = self.cache.get(key)
            if cached and time.monotonic() - cached[0] < 300:
                self.cache.move_to_end(key)
                return cached[1].model_copy(
                    deep=True, update={"session_id": session.session_id, "ai_source": "cached"}
                )
            if not self.reserve_attempt():
                return fallback
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
            except Exception:
                # Never return SDK error bodies, API keys, prompts, or participant payloads.
                return fallback
            self.cache[key] = (time.monotonic(), result.model_copy(deep=True))
            while len(self.cache) > 32:
                self.cache.popitem(last=False)
            return result
