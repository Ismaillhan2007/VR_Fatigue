"""Generate fixtures/replay/demo_session.json: a SYNTHETIC eye-session.v1 demo session.

Not a recording of any person. Deterministic (fixed seed). 90 Hz frame clock, head-local gaze.

Layout: 130 s baseline + 60 s loaded part, both with the same probe layout (PROBE_LAYOUT_ID): a target
jumps every 2 s through a fixed cycle of positions >= 10 degrees apart (amplitudes up to ~40 degrees).
Probe events per trial: target_onset, hold_start (+0.5 s, the acquisition deadline) and target_end (+2 s).

Artifacts like those seen in headset streams: ~2.5% repeated sensor timestamps, occasional 22 ms gaps
(one skipped frame), one ~0.9 s stall in the loaded part with valid=true on both sides, and a few short
tracking-loss bursts (valid=false). The loaded part has slightly slower and less accurate saccades and a
few more late saccades (misses at the 2 degree / 500 ms probe policy).

Run from the repo root: python tools/generate_demo_session.py
"""

import json
import math
import random
from pathlib import Path

OUT = Path(__file__).resolve().parents[1] / "fixtures" / "replay" / "demo_session.json"
SESSION_ID = "00000000-0000-4000-8000-0000000000d1"
PROBE_LAYOUT_ID = "probe_cycle_10_v1"  # documented in docs/ARCHITECTURE.md; same in both phases
BASELINE_S, LOADED_S = 130.0, 60.0
RATE_HZ = 90.0
TRIAL_S, HOLD_START_S = 2.0, 0.5
STALL_START_S, STALL_S = 156.3, 0.9

# (yaw, pitch) degrees, head-local. Consecutive (cyclic) separations are 10..40 degrees.
LAYOUT = [(0, 0), (-14, 6), (12, 8), (-6, -10), (18, -4), (-20, 2), (4, 12), (14, -12), (-12, -6), (10, 5)]

PHASES = {
    # latency mean/sd (ms), landing error sigma (deg), late-saccade probability, fixation noise (deg)
    "baseline": dict(lat=205, lat_sd=22, err=0.42, late=0.02, noise=0.07),
    "loaded": dict(lat=222, lat_sd=28, err=0.5, late=0.04, noise=0.08),
}


def direction(yaw_deg, pitch_deg):
    y, p = math.radians(yaw_deg), math.radians(pitch_deg)
    return (math.sin(y) * math.cos(p), math.sin(p), math.cos(y) * math.cos(p))


def separation(a, b):
    da, db = direction(*a), direction(*b)
    dot = max(-1.0, min(1.0, sum(x * y for x, y in zip(da, db))))
    return math.degrees(math.acos(dot))


def min_jerk(s):
    return 10 * s**3 - 15 * s**4 + 6 * s**5


def vec(d, digits=4):
    return {"x": round(d[0], digits), "y": round(d[1], digits), "z": round(d[2], digits)}


def main():
    for i, a in enumerate(LAYOUT):
        b = LAYOUT[(i + 1) % len(LAYOUT)]
        assert 10 <= separation(a, b) <= 41, (a, b, separation(a, b))

    rng = random.Random(20261024)
    total_s = BASELINE_S + LOADED_S

    # Trials: (onset_s, from_pos, to_pos, phase, latency_s, saccade_s, landing_offset)
    trials = []
    for phase, start, length in (("baseline", 0.0, BASELINE_S), ("loaded", BASELINE_S, LOADED_S)):
        p = PHASES[phase]
        for i in range(int(length / TRIAL_S)):
            onset = start + i * TRIAL_S
            to = LAYOUT[i % len(LAYOUT)]
            frm = LAYOUT[(i - 1) % len(LAYOUT)] if i > 0 else LAYOUT[-1]
            lat = max(0.14, rng.gauss(p["lat"], p["lat_sd"]) / 1000)
            if rng.random() < p["late"]:
                lat = rng.uniform(0.505, 0.56)  # late saccade: a miss at the 500 ms deadline
            amp = separation(frm, to)
            sacc = (21 + 2.2 * amp) / 1000
            r = abs(rng.gauss(0, p["err"])) * 1.25
            theta = rng.uniform(0, 2 * math.pi)
            trials.append((onset, frm, to, phase, lat, sacc, (r * math.cos(theta), r * math.sin(theta)), p["noise"]))

    def gaze_at(t):
        """Eye-in-head (yaw, pitch) at time t from the active trial."""
        idx = min(int(t // TRIAL_S), len(trials) - 1)
        onset, frm, to, _, lat, sacc, off, noise = trials[idx]
        prev_off = trials[idx - 1][6] if idx > 0 else (0.0, 0.0)
        start = (frm[0] + prev_off[0], frm[1] + prev_off[1])
        land = (to[0] + off[0], to[1] + off[1])
        dt = t - onset
        if dt < lat:
            pos = start
        elif dt < lat + sacc:
            s = min_jerk((dt - lat) / sacc)
            pos = (start[0] + (land[0] - start[0]) * s, start[1] + (land[1] - start[1]) * s)
        else:
            # Slow corrective drift removes about half of the landing error over 300 ms.
            k = 0.5 * min(1.0, (dt - lat - sacc) / 0.3)
            pos = (land[0] - off[0] * k, land[1] - off[1] * k)
        return pos[0] + rng.gauss(0, noise), pos[1] + rng.gauss(0, noise)

    loss = [(37.0, 0.15), (88.4, 0.12), (171.7, 0.18)]  # short tracking-loss bursts (valid=false)
    samples = []
    n = int(total_s * RATE_HZ)
    for k in range(n + 1):
        t = k / RATE_HZ
        if STALL_START_S <= t < STALL_START_S + STALL_S:
            continue  # runtime stall: no rows at all, neighbours stay valid
        if k > 0 and k % 97 == 0 and rng.random() < 0.9:
            continue  # occasional 22 ms gap (one skipped frame)
        ts = round(t * 1_000_000)
        lost = any(a <= t < a + d for a, d in loss)
        yaw, pitch = gaze_at(t)
        head_yaw = 1.2 * math.sin(2 * math.pi * t / 23.0)
        hq = (0.0, math.sin(math.radians(head_yaw) / 2), 0.0, math.cos(math.radians(head_yaw) / 2))
        head = {
            "position_m": vec((0.002 * math.sin(t / 7.0), 1.6 + 0.001 * math.sin(t / 5.0), 0.0), 4),
            "rotation_xyzw": [round(v, 6) for v in hq],
        }
        if lost:
            eye_l = eye_r = {"valid": False, "direction": None, "confidence": None}
        else:
            eye_l = {"valid": True, "direction": vec(direction(yaw - 0.05, pitch)), "confidence": None}
            eye_r = {"valid": True, "direction": vec(direction(yaw + 0.05, pitch)), "confidence": None}
        row = {"timestamp_us": ts, "left": eye_l, "right": eye_r, "head_pose": head}
        samples.append(row)
        if rng.random() < 0.025:
            samples.append(json.loads(json.dumps(row)))  # repeated sensor timestamp (duplicate row)

    events = []
    for i, (onset, _, to, phase, *_rest) in enumerate(trials):
        pid = f"{phase[0]}{i:03d}"
        target = vec(direction(*to))
        for kind, at in (("target_onset", onset), ("hold_start", onset + HOLD_START_S), ("target_end", onset + TRIAL_S)):
            events.append({"timestamp_us": round(at * 1_000_000), "probe_id": pid, "kind": kind, "target_direction": target})

    doc = {
        "schema_version": "eye-session.v1",
        "session_id": SESSION_ID,
        "data_source": "synthetic",
        "replay_origin": None,
        "coordinate_frame": "head_local",
        "start_timestamp_us": 0,
        "end_timestamp_us": round(total_s * 1_000_000),
        "capabilities": {
            "gaze": {"available": True, "reason": None},
            "pupil_diameter": {"available": False, "reason": "not_exposed_by_runtime"},
            "eyelid_openness": {"available": False, "reason": "not_exposed_by_runtime"},
            "confidence_informative": False,
        },
        "samples": samples,
        "probe_events": events,
    }
    OUT.write_text(json.dumps(doc, separators=(",", ":"), allow_nan=False) + "\n")
    dup = len(samples) - len({s["timestamp_us"] for s in samples})
    print(f"wrote {OUT.relative_to(OUT.parents[2])}: {len(samples)} rows ({dup / len(samples):.1%} repeated), "
          f"{len(trials)} trials, {OUT.stat().st_size / 1e6:.1f} MB")


if __name__ == "__main__":
    main()
