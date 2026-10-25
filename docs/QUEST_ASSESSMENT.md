# Quest Pro assessment UI and development build (#18)

## Composition

`Assets/App/Quest` is a separate composition assembly. In an Android player, its scene-load hook finds the existing SessionController and installs one Meta OVRCameraRig/OVRManager, a center-eye rendering camera/audio listener and two XRI UI rays on the tracked controller anchors. Desktop Play skips installation and retains its existing UI/input. Other scene cameras/listeners are disabled when the rig is created; no locomotion component is installed.

The Meta SDK startup permission flags remain false. This module neither requests eye permission nor starts native capture. The existing QuestProRenderingController from #19 remains the sole cadence/foveation owner. The app hardware provider is still pending #21; the default replay/synthetic labels remain visible and must never be described as live measurements.

The UI is world space, 1.92 x 1.08 m at 2 m, placed once in front of the tracked head. Point and press either controller trigger; use its thumbstick to scroll. Y/B returns the panel to view and opens Pause; Resume/Exit remain explicit UI actions. Long text bodies use 24 px scrolling instead of shrinking the report. Text readability, controller aiming and keyboard behavior still require on-device review. The system keyboard feature is enabled for optional text; no custom keyboard or runtime downloads are added.

Only probes stay parented to the tracked head. Their target position is the same head-local direction at the existing 2 m distance, independent of desktop projection or panel placement. Recenter or focus/pause loss during Baseline/LoadTask clears the session, including a baseline finished between phases; there is no automatic resume into another coordinate frame. Other active stages pause. An in-flight analysis may finish updating the report during Pause; it never advances the flow or dismisses Pause, and participant transitions still require Resume.

## Dependencies and branch boundaries

- Based on `dev` after #19/#24/#25; reuses their installed packages and rendering setup.
- **Report-only completion #16 / PR #26 is required before building the final APK.** The build command rejects the missing dependency, then regenerates Assessment using the current AssessmentSceneBuilder. This branch does not merge #26 or check in a competing regeneration of its scene/prefabs.
- Android Player/OpenXR settings are published separately on `chore/issue-18-android-settings`, per CONTRIBUTING. Apply both code and settings before building. No package upgrades.
- Integrator review is required for App.Core flow context handling, ProbeTaskRunner and composition. No capture/metrics/contract/forest implementation is replaced. #21 remains responsible for the real native eye adapter, consent/permission/calibration UI and device signal acceptance.

## Reproducible build

Install Unity **6000.6.7f1 ARM64** with **Android Build Support**, its OpenJDK **17.0.18+8**, **NDK r27c**, SDK build-tools **36.0.0**, command-line tools **16.0**, platform-tools **37.0.1** and Android platform **36**. Versions come from this editor's official release module metadata; do not substitute an older editor or package graph. Unity 6.6 supports Android target API 36 ([Unity requirements](https://docs.unity3d.com/6000.6/Documentation/Manual/android-requirements-and-compatibility.html)).

1. Integrate the report-only dependency and this feature/settings through human-reviewed PRs into `dev`.
2. Run **App > Quest Pro > Apply Android Assessment Settings** explicitly. It reuses the existing 90 Hz Meta setup, selects IL2CPP/ARM64, min API 32, target API 36, `com.vrfatigue.prototype`, Oculus Touch profile and Meta system keyboard. Publish settings changes only in their chore PR; no settings mutation occurs during import or build validation.
3. Run **App > Quest Pro > Build Assessment Development APK**. It checks versions/settings/dependency, generates the owned UI through AssessmentSceneBuilder and builds enabled scenes. Default output: `builds/quest/VRFatigue-development.apk` outside the Unity project, git-ignored. Set `VR_QUEST_APK_PATH` to override it.

Batch equivalent (after settings/dependencies):

```sh
VR_QUEST_APK_PATH=/absolute/path/VRFatigue-development.apk \
  "$UNITY_EDITOR" -batchmode -quit -buildTarget Android -projectPath unity \
  -executeMethod App.Quest.Editor.QuestAssessmentBuild.BuildDevelopmentApk \
  -logFile /tmp/quest-assessment-build.log
```

`UNITY_EDITOR` is the executable inside the target Unity.app. The development APK uses local development signing; do not commit signing keys, APKs, SDK binaries or machine-local paths/settings.

## Verification and remaining device acceptance

Automated checks use synthetic/replay data and tracked-pointer models, not screenshots or a simulated claim of physical tracking. They cover real XRI raycasts/Consent/Pause/Exit dispatch, scrolling, head-relative probe geometry, interruption between phases, report completion during pause, single rig/camera/listener, controller action bindings and no startup permission requests. Six invalid Android setting cases are rejected. The dependency integration test uses the actual report-only button and preserves the report without loading ForestDemo.

Exact final test/build evidence is recorded in the PR and the acceptance record below. Local backend smoke checks keep Gemini disabled; no real eye recordings or paid APIs are involved.

The user confirmed no Quest Pro is available. Keep #18 open until a consenting operator installs the APK and records: headset OS/OpenXR runtime; head tracking and both controller rays; readable source/report/preview/Done controls; trigger, slider dragging, scrolling and optional keyboard; Y/B, pause, resume, exit, headset removal/focus loss and recenter during/between phases; successful report-only run; permission deny/revoke and operator-confirmed calibration via #21; and measured sustained CPU/GPU/frame times at the actual refresh rate. Requesting 90 Hz is not evidence of 90 FPS. No device installation, physical comfort, native eye access or performance acceptance is claimed here.
