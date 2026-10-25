# Signals slice 3: baseline and metric comparison

## Scope and interpretation

Target editor: **Unity 6000.6.7f1**. `BaselineEvaluator` builds a frozen within-session reference,
then compares later metric windows against it. This is descriptive change since session start,
not a rested-population norm, a fatigue score, a diagnosis or an accuracy claim. Indicator/EWMA
and offer-state rules are implemented separately in [slice 4](INDICATOR.md). Reconciliation, cloud
requests and UI remain separate work.

Local contracts:

- `baseline-plan.v1`: session ID, explicit baseline/comparison bounds, condition identifiers and
  nullable baseline self-report (only two 1..7 ratings, not the full application `SelfReport`).
- `baseline-comparison.v1`: window measurements, references, signed deviations, flags, provenance
  and complete policy parameters. No raw eye samples are serialized.
- `method_version: baseline-method.v1`, default `baseline-policy.v1`; existing quality and metric
  versions remain recorded. Full backend `SessionSummary` integration is still separate.

## Window and quality policy

Use nonoverlapping **20-second windows**, anchored to each phase start. At least **six complete
usable baseline windows** are required, so nominal baseline duration is **120 seconds**. If a
window fails quality, collect additional complete baseline windows; do not promote a short tail.
A 90-second phase yields four complete windows plus an excluded ten-second tail.

Samples are assigned half-open `[start, end)`: the first sample of the following phase cannot
alter baseline metrics. No boundary padding or synthesized sample restores missing edge time.
At synthetic 100 ms spacing, a full 20 s window therefore has 19.9 s supported gaze time, not 20 s.
Valid time is measured separately; 120 s nominal duration does not promise 120 s valid tracking.

Clean acquisition order/duplicate timestamps globally **before** slicing. This prevents a late row
from becoming valid merely because the newer row that preceded it falls outside the current window.
Keep invalid gaze rows as barriers. `session_quality` preserves global counts; each window reports
its own quality and metric support. Overall session quality does not replace each window's gate.

A baseline window is eligible only when complete, its any-eye quality is usable, and the fixed
selected eye independently meets quality-policy coverage and valid-duration thresholds. Each
metric reference also needs six eligible windows where that particular metric is available.
Missing head/probe values are not substituted with another metric or zero. Baseline `ready` needs
six eligible windows and at least one usable gaze reference; individual references can remain null.

`BaselinePolicy` rejects fewer than six required windows, nonpositive/nonfinite floors and invalid
configuration. A phase is bounded to 1,000 windows to avoid accidental unbounded allocation.
Any configured parameter change needs an appropriate new policy version.

## Fixed eye and probe context

Choose an automatic eye using **baseline samples only**, or respect an explicitly pinned eye.
Freeze the resulting metric policy and reuse it for every baseline/comparison window. Later
tracking improvements in the other eye do not silently switch the reference.

Probe validity (unique ID/phase sequence, static target, overlap and successive target spacing)
is checked against the complete available event stream before window assignment. A window receives
only whole probe groups contained in its bounds; an end marker exactly at the right boundary is
allowed. Crossing groups are excluded and counted in `excluded_boundary_probe_count`. Context
errors still apply to contained trials, so slicing cannot hide an overlap or reset the spacing rule.

## Reference statistics and deviations

For each metric, collect one value from every eligible baseline window where it is available:

```text
median = median(window_values)
mad = median(abs(window_value - median))
scale = max(1.4826 * mad, metric_floor)
delta = current_window_value - median
z_score = clamp(delta / scale, -4, 4)
```

Use ordinary window medians, not pooled per-frame data. Even-sized medians average the middle
two values. Report the usable window count, median, MAD, scale and floor for every reference.
If there are too few metric values, reference statistics remain null with a reason.

Initial denominator floors are engineering guards against division by near-zero, not physiological
thresholds or measurement-accuracy claims:

| Metric | Floor in its named units |
| --- | --- |
| Gaze median/p95 speed | 1 deg/s each |
| Gaze dispersion | 0.1 degree |
| Head speed (context only) | 1 deg/s |
| Probe latency | 10 ms |
| Probe RMS error | 0.1 degree |
| Probe miss fraction | 0.05 |

Store signed `delta`, clipped `z_score` and `clipped` separately. Compare to the clipping bound before
division, so even a tiny positive custom floor cannot serialize Infinity. Exactly at the bound,
`clipped` is false. No score combines different metrics here; head context is not fatigue evidence.

## Self-report and comparable conditions

- Baseline `eye_strain >= 5` or `mental_fatigue >= 5` sets `low_confidence` with
  `self_reported_tired`. Descriptive comparisons still work, labelled relative to how the person
  started this session, without claiming a rested reference.
- Missing baseline ratings add `self_report_missing`; they are never replaced by invented ratings.
- Too few complete/usable windows or no gaze reference make the baseline unavailable and visibly
  low-confidence. Partial baseline windows never contribute to statistics.
- `conditions_id` is a bounded opaque token supplied by the integrator to attest to matching
  scene, lighting, task/probe layout and calibration conditions. It is not automatically measured
  proof. Missing IDs flag `conditions_unconfirmed`; different IDs give `conditions_mismatch`.
  Both conditions prevent comparative values, while retaining current raw metric observations.

A comparison must follow baseline and use the same random session ID, source/origin, acquisition
clock start and capabilities. The capture may grow while retaining that identity. Different people,
replay/live provenance or reset clocks must not share references. Configuration is frozen in the
baseline object; comparison callers cannot silently replace metric/quality policies.

Each complete eligible comparison window reports one deviation per metric. A missing reference,
missing current metric, partial tail, failed window quality or incompatible conditions yields null
delta/z-score with an explicit reason. The raw current value can still be present: unavailability
here describes the **comparison**, not necessarily the underlying observation.

## Integration

```csharp
// Build at the end of baseline; retain only for this participant's active session.
var reference = BaselineEvaluator.Build(capturedSession,
    new AssessmentPhase(baselineStartUs, baselineEndUs, "neutral-probes-v1"),
    new BaselineSelfReport(eyeStrain, mentalFatigue));

// After collecting the loaded task, append to the same capture/clock identity.
var comparison = BaselineEvaluator.Compare(reference, completedSession,
    new AssessmentPhase(taskStartUs, taskEndUs, "neutral-probes-v1"));
string localJson = BaselineJson.Serialize(comparison);
```

Alternatively, parse `BaselineJson.ParsePlan(planJson)` and call
`BaselineEvaluator.Evaluate(session, plan)` for a completed offline capture. With replay, construct
the capture using the provider's `Info` to preserve its source label. Release the capture, reference
and result objects at participant reset/session end. Export/storage and cloud transmission are not
enabled by this module. Only wholly synthetic plans and generators are committed.

## Reproduction and evidence

```sh
python3 tools/generate_baseline_fixture.py --output-dir /tmp/vr-fatigue-baseline-demo
python3 tools/check_signals.py --unity-editor /Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app \
  --replay /tmp/vr-fatigue-baseline-demo/synthetic_baseline_session.json \
  --baseline-plan fixtures/baseline/stable_then_changed.plan.json
```

The generator creates a wholly synthetic 160 s session outside Git. Its baseline has 100 ms
acquisition and 1-degree hold error; the later phase has 200 ms and 2 degrees. These are controlled
test observations, not headset performance claims. Three committed plans cover normal, short and
self-reported-tired baselines without checking in a large repeated raw stream.

Run the existing C# check command with `--reports-dir /tmp/signals-reports`, then
`python contracts/validate_examples.py --reports-dir /tmp/signals-reports` in the Python check
environment. The Python checker independently recomputes reference medians/MAD, scales, eligibility
and signed deviations from emitted window measurements.

Local verification: **86 NUnit methods passed** (24 new baseline cases plus 62 regressions),
with warnings as errors using the installed **6000.3.19f1 compiler/runtime**. Five schemas,
26 committed fixtures, 18 actual C# reports and 16 rejected invalid examples passed checks.
Target **6000.6.7f1** import, Unity Test Runner discovery and Quest Pro behavior remain unverified;
that editor is not installed here. No screenshot tests, headset measurements or API calls were used.
