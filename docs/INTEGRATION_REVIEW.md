# Integration review (2026-10-24, `chore/integration`)

Branches found: `feat/signals-metrics-quest-pro` (no PR), `feat/gemini-backend` (no PR; also merged into stale `dev`), `feat/forest-scene` (PR #6). No `feat/replay-metrics` or `feat/quest-eye`: the Quest Pro bridge lives in the signals branch. PR #5 (app flow) was merged without a teammate review and is re-reviewed here. Merge order: signals -> backend -> forest, on top of `origin/main`.

Verification is listed at the end. Nothing here ran on a headset.

## PR #5 app flow (integrator) - re-review

Works: strict v1 DTOs, flow state machine, provenance labels, local fallback, generated Assessment scene.

Defects found and fixed on this branch:
- `StubMetricsEngine` reported `duration_ms` = baseline + task while `quality` covered the task only; the backend rejects `valid + gap != duration` with 422. Fixed (stub is now only a fallback anyway).
- A backend answer with `ai_source = fallback` was labelled "LOCAL FALLBACK"; now "FALLBACK from backend rules (no AI used)" so the source is honest.
- `safety_notice = stop_session` still allowed entering the forest. The Summary now blocks the scene button.
- `analyze_request` was added without backend review and allowed `preferences = null`; the backend required it (fixed in the backend, see below).
- Template `SampleScene` was in Build Settings (removed).

Open (owner: integrator):
- **No probe targets in the task.** The signals indicator needs probes, so in the app the experimental indicator is always `unavailable` and probe metrics are `null` (labelled `probe: not_collected`). Needs a probe task in `LoadTaskPanel` (record `ProbeEvent`s on the frame clock; the bridge converts them).
- Desktop durations (20 s) give `too_short` baselines by design. For a demo with deviations use 130 s baseline (6 windows of 20 s on the sensor clock).
- Three implementations of CONCEPT 7.3 now exist (Unity `LocalFallbackAnalysis`, backend `local_scene`, forest `SceneConfigMapper`) and differ (`want_quiet`: volume 0.2 vs silent; backend turns motion off for `tired_eyes`; backend orb off by default). Pick one source of truth; the app uses `LocalFallbackAnalysis`, the forest mapper is unused.

## `feat/signals-metrics-quest-pro` (signals/metrics + Quest Pro bridge)

Contains: pure C# `VRFatigue.Signals` (eye-session.v1 JSON, quality, gaze/head/probe metrics, 20 s baseline windows, robust z, experimental indicator with EWMA/hysteresis), `VRFatigue.QuestPro` (`MetaQuestEyeSource` on `OVRPlugin.GetEyeGazesState`), Meta XR Core SDK 207.0.1, 136 EditMode tests, 8 local schemas, generated synthetic fixtures, docs.

Works (checked in code and tests): repeated and out-of-order sensor timestamps are dropped; gaps > 100 ms split segments even when `valid` is true (stalls handled); measured `dt` everywhere; confidence never gates data; unavailable metrics are `null` with a reason; insufficient data and missing probes force `unavailable`; deterministic; head-local directions; SDK code guarded by `VR_FATIGUE_META_207`. Compiles against the real SDK in 6000.6.7f1 (0 errors).

Defects / mismatches:
- Separate type system with the same names as the app seams (`IEyeTrackingProvider`, `ReplayEyeProvider`, `HardwareEyeProvider`, `ProbeEvent`, `BaselinePolicy`). Not connected to `IMetricsEngine` / `EyeProviderFactory`. **Fixed here** with `App.Metrics.SignalsMetricsEngine` (bridge, now returned by `MetricsEngineFactory`) and JSON support in `App.Capture.ReplayEyeProvider`.
- Edits shared docs (CONCEPT constants: dispersion weight 0 instead of 0.5, 120 s minimum baseline; PROJECT_PLAN; HARDWARE) inside a feature branch. Reasonable, but needs explicit team approval.
- Replay fixtures are 1-2 s unit cases; there is no full-length synthetic demo session. Owner: add one `fixtures/replay/demo_session.json` (>= 130 s baseline + task, with probes).
- Indicator needs a `conditions_id` for both phases; the bridge uses one constant (`assessment_canvas_v1`). Real probe/scene layout must match for this to be honest.
- On device, any eye frame whose head pose is more than 1 ms from the gaze timestamp is marked invalid. Verify on the Quest; if all frames fail, every session becomes `insufficient_data`.

Risks: Meta SDK resolve takes about 6 minutes and adds `com.unity.xr.hands`; the SDK creates `Assets/Resources/DevAgentSettings.asset` with a machine-local token and LAN IP (now git-ignored, never commit it).

Owner actions: (1) full-length synthetic replay session with probes; (2) adapter from `QuestProCaptureController` to the app `IEyeTrackingProvider` (replace `App.Capture.HardwareEyeProvider` placeholder) with consent/calibration calls; (3) first-hour device log from `HARDWARE_QUEST_PRO.md`.

## `feat/gemini-backend` (FastAPI + Gemini)

Contains: FastAPI app, strict Pydantic models (`extra=forbid`, strict, no NaN), deterministic fallback, optional Gemini adapter, body-size limit, optional bearer token, 80 tests, `uv.lock`.

Works: offline by default (`VR_GEMINI_ENABLED=false`); the key is read only from the backend env (`.env.example` is empty); Gemini is called only for synthetic input; budget and per-day attempt caps, token caps (2000 in / 1000 out), timeout <= 15 s, single attempt; empty/blocked/invalid/429/5xx/timeout all return the local response; the model can only select approved observation codes and a re-constrained `SceneConfig` (no free prose, no executable output); 422s do not echo input; prompt omits ids and free text.

Defects / mismatches:
- `preferences` was required; the shared contract allows `null`, so every "let the app choose" request from Unity got 422 (and silently fell back). **Fixed here** (+2 tests).
- Unity could not send the optional bearer token. Fixed in `dev`: `HttpAnalysisClient` reads `VR_BACKEND_ACCESS_TOKEN` at runtime (never a scene field or command-line argument).
- The backend accepted non-null `free_text`. Fixed in `dev`: non-null `self_report.free_text` / `preferences.text` get 422 without echoing the text.
- Stricter semantic rules than the schemas (indicator rules, `valid + gap = duration`). Fine, but document them in `contracts/README.md` so producers know.
- Branch is based on the old `feat/contracts-v1` commit and merged into `dev`; base future work on `main`.

## `feat/forest-scene` (PR #6)

Contains: `SceneConfigApplier` (lighting/fog/audio/particles/orb/squirrel with gradual fades, mute), `SceneConfigMapper`, own `SceneConfig`/`Preferences` DTOs (JsonUtility), generated placeholder squirrel OBJ + `SquirrelIdle`, `docs/FOREST_SCENE.md`.

Works: never touches the camera, clamps and rejects non-allowlisted enums, slow transitions, "no animals" and "silent" win.

Defects / mismatches:
- **No `ForestDemo` scene, no `.meta` files, never compiled** (stated in the doc). Here: Unity generated the metas, it compiles, and an *integration placeholder* `ForestDemo.unity` (primitive trees, squirrel, orb, particles, empty audio sources) is generated by `App > Create ForestDemo Placeholder` and added to Build Settings after Assessment. It never overwrites an existing scene; the owner replaces it.
- `SquirrelIdle` had no asmdef (fell into Assembly-CSharp, unreachable from asmdefs). Added `App.Squirrel.asmdef`.
- Second `SceneConfig` DTO duplicates the contract (float, ignores unknown fields). The app now goes through `App.Relaxation.ForestSceneApplier` (the `ISceneApplier` seam), which copies an already sanitised v1 config into it; keep `TryParse` for standalone use only.
- `RenderSettings` (fog, ambient) are written to the active scene (Assessment) and persist after the forest unloads. Reset them on exit.
- No audio clips, no Quaternius/HDRI import yet, MTL materials are not URP (placeholder overrides them). PR #6 description is an empty template; add verification.

Owner actions: real ForestDemo (curated Quaternius subset, URP materials, CC0 audio), keep `SceneConfigApplier` + `ForestSceneApplier` on one object, reset `RenderSettings` on disable, headset frame timing.

## Unity hygiene

`.meta` files: all generated by Unity, none hand-written. Scene ownership: Assessment = integrator (generated), ForestDemo = forest owner (placeholder until replaced). Build Settings: Assessment (0), ForestDemo (1). No duplicate folders. Editor code only in `App.UI.Editor` (Editor platform). New asmdef references: `App.Metrics`/`App.Capture` -> `VRFatigue.Signals`, `App.UI.Editor` -> `App.Relaxation`, `App.Squirrel`.

## Verification (this branch)

See the PR description for the exact commands and counts. Not run: headset, live Gemini, Android build, LAN/headset networking.
