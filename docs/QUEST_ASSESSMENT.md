# Quest Pro assessment UI and development build (#18)

## Composition

`Assets/App/Quest` is a separate composition assembly. In an Android player, its scene-load hook finds the existing SessionController and installs one Meta OVRCameraRig/OVRManager, a center-eye rendering camera/audio listener and two XRI UI rays on the tracked controller anchors. Desktop Play skips installation and retains its existing UI/input. Other scene cameras/listeners are disabled when the rig is created; no locomotion component is installed.

The Meta SDK startup permission flags remain false. This module neither requests eye permission nor starts native capture. The existing QuestProRenderingController from #19 remains the sole cadence/foveation owner. The app hardware provider is still pending #21; the default replay/synthetic labels remain visible and must never be described as live measurements.

The UI is world space, 1.92 x 1.08 m at 2 m, placed once in front of the tracked head. Point and press either controller trigger; use its thumbstick to scroll. Y/B returns the panel to view and opens Pause; Resume/Exit remain explicit UI actions. Long text bodies use 24 px scrolling instead of shrinking the report. Text readability, controller aiming and keyboard behavior still require on-device review. The system keyboard feature is enabled for optional text; no custom keyboard or runtime downloads are added.

Only probes stay parented to the tracked head. Their target position is the same head-local direction at the existing 2 m distance, independent of desktop projection or panel placement. Recenter or focus/pause loss during Baseline/LoadTask clears the session, including a baseline finished between phases; there is no automatic resume into another coordinate frame. Other active stages pause. An in-flight analysis may finish updating the report during Pause; it never advances the flow or dismisses Pause, and participant transitions still require Resume.

## Dependencies and branch boundaries

- Based on `dev` `efaa61a`, including #16/#26/#27 and #19/#24/#25; reuses their packages, report-only implementation and rendering setup.
- **Report-only completion #16 is already in the current base.** The build command still rejects older copies missing this dependency, then regenerates Assessment using the current AssessmentSceneBuilder. No competing Assessment scene/prefab regeneration is checked into this feature PR.
- Android Player/OpenXR settings are published separately on `chore/issue-18-android-settings`, per CONTRIBUTING. Apply both code and settings before building. No package upgrades.
- Integrator review is required for App.Core flow context handling, ProbeTaskRunner and composition. No capture/metrics/contract/forest implementation is replaced. #21 remains responsible for the real native eye adapter, consent/permission/calibration UI and device signal acceptance.

## Reproducible build

Install Unity **6000.6.7f1 ARM64** with **Android Build Support**, its OpenJDK **17.0.18+8**, **NDK r27c**, SDK build-tools **36.0.0**, command-line tools **16.0**, platform-tools **37.0.1** and Android platform **36**. Versions come from this editor's official release module metadata; do not substitute an older editor or package graph. Unity 6.6 supports Android target API 36 ([Unity requirements](https://docs.unity3d.com/6000.6/Documentation/Manual/android-requirements-and-compatibility.html)).

1. Use current `dev` with the integrated report-only flow, then apply this feature/settings through human-reviewed PRs.
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

Meta 207's build callback injects a local DevAgent access token/IP into its Resources asset even when its UI is disabled. The late QuestBuildPrivacy callback disables that service/voice input and clears its address/tokens/config reference before packing; its original local values are restored after the build, including failures through the prescribed builder. This code uses Unity serialization APIs and never logs credential values. No SDK files are modified.

## Verification and remaining device acceptance

Automated checks use synthetic/replay data and tracked-pointer models, not screenshots or a simulated claim of physical tracking. They cover real XRI raycasts/Consent/Pause/Exit dispatch, scrolling, head-relative probe geometry, interruption between phases, report completion during pause, single rig/camera/listener, controller action bindings and no startup permission requests. Six invalid Android setting cases are rejected. The dependency integration test uses the actual report-only button and preserves the report without loading ForestDemo.

Exact final test/build evidence is recorded in the PR and the acceptance record below. Local backend smoke checks keep Gemini disabled; no real eye recordings or paid APIs are involved.

The team now has a Meta Quest Pro; it was last reported not yet USB-connected to this Mac. Hardware is available, while device installation and acceptance remain unverified. Keep #18 open until a consenting operator installs the APK and records: headset OS/OpenXR runtime; head tracking and both controller rays; readable source/report/preview/Done controls; trigger, slider dragging, scrolling and optional keyboard; Y/B, pause, resume, exit, headset removal/focus loss and recenter during/between phases; successful report-only run; permission deny/revoke and operator-confirmed calibration via #21; and measured sustained CPU/GPU/frame times at the actual refresh rate. Requesting 90 Hz is not evidence of 90 FPS. No device installation, physical comfort, native eye access or performance acceptance is claimed here.

## Recorded local acceptance — October 25, 2026

- Latest integrated base: `dev` `efaa61a` (report-only #26/#27 retained). Unity 6000.6.7f1, complete pinned package graph including Meta 207.0.1, OpenXR 1.18.0 and XRI 3.6.1.
- **338 EditMode tests passed**, zero skipped. Includes six invalid Android settings, one-rig structure, DevAgent sanitization and two optional-eye manifest cases. **26 PlayMode tests passed**, zero skipped, with local authenticated FastAPI/Gemini disabled. Runtime code last passed the complete suite; the final changes added only Editor manifest processing and its EditMode checks.
- Real XRI ray interactor + explicitly synthetic OpenXR controller/trigger bindings reach Consent; synthetic tracked-ray checks exercise Pause/Exit, scrolling and the complete report-only route. These checks do not emulate a real headset or native eyes.
- **Android development APK built successfully** with ARM64 IL2CPP/Vulkan, min API 32, target/compile API 36, JDK 17.0.18+8, NDK 27.2.12479018, Gradle 9.3.1 / Android Gradle Plugin 9.1.1. Local output `builds/quest/VRFatigue-development.apk`: **72,594,231 bytes**, SHA-256 `9e2bbd1151a80fef87b48fac9a6e85425de5b859da7e3fa15e48ee86e3609b8e`.
- Inspected actual APK manifest with Android aapt: `com.vrfatigue.prototype`, development/debuggable, arm64-v8a, optional `oculus.software.eye_tracking`, eye permission declared, optional overlay keyboard. The OpenXR eye-foveation manifest initially required eye hardware; a generated-manifest override now preserves optional support without removing its permission. No vendor source or static manifest YAML was edited.
- Independently parsed the packed APK's DevAgent resource using UnityPy: the complete serialized object has service/voice disabled and empty server/token fields. No secret values were printed. Existing scene/prefab metadata were not edited; no screenshot-based checks were run.
- Local evidence: `/tmp/issue18-final-edit.xml`, `/tmp/issue18-final-play.xml`, `/tmp/issue18-build.log`, `/tmp/issue18-apk-inspection.txt`. These are machine-local reports, not portable proof of device acceptance.
- **Not run:** installation, headset tracking/input/keyboard/readability, permission/calibration on Quest Pro, native eye capture, physical comfort and sustained frame-time/90 FPS measurements. A headset is now available but its tests have not been run here. Keep issue #18 open pending these checks; #21 owns native capture integration.

## Packaged replay and headset handoff update

The Android default replay is loaded from its APK StreamingAssets URL with UnityWebRequest, parsed in memory and started as Replay at the configured speed. It is never extracted to disk or sent to a remote host. The consent panel disables duplicate starts during loading and reports a safe retry message on failure; a rejected packaged replay does not silently become generated data. Exit/reset invalidates pending completion so it cannot restart capture. Desktop filesystem replay retains its synchronous path. Tests cover Android URL resolution, actual file-URL loading/parsing, report-only completion, rejected data and Exit/reset while loading. These desktop checks exercise the shared URL-loader branch; physical APK/device acceptance stays unrun.

After both PRs are integrated, install the development APK on the Quest Pro and verify replay provenance, controller input, keyboard, pause/exit, report-only completion, lifecycle interruption and frame timing. Native live eye capture remains #21. The APK hash recorded above predates this replay fix and must not be used as evidence for a rebuilt version.

Follow-up verification on the replay fix and paired settings: **345 EditMode and 29 PlayMode tests passed, zero skipped**, Unity 6000.6.7f1/full pinned packages. PlayMode used an authenticated loopback-only backend with invented input and Gemini disabled. Results: `/tmp/vr-issue18-fixed-editmode.xml` and `/tmp/vr-issue18-fixed-playmode.xml`. Android APK rebuilding/install and physical Quest checks remain unrun on this Mac, whose editor lacks Android Build Support.
