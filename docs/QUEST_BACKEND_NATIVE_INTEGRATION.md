# Runtime settings and native-eye integration checkpoint

Current report/cloud policy: [immediate reports and explicit participant-consented Gemini summaries](CONSENTED_GEMINI_REPORT.md) supersede this checkpoint's original unconditional native-local restriction.

This branch starts at `origin/dev` `4aa9bea` and retains the original commits from
the independently owned task branches. It is a reviewable integration candidate;
it does not merge anything into remote `dev` or close a device issue.

| PR | Input revision | Scope |
| --- | --- | --- |
| #30 | `0f12279146fe0e063c626a0703591dfd10a0c2bf` | Ismail's #20 runtime settings and transfer gate |
| #31 | `633df666c38e0a0b49fcad5da56e8e28d60b2ab6` | Ismail's #15 image evidence and local-only decision |
| #32 | `922f4f70781a699e647ebe594fa35902a446de77` | Michael's #21 native-eye adapter and preparation |
| #33 | `40a266e93b5cc2573d0a3d9fc0cb9240b052d695` | Ismail's #23 bounded smoke verification |
| #35 | `bfd16cc49210345b0588ec1ec33c4778b6562916` | Alexander's #18 trigger, placement, readability and capture-placement race fixes |

#30/#31/#33 combine without conflicts and their combined backend suite passes
152 tests. #32 overlaps #30 in four files. The resolution reads both versions:

- Consent keeps the masked backend settings overlay, explicit upload permission,
  connection status and truthful privacy wording, plus native readiness refresh
  and permission/calibration status. Neither feature silently replaces the other.
- Session Exit clears credentials/upload permission and native diagnostic/setup
  state. Native analysis retains #32's unconditional local client, even when
  backend settings permit invented-session upload. Native diagnostic start also
  cancels a pending invented connection check, like ordinary capture start.
- The EditMode assembly references both App.Reports and VRFatigue.Signals.
- Both sets of Quest presentation tests remain, including tracked backend input
  and native source/permission controls. No generated scene/prefab/GUID changes
  were needed to resolve the merge.

PR #35 arrived during verification. Its rig initialization overlaps native setup
in one location. The resolution configures the world-space UI/input module first,
then native setup controls, then both controller rays. It preserves #35's module
reuse, trigger binding, placement/readability changes and Mobile URP settings.
Those render-scale/MSAA choices still need actual Quest frame-time validation.
The follow-up integration merge `2ae7333` also retains Alexander's `bfd16cc`:
pending placement is cancelled when baseline/task starts, and the current flow
state is checked again before a settled placement. Pressing Start during the
0.5-second settle delay therefore cannot move the panel inside a measured phase.
The original UI -> native setup -> rays initialization order is preserved.
The forest PR #34 (`f827aee`) merges cleanly in a separate Git simulation but is
not included here: its owner reports Unity verification unrun. A clean merge
alone does not establish licensed-asset import, scene correctness or device cost.

An added PlayMode regression holds an authenticated invented HTTP check pending,
starts native diagnostics using a clearly synthetic mock source, releases the
old response and verifies no stale status revives. Exit must clear both features.
The existing native report test now enables invented upload permissions before
starting and still requires a local report and credential clearing on Exit.

## Verification

Previous integration checkpoint (`179375a`, before `bfd16cc`): Unity 6000.6.7f1/full pinned packages: **389 EditMode and 45 PlayMode tests passed,
zero skips**. The combined tracked-pointer test verifies native and backend
controls stay on canvas and the backend overlay opens/closes correctly. Desktop mocks do
not establish real eye samples, device permission/calibration or headset comfort.

Backend verification uses an authenticated loopback-only receiver on port 8138,
invented credentials/data, local runtime and Gemini disabled. The combined Python
suite passed 152 tests. No new model generation, public deployment, billing change
or participant-data upload occurred. The user chose to keep hosting local.

The full Unity runs used the pinned Editor binary with `-batchmode -projectPath
<checkout>/unity -runTests`, `-testPlatform EditMode` or `PlayMode`, and separate
XML/log outputs outside the repository. PlayMode used `VR_RUN_BACKEND_TESTS=1`,
`VR_UNITY_BACKEND_TEST_URL=http://127.0.0.1:8138` and
`VR_SMOKE_BACKEND_URL=http://127.0.0.1:8138`, with the invented receiver token in
the process environment. Both test URL variables are required by the existing
test helpers. An initial run used only the latter, causing two auth assertions
against the default receiver; that setup was corrected before the final run.
The existing unrelated default backend was not reconfigured or stopped.

Native capture/metrics ownership remains Michael's #21; runtime connection and AI
ownership remain Ismail's #20/#15/#23. Alexander reviews the shared app/UI seams
and owns #18's actual device setup. A review request is not reassignment or a
second implementation owner. Original feature branches remain available.

## Review and next device run

Review the conflict resolution with the app integrator and native-eye owner.
Merge only after teammate approval and explicit merge authorization, preserving
merge commits. Because this candidate contains all five original heads, a merge
can retain their history without reimplementing or squashing them. If merging
individual PRs instead, apply the same resolution before the second overlapping
Unity PR; do not accept one complete side of a conflict.

Then rebuild the Android APK with the pinned SDK/modules, install on the Quest
Pro, and record the actual APK/commit and runtime versions. First verify native
permission, calibration, the separate 60-second diagnostic, an eligible complete
baseline/task and report-only completion on-device with upload declined. Test
runtime URL/token entry and invented connection checks separately. Public HTTPS
and two-network tests stay deferred under the user's local-only decision.

Current follow-up results and the ordered owner handoff are recorded in
[PR36_DEVICE_HANDOFF](PR36_DEVICE_HANDOFF.md). Build and test the combined PR
candidate before merging it into dev; device issues remain open until observed.

Unrun: final Android APK rebuild/install, physical Quest UI/keyboard/controller
input, real native-eye timing/quality, measured device cadence/frame times, and
two-network HTTPS. Keep #18/#20/#21/#23 device acceptance open.
