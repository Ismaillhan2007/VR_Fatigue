"""Deterministic, wholly synthetic trajectories with analytical expectations in tests."""
import copy
import json
import math
from pathlib import Path
import uuid

ROOT = Path(__file__).resolve().parents[1]


def direction(degrees):
    angle = math.radians(degrees)
    return {"x": math.sin(angle), "y": 0, "z": math.cos(angle)}


def make_session(name, stamps, gaze_degrees, head_degrees=lambda t: 0):
    template = json.loads((ROOT / "fixtures/replay/steady_gaze.json").read_text())
    template["session_id"] = str(uuid.uuid5(uuid.NAMESPACE_URL, "vr-fatigue/synthetic/metrics/" + name))
    template["end_timestamp_us"] = 2000000
    template["samples"] = []
    template["probe_events"] = []
    for stamp in stamps:
        reading = {"valid": True, "direction": direction(gaze_degrees(stamp / 1000000)), "confidence": 0.0}
        head = math.radians(head_degrees(stamp / 1000000)) / 2
        template["samples"].append({"timestamp_us": stamp, "left": reading, "right": copy.deepcopy(reading),
                                    "head_pose": {"position_m": {"x": 0, "y": 0, "z": 0}, "rotation_xyzw": [0, math.sin(head), 0, math.cos(head)]}})
    return template


def probe(session):
    session["probe_events"] = [{"timestamp_us": t, "probe_id": "probe-1", "kind": kind, "target_direction": direction(0)}
                                for t, kind in [(0, "target_onset"), (1000000, "hold_start"), (2000000, "target_end")]]
    return session


def main():
    destination = ROOT / "fixtures/metrics"
    destination.mkdir(parents=True, exist_ok=True)
    stamps = list(range(0, 2000001, 50000))
    cases = {"steady_acquired": probe(make_session("steady_acquired", stamps, lambda t: 0)),
             "probe_missed": probe(make_session("probe_missed", stamps, lambda t: 12)),
             "delayed_acquisition": probe(make_session("delayed_acquisition", stamps, lambda t: 12 if t < 0.2 else 0)),
             "known_rotation": make_session("known_rotation", stamps, lambda t: 2 * t, lambda t: 20 * t),
             "insufficient_gaze": probe(make_session("insufficient_gaze", [], lambda t: 0))}
    irregular = [0]
    steps = [30000, 70000, 40000, 60000]
    while irregular[-1] < 2000000:
        irregular.append(min(2000000, irregular[-1] + steps[(len(irregular) - 1) % len(steps)]))
    cases["irregular_rotation"] = make_session("irregular_rotation", irregular, lambda t: 2 * t, lambda t: 20 * t)
    lost = probe(make_session("acquisition_tracking_loss", stamps, lambda t: 12 if t < 0.2 else 0))
    for eye in ("left", "right"):
        lost["samples"][2][eye] = {"valid": False, "direction": None, "confidence": None}
    cases["acquisition_tracking_loss"] = lost
    for name, data in cases.items():
        (destination / (name + ".json")).write_text(json.dumps(data, indent=2, allow_nan=False) + "\n")
    print(f"Generated {len(cases)} synthetic metric/probe sessions")


if __name__ == "__main__":
    main()
