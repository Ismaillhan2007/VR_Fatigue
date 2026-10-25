"""Generate a wholly synthetic long session outside Git; plans are committed, raw streams are not."""
import argparse
import json
import math
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def direction(degrees):
    angle = math.radians(degrees)
    return {"x": math.sin(angle), "y": 0, "z": math.cos(angle)}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output-dir", required=True, type=Path)
    parser.add_argument("--comparison-windows", type=int, choices=range(1, 11), default=2,
                        help="Number of synthetic 20 s comparison windows (default 2)")
    args = parser.parse_args()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    data = json.loads((ROOT / "fixtures/replay/steady_gaze.json").read_text())
    data["session_id"] = "00000000-0000-4000-8000-000000000003"
    end_us = 120000000 + args.comparison_windows * 20000000
    data["end_timestamp_us"] = end_us
    data["samples"] = []
    data["probe_events"] = []
    for stamp in range(0, end_us + 1, 100000):
        index, within = divmod(stamp, 10000000)
        target = 0 if index % 2 == 0 else 12
        before = stamp < 120000000
        error = 6 if within < (100000 if before else 200000) else (1 if before else 2)
        reading = {"valid": True, "direction": direction(target + error), "confidence": 0.0}
        data["samples"].append({"timestamp_us": stamp, "left": dict(reading), "right": dict(reading),
                                "head_pose": {"position_m": {"x": 0, "y": 0, "z": 0}, "rotation_xyzw": [0, 0, 0, 1]}})
    for index in range(end_us // 10000000):
        onset = index * 10000000
        for offset, kind in ((0, "target_onset"), (1000000, "hold_start"), (2000000, "target_end")):
            data["probe_events"].append({"timestamp_us": onset + offset, "probe_id": "probe-" + str(index), "kind": kind,
                                         "target_direction": direction(0 if index % 2 == 0 else 12)})
    (args.output_dir / "synthetic_baseline_session.json").write_text(json.dumps(data, indent=2, allow_nan=False) + "\n")
    for path in (ROOT / "fixtures/baseline").glob("*.plan.json"):
        (args.output_dir / path.name).write_text(path.read_text())
    print("Generated 1 synthetic session (" + str(len(data["samples"])) + " samples) and 3 baseline plans in", args.output_dir)


if __name__ == "__main__":
    main()
