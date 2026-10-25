# Assessment UI style (velt.dev look, VR sizes)

The generated assessment UI (`App > Build Assessment Scene`, `AssessmentSceneBuilder`) follows the visual language of
[velt.dev](https://velt.dev), read from its computed styles on 2026-10-25, scaled up for a headset. **Nothing here has been
rendered or run yet**: the builder was edited without the Unity editor, and `Assessment.unity` plus `UI/Prefabs` are
generated files that must be regenerated and committed by whoever runs the builder.

## Render settings requested for the headset

| Setting | Value | Where | State |
| --- | --- | --- | --- |
| Render scale | 1.0 | `Assets/Settings/Mobile_RPAsset.asset` (`m_RenderScale`) | already set by PR #35, unchanged |
| MSAA | 4x | same asset (`m_MSAA`) | already set by PR #35, unchanged |
| Canvas text scale | 3 | `QuestAssessmentPresentation.TextPixelsPerUnit` -> `CanvasScaler.dynamicPixelsPerUnit` | already set by PR #35, unchanged |

The PC asset keeps MSAA off. The world canvas itself stays 1920 x 1080 units at 1 mm per unit, 2 m away.

## Tokens (`UiTheme`)

| Token | Value | Source |
| --- | --- | --- |
| Ink (text, primary button) | `#26251E` | velt button/text colour |
| Paper (text on ink) | `#F7F7F4` | velt light text |
| Card | white, 0.97 alpha, 3 px `#D9D5CF` ring, 16-unit corners | velt 12 px cards and hairline border |
| Accent | `#D95D2B` (primary action on dark, slider handle, checkmark) | velt orange badges |
| Muted (placeholders) | `#5A5953` | darker than velt's grey for headset contrast |
| Type | built-in Unity font, **bold everywhere**, 1.25x the previous sizes | requested: thicker font |

Velt's own typeface (Urbanist, SIL OFL) is not in the project; adding it needs a font file import (a download), which was
not done. Until then the headset uses the Unity legacy font in bold.

## Controls

- Primary button: ink fill, paper text. Secondary (skip, back, exit, pause, mute, optional relaxation, finish without
  feedback): white with an ink ring. On the dark pause overlay the primary action is orange.
- **Buttons are larger**: 380 x 76 units (was 240 x 48), 28 pt bold, rows 84 high; the Quest presentation now only raises
  button sizes (min 330 x 60) instead of forcing 60.
- Toggles: 40-unit outlined box, 24 pt bold label, 52 high. Chips 296 x 52. Sliders: 48 high, thick track, ink fill, 48-unit
  orange handle. Input: 110 high. Provenance bar: ink with paper text, 130 units high (anchors 0.88-1) so its three label rows are not clipped.

## Comfort caveat

Velt is a light theme. Large white panels in a headset are bright and can tire eyes, and this app measures eye signals.
Panels are warm-white at 0.97 alpha and text is high contrast; if testers report glare, lower the card colour in
`UiTheme.Card`/`PanelRoot`. Baseline and task share the same UI, so the comparison conditions are unchanged, but confirm that
on the headset.

## Regenerate and verify

1. Close Unity, pull, reopen `unity/` (6000.6.7f1); confirm no compile errors.
2. `App > Build Assessment Scene`. It writes `Assets/App/UI/Generated/Rounded*.png` and regenerates the prefabs and scene.
3. Commit the regenerated files with their `.meta`. Coordinate with #18 so nobody regenerates the assessment scene concurrently.
4. Check on desktop first (overflow in Check-in, Summary, Relaxation setup), then on the Quest Pro: readable text, buttons
   reachable with the ray, no clipped labels.
