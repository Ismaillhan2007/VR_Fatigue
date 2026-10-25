# VR Fatigue: HackTX implementation plan

## Outcome and scope

Deliver an **eye-tracking app** for the Meta Quest Pro: a participant completes a short, comfortable assessment against a personal baseline, says how they feel (two sliders, chips, optional text), and receives a health/fatigue report with measured observations and limitations. The report is the core outcome; report-only completion is a complete success. The participant may separately opt into a personalized forest as one relaxation use case. [OBJECTIVES.md](OBJECTIVES.md) defines this direction and audits current implementation gaps; [CONCEPT.md](CONCEPT.md) explains the baseline policy and indicator; [HARDWARE_QUEST_PRO.md](HARDWARE_QUEST_PRO.md) lists headset capabilities. Optional Gemini explains supplied metrics and can select supported scene settings. It does not calculate eye measurements or control arbitrary tools.

The report is an **experimental wellness summary**, not a medical diagnosis or proof of fatigue detection. Do not promise a scene is the person's most relaxing environment; ask for their preference and feedback. The headset is a Meta Quest Pro used fully virtual (no passthrough). Eye-data access in our own build is **unverified** until the first-hour test in the hardware document passes. Unity is the team's confirmed engine.

**Core must ship:** replay/assessment, deterministic metrics and personal-baseline comparison, check-in, useful report with local fallback, explicit data/AI source and quality labels, and normal completion without a scene or post-scene rating. Add real tracking as soon as hardware support is proven. Add live Gemini without making it necessary for reports or the offline demo.

**Optional use case to demonstrate:** one local forest with optional squirrels, explicit participation choice, preference controls and comfort controls. Delivering this feature does not make scene participation a condition of using the eye-tracking app. The current code shows reports but still couples normal completion to the scene; the report-only branch below is not yet implemented.

**Stretch only:** another biome, multi-session comparisons, optional report export, pre-generated decorative assets. Defer model training, diagnosis, accounts, a database, autonomous multi-agent orchestration, and runtime 3D/video generation.

## Recommended stack and layout

| Layer | Choice | Responsibility |
| --- | --- | --- |
| VR | Unity LTS + C#, URP, OpenXR, XR Interaction Toolkit | Headset input, UI, prebuilt scenes, comfort controls |
| Eye adapter | Meta XR Core SDK `OVRPlugin` on Quest Pro (head-local/timestamp adapter); `ReplayEyeProvider` elsewhere | Capability checks, calibration status, validity, timestamps |
| Metrics | Pure C# functions with Unity EditMode tests | Local aggregation; no cloud eye processing |
| Backend | Python 3.12 (pinned), FastAPI, Pydantic, `google-genai`, pytest | Contract validation, bounded Gemini call, cache, local fallback |
| Transport | UnityWebRequest + versioned JSON over HTTP locally / HTTPS remotely | One summary request after a session, not frame streaming |
| Storage | Memory by default; synthetic JSON fixtures | No database needed for the hackathon |

The team-selected Unity editor is **6000.6.7f1**. Verify the chosen Meta XR SDK supports it and record exact XR package/SDK versions in the setup PR. Meta XR Core SDK **207.0.1** and OpenXR **1.18.0** are pinned; the adapter and device acceptance steps are in [QUEST_PRO_INTEGRATION.md](QUEST_PRO_INTEGRATION.md). Keep `GEMINI_MODEL` configurable; choose an available stable Flash/Flash-Lite model after checking its structured-output support and project quota. Do not depend on a preview model or assume free quota.

Layout (application scaffold, signals module and backend implemented; report-only completion still planned; application seams in [ARCHITECTURE.md](ARCHITECTURE.md)):

```text
unity/                  # Assets/, Packages/, ProjectSettings/
  Assets/App/
    Core/               # pure C#: contract DTOs, seams, session flow, local rules
    Capture/            # application providers and factory; hardware placeholder
    Metrics/            # application metrics seam and stub
    Reports/            # measured data, AI narrative, fallback, source labels
    Relaxation/         # allowlisted config -> local prefabs/audio/lighting
    UI/                 # setup, consent, preferences, summary, exit
    Scenes/             # Assessment, ForestDemo
    Tests/              # EditMode and PlayMode
  Assets/Signals/       # pure C# replay, hardware provider, quality, metrics, baseline, indicator
  Assets/QuestPro/      # Unity/Meta bridge, separate from pure C# signals
backend/app/            # routes, contracts, gemini_service, fallback, budget
backend/tests/
contracts/v1/           # application and local signal/metric/baseline/indicator schemas
fixtures/               # synthetic replay streams, metric cases, plans and temporal test sequences
docs/                   # plan, setup, demo script, asset credits as implemented
```

## Data flow and feature acceptance

### 1. Prove hardware access before building detection

Create `IEyeTrackingProvider` with `HardwareEyeProvider` and `ReplayEyeProvider`. Expose capabilities separately: gaze, per-eye openness, pupil diameter, tracking confidence, and raw imagery. OpenXR's eye-gaze interaction provides a pose; it does not guarantee blink, pupil, or eye-camera access. Do not plan around raw eye images. On Quest Pro, pupil diameter and openness are expected to be unavailable and `gazeConfidence` is uninformative (do not filter on it); see the hardware document.

Check the actual headset, runtime, permissions, calibration, and per-frame validity on-device. Distinguish native eye gaze from head/controller or synthetic gaze. Missing permissions or unsupported tracking offers replay mode with a persistent label; tracking loss pauses valid-data accumulation instead of silently producing a report.

Acceptance: a 60 s on-device log reports the timestamp statistics listed in the hardware document, and a short session yields timestamped valid gaze samples on supported hardware; the same pipeline runs from a synthetic fixture without a headset. Unsupported optional signals are `null` with a reason, never zero or guessed. If hardware access is still blocked after the first hour of investigation, complete replay integration and keep the hardware adapter as a separate task.

### 2. Calculate observations locally

Capture actual sample timestamps; do not assume render rate equals sensor rate or count repeated sensor samples twice. Define a consistent coordinate frame and compensate for head motion before interpreting changes in gaze. Filter invalid/nonfinite samples and preserve tracking gaps. Aggregate over valid elapsed time.

Start with valid sample coverage, session duration, descriptive gaze angular velocity/distribution, and gaze probes with known targets (latency, RMS error, miss fraction; targets at least 6 degrees apart). Drop repeated sensor timestamps before any metric. Add fixation/saccade estimates only with documented algorithms and configurable experimental thresholds. Add blink rate/closure duration only if a validated openness signal exists; tracking loss alone is not a blink. Pupil metrics are optional and lighting-dependent.

Use at least 120 seconds nominal baseline to obtain six complete usable 20-second windows (same scene, lighting and probe layout as the loaded task), extending capture when windows fail quality. Actual valid time is reported separately. Anchor with baseline self-report; a baseline taken while the participant reports being tired is flagged `low_confidence` (see CONCEPT section 4). The implemented window/reference policy and signed deviations are in [BASELINE.md](BASELINE.md). Store the metric algorithm version and parameters. Show differences as observations, not validated fatigue thresholds. Self-report is not a sensor measurement. When quality is insufficient, report `insufficient_data`; never present a validated fatigue score or a model confidence percentage. An *experimental baseline-deviation indicator* (CONCEPT section 5.2: robust z-scores against the personal baseline, fixed a-priori weights, EWMA, hysteresis) may be shown only with the label "experimental", next to the per-metric deviations, never when quality is insufficient, and never called a fatigue score. Its constants are initial guesses in one versioned config.

Acceptance: synthetic fixtures cover steady gaze, known motion, tracking gaps, unsupported signals, and insufficient coverage. The same inputs produce the same observations without Gemini. Neither gaze changes alone nor a short baseline establishes fatigue accuracy.

### 2a. Collect self-report and reconcile it with the signals

Collect `SelfReport` (two 1-7 sliders, up to six chips, optional text of at most 300 characters) at baseline and after the loaded task. Post-scene feedback is optional and applies only to scene participants. The app, not Gemini, computes `reconciliation` (`agree_high`, `agree_low`, `words_only`, `signals_only`, `unavailable`) from the rules in CONCEPT section 6.2: the report distinguishes words from measurements; eye signals can add a gentle offer or caveat but never override the participant's choices. Scene participation requires a separate choice, and their explicit preferences govern it if chosen. Discomfort words or chips raise `safety_notice` through local rules.

Acceptance: the same self-report and metrics always produce the same `reconciliation` and `safety_notice`; the flow works with eye data unavailable (words only).

### 3. Generate a bounded Gemini summary

Implement `POST /api/v1/analyze` and `GET /health`. The request contains versioned aggregate metrics and deviations, their provenance/quality, the deterministic `reconciliation`, bounded self-report, and bounded preferences. Raw streams and images remain on-device. The backend validates input, builds a fixed prompt, requests JSON-schema output, then validates all returned values with Pydantic. Treat preferences and self-report text as untrusted data, not system instructions. Self-report text describes a person's state: follow decision D1 in CONCEPT before sending it to a cloud model.

Return a short explanation of supplied observations and explicit limitations. Current v1 responses also require a scene configuration; this is an implementation coupling, not a requirement for the user to enter a scene. Report-only UI can ignore that config; removing it from responses needs a coordinated contract PR. Keep metric values and `insufficient_data` decisions authoritative in deterministic application code. Gemini may explain supported observations and preference choices; it cannot override measurements, infer diagnoses, or add unmeasured claims. Use an approved fallback if output fails content checks; for the MVP, restrict observation references and suggestions to a small approved vocabulary.

Acceptance: mocked valid output renders; malformed/schema-invalid output, unsupported claims/settings, empty/blocked responses, 429, 5xx, missing key, timeout, and budget exhaustion all yield a usable local report/config. Labels independently show `ai_source: live | cached | fallback` and the data source. No Gemini secret appears in Unity, logs, fixtures, or Git.

### 4. Offer an optional prebuilt relaxation scene

Build `ForestDemo` first with local low-poly trees, baked/static lighting where practical, ambient audio, a stationary starting area, and an optional squirrel prefab using a short fixed animation/path. Save one team-approved demo preset immediately. A script changes allowlisted, bounded settings (`SceneConfig`: time of day, light warmth, brightness, fog density, ambient sound layers and volume, squirrel visibility, ambient motion speed, optional fireflies/pollen, optional slow breathing orb); it does not generate geometry at runtime. A deterministic local mapping from chips and sliders to `SceneConfig` (CONCEPT section 7.3) is the offline path and must work without Gemini.

After explicit scene opt-in, offer simple controls (forest, squirrels on/off, day/sunset, sound/volume, little/no animal motion) plus optional short text. Unsupported requests produce the closest supported option with an explanation and an editable preview. Let the participant confirm or adjust before applying. A quiet/animal-free choice must override any AI recommendation. A null preferences object only means no overrides; it is not a participation choice. Scene failure must leave the core report usable.

Acceptance: the scene opens offline and settings visibly work; malformed/missing config uses the saved preset. No arbitrary asset downloads, script execution, flashing effects, forced movement, or camera animation. Include pause/exit and mute controls; stop when the participant asks or feels discomfort. Measure frame timing on the actual headset and meet its selected refresh-rate frame budget.

### 5. Integrate the judging path

Target core flow: select live/replay source -> consent and capability check -> baseline/assessment -> check-in -> report with AI/local explanation -> Finish with report -> Done. Retain report review until explicit session end/reset.

Optional branch from the report: choose relaxation -> preferences/config preview -> confirm forest entry -> optional self-rated comfort feedback -> Done. No forest load, scene settings or `post_scene` rating is required for report-only completion. Safety can block scene entry without blocking access to the report or exit.

Acceptance: verify both branches on desktop and then the target headset; repeat the core path with network disabled and tracking unavailable. Report-only completion must reach Done without loading `ForestDemo`, requesting scene preferences or submitting a post-scene rating. Preserve privacy on explicit Exit/reset. Add state-machine/UI coverage for these choices and the existing safety block. Show a desktop spectator view for judges. A failed backend request leaves the report usable. Keep a local build, saved synthetic config/report, and a short recording as backups, all honestly labeled. See [OBJECTIVES](OBJECTIVES.md) for current code evidence and gaps.

## Contract to agree before splitting work

Local signal schemas and matching C# models are implemented in [contracts/v1](../contracts/README.md), with synthetic replay fixtures in `fixtures/`. Application schemas and Unity DTOs were added by the application scaffold; connect the signal outputs to these application contracts and align backend Pydantic models. Summary of the required shape:

| Object | Required fields / semantics |
| --- | --- |
| SessionSummary | `schema_version`, random `session_id`, `data_source` (`live`, `replay`, `synthetic`), `replay_origin` (`recorded`, `synthetic`, or null), `duration_ms`, `capabilities`, `quality`, `baseline`, `metrics`, `deviations`, `strain_indicator` (experimental; `unavailable` when data is insufficient), `self_report` (nullable), `metric_version` |
| SelfReport | `phase` (`baseline`, `pre_task`, `post_task`, `post_scene`), `eye_strain` 1-7, `mental_fatigue` 1-7, `chips` (allowlisted, at most 6), `free_text` <= 300 characters or null |
| quality | `status` (`usable`, `insufficient_data`), valid coverage 0..1, valid duration, gap duration, unavailable-signal reasons; policy thresholds documented and versioned |
| metrics | Unit-named values such as `gaze_speed_deg_s`; optional values are nullable. Blink counts/rates require openness capability. No identity or raw eye frames |
| Preferences | `theme` (`forest` in v1), `animals_enabled`, `time_of_day` (`day`, `sunset`), `sound` (`forest`, `silent`), `motion` (`none`, `gentle`), optional text <= 300 characters |
| AnalysisResponse | `schema_version`, `session_id`, unchanged data provenance/quality, deterministic `reconciliation`, approved-vocabulary `observations`, `narrative`, `limitations`, `safety_notice`, `scene_rationale`, `scene_config`, `ai_source`, `model_id` (null when `fallback`), `prompt_version` |
| SceneConfig | `scene_id` (`forest_v1`), `animals_enabled`, `time_of_day`, `light_warmth`, `brightness` (0.4..1), `fog_density` (0..0.6), `sound`, `master_volume`, `sound_layers`, `motion`, `motion_speed` (0..0.5), `particles`, `orb`; strict enums and ranges. No URLs/code/paths |

Reject unknown schema versions, nonfinite numbers, extra executable fields, and incompatible signals. Keep all fields required with explicit nullability where supported by Gemini's schema subset. Unity validates configuration again before applying it. Cache by schema/metric/prompt/model versions plus canonical metrics/preferences; never reuse another session's identifiers or provenance. Current fixtures cover synthetic replay and insufficient signal quality; `python contracts/validate_examples.py` validates them and checks invalid inputs are rejected. Application/fallback examples from the scaffold are also checked by the same entry point. Live and recorded-replay examples would have to come from a consenting teammate's real session; they stay local and are not committed.

## Cost, privacy, and reliability

- Default to mock/offline development. Enable live Gemini explicitly; choose the model and record current prices/quota before calls. Use synthetic, nonpersonal payloads on the unpaid tier: Google's terms say not to submit sensitive or personal information there. Treat self-report (especially free text) as personal until the team decides otherwise (CONCEPT D1); chips and sliders may drive the local mapping without any cloud call. Real eye summaries remain local in the MVP; any later cloud path needs an explicit data-handling decision and participant consent.
- At most one text-only request per completed analysis. V1 bundles report and scene settings, but generating settings never authorizes scene entry. Cache identical nonpersonal inputs. Never send frames continuously, regenerate 3D worlds, or create an open-ended agent loop.
- Initial engineering caps: <= 2,000 input tokens, <= 1,000 total output/thinking tokens per attempt, <= 100 attempts per development day, one concurrent call per session. Configure thinking/output limits on the selected model and verify accounting. Count retries as attempts; return fallback before exceeding caps.
- Allow at most one retry for transient errors with backoff/Retry-After if it fits a 15-second total deadline; otherwise fall back. Do not retry invalid requests, blocked content, or bad configurations indefinitely.
- Paid requests default off. A human sets the dollar limit and verified per-token rates before enabling them. Reserve worst-case cost before each attempt, include thinking tokens when billed, and stop at the cap; record usage without payloads or secrets. Provider budget alerts alone are not a hard spending limit.
- Estimate text cost with `attempts * (input_tokens * input_rate + billed_output_tokens * output_rate) / 1,000,000`, with rates in USD per million tokens. At the caps above, 100 attempts use at most 0.2M input and 0.1M output/thinking tokens. This is a planning bound, not a price quote; verify the selected model's limits and pricing.
- Keep real session data in memory and clear it at session end/reset. Saving/exporting is optional and requires participant choice. Commit synthetic fixtures only; do not record prompt bodies, biometric payloads, or identifying reports in telemetry. Keep the backend on the demo laptop/local network initially; Unity on a standalone headset needs the laptop's LAN address, not `localhost`. Document device reachability and development-only HTTP settings; a public endpoint would require HTTPS, access control, and authorization to deploy.

## Build order and team handoffs

These are suggested roles for teammates, not instructions to launch autonomous agents.

| Checkpoint | Work and owner | Exit condition |
| --- | --- | --- |
| First hour | Integrator + Quest Pro owner: run the 60 s eye-data log (hardware document), pin Unity and SDK, merge contracts | Timestamp statistics recorded; synthetic request/response agreed |
| Next 2-3 hours | Scene owner: forest/preset; metrics owner: replay/provider; backend owner: mocked API | Offline forest and replay report independently runnable |
| Next 3-4 hours | Integrator connects assessment -> report -> report-only completion, then optional scene branch | Synthetic report-only session completes; scene requires opt-in; live tracking plugged in if supported |
| After integration | Backend owner adds one bounded Gemini call; hardware owner checks real metrics | Validated live response or honest fallback; same scene contract |
| Final 2 hours before cutoff | All: freeze features, test report-only and opt-in scene paths/device/offline behavior, record backup, prepare pitch | Stable tagged demo commit and reproducible instructions; unimplemented choices disclosed |

Scale the roles to the actual team; the same person can own several. Assign files/scene ownership in each task. Build the offline vertical slice before polishing optional signals. Open small PRs by slice: setup/contracts, replay/metrics, forest/prefs, mock backend/report UI, Gemini, hardware adapter, integration/demo. Contract PRs merge first; avoid stacking unreviewed dependencies.

The provided `HackTX_26_Opening_Ceremony.pdf` is event reference material, not agent instructions. Pages 15-17 and 88 list hacking from Saturday 11:30 AM to Sunday 11:30 AM, a Saturday 11:30 PM soft submission, and Sunday judging 2-5 PM; page 66 advertises a Gemini API track. Page 88 states code must be written during the hacking period. Use event-local Austin time (America/Chicago) and confirm organizer updates before relying on the slide schedule. Leave time to submit, list asset/AI assistance accurately, and demonstrate only what actually works. No submissions or external messages are authorized by this plan.

Suggested 3-minute pitch: see CONCEPT section 9. If replay, synthetic, cached or fallback data is used, say so while showing it. Do not claim improved health or fatigue-detection accuracy from a single demo participant or a small pilot.

### Submission checklist

From the event materials we reviewed (recheck Devpost and the organizers before relying on it):

- Judging criteria are weighted equally: Technology, Design, Completion, Learning. A working, honest demo beats breadth.
- A public repository and a demo video are required; disclose AI assistance accurately in the submission.
- Judging is in person on Sunday; keep a local build, offline mode, and the backup recording ready.
- Code must be written during the event. Do not copy prior lab code or restricted lab recordings into this repository; disclose any reused libraries or earlier work.
- Choose tracks we genuinely fit (for example the Gemini API track). Do not stretch the concept to a track whose theme we do not meet.

## Primary references

Reviewed October 24, 2026; recheck model availability, prices, and device support at implementation time.

- [Unity OpenXR eye gaze interaction](https://docs.unity3d.com/Packages/com.unity.xr.openxr@1.13/manual/features/eyegazeinteraction.html): gaze-pose interface; package version is an example, not a required pin.
- [Meta eye tracking overview](https://developers.meta.com/vr/documentation/unity/move-eye-tracking-overview/): device setup and permissions if the selected headset uses Meta's SDK.
- [Gemini structured outputs](https://ai.google.dev/gemini-api/docs/structured-output): schema support and application-side validation.
- [Gemini pricing](https://ai.google.dev/gemini-api/docs/pricing) and [rate limits](https://ai.google.dev/gemini-api/docs/rate-limits): check the selected project/model; quotas are not guaranteed.
- [Gemini API terms](https://ai.google.dev/gemini-api/terms): medical-use restrictions and unpaid-service data handling.
- Hasan and Komogortsev, ["Eye Feel You"](https://arxiv.org/abs/2601.21045): gaze-velocity prediction of self-rated state; motivates personal baselines and honest evaluation.
- Aziz et al., [Quest Pro eye-tracking accuracy](https://arxiv.org/abs/2403.07210): basis for tolerant probe targets and data-loss reporting.
