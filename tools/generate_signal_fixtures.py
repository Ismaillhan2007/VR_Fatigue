"""Generate tiny, wholly synthetic streams and analytically specified quality oracles."""
import copy
import json
import uuid
from pathlib import Path

DEST = Path(__file__).resolve().parents[1] / "fixtures/replay"


def eye(valid=True, direction=None):
    return {"valid": valid, "direction": direction if direction is not None else ({"x": 0, "y": 0, "z": 1} if valid else None), "confidence": 0.0 if valid else None}


def sample(stamp):
    return {"timestamp_us": stamp, "left": eye(), "right": eye(), "head_pose": {"position_m": {"x": 0, "y": 0, "z": 0}, "rotation_xyzw": [0, 0, 0, 1]}}


def session(name, stamps, end=1000000, gaze=True):
    return {
        "schema_version": "eye-session.v1",
        "session_id": str(uuid.uuid5(uuid.NAMESPACE_URL, "vr-fatigue/synthetic/" + name)),
        "data_source": "synthetic", "replay_origin": None, "coordinate_frame": "head_local",
        "start_timestamp_us": 0, "end_timestamp_us": end,
        "capabilities": {
            "gaze": {"available": gaze, "reason": None if gaze else "not_supported"},
            "pupil_diameter": {"available": False, "reason": "not_exposed_by_runtime"},
            "eyelid_openness": {"available": False, "reason": "not_exposed_by_runtime"},
            "confidence_informative": False
        },
        "samples": [sample(t) for t in stamps], "probe_events": []
    }


def expected(s, valid_ms, gap_ms, longest_ms, valid_count, invalid_count=0, duplicate=0, out_of_order=0, out_of_bounds=0, reasons=()):
    duration_ms = (s["end_timestamp_us"] - s["start_timestamp_us"]) / 1000
    return {
        "schema_version": "signal-quality.v1", "session_id": s["session_id"],
        "data_source": "replay", "replay_origin": "synthetic", "duration_ms": duration_ms,
        "policy": {"version": "quality-policy.v1", "maximum_gap_us": 100000, "minimum_valid_duration_us": 1000000, "minimum_coverage": 0.8, "minimum_valid_samples": 2},
        "capabilities": copy.deepcopy(s["capabilities"]),
        "quality": {
            "status": "insufficient_data" if reasons else "usable",
            "valid_coverage": valid_ms / duration_ms if duration_ms else 0.0,
            "valid_duration_ms": float(valid_ms), "gap_duration_ms": float(gap_ms), "longest_gap_ms": float(longest_ms),
            "reasons": list(reasons),
            "counts": {"input": len(s["samples"]), "accepted": valid_count + invalid_count, "valid_gaze": valid_count, "invalid_gaze": invalid_count,
                       "duplicate_timestamps": duplicate, "out_of_order_timestamps": out_of_order, "out_of_bounds_timestamps": out_of_bounds}
        }
    }


def main():
    DEST.mkdir(parents=True, exist_ok=True)
    cases = []
    stamps = list(range(0, 1000001, 100000))
    s = session("steady_gaze", stamps)
    s["probe_events"] = [{"timestamp_us": t, "probe_id": "probe-1", "kind": kind, "target_direction": {"x": 0, "y": 0, "z": 1}}
                         for t, kind in [(0, "target_onset"), (100000, "hold_start"), (1000000, "target_end")]]
    cases.append(("steady_gaze", s, expected(s, 1000, 0, 0, 11)))
    s = session("tracking_gap", [0, 100000, 200000, 800000, 900000, 1000000])
    cases.append(("tracking_gap", s, expected(s, 400, 600, 600, 6, reasons=("too_short", "low_coverage"))))
    s = session("duplicate_and_out_of_order", [0, 100000, 100000, 50000] + stamps[2:])
    cases.append(("duplicate_and_out_of_order", s, expected(s, 1000, 0, 0, 11, duplicate=1, out_of_order=1)))
    s = session("invalid_gaze", stamps)
    s["samples"][4]["left"] = eye(False)
    s["samples"][4]["right"] = eye(False)
    s["samples"][5]["left"] = eye(True, {"x": 0, "y": 0, "z": 0})
    s["samples"][5]["right"] = eye(True, {"x": 0, "y": 0, "z": 0})
    cases.append(("invalid_gaze", s, expected(s, 700, 300, 300, 9, invalid_count=2, reasons=("too_short", "low_coverage"))))
    s = session("monocular_zero_confidence", stamps)
    for item in s["samples"]:
        item["right"] = eye(False)
    cases.append(("monocular_zero_confidence", s, expected(s, 1000, 0, 0, 11)))
    s = session("unsupported_gaze", [], gaze=False)
    cases.append(("unsupported_gaze", s, expected(s, 0, 1000, 1000, 0, reasons=("gaze_unavailable", "no_samples", "too_few_valid_samples", "too_short", "low_coverage"))))
    s = session("empty_session", [])
    cases.append(("empty_session", s, expected(s, 0, 1000, 1000, 0, reasons=("no_samples", "too_few_valid_samples", "too_short", "low_coverage"))))
    s = session("zero_duration", [0], end=0)
    cases.append(("zero_duration", s, expected(s, 0, 0, 0, 1, reasons=("zero_duration", "too_few_valid_samples", "too_short", "low_coverage"))))
    for name, stream, report in cases:
        for filename, data in ((name + ".json", stream), (name + ".quality.json", report)):
            (DEST / filename).write_text(json.dumps(data, indent=2, allow_nan=False) + "\n")
    print(f"Generated {len(cases)} synthetic sessions and {len(cases)} expected reports")


if __name__ == "__main__":
    main()
