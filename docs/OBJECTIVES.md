# Product objectives and current alignment

Updated October 25, 2026. This is the product direction for the eye-tracking app. The implementation audit below describes `origin/dev` at `aa21658`; this documentation branch does not change runtime behavior.

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

This is the target flow. The current flow below still needs implementation changes.

## Current implementation gaps

| Area | Evidence in the current app | Difference from the objective / required follow-up |
| --- | --- | --- |
| Successful completion | [`SessionFlow`](../unity/Assets/App/Core/Flow/SessionFlow.cs) has `Summary -> Scene -> ReRating -> Done`; only `SubmitReRating` moves to `Done`. | Add a normal report-only completion transition from Summary. Keep the optional scene branch, and let scene users finish without requiring feedback. |
| Report UI | [`SummaryPanel`](../unity/Assets/App/UI/Panels/SummaryPanel.cs) exposes `EnterSceneButton`; [`AssessmentSceneBuilder`](../unity/Assets/App/UI/Editor/AssessmentSceneBuilder.cs) labels it "Enter calming scene". | Add equally clear report-only completion and optional-scene choices. Update the builder and regenerate its UI assets in an implementation PR. The current app waits for a click; it does not automatically enter the scene. |
| Exit versus completion | [`SessionFlow.Exit`](../unity/Assets/App/Core/Flow/SessionFlow.cs) replaces the session context; [`SessionController.Exit`](../unity/Assets/App/UI/SessionController.cs) also clears recordings and scene handoff. | Global Exit lets a user decline the forest today, but clears the report and is not successful report-only completion. Keep this privacy behavior for explicit Exit; add a distinct completion action. |
| Preferences versus participation | [`CheckInPanel`](../unity/Assets/App/UI/Panels/CheckInPanel.cs) collects scene preferences during the check-in. "I want to choose scene settings myself" only controls overrides; otherwise `Preferences` is null. | Null preferences mean default settings, not refusal or acceptance of the scene. Move scene controls behind an explicit participation choice. |
| Report/config coupling | [`SummaryFormatter`](../unity/Assets/App/UI/SummaryFormatter.cs) always includes a scene preview/rationale. [`AnalysisResponse`](../backend/app/models.py) and the [response schema](../contracts/v1/analysis_response.schema.json) require `scene_config` and `scene_rationale`. | Make the report useful independently of scene content. An unused valid v1 config can be ignored by the UI; omitting it from the wire requires a coordinated contract change. A returned config is never consent to load a scene. |
| End screen and feedback | [`DonePanel`](../unity/Assets/App/UI/Panels/DonePanel.cs) assumes after-task and after-scene ratings. | Show a report-only completion without invented post-scene values. Treat scene feedback as optional and display it only when collected. |
| Verification | Existing flow checks under [`App/Tests`](../unity/Assets/App/Tests) follow the current scene-oriented flow. | Add coverage for successful report-only completion, explicit scene opt-in, optional feedback and safety/exit behavior. Verify the report with the backend unavailable and with no forest scene loaded. This branch supplies no runtime verification. |
| Eye-tracking readiness | [`EyeProviderFactory` in `HardwareEyeProvider.cs`](../unity/Assets/App/Capture/HardwareEyeProvider.cs) still uses the app hardware placeholder; the [Quest Pro bridge](QUEST_PRO_INTEGRATION.md) has pending device acceptance. The desktop demo uses labelled synthetic replay. | Wire and verify live capture before claiming a working headset eye-tracking product. Keep replay labels visible; rendering a forest does not demonstrate live tracking or validated fatigue detection. |

## Delivery priorities and acceptance

- **Core:** assessment/replay, deterministic metrics and personal baseline, check-in, report, quality/limitations/provenance, offline fallback and successful report-only completion. Report-only users receive the same measured observations, explanation and safety information as scene participants.
- **Optional use case:** one prebuilt parametric forest with explicit opt-in, preferences and comfort controls. It can be delivered and demonstrated without becoming mandatory for report access or session completion.
- **Later:** continuous monitoring, multi-day baselines, additional environments and optional report export. These must not block core report delivery.

Before claiming alignment, demonstrate both branches. In the report-only branch, reach Done without entering/loading `ForestDemo`, choosing scene settings or submitting `post_scene` ratings; keep report review available until explicit end/reset. In the scene branch, require a deliberate choice, honor preferences and safety, and allow completion without feedback. Repeat the core report path offline and with insufficient eye data, showing limitations rather than fabricating results. No-runtime-change documentation alone does not satisfy these criteria.

Use this document with [the concept](CONCEPT.md), [implementation plan](PROJECT_PLAN.md), [current architecture](ARCHITECTURE.md) and [demo/submission claims](SUBMISSION.md). Historical integration records describe the older flow and do not supersede these objectives.
