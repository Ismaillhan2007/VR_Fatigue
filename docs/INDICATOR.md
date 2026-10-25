# Signals slice 4: experimental baseline-deviation indicator

## Meaning and entry point

Target editor: **Unity 6000.6.7f1**. `IndicatorEvaluator.Evaluate(comparison)` consumes the frozen
baseline comparison from slice 3. It returns `signal-indicator.v1`, labelled `experimental: true`,
with `interpretation: baseline_deviation` and method `experimental-indicator.v1`.

The value is an experimental change relative to session start. It is not a fatigue probability,
a confidence percentage, a diagnosis or evidence of improved health. Display the experimental
label, baseline limitations and per-metric deviations alongside it. A `high` level describes this
engineering indicator's band only. The next check-in/reconciliation layer must still respect what
the participant says and chooses.

This indicator supports the core report in an eye-tracking app. Its break-offer event is an invitation, never consent to enter a 3D scene. Report-only completion must remain available regardless of the indicator band; scene participation is a separate optional use case. See [product objectives and current flow gaps](OBJECTIVES.md).

## Fixed signed inputs and policy

Default `indicator-policy.v1` uses the baseline-clipped z-scores from full **20 s** comparison
windows, with expected baseline clipping **+/-4**:

| Metric | Weight | Direction |
| --- | --- | --- |
| Probe latency | 3 | +1 |
| Probe RMS error | 2 | +1 |
| Probe miss fraction | 2 | +1 |
| Gaze speed p95 | 1 | -1 |
| Gaze speed median | 0 | Disabled |
| Gaze dispersion | 0 | Disabled |
| Head speed | 0 | Context only |

The dispersion direction was unspecified in the initial concept. Its earlier proposed weight 0.5
is therefore disabled in v1 rather than inventing a direction. It remains visible as an observation.
The p95 negative direction is the plan's unvalidated hypothesis, not a claim of known fatigue behavior.

```text
signed_combination = sum((weight / total_weight) * direction * z_score)
raw_value = clamp(signed_combination / 3, 0, 1)
```

Default total weight is **8**. All four positive-weight terms must be available, with compatible
baseline references and usable current data. There is no renormalization over whichever terms
happen to remain: a missing required term makes the indicator unavailable. For example, if all
current probes miss and latency cannot be measured, keep the actual miss observations but do not
invent latency or an indicator value.

Parameters are immutable, validated and serialized. Custom policies may change positive weights,
alpha, normalization and thresholds, using a new policy version. V1 keeps the four required terms
and their signs fixed; median/dispersion/head cannot be enabled by custom weights. Nonfinite/zero
required weights, overflowing sums or contradictory thresholds are rejected.

Normalize weights before multiplication, clamp floating-point combination roundoff to +/-4,
then clamp the normalized result to 0..1. A negative combination maps to zero, not a clinical
interpretation. The original deviations and each term's weight/direction/contribution stay visible.

## Missing-data gates

The indicator is unavailable for an unusable baseline, incompatible baseline window/clipping
policy, unknown/different condition IDs, a partial or ineligible window, missing baseline probes,
missing current probes, or any missing/invalid required deviation. Every unavailable point has
`value: null`, `level: unavailable`, a reason, missing-metric names and contribution diagnostics.
Disabled terms have algebraic contribution zero; this is not a fabricated measurement.

A self-reported-tired or missing-rating baseline may still support descriptive changes when its
measurement/condition gates pass. Preserve `baseline_low_confidence` and its reasons prominently;
never re-label it as a rested reference. The report retains the complete baseline comparison,
including nullable observations, source labels and both data-quality policies.

## EWMA and level boundaries

The first available point in a continuous run seeds EWMA with its raw value. Subsequent points use:

```text
value = 0.3 * raw_value + 0.7 * previous_value
```

No invented zero seed, padded samples or elapsed-time decay is used. Clip floating-point roundoff
to 0..1. Classify the **smoothed** value:

| Level | Range |
| --- | --- |
| `low` | value < 0.35 |
| `moderate` | 0.35 <= value < 0.6 |
| `high` | value >= 0.6 |
| `unavailable` | No supported value |

Every point reports the previous level and `level_changed`. These level boundaries are distinct
from the persistent break-offer state below; they do not claim validated thresholds.

## Confirmation and hysteresis

- An offer activates only after **three contiguous valid windows with smoothed value > 0.6**.
  With default windows, this means at least 60 s of completed observations. Exactly 0.6 is a high
  level but does not advance the strict-above-threshold confirmation counter.
- On activation emit `nudge_event: offer_break` once. While active, keep the latch without repeated
  offers; the high counter saturates at the required count.
- Release an active offer only when smoothed value **< 0.4**, emitting `released` once. At exactly
  0.4 it stays active. Moderate values can retain the latch without adding high confirmations.
- Any unavailable window clears EWMA, confirmation and the active latch immediately. Output no
  stale value and no invented `released` observation; record `state_reset: true` and
  `reset_reason: unavailable_window`.
- A skipped time interval also resets state before the next point (`time_gap`). Duplicate,
  overlapping or backwards windows are rejected, so a repeated frame cannot count as confirmation.

The offer is a local data event for a gentle user-controlled prompt. This module sends no messages,
switches no scene and overrides no preference. If a UI recomputes the complete timeline after
appending data, consume only newly completed points and deduplicate historical events by session,
policy/baseline identity and window end. Re-evaluating a timeline deterministically returns its
historical offer event again.

## Integration and state ownership

```csharp
var comparison = BaselineEvaluator.Evaluate(session, plan);
var indicator = IndicatorEvaluator.Evaluate(comparison);
string localJson = IndicatorJson.Serialize(indicator);
```

Each evaluation creates fresh state and preserves the comparison's live/replay/synthetic provenance.
The serialized report includes the original comparison so the UI can show measurements and
limitations together. It remains local; the nested report can contain baseline self-report.
Discard capture/reference/result objects on participant reset.

`IndicatorStateMachine` is a lower-level math helper for already validated normalized inputs, not
an alternative measurement or provenance API. The integration path should use `IndicatorEvaluator`.
Create/reset the helper per session if using it for incremental processing. It accepts equal-size
valid windows, supports explicit unavailable partial windows, and never shares global state.

## Synthetic checks and reproduction

`fixtures/indicator/*.sequence.json` are explicitly synthetic, **test-only** normalized sequences.
They test EWMA and exact boundaries without pretending to be eye measurements or a validated
baseline. `*.plan.json` plus the documented stream recipes in `IndicatorCalculationTests.cs` cover
the real replay -> quality -> metrics -> baseline -> indicator pipeline.

```sh
python3 tools/generate_baseline_fixture.py --output-dir /tmp/vr-fatigue-indicator-demo --comparison-windows 4
python3 tools/check_signals.py --unity-editor /Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app \
  --replay /tmp/vr-fatigue-indicator-demo/synthetic_baseline_session.json \
  --baseline-plan fixtures/indicator/sustained_change.plan.json --indicator
```

The synthetic example has four later comparison windows. A sustained high indicator offers a
break on the third; unavailable input restarts confirmation. Use the existing test command with
`--reports-dir /tmp/signals-reports`, then validate with
`python contracts/validate_examples.py --reports-dir /tmp/signals-reports` in the check environment.
The independent Python checker recomputes weights, all gates, EWMA, levels and offer/release events
from the original comparison, as well as validating the explicit sequence oracles.

Target-editor import, Unity Test Runner discovery and Quest Pro behavior remain unverified:
**6000.6.7f1 is not installed here**. Local checks use the installed **6000.3.19f1 C# toolchain**,
with warnings as errors. No screenshots, real participant data, API calls or clinical validation
are involved.

Completed local verification: **116 NUnit methods passed** (30 indicator cases plus 86 earlier
regressions). Seven schemas, 34 committed fixtures and 23 actual C# reports passed validation;
24 deliberately invalid/contradictory examples were rejected. The complete synthetic CLI path
also validates replay provenance and a single offer on the third sustained high window.
