# Setup: full desktop demo

The desktop app supports a complete report-only session and an explicitly chosen optional forest. Finish with report retains the report until Exit or New session. See [report-only flow and verification](REPORT_ONLY_FLOW.md); headset acceptance remains separate.

Everything below runs without a headset or a Gemini key. Eye data are synthetic and labelled as such; the backend runs its offline rules by default.

## Full demo in four commands

From the repository root (macOS; Unity installed through Hub, [uv](https://docs.astral.sh/uv/) installed):

```bash
cd backend && uv sync --locked --extra dev && cd ..
```

```bash
cd backend && uv run uvicorn app.main:app --host 127.0.0.1 --port 8000
```

Then in Unity: open `unity/` in Hub, open `Assets/App/Scenes/Assessment.unity`, press Play, and walk Consent -> Baseline -> Task -> Check-in -> Summary -> Finish with report. To exercise the optional branch, choose Try optional relaxation -> preferences/preview -> Enter optional forest -> Finish -> optional feedback. With the backend running the top bar shows "AI: FALLBACK from backend rules (no AI used)"; stop the backend and it shows "AI: LOCAL FALLBACK (no AI used: backend unreachable ...)". Both backend and client-fallback paths can finish with the report; neither requires a forest load.

Tests (close the editor first; a project cannot be open twice):

```bash
/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity -batchmode -projectPath unity -runTests -testPlatform EditMode -testResults editmode-results.xml -logFile -
```

PlayMode uses the same command with `-testPlatform PlayMode`. The second PlayMode test runs end to end against the backend only when `VR_SMOKE_BACKEND_URL=http://127.0.0.1:8000` is set (start the backend first); otherwise it is reported as ignored. Backend and contract checks:

```bash
cd backend && uv run pytest -q && cd .. && backend/.venv/bin/python contracts/validate_examples.py
```

## Versions (pinned in this repo)

| Item | Version | Where |
| --- | --- | --- |
| Unity editor | 6000.6.7f1 | `unity/ProjectSettings/ProjectVersion.txt` |
| Render pipeline | URP 17.6.0 (Universal 3D template) | `unity/Packages/manifest.json` |
| Meta XR Core SDK | 207.0.1 (`com.meta.xr.sdk.core`, pulls `com.unity.xr.hands`) | manifest |
| XR Interaction Toolkit / OpenXR / XR Management | 3.6.1 / 1.18.0 / 4.7.0 | manifest |
| Input System | 1.20.1 (active input handling: Input System only) | manifest, Player settings |
| Test Framework / Newtonsoft Json | 1.8.0 / 3.2.2 | manifest |
| Backend | Python 3.12, FastAPI, `uv.lock` | `.python-version`, `backend/pyproject.toml` |

Do not upgrade packages in a feature PR; propose it in a `chore/` PR.

## First open

1. Install **Unity 6000.6.7f1** from Unity Hub (Archive if it is not listed) with **Android Build Support** (OpenJDK, Android SDK & NDK) for the Quest build.
2. Clone into a path **without spaces or non-ASCII characters** (for example `~/dev/VR_Fatigue`). Android/Gradle builds often fail on such paths.
3. Unity Hub -> Add -> Add project from disk -> `unity/`. The first import resolves the Meta SDK and takes several minutes.
4. If a scene is missing: **App > Build Assessment Scene** regenerates the Assessment panels and scene (Assessment stays first in Build Settings). **App > Create ForestDemo Placeholder** creates a primitive placeholder `ForestDemo` only if none exists and adds it to Build Settings; the forest owner replaces it.

Never commit without the `.meta` files Unity creates, never edit GUIDs by hand, and never commit `unity/Assets/Resources/DevAgentSettings.asset` (the Meta SDK writes a machine-local token into it; it is git-ignored).

## SessionController settings (Inspector)

- **Eye Source** (desktop demo default: `Replay` of `../fixtures/replay/demo_session.json`): a **synthetic** 190 s session (130 s baseline + 60 s loaded task, 95 probe trials, realistic artifacts). The top bar says "REPLAY of synthetic data (not live)". `Replay Speed` (default 4) only shortens the wait: metrics use the file's sensor timestamps. `Synthetic` runs the live generator with the same probe task; `Hardware` is the placeholder (falls back to synthetic with a visible note). A missing replay path falls back to `StreamingAssets/fixtures/replay/`. Regenerate the fixture with `python3 tools/generate_demo_session.py` and copy it to StreamingAssets (a test checks the copy).
- **Probe task**: in Baseline and Task a yellow head-locked target (2 m) jumps every 2 s through the fixed layout `probe_cycle_10_v1` (10 positions, jumps 10-30 degrees); both phases use the same layout, so their `conditions_id` honestly match. Probe policy: within 2 degrees by 500 ms. The summary shows per-trial counts.
- **Analysis**: `Http` (default; falls back to local rules on any failure, 15 s timeout), `Mock` (canned fixture), `Local` (offline only).
- **Backend Base Url**: default `http://127.0.0.1:8000`. In a build use `-backendUrl http://<laptop-LAN-IP>:8000`. Never put the Gemini key in Unity.
- **Baseline/Task Seconds**: 130 / 60 in eye-data seconds (matches the demo file). Deviations need at least 6 usable 20 s baseline windows; the experimental indicator also needs probes in both phases. With the demo replay it shows real deviations (still synthetic data, still experimental, not a fatigue score).

## Backend and network

- Same machine: keep `http://127.0.0.1:8000` (`localhost` works only on the same machine).
- Headset or another laptop: start the backend with `--host 0.0.0.0`, use the laptop's **LAN IP** in Unity (not `localhost`, not `0.0.0.0`), open the port in the firewall, and allow HTTP for development builds (Player settings -> Allow downloads over HTTP). Venue Wi-Fi may isolate clients; a phone hotspot is the fallback. See [HARDWARE_QUEST_PRO.md](HARDWARE_QUEST_PRO.md) section 3 and [backend/README.md](../backend/README.md).
- `preferences: null` means the participant lets the app choose; the backend rejects non-null free text with 422 (decision D1). The optional request field `cloud_policy` defaults to `local_only`; `synthetic_demo` is only for invented test scenarios, never for participant check-ins.
- If the backend sets `VR_BACKEND_ACCESS_TOKEN`, start Unity with the same environment variable; the client reads it at runtime. Never put it in scenes, prefabs, URLs or command-line arguments, and never use the Gemini key as the client token.
- Backend generation deadline 12 s, Unity HTTP timeout 15 s; every network/auth failure uses the labelled local fallback.
- Gemini stays off unless `backend/.env` enables it with a key, model, prices and budget (see `backend/.env.example`). Only synthetic data is ever sent to it. The request body is `contracts/v1/analyze_request.schema.json` and never contains free text.

When you change a file in `fixtures/` that has a copy in `unity/Assets/StreamingAssets/fixtures/`, copy it again; a test fails if the copies drift.
