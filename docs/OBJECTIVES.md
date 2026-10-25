# Product objectives and current alignment

Updated October 25, 2026. This is the product direction for the eye-tracking app. The desktop report-only flow is now implemented; [REPORT_ONLY_FLOW](REPORT_ONLY_FLOW.md) records its code and checks. The original audit described `origin/dev` at `aa21658`; native-eye/headset acceptance remains pending.

## Core purpose

VR Fatigue is an **eye-tracking app** for understanding eye-movement changes, self-reported eye strain and mental fatigue. Its primary outcome is a useful **health and fatigue report**, presented as an **experimental wellness summary**: measured observations relative to the participant's own baseline, their check-in, data quality, limitations and data/AI provenance.

The report does not diagnose health conditions, establish that someone is fatigued, or provide validated fatigue detection. The experimental baseline-deviation indicator must keep its label and supporting observations. Missing eye data must produce an honest insufficient-data result, with any self-report clearly distinguished from measurements.

**A report-only session is a complete, successful use of the app.** Users can choose whether to participate in an additional relaxation experience. Rendering a personalized 3D forest is one optional use case; it does not define the product or determine access to the report. Ordinary Unity rendering of the assessment UI and probe targets is still needed; declining the relaxation feature means no forest experience is required.

## Required user choices

1. Consent to assessment and see whether the source is live, replay or synthetic. Consent to eye-data collection is separate from choosing a relaxation scene.
2. Complete the baseline, assessment and check-in, then receive the report with its limitations. The report must work without Gemini, a network connection or a relaxation scene.
3. At the report, choose **Finish with report** or **Try an optional relaxation scene**. Declining the scene is normal completion, not an aborted session. Keep the report available for review until the user ends or resets the session; existing privacy rules still govern clearing data. Saving/exporting is a separate optional feature, not a prerequisite for report-only completion.
4. Only after choosing the scene, ask for scene preferences, preview the supported settings and confirm entry. No eye-signal threshold, self-report, generated configuration or missing preference should be treated as consent to enter it.
5. Scene participants can pause, mute or leave. Post-scene ratings apply only to those who participated; report-only users do not owe a post-scene rating. A `stop_session` notice continues to block scene entry and offer an immediate exit.

Always-on monitoring and multi-day baselines remain future work. The current short assessment is the starting point for the eye-tracking product, not evidence of continuous monitoring or clinical validity.

## Intended flow

```text
Consent + capability/source check
  -> Baseline -> Assessment -> Check-in -> Health/fatigue report (experimental wellness summary)
       -> Finish with report -> Done
       -> Explicitly choose relaxation -> Preferences/preview -> Forest -> Optional feedback -> Done

Pause/exit remain available throughout; stop-session safety prevents scene entry.
```

The desktop state machine and generated UI now implement this flow.

## Current implementation alignment

| Area | Desktop implementation / remaining acceptance |
| --- | --- |
| Successful completion | `SessionFlow.FinishWithReport` moves Summary -> Done without a forest or feedback. Done retains the report until explicit Exit/reset. |
| Explicit optional relaxation | Summary offers a separate choice, then `RelaxationSetupPanel` collects optional preferences and previews the result before deliberate entry. Back returns to the report. |
| Report/config separation | SummaryFormatter shows measurements, quality, safety, explanation, limitations and provenance. Scene preview/rationale appears only after opting in; the v1 wire contract remains unchanged. |
| Feedback | Scene participants may submit or skip post-scene feedback. Done describes actual participation without inventing ratings. |
| Verification | See [desktop state/UI/HTTP checks](REPORT_ONLY_FLOW.md), including offline, insufficient-data, safety, reset and both participation branches. |
| Live eyes and headset | App hardware provider/rig integration and device acceptance remain #18/#21. Synthetic replay labels stay visible. Rendering/foveation is not evidence of live assessment tracking. |
| Hosted transfer | Runtime endpoint/auth and pre-upload participant-transfer controls remain #20. `cloud_policy: local_only` prevents Gemini forwarding, not HTTP upload. Hosted staging remains synthetic-only. |

## Delivery priorities and acceptance

- **Core:** assessment/replay, deterministic metrics and personal baseline, check-in, report, quality/limitations/provenance, offline fallback and successful report-only completion. Report-only users receive the same measured observations, explanation and safety information as scene participants.
- **Optional use case:** one prebuilt parametric forest with explicit opt-in, preferences and comfort controls. It can be delivered and demonstrated without becoming mandatory for report access or session completion.
- **Later:** continuous monitoring, multi-day baselines, additional environments and optional report export. These must not block core report delivery.

Before claiming alignment, demonstrate both branches. In the report-only branch, reach Done without entering/loading `ForestDemo`, choosing scene settings or submitting `post_scene` ratings; keep report review available until explicit end/reset. In the scene branch, require a deliberate choice, honor preferences and safety, and allow completion without feedback. Repeat the core report path offline and with insufficient eye data, showing limitations rather than fabricating results. No-runtime-change documentation alone does not satisfy these criteria.

Use this document with [the concept](CONCEPT.md), [implementation plan](PROJECT_PLAN.md), [current architecture](ARCHITECTURE.md) and [demo/submission claims](SUBMISSION.md). Historical integration records describe the older flow and do not supersede these objectives.
