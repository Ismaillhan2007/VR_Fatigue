# Concept: from eye-movement signals and self-report to a personal calming scene

This document explains *what the product is and why each part exists*. Architecture, caps and acceptance criteria stay in [PROJECT_PLAN.md](PROJECT_PLAN.md); hardware facts in [HARDWARE_QUEST_PRO.md](HARDWARE_QUEST_PRO.md); schemas in [../contracts/](../contracts/).

> **Status:** design only. Nothing here has been built or run on a headset. Claims marked *unverified* must be tested before they appear in the pitch.

## 1. Idea in one paragraph

People who wear XR headsets for long periods do not notice gradual visual strain. The product has two entry points to the same pipeline. A **background layer** watches eye movement against the person's own baseline and, when it deviates for long enough, offers a break. An **agent** is for the moment the person already knows they feel off: they say or tap how they feel and what they want. Either way the result is the same: a short **personalized calming scene** assembled from a fixed, safe set of parameters.

Hackathon scope: the agent path plus a short, live eye-movement assessment on a Meta Quest Pro, one parametric forest, honest labels. The always-on background layer is the *vision*, shown as a nudge in the demo, not a finished product.

| Part | Hackathon MVP | Vision (say it, do not claim it) |
| --- | --- | --- |
| Eye signals | Short baseline, then a short loaded task with gaze probes | Continuous low-overhead monitoring while using apps |
| Reference | Personal baseline from the same session | Multi-day personal baseline, stored locally with consent |
| Agent | Chips, two sliders, optional short text -> structured state | Voice conversation, memory of what helped |
| Scene | Parametric forest, bounded settings | More biomes, optional generated skyboxes, learned preferences |
| Evidence | Small honest pilot | Proper study |

## 2. Why eye movement alone is not enough (design consequence)

- Related work on predicting self-rated fatigue from gaze (Eye Feel You, arXiv 2601.21045) reports weak correlations for known subjects and no better than a baseline predictor across subjects. We therefore **never** use a population threshold. Everything is *relative to the person's own baseline* and **paired with their own words**.
- Quest Pro gaze has person- and condition-dependent accuracy (median about 1 degree, much worse tails; Aziz et al., arXiv 2403.07210). Probes use large targets and tolerant thresholds.
- Pupil size and eyelid openness are **not available** on Quest Pro through the standard SDK, so blink rate and pupil metrics are out. Do not synthesise them.

Consequence: the eye signal is a *weak, transparent hint*; the person's report is authoritative for what scene they get.

## 3. Session flow

Total live demo: about 8-9 minutes; the pilot protocol uses the longer values.

| # | Step | Demo / pilot | What happens | Data |
| --- | --- | --- | --- | --- |
| 0 | Consent and capability check | 30 s | Explain what is recorded and that it stays local; check eye tracking permission, calibration, tracking status. Unsupported -> labelled replay. | `capabilities` |
| 1 | Baseline | 120 s minimum / 2 min minimum | Standardised gaze task (see 5.1) in a calm neutral scene. Extend if needed to collect six complete usable windows. Then sliders `eye_strain`, `mental_fatigue` (1-7). | metrics windows + `SelfReport(baseline)` |
| 2 | Load task | 2 min / 5+ min | Work-like visual task (dense reading plus visual search) with the same probes embedded. | metrics windows |
| 3 | Check-in | 30 s | Sliders again, chips, optional text. This is the agent entry. A "tired" background trigger would open the same screen. | `SelfReport(post_task)` |
| 4 | Summary | 20 s | Per-metric deviation from baseline, the experimental indicator, reconciliation with the words, limitations. | `SessionSummary`, `AnalysisResponse` |
| 5 | Scene | 2-3 min | Calming forest from `SceneConfig`; participant can adjust or exit at any time. | `SceneConfig` |
| 6 | Re-rating | 20 s | Same sliders again. Compare to step 3 as a small before/after. | `SelfReport(post_scene)` |

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
| high | high / moderate | `agree_high` | Offer the calming scene; show both. |
| high | low | `words_only` | The person's report counts. Scene follows words; say the eye signals did not show a strong change. |
| low | high | `signals_only` | Gentle offer ("your eye movements changed from your baseline - want a short break?"). Never overrides what they said. |
| low | low | `agree_low` | No nudge; scene only if they ask. |
| any | unavailable | `unavailable` | Words only; show "no usable eye data". |

### 6.3 What Gemini does

One text-only request per analysis: given the metrics, deviations, reconciliation, bounded self-report and preferences, return (a) a short explanation using only approved observation codes, (b) limitations, (c) a `SceneConfig` plus a one-line rationale. It does not compute measurements, change `reconciliation`, lower a `safety_notice`, or add unmeasured claims. Free text is treated as untrusted data.

### 6.4 Safety and privacy

- Discomfort chips or words (dizziness, nausea, pain) are matched by simple local rules and raise `safety_notice` (`take_break` / `stop_session`): "take the headset off and rest". This is not a medical judgement.
- This is a wellness demo. No diagnosis, no treatment advice.
- **Decision D1 (needs a human):** self-report free text describes a person's state. Gemini's free tier says not to submit sensitive or personal data. Default until decided: free text and chips are used only locally (deterministic mapping, section 7.3); the cloud call receives chips, sliders and synthetic or team-volunteer data with explicit opt-in on a plan whose data terms permit it.

## 7. Scene engine

### 7.1 Principle

Scenes are **assembled from a fixed parametric forest**, not generated freely. A model (or the local rules) only chooses values inside bounds. This keeps the demo reliable, comfortable and testable, and satisfies the rule that no model-generated code, URLs or arbitrary assets are executed.

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

6-10 volunteers, with consent, within-subject. Protocol: baseline ratings -> loaded task -> ratings -> 3 minutes in the scene -> ratings. If time allows, counterbalance with a 3-minute neutral empty room as a control.

Report only: n, mean change in each slider with a plain range, the correlation between the indicator and the slider change (Pearson r) with the caveat that n is tiny, and MAE of the indicator-implied rating against a mean-rating baseline, in the spirit of Eye Feel You. State that results are not generalisable, not clinical, and the scene was not shown to improve health. A negative result is a valid, reportable finding.

## 9. Demo script (3 minutes)

1. (20 s) Problem and scope: long XR use, gradual strain, we propose a personal hint plus an agent. Experimental wellness prototype.
2. (40 s) Show baseline and loaded task on the Quest Pro (or labelled replay) and the per-metric deviations.
3. (40 s) Agent check-in: sliders, chips, one sentence; show the reconciliation line.
4. (40 s) The calming scene appears; show the editable preview and the mute/exit control.
5. (40 s) Before/after ratings, pilot numbers with n, limits, and the vision (background layer, multi-day baseline).

Say out loud when anything is replay, synthetic, cached or fallback.

## 10. Risks

| Risk | Mitigation |
| --- | --- |
| Eye data unavailable or unstable on the day | Labelled replay path; hardware adapter is a separate task |
| Indicator looks random with n small | Show per-metric deviations and words; do not claim accuracy |
| Baseline taken while tired | Section 4 |
| Gemini or network down | Local fallback mapping (7.3); saved preset |
| Free-tier data terms vs personal text | Decision D1 |
| Cybersickness or discomfort | Static camera, no locomotion, short sessions, visible exit, `safety_notice` |
| Scope creep (voice, skybox, biomes) | Stretch list; forest first |
| Prior lab code or data pulled in | Code must be written during the event and AI use disclosed; restricted lab recordings must never enter this repo |

## 11. Open decisions for the team

- **D1** Cloud use of self-report text and the Gemini plan (section 6.4).
- **D2** Meaning of `fatigue score` ban in PROJECT_PLAN: this concept allows an *experimental baseline-deviation indicator* with the labels above; the words "fatigue score" remain forbidden.
- **D3** Who owns the Quest Pro build and the first-hour eye-data test (see HARDWARE_QUEST_PRO.md).
- **D4** Whether the volunteer pilot is feasible in the time left; if not, show the protocol and ratings from the team only.
- **D5** Whether to ship a second biome or the skybox stretch at all.

## 12. Related work (for the write-up, not as validation)

- Hasan and Komogortsev, "Eye Feel You", arXiv 2601.21045: gaze-velocity models predicting self-rated state; the case for personal baselines.
- Aziz et al., arXiv 2403.07210: Quest Pro eye-tracking accuracy and data loss.
- Feit et al., CHI 2017: accuracy and precision vary strongly between people and conditions.
- Barz et al., ETRA 2018: gaze interfaces that adapt to estimated error.
