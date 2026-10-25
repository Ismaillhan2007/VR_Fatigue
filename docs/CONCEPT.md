# Concept: eye tracking and wellness reports with optional relaxation

This document explains *what the product is and why each part exists*. Architecture, caps and acceptance criteria stay in [PROJECT_PLAN.md](PROJECT_PLAN.md); hardware facts in [HARDWARE_QUEST_PRO.md](HARDWARE_QUEST_PRO.md); schemas in [../contracts/](../contracts/).

> **Status:** product design with a partially implemented desktop prototype; headset acceptance remains unverified. The report-only branch below is a requirement, not implemented behavior. See [objectives and the code-backed gap audit](OBJECTIVES.md) and the [README status](../README.md#status).

## 1. Idea in one paragraph

This is an **eye-tracking app** that helps people understand eye-movement changes, self-reported eye strain and mental fatigue. Its primary result is a **health/fatigue report presented as an experimental wellness summary**, combining a personal-baseline comparison, the participant's check-in and explicit limitations. Receiving this report is a complete use of the app. The participant may then choose a personalized calming scene; rendering that scene is one optional use case. A future **background layer** could offer a check-in when eye movement changes persist; the current **agent/check-in** path lets people say how they feel. Neither entry point requires participation in a scene.

Hackathon target: a short eye-movement assessment on a Meta Quest Pro (labelled replay until live capture is verified), a check-in and useful report, plus one parametric forest offered separately. Report-only completion is required but still missing from the current demo flow. The always-on background layer is the *vision*, not a finished product.

| Part | Hackathon MVP | Vision (say it, do not claim it) |
| --- | --- | --- |
| Eye signals | Short baseline, then a short loaded task with gaze probes | Continuous low-overhead monitoring while using apps |
| Reference | Personal baseline from the same session | Multi-day personal baseline, stored locally with consent |
| Agent | Chips, two sliders, optional short text -> structured state | Voice conversation, memory of what helped |
| Report | Core outcome; normal completion without a scene is required | Longer-term comparisons and optional export |
| Optional scene | Participant opts into a parametric forest with bounded settings | More optional biomes, generated skyboxes, learned preferences |
| Evidence | Small honest pilot | Proper study |

## 2. Why eye movement alone is not enough (design consequence)

- Related work on predicting self-rated fatigue from gaze (Eye Feel You, arXiv 2601.21045) reports weak correlations for known subjects and no better than a baseline predictor across subjects. We therefore **never** use a population threshold. Everything is *relative to the person's own baseline* and **paired with their own words**.
- Quest Pro gaze has person- and condition-dependent accuracy (median about 1 degree, much worse tails; Aziz et al., arXiv 2403.07210). Probes use large targets and tolerant thresholds.
- Pupil size and eyelid openness are **not available** on Quest Pro through the standard SDK, so blink rate and pupil metrics are out. Do not synthesise them.

Consequence: the eye signal is a *weak, transparent hint*. The report distinguishes measured changes from what the person says. Their choice determines whether a scene is used at all and, if chosen, their preferences determine its settings.

## 3. Session flow

The intended report-only path ends at step 4; steps 5-6 apply only to people who choose relaxation. An opt-in scene demo was planned at about 8-9 minutes; report-only sessions omit that time. See [SETUP](SETUP.md) for current replay timing and [OBJECTIVES](OBJECTIVES.md) for missing branching behavior.

| # | Step | Demo / pilot | What happens | Data |
| --- | --- | --- | --- | --- |
| 0 | Consent and capability check | 30 s | Explain what is recorded and that it stays local; check eye tracking permission, calibration, tracking status. Unsupported -> labelled replay. | `capabilities` |
| 1 | Baseline | 120 s minimum / 2 min minimum | Standardised gaze task (see 5.1) in a calm neutral scene. Extend if needed to collect six complete usable windows. Then sliders `eye_strain`, `mental_fatigue` (1-7). | metrics windows + `SelfReport(baseline)` |
| 2 | Load task | 2 min / 5+ min | Work-like visual task (dense reading plus visual search) with the same probes embedded. | metrics windows |
| 3 | Check-in | 30 s | Sliders again, chips, optional text. This is the agent entry. A "tired" background trigger would open the same screen. | `SelfReport(post_task)` |
| 4 | Report and completion choice | 20 s | Per-metric deviation from baseline, experimental indicator, reconciliation, limitations and provenance. Finish successfully with the report, or explicitly choose relaxation. | `SessionSummary`, `AnalysisResponse` |
| 5 | Optional scene | 2-3 min if chosen | Ask for preferences/preview after opt-in, then enter the calming forest. Participant can adjust or exit at any time. | `Preferences`, `SceneConfig` |
| 6 | Optional scene feedback | 20 s if offered and accepted | Same sliders again only for scene participants. Missing feedback is not a fabricated before/after comparison. | `SelfReport(post_scene)` if collected |

## 4. Baseline policy (the weak point, handled explicitly)

A baseline taken while already tired is wrong. Mitigations, in order of importance:

1. **Anchor with words.** Ask the sliders at baseline. If `eye_strain >= 5` or `mental_fatigue >= 5`, mark `baseline.low_confidence = true` (`self_reported_tired`). The UI says "compared with how you started today, not with your rested self"; the app still works, claims shrink.
2. **Same conditions.** Baseline and loaded task use the same lighting, scene and probe layout, so differences are not scene artefacts.
3. **Enough windows.** At least 6 complete usable 20 s windows (120 s nominal minimum); exclude partial/poor-quality windows and extend acquisition when needed. Otherwise mark `low_confidence` (`too_short` or `insufficient_usable_windows`) and show no indicator. Actual valid time is reported separately. [BASELINE.md](BASELINE.md) defines the implemented window/reference policy.
4. **Within-session drift.** The indicator is a *change since the start of this session*. That is honest and available even with a poor baseline.
5. **Later (not MVP).** Multi-day local baseline with the participant's opt-in.

## 5. Signals and the experimental indicator

### 5.1 Standardised task and probes

Free viewing varies too much between people and content. The assessment therefore includes **probes with known targets**: a small target jumps between positions at least 6 degrees apart (accuracy-tolerant), the person looks at it, and a hold interval follows.

| Metric (`SessionSummary.metrics`) | Definition | Hypothesised direction under strain (to be tested, not assumed) |
| --- | --- | --- |
| `probe_latency_ms` | Target onset -> gaze within 3 degrees of target | up |
| `probe_rms_error_deg` | RMS angular error to target during hold | up |
| `probe_miss_fraction` | Targets not acquired within 1.0 s | up |
| `gaze_speed_median_deg_s`, `gaze_speed_p95_deg_s` | Angular speed of the gaze direction vector (Savitzky-Golay window 7, order 2, using real timestamps) | p95 may go down |
| `gaze_dispersion_deg` | Spread of gaze directions in a window | unclear; reported, low weight |
| `head_speed_deg_s` | Head angular speed (context only) | context |

Data hygiene before any metric: drop rows with a repeated sensor timestamp; mark gaps longer than 100 ms as invalid segments; use measured `dt`, never an assumed rate; do not filter on `confidence` (it is constant on Quest Pro); compensate head motion by working in a head-relative direction or by reporting `head_speed` next to the metric.

### 5.2 Indicator maths (deterministic, versioned)

For each metric `f` and 20 s window `w`:

- Baseline reference: median `m_f` and `s_f = max(1.4826 * MAD_f, floor_f)` over baseline windows (floor prevents divide-by-near-zero).
- Deviation: `z_f = clamp((x_f - m_f) / s_f, -4, 4)`. Stored in `SessionSummary.deviations`.
- Signed combination with fixed, a-priori weights and directions: `d = sum(w_f * sign_f * z_f) / sum(w_f)`; implemented v1 weights: latency 3 (+), rms error 2 (+), miss fraction 2 (+), speed p95 1 (-), others 0. Dispersion's earlier proposed weight 0.5 is disabled because its direction is unspecified; it remains an observation. All four active deviations are required; do not renormalize around missing measurements.
- Indicator `v = clamp(d / 3, 0, 1)`, smoothed with EWMA (alpha 0.3) across contiguous windows, seeded from the first valid value. Level: `low < 0.35`, `moderate < 0.6`, `high` otherwise. Nudge only after 3 consecutive smoothed windows strictly above `0.6`, release strictly below `0.4` (hysteresis). Missing/ineligible windows or a time gap reset smoothing and confirmation; never carry a stale value across loss.
- Not computed (`level = unavailable`, `value = null`) when `quality.status = insufficient_data`, when fewer than 6 baseline windows exist, or when probes were not run.

All constants above are **initial guesses**, kept in one versioned config (`method_version`). They are not validated thresholds. The UI always shows the per-metric deviations next to the indicator and the label "experimental". [INDICATOR.md](INDICATOR.md) defines the implemented policy, missing-data gates, state rules and reproduction commands.

### 5.3 Wording rules

Allowed: "deviation from your baseline", "eye-movement changes compared with how you started", "experimental", "you reported".
Not allowed: "fatigue score", "you are fatigued", "detected", "diagnosis", accuracy or percentage claims, "clinically", "improves health".

## 6. The agent

### 6.1 Inputs (all bounded)

`SelfReport`: two 1-7 sliders, up to 6 chips (`tired_eyes`, `tired_mind`, `tense`, `restless`, `overstimulated`, `low_energy`, `want_quiet`, `want_nature`, `want_dim`, `want_movement`), optional text up to 300 characters. Voice is **dictation into the text box** (system keyboard) in the MVP; no audio is streamed to Gemini.

### 6.2 Reconciling words and signals (computed by the app, not by Gemini)

"Words high" means `max(eye_strain, mental_fatigue) >= 5` or a rise of at least 2 over the baseline rating.

| Words | Signals | `reconciliation` | Behaviour |
| --- | --- | --- | --- |
| high | high / moderate | `agree_high` | Show both in the report; offer a break without requiring scene participation. |
| high | low | `words_only` | The person's report counts; say the eye signals did not show a strong change. Offer optional relaxation. |
| low | high | `signals_only` | Gentle offer ("your eye movements changed from your baseline - want a short break?"). Never overrides what they said. |
| low | low | `agree_low` | No nudge; scene only if they ask. |
| any | unavailable | `unavailable` | Words only; show "no usable eye data". |

Every row retains report-only completion. An offer is never scene consent; `stop_session` still blocks scene entry.

### 6.3 What Gemini does

Gemini is an optional explanation adapter; local rules must produce a useful report without it. The current v1 analysis response bundles (a) a short explanation using only approved observation codes, (b) limitations, and (c) a `SceneConfig` plus a one-line rationale. Bundling a config does not mean the participant chose a scene. The intended UI keeps the report independent and only applies scene settings after opt-in; changing the wire shape requires a contract PR. Gemini does not compute measurements, change `reconciliation`, lower a `safety_notice`, or add unmeasured claims.

### 6.4 Safety and privacy

- Discomfort chips or words (dizziness, nausea, pain) are matched by simple local rules and raise `safety_notice` (`take_break` / `stop_session`): "take the headset off and rest". This is not a medical judgement.
- This is a wellness demo. No diagnosis, no treatment advice.
- **Decision D1 (current policy):** free text stays on-device and interactive check-ins use `cloud_policy: local_only`, even with synthetic eye samples. Gemini may receive only wholly invented test requests explicitly marked `synthetic_demo`; see [contracts](../contracts/README.md). Scene opt-in does not authorize cloud processing. Any later participant cloud path needs a separate data-handling decision and consent.

## 7. Scene engine

### 7.1 Principle

The scene engine serves an **optional relaxation use case** of the eye-tracking app. It must not be required to obtain a report or finish a session. If the participant chooses it, scenes are **assembled from a fixed parametric forest**, not generated freely. A model (or the local rules) only chooses values inside bounds. This keeps the demo reliable, comfortable and testable, and satisfies the rule that no model-generated code, URLs or arbitrary assets are executed.

### 7.2 Parameters (`SceneConfig` v1)

| Field | Range / values | Effect |
| --- | --- | --- |
| `time_of_day` | `day`, `sunset` | Lighting preset |
| `light_warmth` | 0..1 | Colour temperature of light and fog tint |
| `brightness` | 0.4..1.0 | Global exposure (never fully dark) |
| `fog_density` | 0..0.6 | Soft depth, lower contrast |
| `sound`, `master_volume` | `forest`/`silent`, 0..1 | Mute override |
| `sound_layers` | wind, birds, water, pad: 0..1 each | Ambient mix |
| `motion`, `motion_speed` | `none`/`gentle`, 0..0.5 | Ambient motion (leaves, grass, animals) |
| `animals_enabled` | bool | Optional squirrels |
| `particles` | subset of `fireflies`, `pollen` | Slow decorative particles |
| `orb` | `enabled`, `breath_seconds` 4..8 | Optional slow breathing / smooth-pursuit orb |

Comfort constraints are in the engine, not the schema alone: no flashing, no luminance steps faster than a slow fade, no camera movement, no locomotion, always-available pause, mute and exit.

### 7.3 Local fallback (no AI, no network) - must always work

Start from the saved preset `fixtures/scene_config_calm_forest_sunset.json`, then apply modifiers for chips and sliders, then clamp:

| Chip / condition | Modifier |
| --- | --- |
| `tired_eyes` | `brightness` -0.15, `light_warmth` +0.15, `fog_density` +0.1, `motion_speed` 0 |
| `overstimulated` | `particles` empty, `animals_enabled` false, layers: `pad` only |
| `tense` or `restless` | `orb.enabled` true, `breath_seconds` 6 |
| `low_energy` | `time_of_day` day, `birds` +0.2, `motion` gentle |
| `want_quiet` | `sound` silent, or `master_volume` 0.2 |
| `want_dim` | `brightness` 0.5 |
| `want_nature` | `birds` +0.2, `water` +0.2 |
| `want_movement` | `motion` gentle, `motion_speed` 0.3 |

Explicit participant choices (Preferences) always win over any AI or rule output; quiet and animal-free choices override everything.

### 7.4 AI-assisted additions (all optional, never required)

- Gemini proposes the `SceneConfig` within the same bounds; the app validates, clamps and shows an editable preview.
- A generated skybox image is a stretch item: pre-generated and cached, applied only if present, costs approved by a human first, no runtime dependency. Not part of the MVP acceptance.
- A short generation delay is hidden by the breathing orb screen; if the response is late, use the local result.

## 8. Evaluation (small pilot, honestly labelled)

First verify that participants can understand the report and finish without a scene. A separate proposed relaxation pilot involves 6-10 volunteers who explicitly choose to participate, within-subject: baseline ratings -> loaded task -> ratings/report -> 3 minutes in the scene -> ratings. Declining the scene does not block the report or count as failed app use; exclude report-only sessions from scene before/after calculations. If time allows, counterbalance with a 3-minute neutral empty room as a control.

Report only: n, mean change in each slider with a plain range, the correlation between the indicator and the slider change (Pearson r) with the caveat that n is tiny, and MAE of the indicator-implied rating against a mean-rating baseline, in the spirit of Eye Feel You. State that results are not generalisable, not clinical, and the scene was not shown to improve health. A negative result is a valid, reportable finding.

## 9. Demo script (3 minutes)

1. (20 s) Problem and scope: an eye-tracking app with a health/fatigue report, presented as an experimental wellness summary. The forest is an optional use case.
2. (40 s) Show baseline and loaded task on the Quest Pro (or labelled replay) and the per-metric deviations.
3. (40 s) Agent check-in: sliders, chips, one sentence; show the reconciliation line.
4. (40 s) Show the report as the primary result. Explain that normal report-only completion is the objective and still missing in this build; choose the forest deliberately to demonstrate the optional use case and mute/exit controls.
5. (40 s) Show scene feedback only if collected; label any pilot proposal as unrun. Describe limits and the vision (background layer, multi-day baseline).

Say out loud when anything is replay, synthetic, cached or fallback.

## 10. Risks

| Risk | Mitigation |
| --- | --- |
| Eye data unavailable or unstable on the day | Labelled replay path; hardware adapter is a separate task |
| Indicator looks random with n small | Show per-metric deviations and words; do not claim accuracy |
| Baseline taken while tired | Section 4 |
| Gemini or network down | Local report fallback; saved preset/mapping (7.3) for optional scene users |
| Free-tier data terms vs personal text | Decision D1 |
| Cybersickness or discomfort | Static camera, no locomotion, short sessions, visible exit, `safety_notice` |
| Scene work displaces core eye-tracking/report work | Report-only completion first; one optional forest before any additional biomes |
| Prior lab code or data pulled in | Code must be written during the event and AI use disclosed; restricted lab recordings must never enter this repo |

## 11. Open decisions for the team

- **D1** Any future expansion beyond the current local-only participant policy (section 6.4).
- **D2** Meaning of `fatigue score` ban in PROJECT_PLAN: this concept allows an *experimental baseline-deviation indicator* with the labels above; the words "fatigue score" remain forbidden.
- **D3** Who owns the Quest Pro build and the first-hour eye-data test (see HARDWARE_QUEST_PRO.md).
- **D4** Whether the volunteer pilot is feasible in the time left; if not, show the protocol and ratings from the team only.
- **D5** Whether to ship a second biome or the skybox stretch at all.

## 12. Related work (for the write-up, not as validation)

- Hasan and Komogortsev, "Eye Feel You", arXiv 2601.21045: gaze-velocity models predicting self-rated state; the case for personal baselines.
- Aziz et al., arXiv 2403.07210: Quest Pro eye-tracking accuracy and data loss.
- Feit et al., CHI 2017: accuracy and precision vary strongly between people and conditions.
- Barz et al., ETRA 2018: gaze interfaces that adapt to estimated error.
