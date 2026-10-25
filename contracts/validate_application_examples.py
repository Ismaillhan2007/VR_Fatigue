#!/usr/bin/env python3
"""Check that fixtures match the v1 JSON Schemas.

Usage (from the repo root):
    python -m pip install jsonschema referencing
    python contracts/validate_examples.py

Valid fixtures must pass, files under fixtures/invalid/ must be rejected, and a few
in-memory mutations check cross-field rules without committing fabricated "live" data.
Offline, no network, no private data.
"""
import copy
import json
import sys
from pathlib import Path

from jsonschema import Draft202012Validator
from referencing import Registry, Resource

ROOT = Path(__file__).resolve().parent.parent
SCHEMAS = ROOT / "contracts" / "v1"
FIX = ROOT / "fixtures"

registry = Registry()
validators = {}
for path in sorted(SCHEMAS.glob("*.schema.json")):
    doc = json.loads(path.read_text(encoding="utf-8"))
    Draft202012Validator.check_schema(doc)
    registry = registry.with_resource(doc["$id"], Resource.from_contents(doc))
for path in sorted(SCHEMAS.glob("*.schema.json")):
    doc = json.loads(path.read_text(encoding="utf-8"))
    validators[path.name.removesuffix(".schema.json")] = Draft202012Validator(doc, registry=registry)

VALID = {
    "self_report_baseline.json": "self_report",
    "self_report_post_task.json": "self_report",
    "session_synthetic_usable.json": "session_summary",
    "session_synthetic_insufficient.json": "session_summary",
    "preferences_default.json": "preferences",
    "scene_config_calm_forest_sunset.json": "scene_config",
    "analysis_fallback.json": "analysis_response",
    "analyze_request_synthetic.json": "analyze_request",
    "analyze_request_consented_summary.json": "analyze_request",
}
INVALID = {
    "invalid/scene_config_unknown_field.json": "scene_config",
    "invalid/scene_config_out_of_range.json": "scene_config",
    "invalid/scene_config_unknown_particle.json": "scene_config",
    "invalid/session_unknown_schema_version.json": "session_summary",
    "invalid/session_synthetic_claims_native_gaze.json": "session_summary",
    "invalid/session_insufficient_with_indicator.json": "session_summary",
    "invalid/analysis_fallback_names_model.json": "analysis_response",
    "invalid/self_report_out_of_range.json": "self_report",
    "invalid/analyze_request_with_free_text.json": "analyze_request",
}

failures = []


def load(rel):
    return json.loads((FIX / rel).read_text(encoding="utf-8"))


def check(label, kind, data, should_pass):
    ok = validators[kind].is_valid(data)
    status = "ok " if ok == should_pass else "FAIL"
    if ok != should_pass:
        failures.append(label)
    print(f"{status} {label} -> {'valid' if ok else 'invalid'} (expected {'valid' if should_pass else 'invalid'})")


for rel, kind in VALID.items():
    check(rel, kind, load(rel), True)
for rel, kind in INVALID.items():
    check(rel, kind, load(rel), False)

# In-memory mutations (never written to disk).
live = copy.deepcopy(load("session_synthetic_usable.json"))
live["data_source"] = "live"
live["capabilities"]["native_eye_gaze"] = True
check("mutation: live + native gaze", "session_summary", live, True)

replay = copy.deepcopy(live)
replay["data_source"] = "replay"
check("mutation: replay without replay_origin", "session_summary", replay, False)
replay["replay_origin"] = "recorded"
check("mutation: recorded replay", "session_summary", replay, True)

live_with_origin = copy.deepcopy(live)
live_with_origin["replay_origin"] = "recorded"
check("mutation: live with replay_origin", "session_summary", live_with_origin, False)

nonfinite = copy.deepcopy(load("session_synthetic_usable.json"))
nonfinite["metrics"]["probe_latency_ms"] = "NaN"
check("mutation: non-numeric metric", "session_summary", nonfinite, False)

if failures:
    print(f"\n{len(failures)} check(s) failed: {failures}")
    sys.exit(1)
print("\nAll contract checks passed.")
