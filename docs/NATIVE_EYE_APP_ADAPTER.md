# Native Quest eye capture in the app (#21)

## Scope and status

This is the code portion of #21, based on `dev` `4aa9bea` with the merged #18 rig/UI/build and packaged-replay fix. Physical headset setup/install/UI acceptance remains Alexander's #18; Michael owns native capture and real-metrics acceptance in #21. Neither issue is closed by desktop tests.

The app's `HardwareEyeProvider` is now an adapter over the existing `VRFatigue.Signals.HardwareEyeProvider`, composed with `MetaQuestEyeSource` by `App.Quest.QuestNativeEyeFactory`. App.Capture has no Meta/Unity SDK dependency. The Android composition registers the source factory before scene loading; desktop has no automatic native source. A second native owner or a separately installed QuestProCaptureController is rejected. The existing QuestProRenderingController remains the only cadence/foveation owner.

Hardware failure no longer automatically substitutes synthetic data. The participant stays at preparation until permission, calibration, focus and rendering readiness allow an explicit Start, or explicitly selects replay. A source used in tests declares synthetic provenance; only the actual Meta source declares live, and native capability is enabled only after successful start. This is not evidence that a physical device delivered samples.

## Operator flow after #18 device readiness

1. Use the current integrated build, then apply this feature through a reviewed PR into dev. Use Unity 6000.6.7f1 and the pinned packages. Rebuild the development APK with the existing QuestAssessmentBuild command; earlier APKs do not contain this adapter.
2. On the consent screen select **Use native eyes**. Select **I consent: request eye permission** only after the participant consents; respond to the asynchronous OS prompt. Granting permission never auto-starts capture. The prompt may temporarily steal focus while still at preparation; that does not clear preparation.
3. Complete the headset's eye calibration for the current wearer. Check **I completed headset eye calibration…**. This is an operator assertion, not a runtime calibration-quality measurement. If system eye tracking is disabled or start fails, the UI stays unavailable; it does not invent calibration status or replace native eyes with replay.
4. Run **Run separate 60-second eye diagnostic** first. Follow the yellow head-relative target. Completion stops native tracking/releases the rendering lock and shows aggregate timing/quality diagnostics in a scrollable body. The full existing `hardware-diagnostics.v1` JSON is available locally through `SessionController.HardwareDiagnostics`; it is not logged/saved/uploaded automatically. Record only approved sanitized aggregate evidence. Use **Clear diagnostic and start a new session** before an assessment.
5. Grant/confirm preparation again for the new session, then **I agree, start**. The ordinary configured assessment is 130 seconds baseline and 60 seconds task; native capture has no desktop Skip shortcut. Fewer than six eligible 20-second baseline windows cannot establish an indicator. The separate diagnostic cannot transition to task or become an assessment reference.
6. Finish the check-in and report using **Finish with report**; no forest/preferences/post-scene feedback is required. Native analysis is forced to local deterministic rules even if an Inspector field is later changed to Http. Backend transfer settings/consent are separate #20 work.
7. Exit or New session clears all app-owned recordings, native provider buffers, calibration confirmation, diagnostics and report. Choosing replay during permission preparation disposes the old native owner; a later OS permission decision cannot revive it. Disabling/destroying the session component also clears native state.

## Timing and measurements

- The existing Meta source reads SDK gaze time and head pose at that time, performs handedness/head-local conversion once, and preserves invalid/untracked or unaligned frames as barriers. There is no confidence gate, blink inference, current-camera compensation or fabricated pupil/openness.
- Native samples retain acquisition time in microseconds relative to source start, converted to seconds at the app seam. `FrameTime` remains receipt time; it does not replace sample time. A new TryGetCaptureTime operation reads the same native runtime clock for phase boundaries, including empty/leading/trailing gaps. Phase overshoot is capped at the requested end, not padded with synthetic rows.
- Native probe onset/hold/end are stamped when the corresponding event actually occurs. Probe onset is stamped after showing the target. Only complete ordered native event triples enter probe metrics. They bypass the legacy receipt-frame interpolation; missing events are not replaced by scheduled future events. Render/display latency still requires device verification.
- Original native received/repeated counters are snapshotted per phase before deduplication. The report can retain a truthful repeated fraction rather than reporting zero merely because the adapter already removed repeats. Between-phase polls do not become app phase rows/counters.
- One rendering lock spans baseline, the participant's baseline ratings and task. The existing owner verifies actual foveation/cadence; the composition additionally observes render scale/viewport, URP render scale/MSAA, quality level, pipeline and global ambient/fog/sky settings. A changed observed condition aborts; no in-place repair is hidden from the comparison. Material contents, arbitrary individual lights and physical display latency still require a controlled device setup and acceptance review.
- Pause, focus loss, recenter, permission loss, tracking failure, native clock faults and rendering invalidation during the comparison abort it, release the lock and clear the reference/report. Native samples are never silently resumed into the same baseline. The 10-minute native bound includes time between phases; timing out requires a new session.

The required v1 report contracts are unchanged. New optional phase/probe clock/counter fields are internal app structures; no JSON schema/backend changes, settings/package upgrades, generated scene/prefab YAML edits or vendor code changes are included.

## Validation and remaining acceptance

Tests use explicitly synthetic hardware sources. They cover consent/calibration/permission/focus/render gates, timestamp/head alignment, one-eye validity, duplicates/pre-phase rows, empty phase bounds, rendering lock across phases, native probe clocks with missing gaze and delayed receipt, original repeated counters, safe completion/disposal, pause/revocation between phases, pending permission/source changes, separate diagnostics and local-only report completion. Existing replay/report-only/backend regressions run alongside them. No screenshot-based testing is used.

Physical installation, native permissions/calibration, a real 60-second diagnostic/known-target accuracy check, an eligible real baseline/task report, timing/90 FPS and headset comfort/readability remain **UNRUN** until #18's headset/build handoff. Keep #21 open and request integrator review of the app capture/metrics/SessionController seams. No clinical validity or successful live-eye test is claimed.

## Recorded code verification — October 25, 2026

- Unity **6000.6.7f1**, complete pinned packages: **359 EditMode and 37 PlayMode passed**, none skipped. PlayMode included the authenticated loopback backend with Gemini disabled and wholly invented input.
- Native production composition was exercised in the Editor as unavailable, including real Unity owner OnDisable/OnDestroy cleanup. Fake native-acquisition paths use explicit synthetic provenance and cannot establish device support or accuracy.
- Contract validation: **14 schemas, 35 fixtures and 7 rejected invalid examples**; no C# report files regenerated by this validator run. Missing asset metadata/duplicate GUID checks clean; secret filename scan only `backend/.env.example`; handwritten whitespace check clean.
- Local evidence: `/tmp/issue21-edit.xml`, `/tmp/issue21-play.xml`, `/tmp/issue21-edit.log`, `/tmp/issue21-play.log`. No screenshot checks, device recordings or paid API calls.
- **Not run for this feature:** a new Android APK build/install or any physical headset/eye/90 FPS acceptance. The earlier #18 APK predates these changes and is not evidence for this adapter.
- After the final preparation-label adjustment, all **6 native integration PlayMode tests passed again**, none skipped (`/tmp/issue21-preparation.xml`). Before Start the source explicitly says preparing/no samples collected; it does not present a selected-but-unavailable source as active live capture.
