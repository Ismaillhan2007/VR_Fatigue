# Signals slice 2: basic gaze, head and probe measurements

## Scope

Target editor: **Unity 6000.6.7f1**. `MetricsEvaluator` consumes the completed
`QualityResult` from slice 1 and the session's probe events. It produces deterministic local
measurements in `signal-metrics.v1`, with `metric_version: gaze-metrics.v1`. It never calls Gemini.
Baseline and deviations are implemented in [slice 3](BASELINE.md), with the experimental indicator in [slice 4](INDICATOR.md). The hardware adapter is implemented in [slice 5](QUEST_PRO_INTEGRATION.md); UI remains integration work.

These are descriptive experimental observations, not validated fatigue detection. A probe latency
is the first observed sample inside tolerance, with sensor/display latency and between-sample
uncertainty uncorrected. Hardware timing must still be verified.

## Eye choice and valid time

All gaze directions must already be `head_local` (+X right, +Y up, +Z forward). Normalize each
finite nonzero direction with scaled arithmetic to avoid overflow. Head motion is not added to gaze.

Use **one eye for the whole evaluation**, never a changing per-frame blend. `MetricPolicy.GazeEye`
can pin `left`/`right`; default `auto` selects the eye with the greater continuous valid duration,
breaking ties to the left. Store `selected_eye` in the report. For later baseline/window comparisons,
select once for the full session, then pin that eye across all compared windows. Re-selecting per
window could compare different eyes and is not allowed for the baseline workflow.

Build segments from the accepted, chronological slice-1 stream. Invalid selected-eye gaze and gaps
above the quality policy's maximum gap break segments. Never join across these barriers or replace
missing data with head gaze. Selected-eye coverage and duration must meet the slice-1 quality
thresholds even if combined any-eye quality is usable.

All metrics are null with `insufficient_data` when session quality fails. Otherwise each metric has
its own prerequisites and an explicit `unavailable_reasons` entry; available values have null reasons.

## Calculations

| Output | Definition |
| --- | --- |
| `gaze_speed_median_deg_s` | Duration-weighted median of consecutive smoothed direction angular speeds |
| `gaze_speed_p95_deg_s` | Duration-weighted 95th percentile of those speeds |
| `gaze_dispersion_deg` | Time-weighted RMS angular distance of raw directions from the normalized time-weighted mean direction |
| `head_speed_deg_s` | Duration-weighted median of adjacent valid head-orientation angular speeds, context only |
| `probe_latency_ms` | Median first-observed acquisition latency across acquired probes |
| `probe_rms_error_deg` | RMS angular error across all eligible hold intervals, weighted by valid hold time |
| `probe_miss_fraction` | Missed / (acquired + missed); unavailable trials are excluded and counted explicitly |

Direction angular distance uses `atan2(norm(cross(a,b)), dot(a,b))` on unit vectors. Divide by
measured timestamp difference and convert to degrees/second. Percentiles use the inverse weighted
empirical CDF: the first sorted value reaching the requested fraction of total valid interval time.
This avoids weighting dense sampling periods more heavily merely because they contain more rows.

### Smoothing

Generalized Savitzky-Golay: degree-2 least-squares fits of normalized X/Y/Z directions over seven
actual timestamps by default. Center/scale local time; solve with reorthogonalized QR; evaluate at
the current sample and renormalize. Use a complete shifted window near segment edges, without
padding, extrapolation or samples from another segment. Singular/near-zero fits become unavailable.

Segments shorter than the configured odd window (3..101, default 7) contribute no speed values.
The remaining speed intervals must themselves meet the quality policy's minimum session coverage
and valid duration; no unsmoothed fallback is substituted. Report their count and duration.

Dispersion uses raw normalized directions, with trapezoidal endpoint time weights over valid
intervals. A near-zero mean resultant has no stable mean direction: return
`ambiguous_mean_direction`, not an arbitrary direction or zero dispersion.

### Head context

Use adjacent accepted rows only, with valid finite unit quaternions and gaps within policy.
The relative quaternion's shortest rotation is invariant to sign (`q` versus `-q`). A missing or
invalid pose breaks continuity; missing position/orientation is not inferred from gaze. Head
interval coverage and duration must independently meet the quality policy. Rotation faster than
the sampling can resolve is not reconstructed. No translation metric is produced.

## Probe contract and outcomes

A unique probe ID needs exactly three chronological events:
`target_onset < hold_start < target_end`. All carry the same normalized head-relative target
direction. Invalid sequences, moving targets, overlapping trials, and successive target spacing
below the default **6 degrees** are marked unavailable with reasons. The first trial has no
preceding target-spacing requirement. World-fixed/moving targets require a later contract.

Use **raw selected-eye samples**, not smoothed data, for acquisition and errors:

- Search from onset through the default 1,000,000 us deadline, inclusive, capped at trial end.
  A sample within the default 3-degree tolerance is a candidate acquisition.
- Credit a latency only when the full onset-to-candidate prefix has continuous valid selected-eye
  coverage. No interpolation creates a hit between samples. A hit exactly at onset has latency zero.
- If no hit exists, classify a miss only after a fully observed complete deadline window.
  An unacquired shorter trial is `probe_too_short`; a tracking gap yields
  `acquisition_tracking_loss`. Neither is a miss.
- Hold quality is evaluated independently of acquisition outcome. Default eligibility requires
  at least 100,000 us valid hold time and 80% hold coverage. Thus a trial can have an unavailable
  acquisition but a measured hold error, or the reverse.
- Within valid intervals only, integrate squared angular errors using trapezoidal endpoint values.
  Clip at hold boundaries by linear interpolation of **squared error**, never across tracking gaps.
  Divide by valid hold time and take the square root. Aggregate eligible holds by their valid time.

These defaults are versioned engineering choices, not validated physiological thresholds. The
report contains each probe's outcome, nullable latency/error, reasons and observed/window durations,
plus aggregate acquired/missed/excluded counts. A missing aggregate always has a reason.

## Integration

```csharp
var session = SignalJson.ParseSession(localJson);
var replay = new ReplayEyeProvider(session);
var batch = replay.ReadAvailable(replay.Info.DurationUs); // Complete offline evaluation.
var quality = QualityEvaluator.Evaluate(replay.Info, batch.Samples);
var result = MetricsEvaluator.Evaluate(quality, batch.ProbeEvents);
string json = MetricsJson.Serialize(result);
```

In paced playback, collect samples **and probe events from every batch** until completion.
Preserve `provider.Info` so replay remains honestly labelled. The result includes quality,
capability reasons, both policy versions, selected eye and supporting durations/counts; raw eye
samples are never serialized into the metric report. Full backend `SessionSummary` mapping is
still a separate integration decision. The application must respect nullable values/reasons.

## Reproduction and verification

```sh
python3 tools/check_signals.py --unity-editor /Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app
python3 tools/check_signals.py --unity-editor /Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app \
  --replay fixtures/metrics/delayed_acquisition.json --metrics
```

To validate actual reports, add `--reports-dir /tmp/signals-reports` to the test run, then use
`python contracts/validate_examples.py --reports-dir /tmp/signals-reports` in the Python check
environment. Regenerate committed synthetic inputs with `python3 tools/generate_metric_fixtures.py`.
No real eye data, API calls, scenes or screenshots are needed.

Local evidence: **62 NUnit methods passed**, including all 31 slice-1 regressions and 31 metric
cases. Analytical cases cover steady gaze, a known 2 deg/s sweep, irregular timestamps, separate
20 deg/s head rotation, 200 ms acquisition, a 12-degree hold error, deadlines, clipped holds,
tracking loss, malformed/overlapping probes and time-weighted statistics. Three JSON Schemas,
23 committed fixtures and 15 actual C# reports passed validation; eight invalid examples were rejected.

Checks used the installed **6000.3.19f1 C# compiler/runtime**, with warnings as errors; they do not
establish Unity editor import or headset behavior. **6000.6.7f1 is not installed here**. Target-editor
assembly/package resolution, EditMode discovery, Meta compatibility and real acquisition/probe
timing remain to be checked on that editor/device.
