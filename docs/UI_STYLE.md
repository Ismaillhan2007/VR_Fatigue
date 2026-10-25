# Assessment UI style (calm dark, VR sizes)

The generated assessment UI (`App > Build Assessment Scene`, `AssessmentSceneBuilder`) keeps the velt.dev vocabulary
introduced in #37 (rounded cards, solid primary buttons, bold headings) in a calm dark palette. #37's own comfort caveat
came true: large white panels glared in the Quest Pro, and this app asks about eye strain. `Assessment.unity` and
`UI/Prefabs` are generated: change the builder, then regenerate.

## Render settings for the headset

| Setting | Value | Where |
| --- | --- | --- |
| Render scale | 1.0 | `Assets/Settings/Mobile_RPAsset.asset` (`m_RenderScale`) |
| MSAA | 4x | same asset (`m_MSAA`) |
| Canvas text scale | 3 | `QuestAssessmentPresentation.TextPixelsPerUnit` -> `CanvasScaler.dynamicPixelsPerUnit` |

The world canvas stays 1920 x 1080 units at 1 mm per unit, 2 m away (1 unit is about 0.03 degrees).

## Type scale (canvas units)

| Role | Size | Notes |
| --- | --- | --- |
| Eyebrow (step label) | 28 bold, accent | "STEP 1 OF 4 · BASELINE" |
| Title | 58 bold | |
| Body | 38 regular | about 1.1 degrees per em; the previous 20-25 units were unreadable in the headset |
| Small / muted | 30 | |
| Buttons | 38 bold, 360 x 92 | |
| Rating numbers | 44 bold, 120 x 100 each | |

The Quest presentation no longer shrinks report bodies to 24; scroll views keep the builder size (minimum 30).

## Tokens (`UiTheme`)

| Token | Value | Use |
| --- | --- | --- |
| Card | `#17221E`, 0.97 alpha, 32-unit corners | panels |
| Surface | `#25332D` | secondary buttons, unselected numbers and chips, inputs |
| Bar | `#0E1512` | provenance bar, pause overlay |
| Text / Muted | `#EEF2EC` / `#A9B9AF` | |
| Accent / AccentText | `#8ED1B0` / `#0E1A14` | primary action, selection, step labels |

The probe target colour is a measurement stimulus and is unchanged. Controls rest slightly dimmed (0.86) and brighten
under the ray, so hover is visible.

## Controls and flow

- **Instructions before each recording.** Baseline and task each open on an instruction screen; nothing is recorded
  until the person presses "I'm ready, start" / "Start task" (`SessionController.StartPhase`). Start is ignored while
  paused; Exit discards the waiting phase. A large countdown shows during recording. The panel never moves in these
  states (placement is cancelled on Baseline/LoadTask).
- **1-7 ratings are seven number buttons** (`RatingRow`), not sliders. Nothing is preselected; Continue / Next / Submit
  stay disabled until both numbers are chosen; pressing the chosen number again keeps it.
- **Check-in in three steps:** ratings -> optional feeling chips (pills grouped "I feel" / "I would like", up to 6) ->
  optional own words plus the opt-in Gemini summary choice from #36. Selected pills turn accent (`ToggleVisual`).
- **Own words / dictation.** Pointing at the box opens the Quest system keyboard; its microphone key dictates. The words
  stay on the headset for the local report and safety rules; they are never sent to the backend or Gemini (AGENTS.md).
- **Forest menu can be hidden.** "Hide menu" collapses the scene panel and provenance bar to one "Show menu" button with
  the Data / AI labels above it, so provenance stays visible. Leaving the scene restores the bar.
- Provenance bar: anchors 0.882-0.998 (from ReDBuss's fix) so the three label rows are not clipped.

## Regenerate and verify

1. Close Unity, pull, reopen `unity/` (6000.6.7f1); confirm no compile errors.
2. `App > Build Assessment Scene`; commit the regenerated prefabs and scene with their `.meta`.
3. PlayMode tests save screenshots when `VR_UI_SCREENSHOT_DIR` is set (`baseline-instructions`, `baseline-rating`,
   `task-instructions`, `check-in`, `report`, ...). Check those, then the Quest Pro: readable text, every control reachable
   with the ray, keyboard and microphone on the own-words step.
