# Unity architecture and seams

Pure C# (no `UnityEngine`) wherever possible, so the logic is unit-tested in EditMode and shared rules stay deterministic. Each teammate plugs in behind an interface and edits only their own folder.

## Assemblies

```text
unity/Assets/App/
  Core/        App.Core        no UnityEngine. Contracts (DTOs + validation), seams, flow state machine, local rules
  Metrics/     App.Metrics     no UnityEngine. IMetricsEngine implementation(s) + MetricsEngineFactory
  Capture/     App.Capture     eye providers: Synthetic, Replay (CSV), Hardware placeholder, EyeProviderFactory
  Reports/     App.Reports     analysis clients: Mock (fixture), Http (UnityWebRequest)
  Relaxation/  App.Relaxation  config handoff, scene loader, logging stub; Forest/ for the forest implementation
  UI/          App.UI          SessionController, panels; UI/Editor = scene/prefab generator
  Scenes/      Assessment.unity (generated), ForestDemo.unity (forest owner)
  Tests/EditMode/ App.Tests.EditMode
```

The implemented signal module is separate: `Assets/Signals/` (`VRFatigue.Signals`) and
`Assets/QuestPro/` (`VRFatigue.QuestPro`). It provides JSON replay, real metric calculations
and the native eye adapter. `MetricsEngineFactory` returns `SignalsMetricsEngine`, which bridges the app seam to this module;
`ReplayEyeProvider` loads its eye-session.v1 JSON. The Quest Pro provider is not wired into
`EyeProviderFactory` yet. See [signal/Quest handoff](QUEST_PRO_INTEGRATION.md).

## Data flow

```text
 IEyeTrackingProvider ──GazeSample[]──▶ PhaseRecording (baseline, task)
   Synthetic | Replay | Hardware(later)        │
                                               ▼
 SelfReport(baseline) ───────────────▶ IMetricsEngine.Compute ──▶ SessionSummary (v1)
                                               │   data_source, quality, baseline, metrics, deviations,
                                               │   strain_indicator (experimental), self_report
 SelfReport(post_task) + Preferences? ─────────┤
                                               ▼
                         AnalyzeRequest.Create  (reconciliation + safety_notice computed locally,
                                               │  free text removed)
                                               ▼
                         IAnalysisClient.AnalyzeAsync
                           Http ──POST /api/v1/analyze──▶ backend/Gemini
                             │  any failure, invalid JSON or AnalysisResponseGuard violation
                             └──────────────▶ LocalFallbackAnalysis (CONCEPT 7.3)
                           Mock | Local                   │
                                               ▼
                         AnalysisResponse (ai_source live | cached | fallback)
                                               │
                         SceneConfigSanitizer.Sanitize (clamp + Preferences win)
                                               ▼
                         SceneConfigHandoff ──▶ ISceneApplier.Apply (ForestDemo or logging stub)
```

`SessionFlow` (Core/Flow) owns the order Consent -> Baseline -> LoadTask -> CheckIn -> Summary -> Scene -> ReRating -> Done, plus pause (blocks transitions) and exit (from anywhere, clears data). It rejects summaries/analyses whose session id or data provenance differ from the provider chosen at consent, so nothing can be relabelled as live later.

## Seams and owners

| Seam | Interface | Owner / branch | Plug in by editing only |
| --- | --- | --- | --- |
| Eye data | `App.Core.Capture.IEyeTrackingProvider`, `GazeSample`, `ProbeEvent` | replay: `feat/replay-metrics`; headset: `feat/quest-eye` | `Capture/` (new provider class; register in `EyeProviderFactory`) |
| Metrics | `App.Core.Metrics.IMetricsEngine` (+ `BaselinePolicy`) | `feat/replay-metrics` | `Metrics/` (return your engine from `MetricsEngineFactory.Create`) |
| AI explanation | HTTP contract `contracts/v1/analyze_request` -> `analysis_response` | `feat/gemini-backend` | `backend/`; Unity side needs no change |
| Scene | `App.Core.Scene.ISceneApplier` (implemented by `ForestSceneApplier` over the forest `SceneConfigApplier`) | `feat/forest-scene` | `Relaxation/`, `Squirrel/`, `Scenes/ForestDemo.unity` (placeholder until replaced) |
| Flow, UI, integration | `SessionController`, panels, `Assessment.unity` | integrator (`feat/app-flow`) | `UI/`, `Core/Flow/` |

Changes to `Core/Contracts` or `contracts/v1` go through a contract PR that updates the schema, fixtures, C# DTOs and Pydantic models together.

### Metrics engine contract

- Deterministic; same inputs, same summary. No network, no AI.
- Drop repeated sensor timestamps, treat gaps > 100 ms as invalid, use measured `dt`, never filter on confidence (CONCEPT 5.1).
- Unavailable values are `null` with an entry in `quality.unavailable`, never 0.
- Apply `BaselinePolicy.Evaluate` for `baseline.low_confidence`; emit `strain_indicator.level = unavailable` and `value = null` for insufficient data, fewer than 6 baseline windows, or no probes.
- `StubMetricsEngine` already does the quality bookkeeping and returns the fixture's metric values; its `metric_version` is `stub-0.1`.

### Forest scene contract

- Put a `MonoBehaviour` implementing `ISceneApplier` in `ForestDemo`. `RelaxationSceneLoader` loads the scene additively (it must be in Build Settings), finds the first `ISceneApplier`, and calls `Apply` with an already sanitised config.
- For standalone testing read `SceneConfigHandoff.Current` in `Start` (it defaults to the offline preset).
- Only prebuilt assets; fades instead of steps; no camera motion; `sound = silent` means all layers off.

### Shared rules

- `duration_ms` is the quality-covered duration; rounded valid + gap milliseconds sum exactly to it.
- Null preferences mean no explicit overrides; request free-text fields must be null (safety and reconciliation are computed locally first).
- All three local scene mappings (Unity `LocalFallbackAnalysis`, backend `local_scene`, forest `SceneConfigMapper`) are tested against `fixtures/integration/scene_rules.json`: preset, chip rows in CONCEPT 7.3 order (a later row wins), then explicit preferences; `want_quiet` caps volume at 0.2, explicit silence zeros every layer, `stop_session` constraints run last (Unity and backend).

### Backend contract

- Request: `analyze_request.schema.json`. Response: `analysis_response.schema.json`.
- The app rejects a response (and uses its local fallback) if it changes `session_id`, `data_source`, `replay_origin`, `quality.status` or `reconciliation`, lowers `safety_notice`, says `live` without a `model_id`, or fails schema validation. Timeout: 15 s.

## Probe task (CONCEPT 5.1)

`App.Core.Capture.ProbeSchedule` defines the layout (`probe_cycle_10_v1`: ten head-local positions, a jump every 2 s, 10-30 degrees apart, acquisition window 0.5 s). `SessionController` runs it in Baseline and Task, records `ProbeEvent`s (trial id, onset/hold-start/end on the frame clock, head-local target) and shows the target through `ProbeTaskRunner`. A replay that carries its own trials (`IProbeSource`) supplies them instead; a synthetic provider (`IProbeFollower`) moves its gaze to the shown target. `SignalsMetricsEngine` maps probe times onto the sensor clock, uses a stricter policy (2 degrees within 500 ms) and sets `conditions_id` to the layout id for phases with probes (`assessment_canvas_no_probes` otherwise), so a phase with probes is never compared with one without.

## Replay CSV format (stub)

Header (exact), one row per frame, times in seconds, directions are head-frame unit vectors, `valid` flags `1`/`0`, empty `confidence` = unavailable:

```text
frame_time_s,sensor_time_s,valid,left_valid,right_valid,left_x,left_y,left_z,right_x,right_y,right_z,head_px,head_py,head_pz,head_qx,head_qy,head_qz,head_qw,confidence
```

Synthetic replay files may be committed under `fixtures/replay/` after review. Recorded sessions stay in the git-ignored `session-data/` folder.

## Adding the Quest Pro adapter later (`feat/quest-eye`)

1. Install the Meta XR Core SDK version that supports Unity 6000.6 (check its release notes) in a separate PR; record the version in `docs/SETUP.md`.
2. Create `Capture/Quest/App.Capture.Quest.asmdef` referencing `App.Core`, `App.Capture` and the Meta SDK assembly, with a `defineConstraints` entry for the SDK so desktop builds without it still compile.
3. Implement `IEyeTrackingProvider` with one `OVREyeGaze` per eye: `DataSource = Live`, `NativeEyeGaze = true` only when both eye poses are actually tracked, `ConfidenceInformative = false`, openness/pupil unavailable with `not_exposed_by_runtime`. Use sensor timestamps; keep invalid frames as `Valid = false`.
4. Replace the body of `HardwareEyeProvider` (or have `EyeProviderFactory` create the Quest provider) and keep the synthetic fallback when permission or calibration is missing, with the reason in `ProviderNote`.
5. Switch the canvas to world space (the panels are prefabs; only the Canvas in `AssessmentSceneBuilder.BuildScene` changes) and add an XR Origin from the XR Interaction Toolkit Starter Assets.
6. Run the 60 s log from `HARDWARE_QUEST_PRO.md` and paste the numbers into that PR.
