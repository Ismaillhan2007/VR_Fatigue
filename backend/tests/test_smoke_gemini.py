"""Exercise the manual smoke's complete sequence without contacting Google."""

import asyncio
import json
from decimal import Decimal
from types import SimpleNamespace

import pytest
from test_api import FakeAdapter

from app.config import Settings
from scripts import smoke_gemini


@pytest.mark.parametrize("behavior", ["valid", "invalid"])
def test_smoke_reports_live_cache_and_privacy_truthfully_without_network(
    monkeypatch, capsys, behavior
):
    class OfflineProbe(FakeAdapter):
        last_usage = {"prompt_token_count": 100, "candidates_token_count": 20}
        failure = None

        def __init__(self, _settings):
            super().__init__(behavior)

    # Never load a developer dotenv or use an actual key, even on a configured Mac.
    monkeypatch.setattr(
        smoke_gemini,
        "Settings",
        lambda **kwargs: Settings(
            _env_file=None, GEMINI_API_KEY="invented-smoke-key-marker", **kwargs
        ),
    )
    monkeypatch.setattr(smoke_gemini, "ProbeAdapter", OfflineProbe)
    args = SimpleNamespace(
        model="test-model",
        input_price=Decimal("0.25"),
        output_price=Decimal("1.50"),
        budget_usd=Decimal("0.002"),
    )
    code = asyncio.run(smoke_gemini.check(args))
    output = capsys.readouterr().out
    result = json.loads(output)
    assert result["adapter_attempts"] == 1
    assert result["reserved_worst_case_usd"] == "0.002"
    for name in (
        "real_data_not_sent_to_gemini",
        "recorded_data_not_sent_to_gemini",
        "interactive_checkin_not_sent_to_gemini",
        "free_text_rejected_before_analysis",
        "shared_contracts",
    ):
        assert result["checks"][name] is True
    assert "invented-interactive-checkin-marker" not in output
    assert "invented-smoke-key-marker" not in output
    assert result["checks"]["live_generation"] is (behavior == "valid")
    assert result["checks"]["cache_hit"] is (behavior == "valid")
    assert code == (0 if behavior == "valid" else 1)
