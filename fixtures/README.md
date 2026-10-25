# Fixtures

Everything here is **synthetic**: hand-written values that match the v1 contracts. None of it is a measurement of a person, and no file comes from a lab or participant recording.

- Valid examples: `session_synthetic_*.json`, `self_report_*.json`, `preferences_default.json`, `scene_config_calm_forest_sunset.json` (the saved offline demo preset), `analysis_fallback.json`.
- `invalid/` holds inputs that must be rejected. `contracts/validate_examples.py` checks both groups.
- `integration/scene_rules.json` holds synthetic scene-rule expectations shared by Python and C# tests. It covers null preferences, chip order, explicit overrides, full silence and stop-session behavior; it is a test-case list rather than a top-level v1 contract object.
- There is deliberately no committed `live` example: that would be invented data presented as a measurement. Live and recorded-replay sessions come from consenting teammates, stay in the git-ignored `session-data/` folder, and are never committed.

Gaze replay fixtures (CSV/JSON streams) belong in `fixtures/replay/` once the replay provider exists. Generate them synthetically and include the artefacts real headset streams show, so the metrics code is tested against them:

- repeated sensor timestamps (about 2-3% of rows) that must be dropped before metrics;
- an occasional 2x frame gap (about 22 ms at a 90 Hz frame clock) and rare stalls of several hundred ms;
- tracking-lost segments (`valid=false`), segments with a constant `confidence` value, and unsupported openness/pupil columns left empty;
- a steady-gaze file, a known-motion file (saccades to targets at known angles and times), and a baseline-plus-loaded pair for the indicator tests.

`.gitignore` blocks `*.csv` everywhere except under `fixtures/`; review any new CSV there for provenance before committing.
