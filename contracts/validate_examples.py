"""Validate committed local signal contracts/fixtures; never accesses the network."""
import argparse
import copy
import json
import math
import statistics
import subprocess
import sys
from pathlib import Path

from jsonschema import Draft202012Validator, FormatChecker
from referencing import Registry, Resource
from indicator_checks import check_indicator_accounting, check_sequence
from hardware_checks import check_hardware_accounting

ROOT = Path(__file__).resolve().parents[1]


def reject_constant(value):
    raise ValueError(f"Nonfinite JSON number: {value}")


def unique_keys(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"Duplicate JSON property: {key}")
        result[key] = value
    return result


def finite_float(value):
    parsed = float(value)
    if not math.isfinite(parsed):
        raise ValueError("Nonfinite JSON number")
    return parsed


def load(path):
    return json.loads(path.read_text(), parse_constant=reject_constant, parse_float=finite_float, object_pairs_hook=unique_keys)


def check_metric_accounting(data):
    """Cross-field arithmetic remains an application check, not just schema validation."""
    metrics = data["metrics"]
    for name, value in metrics.items():
        if (value is None) != (data["unavailable_reasons"][name] is not None):
            raise AssertionError(f"Value/reason mismatch: {name}")
        if data["quality"]["status"] == "insufficient_data" and value is not None:
            raise AssertionError("Insufficient eye data must not produce metrics")
    support = data["support"]
    probes = data["probes"]
    if support["probe_count"] != len(probes):
        raise AssertionError("Probe count mismatch")
    for outcome, key in (("acquired", "acquired_probe_count"), ("missed", "missed_probe_count"), ("unavailable", "excluded_probe_count")):
        if support[key] != sum(p["outcome"] == outcome for p in probes):
            raise AssertionError("Probe outcome count mismatch")
    total = support["acquired_probe_count"] + support["missed_probe_count"]
    miss = metrics["probe_miss_fraction"]
    if total == 0:
        if miss is not None:
            raise AssertionError("No evaluable probes must yield a null miss fraction")
    elif miss is None or not math.isclose(miss, support["missed_probe_count"] / total, abs_tol=1e-12):
        raise AssertionError("Miss denominator must exclude unavailable probes")
    if support["gaze_speed_duration_ms"] > support["gaze_valid_duration_ms"] or support["gaze_valid_duration_ms"] > data["quality"]["valid_duration_ms"]:
        raise AssertionError("Gaze support cannot bridge missing intervals")
    if support["head_speed_duration_ms"] > data["duration_ms"]:
        raise AssertionError("Head support exceeds session duration")


def check_baseline_accounting(data):
    """Recompute reference statistics and deviations independently from emitted window values."""
    baseline, comparison, policy = data["baseline"], data["comparison"], data["baseline_policy"]
    assert comparison["start_timestamp_us"] >= baseline["end_timestamp_us"]
    assert data["quality_policy"] == data["session_quality"]["policy"]
    assert data["metric_policy"]["gaze_eye"] == data["selected_eye"]
    keys = ("session_id", "data_source", "replay_origin")
    assert all(data[k] == data["session_quality"][k] for k in keys)
    for phase in (baseline, comparison):
        cursor = phase["start_timestamp_us"]
        for index, window in enumerate(phase["windows"]):
            assert window["index"] == index and window["start_timestamp_us"] == cursor
            span = window["end_timestamp_us"] - cursor
            assert 0 < span <= policy["window_duration_us"]
            assert window["complete"] == (span == policy["window_duration_us"])
            measured = window["measurements"]
            check_metric_accounting(measured)
            assert all(measured[k] == data[k] for k in keys)
            assert measured["selected_eye"] == data["selected_eye"]
            assert measured["metric_policy"] == data["metric_policy"] and measured["quality_policy"] == data["quality_policy"]
            assert math.isclose(measured["duration_ms"], span / 1000)
            valid = measured["support"]["gaze_valid_duration_ms"]
            eligible = (window["complete"] and measured["quality"]["status"] == "usable"
                        and valid >= data["quality_policy"]["minimum_valid_duration_us"] / 1000
                        and valid / measured["duration_ms"] >= data["quality_policy"]["minimum_coverage"])
            assert window["eligible"] == eligible
            cursor = window["end_timestamp_us"]
        assert cursor == phase["end_timestamp_us"]
    usable = [w for w in baseline["windows"] if w["eligible"]]
    assert baseline["usable_window_count"] == len(usable)
    assert baseline["complete_window_count"] == sum(w["complete"] for w in baseline["windows"])
    assert math.isclose(baseline["selected_eye_valid_duration_ms"], sum(w["measurements"]["support"]["gaze_valid_duration_ms"] for w in baseline["windows"]))
    for key, reference in baseline["references"].items():
        values = [w["measurements"]["metrics"][key] for w in usable if w["measurements"]["metrics"][key] is not None]
        assert reference["usable_window_count"] == len(values)
        assert reference["floor"] == policy["metric_floors"][key]
        if len(values) < policy["minimum_usable_windows"]:
            assert reference["median"] is None and reference["scale"] is None
        else:
            median = statistics.median(values)
            mad = statistics.median(abs(v - median) for v in values)
            scale = max(policy["mad_multiplier"] * mad, reference["floor"])
            for actual, expected in ((reference["median"], median), (reference["mad"], mad), (reference["scale"], scale)):
                assert math.isclose(actual, expected, rel_tol=1e-9, abs_tol=1e-9)
    has_gaze = any(baseline["references"][k]["median"] is not None for k in ("gaze_speed_median_deg_s", "gaze_speed_p95_deg_s", "gaze_dispersion_deg"))
    assert (baseline["status"] == "ready") == (len(usable) >= policy["minimum_usable_windows"] and has_gaze)
    assert baseline["low_confidence"] == bool(baseline["low_confidence_reasons"])
    rating = baseline["self_report"]
    if rating is None:
        assert "self_report_missing" in baseline["low_confidence_reasons"]
    elif max(rating.values()) >= 5:
        assert "self_reported_tired" in baseline["low_confidence_reasons"]
    comparable_conditions = baseline["conditions_id"] is not None and baseline["conditions_id"] == comparison["conditions_id"]
    any_deviation = False
    for window in comparison["windows"]:
        for key, deviation in window["deviations"].items():
            value = window["measurements"]["metrics"][key]
            assert deviation["value"] == value
            reference = baseline["references"][key]
            can_compare = baseline["status"] == "ready" and comparable_conditions and window["eligible"] and reference["median"] is not None and value is not None
            assert (deviation["z_score"] is not None) == can_compare
            if can_compare:
                any_deviation = True
                delta = value - reference["median"]
                raw = delta / reference["scale"]
                expected = max(-policy["z_clip"], min(policy["z_clip"], raw))
                assert math.isclose(deviation["delta"], delta, rel_tol=1e-9, abs_tol=1e-9)
                assert math.isclose(deviation["z_score"], expected, rel_tol=1e-9, abs_tol=1e-9)
                assert deviation["clipped"] == (abs(delta) > policy["z_clip"] * reference["scale"])
    assert (comparison["status"] == "comparable") == any_deviation


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--reports-dir", type=Path, help="Also validate actual C# fixture reports")
    args = parser.parse_args()
    subprocess.run([sys.executable, str(ROOT / "contracts/validate_application_examples.py")], check=True)
    schemas = [load(path) for path in sorted((ROOT / "contracts/v1").glob("*.schema.json"))]
    registry = Registry().with_resources((s["$id"], Resource.from_contents(s)) for s in schemas)
    validators = {}
    for schema in schemas:
        Draft202012Validator.check_schema(schema)
        validators[schema["$id"]] = Draft202012Validator(schema, registry=registry, format_checker=FormatChecker())
    checked = 0
    for path in sorted((ROOT / "fixtures/replay").glob("*.json")):
        data = load(path)
        schema_id = "urn:vr-fatigue:signal-quality:v1" if path.name.endswith(".quality.json") else "urn:vr-fatigue:eye-session:v1"
        validators[schema_id].validate(data)
        checked += 1
    if checked == 0:
        raise AssertionError("No fixtures found")
    for path in sorted((ROOT / "fixtures/metrics").glob("*.json")):
        validators["urn:vr-fatigue:eye-session:v1"].validate(load(path))
        checked += 1
    for path in sorted((ROOT / "fixtures/baseline").glob("*.plan.json")):
        validators["urn:vr-fatigue:baseline-plan:v1"].validate(load(path))
        checked += 1
    for path in sorted((ROOT / "fixtures/indicator").glob("*.plan.json")):
        validators["urn:vr-fatigue:baseline-plan:v1"].validate(load(path))
        checked += 1
    for path in sorted((ROOT / "fixtures/indicator").glob("*.sequence.json")):
        data = load(path)
        validators["urn:vr-fatigue:indicator-sequence:v1"].validate(data)
        check_sequence(data)
        checked += 1
    actual_count = 0
    invalid_metric_count = 0
    invalid_baseline_count = 0
    invalid_indicator_count = 0
    invalid_hardware_count = 0
    if args.reports_dir:
        hardware_paths = sorted(args.reports_dir.glob("*.hardware.json"))
        if {p.name for p in hardware_paths} != {"clock_reset.hardware.json", "capture_pipeline.hardware.json"}:
            raise AssertionError("Hardware reports must match both synthetic source tests")
        for path in hardware_paths:
            data = load(path)
            validators["urn:vr-fatigue:hardware-diagnostics:v1"].validate(data)
            check_hardware_accounting(data)
            assert data["data_source"] == "synthetic" and data["sdk_package_version"] == "mock"
            assert not data["recording_period_complete"]
            if path.name.startswith("clock_reset"):
                assert data["capture_state"] == "faulted" and data["status_reason"] == "runtime_clock_reset"
                assert data["counts"]["clock_resets"] == 1
            else:
                assert data["capture_state"] == "stopped" and data["status_reason"] == "completed"
                assert data["duration_ms"] == 200000 and data["counts"]["accepted_samples"] == 2000
            actual_count += 1
        hardware_sample = load(args.reports_dir / "capture_pipeline.hardware.json")
        for dotted, value in (("schema_version", "hardware-diagnostics.v999"), ("headset_review_required", False),
                              ("confidence.used_as_filter", True), ("script", "do-not-execute")):
            candidate = copy.deepcopy(hardware_sample)
            parts = dotted.split(".")
            container = candidate
            for part in parts[:-1]:
                container = container[part]
            container[parts[-1]] = value
            if validators["urn:vr-fatigue:hardware-diagnostics:v1"].is_valid(candidate):
                raise AssertionError("Hardware schema accepted misleading output")
            invalid_hardware_count += 1
        for field in ("dt_count", "accepted_samples"):
            candidate = copy.deepcopy(hardware_sample)
            candidate["counts"][field] += 1
            try:
                check_hardware_accounting(candidate)
            except AssertionError:
                invalid_hardware_count += 1
            else:
                raise AssertionError("Hardware accounting accepted incorrect counters")
        expected_names = {p.name for p in (ROOT / "fixtures/replay").glob("*.quality.json")}
        actual_paths = sorted(args.reports_dir.glob("*.quality.json"))
        if {p.name for p in actual_paths} != expected_names:
            raise AssertionError("Actual reports must match the full expected fixture set")
        for path in actual_paths:
            validators["urn:vr-fatigue:signal-quality:v1"].validate(load(path))
            actual_count += 1
        expected_metric_names = {p.stem + ".metrics.json" for p in (ROOT / "fixtures/metrics").glob("*.json")}
        actual_metrics = sorted(args.reports_dir.glob("*.metrics.json"))
        if {p.name for p in actual_metrics} != expected_metric_names:
            raise AssertionError("Actual metric reports must match the full metric fixture set")
        for path in actual_metrics:
            data = load(path)
            validators["urn:vr-fatigue:signal-metrics:v1"].validate(data)
            check_metric_accounting(data)
            actual_count += 1
        sample_metric = load(args.reports_dir / "steady_acquired.metrics.json")
        mutations = [
            ("metric_version", "gaze-metrics.v999"),
            ("metrics.probe_miss_fraction", 2),
            ("unavailable_reasons.gaze_speed_median_deg_s", "contradicts_present_value"),
            ("script", "do-not-execute"),
        ]
        for dotted, value in mutations:
            candidate = copy.deepcopy(sample_metric)
            parts = dotted.split(".")
            container = candidate
            for part in parts[:-1]:
                container = container[part]
            container[parts[-1]] = value
            if validators["urn:vr-fatigue:signal-metrics:v1"].is_valid(candidate):
                raise AssertionError("Metric schema accepted invalid or contradictory output")
            invalid_metric_count += 1
        expected_baseline_names = {p.name.replace(".plan.json", ".baseline.json") for p in (ROOT / "fixtures/baseline").glob("*.plan.json")}
        baseline_paths = sorted(args.reports_dir.glob("*.baseline.json"))
        if {p.name for p in baseline_paths} != expected_baseline_names:
            raise AssertionError("Actual baseline reports must match all committed plans")
        for path in baseline_paths:
            data = load(path)
            validators["urn:vr-fatigue:baseline-comparison:v1"].validate(data)
            check_baseline_accounting(data)
            actual_count += 1
        baseline_sample = load(args.reports_dir / "stable_then_changed.baseline.json")
        for dotted, value in (("method_version", "baseline-method.v999"), ("script", "do-not-execute"),
                              ("baseline_policy.metric_floors.probe_latency_ms", 0),
                              ("baseline.references.probe_latency_ms.unavailable_reason", "contradicts_present_reference")):
            candidate = copy.deepcopy(baseline_sample)
            parts = dotted.split(".")
            container = candidate
            for part in parts[:-1]:
                container = container[part]
            container[parts[-1]] = value
            if validators["urn:vr-fatigue:baseline-comparison:v1"].is_valid(candidate):
                raise AssertionError("Baseline schema accepted invalid output")
            invalid_baseline_count += 1
        wrong_reference = copy.deepcopy(baseline_sample)
        wrong_reference["baseline"]["references"]["probe_latency_ms"]["median"] += 1
        try:
            check_baseline_accounting(wrong_reference)
        except AssertionError:
            invalid_baseline_count += 1
        else:
            raise AssertionError("Semantic check accepted an incorrect baseline median")
        expected_indicator_names = {p.name.replace(".plan.json", ".indicator.json") for p in (ROOT / "fixtures/indicator").glob("*.plan.json")}
        indicator_paths = sorted(args.reports_dir.glob("*.indicator.json"))
        if {p.name for p in indicator_paths} != expected_indicator_names:
            raise AssertionError("Actual indicator reports must match all indicator plans")
        for path in indicator_paths:
            data = load(path)
            validators["urn:vr-fatigue:signal-indicator:v1"].validate(data)
            check_indicator_accounting(data, check_baseline_accounting)
            actual_count += 1
        indicator_sample = load(args.reports_dir / "sustained_change.indicator.json")
        mutations = (("method_version", "experimental-indicator.v999"), ("experimental", False),
                     ("script", "do-not-execute"), ("policy.terms.head_speed_deg_s.weight", 1),
                     ("windows.0.level", "fatigue"), ("windows.0.value", None))
        for dotted, value in mutations:
            candidate = copy.deepcopy(indicator_sample)
            parts = dotted.split(".")
            container = candidate
            for part in parts[:-1]:
                container = container[int(part)] if isinstance(container, list) else container[part]
            container[parts[-1]] = value
            if validators["urn:vr-fatigue:signal-indicator:v1"].is_valid(candidate):
                raise AssertionError("Indicator schema accepted invalid or misleading output")
            invalid_indicator_count += 1
        for kind in ("value", "premature_offer"):
            candidate = copy.deepcopy(indicator_sample)
            point = candidate["windows"][0]
            if kind == "value":
                point["value"] = point["value"] - 0.01
            else:
                point["nudge_event"], point["nudge_active"] = "offer_break", True
            try:
                check_indicator_accounting(candidate, check_baseline_accounting)
            except AssertionError:
                invalid_indicator_count += 1
            else:
                raise AssertionError("Semantic check accepted wrong smoothing or a premature offer")
    # Assert strict rejection as well as happy-path acceptance.
    sample = load(ROOT / "fixtures/replay/steady_gaze.json")
    invalids = []
    for field, value in (("schema_version", "eye-session.v999"), ("script", "do-not-execute"), ("replay_origin", "recorded")):
        candidate = dict(sample)
        candidate[field] = value
        invalids.append(candidate)
    candidate = dict(sample, samples=[dict(sample["samples"][0], timestamp_us=-1)])
    invalids.append(candidate)
    for candidate in invalids:
        if validators["urn:vr-fatigue:eye-session:v1"].is_valid(candidate):
            raise AssertionError("Schema accepted an invalid example")
    plan_sample = load(ROOT / "fixtures/baseline/stable_then_changed.plan.json")
    invalid_plans = []
    for dotted, value in (("schema_version", "baseline-plan.v999"), ("baseline.self_report.eye_strain", 8), ("script", "do-not-execute")):
        candidate = copy.deepcopy(plan_sample)
        parts = dotted.split(".")
        container = candidate
        for part in parts[:-1]:
            container = container[part]
        container[parts[-1]] = value
        invalid_plans.append(candidate)
    for candidate in invalid_plans:
        if validators["urn:vr-fatigue:baseline-plan:v1"].is_valid(candidate):
            raise AssertionError("Plan schema accepted invalid input")
    print(f"PASS: {len(schemas)} schemas, {checked} fixtures, {actual_count} actual C# reports, {len(invalids) + invalid_metric_count + invalid_baseline_count + invalid_indicator_count + invalid_hardware_count + len(invalid_plans)} rejected invalid examples")


if __name__ == "__main__":
    main()
