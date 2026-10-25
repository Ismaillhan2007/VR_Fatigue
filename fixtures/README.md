# Fixtures

Everything here is **synthetic**: hand-written values that match the v1 contracts. None of it is a measurement of a person, and no file comes from a lab or participant recording.

- Valid examples: `session_synthetic_*.json`, `self_report_*.json`, `preferences_default.json`, `scene_config_calm_forest_sunset.json` (the saved offline demo preset), `analysis_fallback.json`.
- `invalid/` holds inputs that must be rejected. `contracts/validate_examples.py` checks both groups.
- There is deliberately no committed `live` example: that would be invented data presented as a measurement. Live and recorded-replay sessions come from consenting teammates, stay in the git-ignored `session-data/` folder, and are never committed.

The signal module now supplies JSON replay fixtures in `fixtures/replay/`; the application CSV replay stub uses a separate format. Generate them synthetically and include the artefacts real headset streams show, so the metrics code is tested against them:

- repeated sensor timestamps (about 2-3% of rows) that must be dropped before metrics;
- an occasional 2x frame gap (about 22 ms at a 90 Hz frame clock) and rare stalls of several hundred ms;
- tracking-lost segments (`valid=false`), segments with a constant `confidence` value, and unsupported openness/pupil columns left empty;
- a steady-gaze file, a known-motion file (saccades to targets at known angles and times), and a baseline-plus-loaded pair for the indicator tests.

`.gitignore` blocks `*.csv` everywhere except under `fixtures/`; review any new CSV there for provenance before committing.

---

# Synthetic signal fixtures

All committed `replay/` sessions are generated, nonpersonal examples. No headset recordings or
real eye measurements are included. Each `<case>.json` has an expected `<case>.quality.json`
report for playback under `quality-policy.v1`.

| Case | Expected behavior |
| --- | --- |
| `steady_gaze` | Full valid time; probe events demonstrate the format only |
| `tracking_gap` | 600 ms gap, 400 ms valid time; insufficient data |
| `duplicate_and_out_of_order` | Bad rows counted/excluded without inflating valid time |
| `invalid_gaze` | Null/zero-vector gaze breaks continuity; 700 ms valid time |
| `monocular_zero_confidence` | One usable eye suffices; zero confidence does not reject gaze |
| `unsupported_gaze` | Explicit missing capability, no invented observations |
| `empty_session` | All elapsed time is missing; insufficient data |
| `zero_duration` | No division by zero; insufficient data |

The deliberately small streams use 100 ms intervals to exercise the quality-gap boundary; they
do not imitate a measured Quest Pro sampling rate. They are too short for baseline/indicator
computation. Regenerate with `python3 tools/generate_signal_fixtures.py`. Expected reports are
analytical oracles specified in that script, not copied from C# output.

See `contracts/README.md` for schema checks. Real recordings stay local in ignored session-data
directories and are never committed.

## Basic metric inputs

`metrics/` adds seven fully synthetic two-second sessions, all in the same local `eye-session.v1`
format. Their analytical expectations live in `MetricCalculationTests.cs`:

| Case | Expected observation |
| --- | --- |
| `steady_acquired` | Zero gaze/head motion and error; immediate acquisition; no miss |
| `known_rotation` | 2 deg/s head-relative gaze sweep, separate 20 deg/s head rotation |
| `irregular_rotation` | Same physical speeds with deliberately uneven timestamps |
| `delayed_acquisition` | First raw acquisition at 200 ms; hold error zero |
| `probe_missed` | Fully observed miss; hold RMS 12 degrees; latency unavailable |
| `acquisition_tracking_loss` | Globally usable stream, acquisition unavailable after a gap; valid hold |
| `insufficient_gaze` | No samples; every metric is null with an explicit reason |

The nominal 50 ms spacing and irregular examples are synthetic test parameters, not a claimed
headset rate. Regenerate with `python3 tools/generate_metric_fixtures.py`. Actual C# report outputs
can be written to a temporary directory with `tools/check_signals.py --reports-dir`; they are not
copied back as their own expected values. See [METRICS.md](../docs/METRICS.md) for formulas and checks.

## Baseline plans

`baseline/` contains three explicitly synthetic plans: `stable_then_changed`, `short_baseline`,
and `tired_baseline`. The associated long synthetic stream is generated locally, not committed:

```sh
python3 tools/generate_baseline_fixture.py --output-dir /tmp/vr-fatigue-baseline-demo
```

The fixed recipe has 1,601 samples over 160 seconds at synthetic 100 ms intervals, with a 120 s
baseline and 40 s comparison. Acquisition changes from 100 to 200 ms; hold error from 1 to 2 degrees.
The short plan deliberately uses only 90 s baseline; the tired plan has a synthetic baseline rating
of 5. These are test inputs, not real participant reports or headset performance measurements.
NUnit builds the same analytical recipe independently in memory. See [BASELINE.md](../docs/BASELINE.md).

## Experimental indicator inputs

`indicator/` has five baseline plans and three explicitly synthetic, **test-only** normalized
sequences. The sequences verify temporal math only; they are not eye recordings or evidence
of fatigue. Their analytical expected values, levels and offer events are checked in C# and Python.

The plan files are used with specific synthetic stream recipes in `IndicatorCalculationTests.cs`:

| Plan | Stream recipe |
| --- | --- |
| `sustained_change` | Baseline plus four later windows; offer on the third high window |
| `tracking_gap` | Same stream with invalid gaze from 160 to 180 s; confirmation restarts |
| `no_probes` | Same gaze stream but no probe-event stream; indicator unavailable |
| `tired_baseline` | Same valid stream; synthetic baseline eye-strain rating 5 stays visible |
| `short_baseline` | Same stream; only 90 s baseline, so indicator unavailable |

Plans describe bounds/ratings/conditions only: a `tracking_gap` or `no_probes` filename does not
modify a capture. The test supplies that stream variation explicitly. For the sustained valid CLI
example, generate the stream with `--comparison-windows 4`; the baseline generator's default
remains two comparison windows. No generated raw stream or actual report is committed.
