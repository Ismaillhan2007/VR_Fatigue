"""Build a synthetic HTTP request from the team's shared fixtures."""

import json
from pathlib import Path

root = Path(__file__).resolve().parents[2]
print(
    json.dumps(
        {
            "schema_version": "1.0",
            "session_summary": json.loads(
                (root / "fixtures/session_synthetic_usable.json").read_text()
            ),
            "preferences": json.loads((root / "fixtures/preferences_default.json").read_text()),
            "reconciliation": "agree_high",
            "safety_notice": "none",
        },
        indent=2,
        allow_nan=False,
    )
)
