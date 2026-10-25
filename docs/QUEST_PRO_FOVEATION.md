# Quest Pro foveation and 90 Hz

The runtime module adapts the Quest Pro path from the supplied
`ETRA2027_acquisition_source_code/QP_source_2.10.4` project. It retains the
native `OVRPlugin.GetEyeGazesState(Step.Render, -1, ...)` acquisition and
`EyeGazesState.Time`, Meta handedness conversion, head-local gaze, a request
for a supported 90 Hz display rate, Medium foveation and dynamic foveation
disabled. It does not import participant recordings, CSV writers, study tasks,
the old Oculus 1.79 SDK, or the other headset projects.

The adapted rendering/geometry files are subject to
[PolyForm Noncommercial 1.0.0 and the required notices](licenses/ETRA2027-QP-LICENSE.md).
This notice applies to that adaptation; it does not relicense the entire repository.

## Runtime behavior

`QuestProRenderingController` installs one persistent owner before the first
scene in an Android player. Desktop/Editor do not install it or request eye
permission. It waits for a running OpenXR display, Vulkan and an `OVRManager`.
It owns the Meta foveation path, not Unity's separate SRP foveation controller.
The Legacy backend is intentional to match the supplied Meta implementation;
it is also usable with URP, although Unity recommends its SRP API for broader
render-texture coverage. Do not enable both paths.

Supported devices use fixed Medium foveation before participant consent or
without eye permission/capability. Eye-tracked Medium additionally requires
consent, OS permission and runtime support. Unsupported fixed foveation is Off.
No pupil diameter, openness, or live capture capability is inferred from ETFR.
The rendering runtime consumes gaze internally; this controller never records it.

`QuestProCaptureController.RequestPermission(consent)` supplies consent to the
renderer and invokes the existing asynchronous OS permission request. Wait for
permission and operator calibration before `BeginCapture(true, true)`.
Beginning capture locks the verified foveation mode and display cadence.
Late permission grants do not change a fixed-mode assessment. Permission loss,
focus loss, or rendering mode/level/cadence changes invalidate capture with
`rendering_conditions_changed`; start a new baseline. Keep one capture lock
across baseline and task, partitioning that session for analysis. Do not finish
and restart rendering conditions between those two phases.

`TryFinishCapture`, reset, withdrawal and teardown release the lock. Withdrawal
immediately disables ETFR. The public `Status`, `ActualMode`, `ActualDisplayHz`
and `SupportsRequestedDisplayHz` expose aggregate configuration diagnostics;
logs contain no gaze samples or participant identifiers. Unavailable runtime
state is reported as Unavailable, not as proof of disabled native rendering.

The source's render-step head conversion is adapted into `QuestGazeGeometry`.
Missing/zero/nonfinite rotations cannot become forward gaze. Our existing
`MetaQuestEyeSource` continues reading head pose at the *eye sample's timestamp*,
and the Signals provider rejects stale/misaligned timestamps. This avoids replacing
that protection with a current scene-camera transform from the old acquisition app.

The assessment app's separate `App.Capture.HardwareEyeProvider` is still a
placeholder with labelled synthetic fallback. This change connects the existing
Quest Pro capture bridge to rendering; it does not claim the assessment UI is
already collecting live eye measurements. The integrator must use that bridge
and retain the calibration/permission wait before selecting a live app provider.

## Project configuration

Use Unity **6000.6.7f1**, Meta XR Core **207.0.1**, OpenXR **1.18.0**.
In Unity, run **App > Quest Pro > Apply 90 Hz Meta Foveation Settings**.
The explicit editor command sets Vulkan-only Android rendering, Multiview
(`SinglePassInstanced`), Legacy foveation API, both Meta foveation features,
disables the competing Unity foveation feature, and declares optional eye
tracking support in the Meta project configuration. It also assigns the Android
OpenXR loader and enables Initialize XR on Startup; an existing alternative
loader must first be disabled explicitly.
An Android build fails with actionable errors when these requirements are missing.
Configuration is never silently changed on import or during a build.

Commit `ProjectSettings`/XR configuration separately on a `chore/` branch, per
the repository workflow. Preserve GUIDs and use the Unity APIs/menu, not hand
edits to YAML. Do not commit SDK caches or `DevAgentSettings.asset`.

## Device acceptance

On Quest Pro, verify permission denied, granted, withdrawn, unsupported eye
tracking, runtime startup, pause/resume, scene changes and display readback.
Check `[QuestRendering]` diagnostics for verified mode, Medium, dynamic=False,
and actual displayHz=90. Fixed mode is expected without consent/permission;
Unavailable must not be presented as active ETFR.

90 Hz is a requested display cadence, not measured application throughput or
eye sample frequency. Use device profiling after warmup and over several minutes:
record CPU/GPU times, application FPS, dropped frames and thermal behavior.
The frame budget is 11.11 ms. Compare Off, fixed and eye-tracked modes at the
same render scale, MSAA, scene and task; inspect foveal clarity on the headset.
Do not change those conditions inside a baseline/task pair. Foveation reduces
GPU shading work; a CPU-bound scene still needs CPU optimization.

Primary references:
[Meta ETFR](https://developers.meta.com/vr/documentation/unity/unity-eye-tracked-foveated-rendering/),
[Unity foveation setup](https://docs.unity.com/en-us/engine/6000.5/manual/xr/graphics/foveated-rendering/enable),
and the installed SDK/OpenXR source for the pinned versions.
