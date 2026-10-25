# Quest Pro capture: integrator handoff

Product objective: an eye-tracking assessment and experimental wellness report, with successful report-only completion. Rendering a forest is a separately chosen relaxation use case. Hardware consent must not imply scene participation; see [objectives and current flow gaps](OBJECTIVES.md).

## Delivered and pending

The hardware module is implemented: pure C# `HardwareEyeProvider`, an SDK-backed
`MetaQuestEyeSource`, and `QuestProCaptureController`. The existing replay, quality,
metrics, baseline and indicator code is reused. This is a source/API handoff; the upstream application scaffold now supplies session UI, DTOs and integration seams.
The app now uses the signal metrics engine and JSON replay. The native capture bridge is
not yet wired into the app's `EyeProviderFactory`; a validated Quest APK remains pending.
The backend and placeholder forest are integrated, but normal report-only completion is not.

Target editor: **Unity 6000.6.7f1**, retained in `unity/ProjectSettings/ProjectVersion.txt`.
Meta XR Core SDK: **207.0.1**, the latest stable release reported by the
[official Unity registry](https://packages.unity.com/com.meta.xr.sdk.core) on 2026-10-24.
Its package metadata requires Unity 6000.0.66f2 or newer; this minimum is not proof of
compatibility with every newer editor. OpenXR is pinned to stable **1.18.0**,
XR Management 4.7.0, Newtonsoft 3.2.2 and Test Framework 1.8.0 (preserved from the updated application project).
Animation, Asset Bundle and Physics 2D builtin modules are explicit dependencies:
SDK source compilation needs them even though the capture module does not.

The official SDK archive was downloaded and its SHA-1 matched registry metadata:
`d4cd875f54ee95c30df66cf9716914e219f77272`. The installed local cache is
`unity/Library/PackageCache/com.meta.xr.sdk.core@207.0.1`; `Library/` is ignored.
Teammates restore packages from the committed manifest/lock; do not copy licensed SDK
source into `Assets/` or Git. See [ASSETS.md](ASSETS.md).

**Pending acceptance:** import/test in 6000.6.7f1, Android build, OS permission flow,
headset/runtime compatibility, calibrated directions, native eye/head time alignment,
and the 60-second device check below. No real eye data or physical headset verification
has been obtained here. Local verification results are recorded at the end.

## Scene and Android setup (integrator owns scene/prefab edits)

1. Open `unity/` in the target editor; install Android Build Support, SDK/NDK and OpenJDK.
   Let UPM resolve dependencies. Keep existing `.meta` files.
2. Configure an Android standalone build and Android OpenXR loader under XR Plug-in
   Management. Enable Meta Quest support using the installed OpenXR settings and
   resolve applicable Meta Project Setup Tool requirements. Record the actual headset
   OS/runtime and build settings in the integration PR.
3. Add the SDK's camera rig/`OVRManager` to the assessment scene. Set eye tracking
   support to **Supported** so replay remains available on unsupported devices.
   Ensure the generated Android manifest contains `com.oculus.permission.EYE_TRACKING`.
   Confirm this in the built APK; adding a package alone does not configure a scene.
4. Add `VRFatigue.QuestPro.QuestProCaptureController` to an active object. Set maximum
   seconds (default 300, capped at 600). No per-eye `OVREyeGaze` component is required:
   this adapter reads `OVRPlugin.GetEyeGazesState` directly. Avoid other components
   independently starting/stopping eye tracking during a capture.
5. Show explicit consent and calibration instructions. After consent, call
   `RequestPermission(true)`. This launches an asynchronous OS request; it does not
   mean permission was granted. Wait/check readiness, complete headset eye calibration,
   then call `BeginCapture(true, calibrationConfirmedByOperator: true)`.
   Calibration confirmation is an operator assertion, not an SDK calibration measurement.
6. If start fails, display `Provider.StatusReason`; `MetaQuestEyeSource.SetupStatus`
   distinguishes editor/non-Android, missing manager and unsupported SDK version.
   Offer explicitly labelled replay through `ReplayEyeProvider` when appropriate.
   There is no automatic substitution of replay or head gaze for live eye data.

The SDK bridge is a separate `VRFatigue.QuestPro` assembly referencing `Oculus.VR`.
`VRFatigue.Signals` lives in `Assets/Signals/` and stays free of UnityEngine references. The tested API family is
207.x, enabled by the assembly's package version define; review APIs and update the
package-version diagnostic before upgrading to a different family. Desktop capture
returns unavailable; desktop metrics continue through replay.

## Capture API and integration boundaries

`Update()` polls the native source once per render update. `BatchReady` emits new rows
and pending probe events; consumers must retain all batches if computing incrementally.
`TryFinishCapture(out EyeSession session)` returns the complete immutable session only
for normal completion or duration limit and no source error. It rejects permission/focus
loss, pause, recenter, clock faults and buffer-limit exits. Always gate the downstream
report on this result, then on the existing quality/baseline/indicator statuses.

Use `RecordProbe(id, kind, headLocalTargetDirection)` when a target actually appears,
hold starts or target ends; kinds are `target_onset`, `hold_start`, `target_end`.
Targets are head-relative (+X right, +Y up, +Z forward), at least 6 degrees apart.
The method stamps events from the native runtime clock; do not pass render timestamps
or mix world-fixed target vectors into this interface. Probe timing still includes
render/display latency and is not established physical sensor latency.

The finished `EyeSession` feeds `QualityEvaluator`, `MetricEvaluator`,
`BaselineEvaluator` and `IndicatorEvaluator` unchanged. A baseline plan must use that
session's ID and relative bounds, at least six eligible complete 20-second baseline
windows, matching conditions and subsequent comparison windows. See
[BASELINE.md](BASELINE.md) and [INDICATOR.md](INDICATOR.md). Never treat a short
60-second hardware check as a ready baseline or validated fatigue assessment.

Pause, focus loss, disable and `OVRDisplay.RecenteredPose` abort capture. There is no
auto-resume across coordinate or timing changes. Start again with explicit user action,
a new session ID and a new baseline plan. `ResetParticipant()` clears the provider's
buffers; `WithdrawConsent()` also marks consent withdrawn. The integrator must discard
any already returned batches/sessions/reports and clear UI-owned data too. Destroying
the controller disposes its provider. No raw samples are logged, saved or uploaded by
these components; stop any external subscriber recording when consent is withdrawn.

## Timing, quality and limits

- Sample time is SDK `EyeGazesState.Time`; session/probe clock is
  `OVRPlugin.GetTimeInSeconds()`. Both are native runtime seconds, rebased to integer
  microseconds. The diagnostic says `runtime_reported_unverified`: sensor acquisition
  semantics and latency require device verification.
- Head pose is queried at eye time through `GetNodePoseStateAtTime`. SDK pose
  conversion changes handedness into Unity axes. Eye orientation is converted into
  head-local direction using this head quaternion, never the scene's current Transform.
  Missing/untracked/nonfinite head poses or head times more than 1,000 us from eye time
  produce an invalid gaze barrier. Check `unaligned_head_frames` first if coverage is low.
- Duplicate, backward, nonfinite, pre-start, future and post-end eye times are counted
  and rejected. Quantization collisions are counted separately. A late sample is never
  moved back into the session. Long gaps remain gaps; no interpolation is performed.
- Per-eye validity is native `IsValid` plus finite nonzero direction. Confidence is
  observed, never used as a filter. Pupil diameter and eyelid openness remain unavailable
  with `not_exposed_by_runtime`; invalid tracking is not a blink.
- No 90 Hz assumption. The adapter polls a cached SDK state, so device timestamp
  statistics must establish what unique data arrives. Counts of repeated frames are
  retained even though only unique accepted samples reach metrics.
- Buffers default to 100,000 samples/10,000 probe events and at most 10 minutes;
  the controller defaults to 5 minutes. Reaching a buffer limit stops capture and the
  controller rejects that session for analysis. Reset between participants.

## 60-second device acceptance

Run a standalone development APK on a consenting operator's Quest Pro, after eye
calibration. Keep a stable head-relative target layout. Complete at least 60 seconds
without pause/recenter, then finish. Call `GetAggregateDiagnostics()` and inspect the
local JSON against `contracts/v1/hardware-diagnostics.schema.json`.

Record aggregate values in the setup PR: actual SDK/editor/runtime/device versions,
permission/calibration confirmation, capture state/reason, duration, accepted samples,
`median_dt_ms`, every `dt_histogram` bucket, `repeated_fraction`, rejected timestamp
counts, `unaligned_head_frames`, clock faults, per-eye confidence range/variation,
quality valid coverage/duration, longest gap and invalid sample fraction
(`quality_report.quality.counts.invalid_gaze / accepted_samples`, null if empty).
The histogram describes **accepted unique sample intervals**; repeated fraction uses
all frames received. Raw eye/head rows stay local and are not required in the PR.
`recording_period_complete` only means a live-source duration of at least 60 seconds;
`headset_review_required` always stays true. Neither field certifies a successful test.

Confirm directions follow left/right/up/down targets in head-local axes, head turns
with a stable head-relative target do not create false gaze motion, pose times align,
and timestamps progress with the expected clock. Compare sustained valid/invalid
tracking and long stalls. Repeat deny/revoke permission, headset removal/focus loss,
recenter, reset and replay selection. An aborted session must not yield a usable report.
If native head time is not aligned, investigate it before changing tolerance; never
replace the head pose with a fresh predicted scene pose to make coverage pass.

Finally run the same metrics pipeline on a synthetic replay, then a real completed
baseline/comparison session if hardware acceptance passes. Keep live/replay provenance
visible in the application's UI and the indicator explicitly experimental. The application
UI and summary contracts exist under `Assets/App/`; mapping signal results into those
DTOs, replacing the application stubs and forest-scene wiring remain integrator work.

## Verification

The deterministic toolchain check runs core code and all parameterless NUnit test
methods without launching Unity:

```sh
python3 tools/check_signals.py --unity-editor /Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app --reports-dir /tmp/vr-fatigue-signal-reports
python3 contracts/validate_examples.py --reports-dir /tmp/vr-fatigue-signal-reports
```

Use the Python environment in [contracts/README.md](../contracts/README.md).
This helper does not compile `Assets/QuestPro` or emulate MonoBehaviour lifecycle.
The new 20 hardware tests use explicitly synthetic sources: gates, timestamps,
head alignment, faults, buffer limits, reset/consent clearing, and capture -> baseline
-> experimental indicator. Two aggregate hardware outputs are independently checked
by Python; they cannot establish hardware support.

Completed locally with installed **Unity 6000.3.19f1** (not the target editor):

- Real SDK **207.0.1** import and compilation in an isolated temporary Unity project:
  exit 0, no C# compiler errors; `VRFatigue.Signals`, `VRFatigue.QuestPro` and test
  assemblies produced. The bridge's `VR_FATIGUE_META_207` version define was enabled.
- Unity EditMode Test Runner: **136 passed, 0 failed, 0 skipped**; NUnit XML at
  `/tmp/vr-fatigue-unity-editmode-results.xml` on this machine.
- Standalone core compiler/runner: **136 passed, 0 failed**. Python validation:
  **8 schemas, 34 fixtures, 25 actual C# reports, 30 invalid examples rejected**.
- The UPM dependency graph was resolved in that temporary project using the extracted,
  checksum-verified SDK as a local package. The committed lock changes only its SDK
  entry to registry version `207.0.1`/`https://packages.unity.com`; it has no local paths.
  That isolated check used the installed editor's Test Framework **1.6.0**. After pulling
  the application scaffold, its **1.8.0** pin and other package versions were preserved;
  only Meta SDK/XR Management and missing SDK dependencies were added to its lock.
  Re-resolve/verify the combined lock in the target editor before merging.

No screenshot tests, target-editor run, Android build, USB/Link capture or headset
recording were performed. Compiler/Test Runner evidence does not prove live eye access.

## Synchronization with the application scaffold

This feature branch is based on `origin/main` commit `4665da0` (pulled before publishing).
All upstream `Assets/App/` source and GUIDs are preserved. The signal module was moved
to `Assets/Signals/` with its own GUIDs retained, avoiding assembly/file collisions with
`App.Capture` and `App.Metrics`. `App.Capture.HardwareEyeProvider` is still the application
placeholder; the implemented provider is `VRFatigue.Signals.HardwareEyeProvider`,
used by `QuestProCaptureController`. The integrator must explicitly bridge the application
interfaces before its hardware selector or summary screen uses these new implementations.
Both sets of application/signal schemas and fixture validation are retained.

After synchronization, a combined isolated Unity 6000.3.19f1 check compiled the real
SDK bridge alongside upstream `App.Core`, `App.Capture` and `App.Metrics` and ran both
EditMode suites: **235 passed, 0 failed, 0 skipped** (136 signal tests and 99 application
cases). Required repository/StreamingAssets fixtures were copied into that temporary
project. Results: `/tmp/vr-fatigue-after-pull-final.xml`. The common Python validator
also passed all 14 schemas, both application fixture checks and the 25 emitted signal
reports. This check excludes application UI/scene/PlayMode assemblies and does not
replace importing the complete combined project in Unity 6000.6.7f1 or testing Android.
