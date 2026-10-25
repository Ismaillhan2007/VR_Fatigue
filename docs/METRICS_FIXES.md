# Capture quality and calculation failures

This change fixes the application bridge rather than changing the underlying gaze
algorithms or the experimental indicator's weights and thresholds.

## Phase bounds and replay timing

`PhaseRecording` now carries optional paired `SensorStartTime`/`SensorEndTime` bounds.
They describe acquisition time even when no samples arrive. Legacy callers must set
`StartTime`/`EndTime`; the bridge maps those frame-clock bounds through captured rows,
using `SensorClockRate` for extrapolation. It never replaces the phase end with the
last received sample. New hardware integrations should provide native acquisition
bounds rather than relying on frame-clock inference.

The full session retains missing leading/trailing time and an entirely empty task.
Insufficient quality suppresses both metrics and deviations. Phase bounds must be
finite, ordered and nonoverlapping. `Clear()` also clears the acquisition metadata.

JSON replay starts at the declared session start, including any leading gap. Capture
uses `BeginPhase` to stop its clock at the requested acquisition boundary; a delayed
render frame cannot consume the next phase. `SensorTime` advances through missing
samples and remains held between phases. The controller starts this clock after file
loading, so parsing time does not consume the baseline, especially at high speed.
Probe trials beginning at the next phase's boundary are deferred to that phase.

Application metric output now identifies these mapping semantics with
`gaze-metrics.v1+capture.v2`. Shared HTTP/JSON fields, schemas, package versions,
serialized scenes and prefabs are unchanged.

## Errors and current indicator state

`MetricsEngineFactory.TryCompute` is the application's guarded calculation entry point.
A calculation exception returns `false` and a `processing-error.v1` summary with
`quality.status = insufficient_data`, null metrics/deviations, and an unavailable
indicator. Session ID, source, replay origin and local self-report are preserved.
There is no substitution of `StubMetricsEngine` values. The summary UI shows a retry
message, and exception details are not shown or logged by the controller. The normal
local/backend explanation path remains available for the participant's check-in.

The indicator is taken from the final comparison window's state. An unavailable or
partial final window clears the current indicator; an earlier usable value is not
revived. For the default replay, exact 60-second task boundaries retain three complete
20-second windows. Ending early may correctly produce an unavailable current value.
Probe diagnostics are cleared before every new calculation so errors cannot leave
another result's trial counts visible.

## Regression coverage

EditMode cases cover missing leading/trailing data, an empty task, a completely empty
capture, legacy frame bounds, a recent invalid window, a partial final window, native
phase limits at accelerated replay speed, held replay clocks and declared leading gaps.
Calculation failures are checked with synthetic, recorded-replay and live provenance
as in-memory test cases, without committing fabricated live recordings.

PlayMode coverage includes a deliberately corrupt synthetic timestamp through the
actual controller/summary UI, clearing the error on exit, and the normal accelerated
replay through summary and the forest. A C#-serialized error request is also validated
by the backend's Pydantic model and receives HTTP 200 with a deterministic fallback.
No cloud call is required.

## Restore points and review

Before updating this feature's base to the newly approved workflow PR, restore points
were created at `main` commit `170151f` and `dev` commit `aa21658`:

- `backup/main-before-metrics-fixes-2026-10-25`
- `backup/dev-before-metrics-fixes-2026-10-25`

Verified Git bundles with complete history are kept outside the checkout; the second
also preserves the in-progress patch as a stash ref. Feature changes have not been
merged into `dev` or `main`. The app capture/UI/bridge changes need integrator review.

## Verification recorded for this feature

- Unity 6000.5.2f1, isolated temporary project using the real Meta XR Core SDK 207.0.1:
  **301 EditMode cases passed**, including 12 added regression cases.
- **13 PlayMode cases passed, none skipped** with a local FastAPI process and Gemini
  explicitly disabled. This includes the saved 130+60-second replay at its default
  speed, actual native HTTP/authentication/422 checks, offline fallback, the corrupt
  timestamp path and transition into the forest. An initial offline-only run passed
  its 10 applicable cases and explicitly skipped the three backend-dependent cases.
- Backend: **130 pytest cases passed**; Ruff passed. Python contract checks passed
  **14 schemas and 35 fixtures**. The actual C# processing-error request passed
  Pydantic validation and returned HTTP 200 with a deterministic fallback.
- No deleted files, incidental package/settings/scene/prefab changes or tracked
  private files were found; the required filename scan returns only `.env.example`.

Target Unity 6000.6.7f1 is not installed here. Target-editor import, Android/Quest Pro,
LAN between laptops and visual/rendering acceptance remain unrun. No screenshot tests
or paid/live Gemini requests were used. The isolated project uses compatible test
packages and does not establish the target URP package graph's compatibility.

## Standalone test command compatibility

`tools/check_signals.py` resolves the bundled NUnit assembly from either `net472` or
`net40` instead of assuming the older directory. The real command compiled and ran
**136 core tests successfully on both Unity 6000.3.19f1 and 6000.5.2f1**. Each run
regenerated all 25 C# reports; Python then validated 14 schemas, 35 fixtures and those
reports, rejecting 30 invalid examples. This helper checks the core signal assembly;
it does not replace application EditMode/PlayMode or target-editor acceptance.
