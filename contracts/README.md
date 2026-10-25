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

- Additive v1 request field `cloud_policy` defaults to `local_only` when absent. Interactive Unity check-ins always use `local_only`, including those with synthetic eye samples. `synthetic_demo` explicitly declares that the entire request (including sliders, chips and preferences) is invented test data. It does not authorize participant data; real/recorded eye input remains excluded. The backend also returns an optional allowlisted `X-VR-Fallback-Reason` HTTP header; response JSON is unchanged. Upgrade the backend before clients that send the new request field.

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

## Backend 422 rules beyond the JSON Schemas

The FastAPI models are stricter than the schemas. A schema-valid request can still get HTTP 422 when:

- `self_report.free_text` or `preferences.text` is not `null` (decision D1: text stays on the device); the error never echoes the text;
- `quality.valid_duration_ms + quality.gap_duration_ms != duration_ms`;
- `strain_indicator.level != "unavailable"` while quality is `insufficient_data`, the baseline is `absent` or has fewer than 6 windows, or any probe metric is `null`;
- `strain_indicator.level == "unavailable"` with a non-null `value` (or the reverse);
- the indicator is `unavailable` but `reconciliation` is not `unavailable`;
- synthetic or synthetic-replay input claims `native_eye_gaze`, or `replay_origin` does not match `data_source`;
- `probe_miss_fraction` is above 1, or the body exceeds `VR_MAX_REQUEST_BYTES` (413) / has an invalid `Content-Length` (400).

Unity's `AnalyzeRequest.Create` and the metrics bridge produce requests that satisfy these rules; `backend/tests` and the Unity PlayMode backend smoke test check them.
