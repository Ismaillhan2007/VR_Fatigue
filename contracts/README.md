# Shared JSON contracts (v1)

JSON Schema draft 2020-12 for the objects named in [docs/PROJECT_PLAN.md](../docs/PROJECT_PLAN.md). Unity (C# DTOs), the backend (Pydantic) and the fixtures must all match these files. Change a schema only through a contract PR that updates fixtures and both clients.

| Schema | Produced by | Consumed by |
| --- | --- | --- |
| `session_summary.schema.json` | Unity metrics code (deterministic) | Backend, report UI |
| `self_report.schema.json` | Unity UI | Metrics (baseline policy), backend |
| `preferences.schema.json` | Unity UI | Backend, scene |
| `analysis_response.schema.json` | Backend (Gemini, cache or local fallback) | Unity report UI, scene |
| `scene_config.schema.json` | Backend or local preset | Unity scene engine (validates and clamps again) |
| `analyze_request.schema.json` | Unity (`AnalyzeRequest.Create`) | Backend `POST /api/v1/analyze`; free text is always `null` (decision D1 default) |

Rules enforced by the schemas (clients must enforce them too):

- `additionalProperties: false` everywhere; unknown `schema_version` is rejected.
- Unavailable signals are `null` with an entry in `quality.unavailable`, never `0`.
- Data provenance (`data_source`, `replay_origin`) and AI provenance (`ai_source`, `model_id`) are separate fields. Synthetic data can never claim `native_eye_gaze`; a fallback response never names a model; `insufficient_data` forces the strain indicator to `unavailable`.
- `strain_indicator.kind` is always `experimental_baseline_deviation`. It is not a validated fatigue score or a diagnosis.
- `SceneConfig` has bounded numbers and enums only: no URLs, paths, scripts or free text. Unity clamps and re-validates before applying.
- Non-finite numbers are not valid JSON; clients must reject `NaN`/`Infinity` before serialising.

Check the fixtures against the schemas:

```sh
python -m pip install jsonschema referencing
python contracts/validate_examples.py
```

This is a schema check only. It does not exercise Unity, the backend or a headset.

---

# Signal contracts v1

Implemented in the signal, metric, baseline, indicator and hardware slices:

- `v1/eye-session.schema.json`: local samples, provenance, capabilities and probe events.
- `v1/signal-quality.schema.json`: aggregate quality result with exact policy parameters.
- `v1/signal-metrics.schema.json`: gaze/probe metrics, unavailable reasons, selected eye,
  policy versions, supporting durations and individual probe outcomes.
- `v1/baseline-plan.schema.json`: local phase bounds, condition IDs and nullable synthetic/example baseline ratings.
- `v1/baseline-comparison.schema.json`: frozen baseline statistics, signed per-window deviations and confidence flags.
- `v1/signal-indicator.schema.json`: explicitly experimental indicator, full contribution trace,
  unavailable states, EWMA and break-offer events, retaining the original baseline comparison.
- `v1/indicator-sequence.schema.json`: synthetic test-only normalized temporal inputs; not an application measurement API.

- `v1/hardware-diagnostics.schema.json`: local aggregate capture timing, counters, confidence range, quality and explicit pending headset review.

These are local capture/replay contracts, **not** the backend analyze request. Never send an
`eye-session.v1` stream to Gemini. Application contracts (`SessionSummary`, `SelfReport`, `Preferences`, `AnalysisResponse`,
`SceneConfig`) and Unity DTOs are supplied by the application scaffold above; an explicit
adapter must map local signal results into them.

All fields are required; unavailable values use explicit nulls/reasons. Unknown properties and
versions, duplicate property names and nonfinite numbers are rejected. The C# reader also checks
semantic conditions: session bounds, provenance, chronological/in-bounds probe events,
nonzero probe directions and unit head quaternions. Sample timing/gaze faults reach quality
accounting rather than being sorted or repaired. Schema validation alone is not sufficient.

Use Python 3.11+ for the check environment:

```sh
python3 -m venv .venv
.venv/bin/python -m pip install -r contracts/requirements.txt
.venv/bin/python contracts/validate_examples.py
```

The checker resolves references locally without network requests. See [signal semantics and
integration instructions](../docs/SIGNALS.md) before writing an adapter.
Metric formulas and probe rules are documented in [docs/METRICS.md](../docs/METRICS.md).
Baseline/reference math and comparable-condition rules are in [docs/BASELINE.md](../docs/BASELINE.md).
Indicator arithmetic, gates and state transitions are in [docs/INDICATOR.md](../docs/INDICATOR.md).

Hardware adapter setup and synthetic-source verification are in
[docs/QUEST_PRO_INTEGRATION.md](../docs/QUEST_PRO_INTEGRATION.md).
