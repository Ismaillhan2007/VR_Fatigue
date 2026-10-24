# Shared JSON contracts (v1)

JSON Schema draft 2020-12 for the objects named in [docs/PROJECT_PLAN.md](../docs/PROJECT_PLAN.md). Unity (C# DTOs), the backend (Pydantic) and the fixtures must all match these files. Change a schema only through a contract PR that updates fixtures and both clients.

| Schema | Produced by | Consumed by |
| --- | --- | --- |
| `session_summary.schema.json` | Unity metrics code (deterministic) | Backend, report UI |
| `self_report.schema.json` | Unity UI | Metrics (baseline policy), backend |
| `preferences.schema.json` | Unity UI | Backend, scene |
| `analysis_response.schema.json` | Backend (Gemini, cache or local fallback) | Unity report UI, scene |
| `scene_config.schema.json` | Backend or local preset | Unity scene engine (validates and clamps again) |

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
