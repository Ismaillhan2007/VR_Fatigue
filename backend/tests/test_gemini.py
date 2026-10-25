import asyncio
import json
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest

from app.config import Settings
from app.fallback import local_response
from app.gemini import GeminiAdapter, output_schema
from app.models import AnalysisRequest

ROOT = Path(__file__).resolve().parents[2]


def request():
    return AnalysisRequest.model_validate(
        {
            "schema_version": "1.0",
            "session_summary": json.loads(
                (ROOT / "fixtures/session_synthetic_usable.json").read_text()
            ),
            "preferences": json.loads((ROOT / "fixtures/preferences_default.json").read_text()),
            "reconciliation": "agree_high",
            "safety_notice": "none",
        }
    )


class FakeAsyncClient:
    def __init__(self, tokens, text):
        self.models = SimpleNamespace(
            count_tokens=AsyncMock(return_value=SimpleNamespace(total_tokens=tokens)),
            generate_content=AsyncMock(return_value=SimpleNamespace(text=text)),
        )

    async def __aenter__(self):
        return self

    async def __aexit__(self, *_args):
        return None


def adapter():
    return GeminiAdapter(
        Settings(_env_file=None, GEMINI_API_KEY="unit-test-placeholder", GEMINI_MODEL="test-model")
    )


def test_sdk_adapter_calls_are_bounded_and_schema_validated():
    body = request()
    fallback = local_response(body)
    fake = FakeAsyncClient(
        1500,
        json.dumps(
            {
                "observation_codes": [item.code for item in fallback.observations],
                "scene_config": fallback.scene_config.model_dump(),
            }
        ),
    )
    with patch("app.gemini.genai.Client", return_value=SimpleNamespace(aio=fake)) as constructor:
        result = asyncio.run(adapter().generate(body, fallback.observations))
    assert result.scene_config.scene_id == "forest_v1"
    options = constructor.call_args.kwargs["http_options"]
    assert options.timeout <= 15000
    assert options.retry_options.attempts == 1
    config = fake.models.generate_content.call_args.kwargs["config"]
    assert config.max_output_tokens == 1000
    assert config.response_schema is None
    assert config.response_json_schema["additionalProperties"] is False
    assert config.thinking_config.thinking_budget == 0
    assert config.automatic_function_calling.disable is True


def test_cloud_schema_preserves_strict_singleton_literals_as_enums():
    schema = output_schema()
    scene = schema["$defs"]["SceneConfig"]
    assert scene["properties"]["scene_id"]["enum"] == ["forest_v1"]
    assert scene["properties"]["schema_version"]["enum"] == ["1.0"]
    assert scene["additionalProperties"] is False
    assert '"const"' not in json.dumps(schema)


def test_gemini_3_uses_minimal_thinking_and_records_only_token_usage():
    body = request()
    fallback = local_response(body)
    fake = FakeAsyncClient(
        1500,
        json.dumps(
            {
                "observation_codes": [item.code for item in fallback.observations],
                "scene_config": fallback.scene_config.model_dump(),
            }
        ),
    )
    fake.models.generate_content.return_value.usage_metadata = SimpleNamespace(
        prompt_token_count=1400,
        candidates_token_count=300,
        thoughts_token_count=20,
        total_token_count=1720,
    )
    live_adapter = GeminiAdapter(
        Settings(
            _env_file=None,
            GEMINI_API_KEY="unit-test-placeholder",
            GEMINI_MODEL="gemini-3.1-flash-lite",
        )
    )
    with patch("app.gemini.genai.Client", return_value=SimpleNamespace(aio=fake)):
        asyncio.run(live_adapter.generate(body, fallback.observations))
    config = fake.models.generate_content.call_args.kwargs["config"]
    assert config.thinking_config.thinking_level == "MINIMAL"
    assert config.thinking_config.thinking_budget is None
    assert live_adapter.last_usage == {
        "prompt_token_count": 1400,
        "candidates_token_count": 300,
        "thoughts_token_count": 20,
        "total_token_count": 1720,
    }


@pytest.mark.parametrize("tokens", [2001, None])
def test_input_token_limit_skips_generation(tokens):
    body = request()
    fake = FakeAsyncClient(tokens, "unused")
    with patch("app.gemini.genai.Client", return_value=SimpleNamespace(aio=fake)):
        with pytest.raises(ValueError):
            asyncio.run(adapter().generate(body, local_response(body).observations))
    fake.models.generate_content.assert_not_called()


@pytest.mark.parametrize("text", [None, "not JSON", '{"arbitrary_script": "not allowed"}'])
def test_blocked_or_invalid_response_is_rejected(text):
    body = request()
    fake = FakeAsyncClient(1000, text)
    with patch("app.gemini.genai.Client", return_value=SimpleNamespace(aio=fake)):
        with pytest.raises(ValueError):
            asyncio.run(adapter().generate(body, local_response(body).observations))
