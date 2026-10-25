# Setup (Unity, desktop mode)

Everything below runs without a headset, backend or Gemini key. Eye data are synthetic and labelled as such.

## Versions (pinned in this repo)

| Item | Version | Where |
| --- | --- | --- |
| Unity editor | 6000.6.7f1 | `unity/ProjectSettings/ProjectVersion.txt` |
| Render pipeline | URP 17.6.0 (Universal 3D template) | `unity/Packages/manifest.json` |
| XR Interaction Toolkit | 3.6.1 | manifest |
| OpenXR | 1.18.0 | manifest |
| Input System | 1.20.1 (active input handling: Input System only) | manifest, Player settings |
| Test Framework | 1.8.0 | manifest |
| Newtonsoft Json | 3.2.2 (`com.unity.nuget.newtonsoft-json`) | manifest |
| Meta XR SDK | not installed yet (comes with `feat/quest-eye`) | - |

The package versions are the ones this editor ships as verified. Do not upgrade packages in a feature PR; propose it in a `chore/` PR.

## First open

1. Install **Unity 6000.6.7f1** from Unity Hub (Installs -> Install Editor -> Archive if it is not listed). Add **Android Build Support** (with OpenJDK and Android SDK & NDK) for the later Quest build.
2. Clone into a path **without spaces or non-ASCII characters** (for example `~/dev/VR_Fatigue`). Android/Gradle builds often fail on such paths; desktop play mode is fine either way.
3. Unity Hub -> Add -> Add project from disk -> select the `unity/` folder. The first import takes a few minutes.
4. If `Assets/App/Scenes/Assessment.unity` is missing or broken, run the menu **App > Build Assessment Scene**. It regenerates the panel prefabs in `Assets/App/UI/Prefabs/` and the scene, and puts the scene first in Build Settings. It never touches `ForestDemo`.

Never commit without the `.meta` files Unity creates next to new assets, and never edit GUIDs by hand.

## Run in desktop mode

1. Open `Assets/App/Scenes/Assessment.unity` and press Play.
2. The `SessionController` object holds the settings (Inspector):
   - **Eye Source**: `Synthetic` (default), `Replay` (needs `Replay Csv Path`, format in [ARCHITECTURE.md](ARCHITECTURE.md)), `Hardware` (placeholder; falls back to synthetic with a visible note).
   - **Analysis**: `Http` (default; calls the backend and falls back to local rules on any failure), `Mock` (canned fixture), `Local` (offline rules only).
   - **Backend Base Url**: default `http://127.0.0.1:8000`. In a build, pass `-backendUrl http://<laptop-ip>:8000`.
   - **Baseline/Task Seconds**: 20 s each on desktop; the pilot protocol uses 120 s / 300 s. With less than 120 s of baseline the baseline is flagged `too_short` and the experimental indicator is `unavailable`, which is correct behaviour.
3. Walk through Consent -> Baseline -> Task -> Check-in -> Summary -> Scene -> Re-rating. The top bar always shows the data source (SYNTHETIC / REPLAY / LIVE) and the AI source (live / cached / LOCAL FALLBACK). Pause and Exit work in every step.

Without the backend running, `Http` mode shows "LOCAL FALLBACK (no AI used: backend unreachable ...)". That is the offline demo path and must keep working.

## Tests

- Editor: Window -> General -> Test Runner -> **EditMode** -> Run All. Tests read the shared fixtures from `../fixtures/` directly.
- Command line (close the editor first; the project cannot be open twice):

```bash
/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath unity -runTests -testPlatform EditMode -testResults editmode-results.xml -logFile -
```

- PlayMode smoke test (loads `Assessment`, runs one synthetic session offline): same command with `-testPlatform PlayMode`.
- Contract fixtures (Python, no Unity):

```bash
python -m pip install jsonschema referencing && python contracts/validate_examples.py
```

When you change a file in `fixtures/` that has a copy in `unity/Assets/StreamingAssets/fixtures/`, copy it again; a test fails if the copies drift.

## Backend connection (when `feat/gemini-backend` is running)

- Same machine: keep the default URL.
- Headset on Wi-Fi: use the laptop's LAN IP, open the port in the firewall, and allow HTTP for development builds (Player settings -> Allow downloads over HTTP). See [HARDWARE_QUEST_PRO.md](HARDWARE_QUEST_PRO.md) section 3.
- The request body is `contracts/v1/analyze_request.schema.json`; it never contains free text.
