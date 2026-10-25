# PR #36 follow-up and Quest Pro handoff

Updated product target: [immediate reports and consented Gemini summaries](CONSENTED_GEMINI_REPORT.md). The historical offline checks below remain valid; cloud opt-in adds a separate optional path.

## Fixed integration defect

The combined candidate previously contained Alexander's first #35 commit
`794d5f6`, but missed `bfd16cc`. A Start click during the 0.5-second tracking-settle
delay could therefore move the panel after baseline recording began. Integration
merge `2ae733351f3c3b23b4148b2030ec8f16d06177a9` includes the latest #35 head
without conflicts, preserves both parents, and retains the UI -> native setup ->
controller rays initialization order. The placement gate cancels pending work
on baseline/task entry and checks the flow state immediately before placement.
Three regression tests cover settling/tracking loss, early Start, and the next
session's placement. No native adapter, wire contract, scene or prefab was rewritten.

## Current desktop verification

Runtime source tested: `2ae733351f3c3b23b4148b2030ec8f16d06177a9`, based on
dev `4aa9bea`. Documentation-only commits may follow this tested runtime tree.

- Unity 6000.6.7f1, full pinned packages: 392 EditMode tests passed, zero failures/skips.
- Unity PlayMode: 45 tests passed, zero failures/skips, including authenticated
  HTTP, offline report completion, trigger order, joint native/backend controls,
  native lifecycle and stale-check/reset regressions.
- Backend: 152 tests passed; Ruff passed for app/tests/scripts.
- Contracts: 14 schemas, 35 fixtures, seven rejected invalid examples passed.
- Authenticated loopback probe: health/analyze 200, session/provenance preservation,
  local-only rules, missing/wrong token 401 and semantic 422 passed. A first probe
  without the test token returned the expected 401; the matching-token probe passed.

Unity tests use an isolated checkout and, for PlayMode, a separate authenticated
loopback receiver on port 8146 with wholly invented input and Gemini disabled.
Both `VR_UNITY_BACKEND_TEST_URL` and `VR_SMOKE_BACKEND_URL` point to that receiver;
`VR_RUN_BACKEND_TESTS=1` enables the HTTP cases. The fixture credential is invented;
no developer dotenv, actual backend credential or participant eye data is used.
Existing seven packaged-image checks are historical evidence for unchanged
backend runtime; this follow-up does not claim a fresh packaged-image run.
The follow-up source/docs whitespace and deleted-file checks passed; only the
expected backend/.env.example appeared in the private-filename scan. Unity's
incidental OpenXR settings rewrite was restored after the runs; no package,
ProjectSettings, scene or prefab changes are part of this follow-up.

## Alexander — #18 build, UI and performance

Review #36's rig/session/UI integration and build the current combined PR head,
not an older #35 APK. This Mac has Unity 6000.6.7f1 but only Mac/WebGL playback
engines; Android Build Support/SDK/NDK/OpenJDK are absent. Use the team's
Android-capable machine with the pinned package graph. Close the Editor before
updating the checkout. Apply the reviewed Android assessment settings and build
the development APK through the existing App > Quest Pro menu.

1. Record source commit and APK hash, successful build/install, Unity/SDK and
   headset OS/runtime versions. Confirm an authorized USB device and launch.
2. Test both controller rays, trigger clicks, slider dragging and thumbstick
   scrolling. Check panel/text readability and reachable report/Done/Exit controls.
   Test Backend settings keyboard entry, masking and overlay close/reopen without
   entering actual credentials into screenshots or committed assets.
3. Exercise early Start during the settle delay, stable-tracking placement, return
   to Consent and headset remount. No delayed placement may occur during Baseline
   or LoadTask. Preserve the fixed conditions across the assessment.
4. With upload declined and Gemini off, complete bundled synthetic replay ->
   report -> Finish with report. Keep REPLAY-of-synthetic and local-fallback labels;
   do not require a forest load, preferences or scene feedback.
5. Test Y/B pause access, resume where valid, focus/headset removal, recenter, Exit
   and New session. Native interruptions must discard the old baseline and require
   a new session; reset/late work must not revive capture, reports or credentials.
6. Measure CPU/GPU frame times, actual display cadence and dropped frames after
   warmup and during a short comfort run. Render scale 1.0 plus MSAA 4x is an
   unmeasured regression risk, not an established performance failure. At observed
   90 Hz the frame budget is 11.11 ms. If performance fails, fix/profile it on a
   focused branch and repeat the same scenarios with constant capture settings.

Report pass/fail/unrun and link each defect. Hand Michael the exact usable
commit/APK and known limitations. Alexander owns these fixes; Michael does not
need to reimplement controller/UI work to start native acceptance.

## Michael — #21 native eyes and real report

Review #36's native-eye integration and preserved local-only analysis. The adapter
already exists in #32; the remaining gate is physical validation using Alexander's
usable combined build. Native desktop tests are not actual hardware evidence.

1. Verify consent, asynchronous OS permission denied/granted, system tracking
   disabled and operator-confirmed calibration. Unsupported/unavailable tracking
   must be explicit; replay must require an explicit choice and keep its label.
2. Use calibrated known targets to inspect direction signs, head-local alignment,
   validity and acquisition timing. Record aggregate error observations without
   clinical claims.
3. Run the separate 60-second diagnostic: unique sample dt distribution, repeats,
   timestamp rejects, invalid fraction, head alignment and gaps/longest stall.
   The diagnostic is not an eligible 130-second assessment baseline.
4. Run the full 130-second baseline and 60-second task with the same probe layout,
   rendering/cadence conditions and one lock across the comparison. Require at
   least six usable 20-second baseline windows; insufficient quality must remain
   unavailable rather than inventing metrics. Finish with a live-labelled local
   report without loading the forest.
5. Test pause, focus loss, recenter, permission loss, source changes, Exit and New
   session. Old samples/baselines/reports must clear; capture must not restart from
   late work. Repeat explicit replay as a regression check.

Fix native timing, validity, metrics or capture-lifecycle defects in Michael's
area and link the evidence to #21. Keep real eyes and reports on-device; no backend
upload or Gemini call is needed for acceptance.

## Ismail — remaining integration and approval

Publish the updated #36 branch and exact test results. Own fixes to runtime backend
settings, authentication, transfer controls and report fallback; coordinate
Alexander's physical keyboard/network-failure observations without transferring
issue ownership. Request Alexander's app/UI review and Michael's native review.
No independent approval has been supplied by these automated tests.

Do not merge #36 until the required teammate review and agreed device gates pass;
use a merge commit when explicitly authorized. Avoid independently merging the
overlapping feature PRs without preserving #36's resolutions. Keep #18/#20/#21/#23
open for remaining device criteria. Hosting/two-network HTTPS and new paid Gemini
calls remain deferred. Morthan separately owns #17/#22/#34 forest generation,
licensed assets, desktop/device visuals and profiling; optional forest work does
not block the first core report-only headset run.

Evidence for each device scenario: commit/APK hash, versions, expected/observed
behavior, pass/fail/unrun, safe diagnostics and requested versus measured cadence.
Keep credentials, serials, private reports and raw eye streams out of GitHub.
