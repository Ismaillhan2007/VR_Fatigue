# Signals slice 1: formats, replay, quality

## Scope and target

The team-selected editor is **Unity 6000.6.7f1**. This slice implements C# data models,
`IEyeTrackingProvider`, `ReplayEyeProvider`, strict JSON loading and deterministic quality.
Scene/project setup belongs to the integrator. Basic gaze/probe metrics are now implemented in
[slice 2](METRICS.md), baseline/comparison in [slice 3](BASELINE.md), and the experimental indicator in [slice 4](INDICATOR.md). The hardware provider and SDK bridge are implemented in [slice 5](QUEST_PRO_INTEGRATION.md), with physical-device acceptance pending.

Owned paths: `unity/Assets/Signals/Capture/`, `Metrics/`, their EditMode tests, local contracts and
synthetic fixtures. Application schemas/DTOs now exist in `contracts/v1` and `Assets/App/Core`; mapping the signal outputs to those contracts remains integration work.

## Input contract: eye-session.v1

- UUID session ID; source `live`, `synthetic`, or `replay`. Replay origin is `recorded`/`synthetic`
  for replay, null otherwise. Explicit start/end times include leading/trailing tracking loss.
- Timestamps are nonnegative signed 64-bit integer **microseconds** on one monotonic acquisition
  clock. Probe events must use the same clock. Map the actual SDK clock in the hardware adapter;
  never replace repeated sensor timestamps with fresh render-frame times. Samples at both session
  boundaries are in bounds. End must not precede start.
- `coordinate_frame: head_local`: +X right, +Y up, +Z forward. Convert world gaze before ingestion.
  Eye directions are finite nonzero vectors; later angular calculations normalize them. A false
  validity flag, null direction or zero vector makes that eye unusable.
- V1 accepts at least one usable eye. This does not establish binocular quality; later metrics
  must check their own signal prerequisites. Confidence is diagnostic, never a quality gate.
- Optional head pose is position in metres plus head-local-to-world quaternion `[x,y,z,w]` whose
  squared norm is within 0.001 of one. The world origin stays fixed; recentering restarts acquisition.
  Pose is context for future metrics and does not gate v1 gaze quality.
- Pupil diameter/openness have explicit capability availability/reasons. No zero-valued pupil or
  blink measurements are manufactured. Raw eye imagery and identity are outside the contract.
- Probe events contain timestamp, probe ID, kind (`target_onset`, `hold_start`, `target_end`) and
  head-relative target direction. They must be chronological and in bounds. Initial targets are
  head-relative; world-fixed targets require per-sample conversion in a future extension.
  Probe sequence/measurement validation is implemented by slice 2.

All JSON fields are required, including nullable fields. The reader rejects unknown fields/versions,
duplicate properties, bad types, nonfinite JSON numbers and inconsistent metadata. Null/zero/invalid
gaze and sample timing faults remain stream observations for quality accounting. Programmatic
nonfinite gaze is also treated as invalid. Schema validation is structural; runtime validation is required.

## Replay

`ReadAvailable(elapsedUs)` emits available samples/events once. The caller supplies elapsed time
since session start, independent of absolute timestamps. Equal times allow a pause; backwards
time is rejected until `Reset()`. Reset reproduces input. Completion includes trailing session time.

Preserve stored arrival order and timestamps, including duplicates/late/out-of-bounds rows. A row
becomes due at its timestamp clamped to session-relative bounds, or the previous row's due time
if later. This delivers bad rows for quality accounting without sorting or reversing replay time.
Probe events form a separate ordered channel in each batch.

Replay always exposes `data_source: replay`; synthetic input becomes `replay_origin: synthetic`,
live/recorded input becomes `recorded`, and existing replay keeps its origin. Input stays immutable.

## Quality policy

Initial `quality-policy.v1` defaults are prototype engineering choices, not physiological thresholds:

| Parameter | Inclusive default |
| --- | --- |
| Maximum adjacent-sample gap | 100,000 us |
| Minimum valid time | 1,000,000 us |
| Minimum valid-time coverage | 0.8 |
| Minimum usable unique samples | 2 |

Emit the policy version and all parameters in `signal-quality.v1`. Change the version when changing
thresholds. `usable` describes signal quality, not baseline/indicator readiness. The planned baseline
needs six complete usable 20 s windows: at least 120 s nominal baseline, extended when windows fail.
Actual valid time is recorded separately; the baseline implementation is documented in `BASELINE.md`.

1. Count/exclude out-of-bounds rows. Deduplicate in-bound timestamps: first occurrence wins even
   when invalid. A late first occurrence is out-of-order; later repetitions of it are duplicates.
2. Preserve strictly increasing accepted rows, including invalid gaze barriers. Gaze capability
   and at least one valid finite nonzero eye direction are required for usable gaze.
3. Credit only intervals between adjacent accepted usable rows with `dt <= maximum_gap_us`.
   Invalid rows break continuity. Longer gaps receive no partial/interpolated credit.
4. Merge touching valid intervals. Valid time plus gap time equals full session duration; coverage
   is their time ratio (zero for zero duration). Longest gap includes missing time at session edges.
5. Return `insufficient_data` and reasons for unavailable gaze, no samples, zero duration,
   insufficient sample count, short valid time or low coverage.

`AcceptedStream` retains invalid barriers; `ValidIntervals` marks usable spans. Future metrics must
respect those spans rather than join all valid-looking rows across gaps. Serialized quality reports
include counts, policy, provenance and capability reasons, never raw samples.

## Integration

```csharp
var session = SignalJson.ParseSession(localJson);
IEyeTrackingProvider provider = new ReplayEyeProvider(session);
var rows = new List<EyeSample>();
// Each update: elapsedUs is a monotonic time since this session began.
rows.AddRange(provider.ReadAvailable(elapsedUs).Samples);
// At the explicit session end, after draining the final batch:
var quality = QualityEvaluator.Evaluate(provider.Info, rows);
string reportJson = SignalJson.SerializeQuality(quality);
```

Keep rows from all batches; evaluating only the last batch loses history. Collect `batch.ProbeEvents`
as well for the next slice. Use the provider's metadata to preserve replay labels. Agree the future
`SessionSummary` mapping with integration/backend before transmitting any aggregates. The local
session stream must not be sent to Gemini.

## Verification

After project setup, run `VRFatigue.Signals.Tests` with Unity's EditMode runner. Alternatively use
an installed macOS Unity toolchain without launching the editor:

```sh
python3 tools/check_signals.py --unity-editor /Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app
python3 tools/check_signals.py --unity-editor /Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app \
  --replay fixtures/replay/tracking_gap.json
```

The helper compiles with warnings as errors in a temporary directory, using installed Newtonsoft
and NUnit assemblies. Its small runner invokes the same parameterless test methods directly; it
does not emulate Unity lifecycle or Test Runner discovery. `--replay` prints aggregate quality JSON.
Assembly definitions explicitly reference the precompiled Newtonsoft DLL, following Unity's
[assembly definition format](https://docs.unity.com/en-us/engine/6000.6/manual/scripting/compilation-and-code-reload/script-compilation/assembly-definition-files/file-format).

Install Python checks as described in `contracts/README.md`. Run `python contracts/validate_examples.py`.
To validate actual C# reports too, use `check_signals.py --reports-dir /tmp/signals-reports`, then
`contracts/validate_examples.py --reports-dir /tmp/signals-reports` in that Python environment.

Initial local verification used the installed **6000.3.19f1 compiler/runtime**, not the target editor:
31 NUnit methods passed, including eight golden reports, interval boundaries, provenance, malformed
input, replay/reset and 100 irregular-stream accounting cases. Unity **6000.6.7f1** is not installed
here. Target-editor import, Unity Test Runner discovery
and headset behavior remain unverified. No screenshots, scene checks, API calls or real recordings
were used. Repeat checks in the agreed editor when available.

Schema verification also passed with Python 3.12.14/jsonschema 4.26.0: two schemas, 16 committed
fixtures, eight actual C# reports and four deliberately invalid input examples rejected.

The paragraph above records the initial slice check. Current cross-module and SDK import
results are recorded in [QUEST_PRO_INTEGRATION.md](QUEST_PRO_INTEGRATION.md).
