# VR_Fatigue

HackTX 2026 prototype for the **Meta Quest Pro**: a short eye-movement check against your own baseline, a check-in where you say how you feel, and a personalized calming forest built from what you said. Experimental wellness prototype: **not a medical device, not a diagnosis, not validated fatigue detection.**

## The idea in one minute

Long VR sessions cause gradual strain that people rarely notice. Gaze alone is a weak signal and differs a lot between people, so the app never uses a population threshold. It compares you with **your own baseline from the same session**, puts **your own words first**, and offers a calm break whose settings you can see and change.

```text
Consent -> Baseline (probe targets) -> Task (same probes) -> Check-in (2 sliders, chips, optional text)
        -> Summary (per-metric deviations, experimental indicator, explanation) -> Forest -> Re-rating
```

1. **Capture.** An eye provider delivers timestamped gaze samples: synthetic generator, replay file, or (later) the Quest Pro. A yellow head-locked target jumps every 2 s (same layout in both phases) to measure latency, accuracy and misses.
2. **Metrics (deterministic, on device).** Quality checks (repeated timestamps dropped, stalls and gaps handled), gaze/head speed, probe metrics, robust deviation from the baseline, and an **experimental baseline-deviation indicator** (never called a fatigue score).
3. **Reconciliation and safety (on device).** The app compares words and signals and checks for discomfort. Free text never leaves the device.
4. **Explanation (backend).** FastAPI validates the summary and returns a short explanation plus a bounded scene configuration. It uses deterministic rules by default; Gemini is optional, only for synthetic input, and only picks approved explanation codes and bounded settings.
5. **Scene.** Unity re-validates and clamps the configuration, applies the participant's explicit choices (they always win), and the forest fades lighting, fog, sound, particles, a breathing orb and an optional squirrel. The camera never moves; pause, mute and exit are always available.

Every screen shows where the data came from (LIVE / REPLAY / SYNTHETIC) and where the explanation came from (live / cached / fallback AI).

## What is in the repo

| Part | Path | Docs |
| --- | --- | --- |
| Unity app (session flow, UI, seams, probe task) | `unity/Assets/App` | [Architecture](docs/ARCHITECTURE.md), [Setup](docs/SETUP.md) |
| Signals, metrics, baseline, indicator, Quest Pro bridge | `unity/Assets/Signals`, `unity/Assets/QuestPro` | [Signals](docs/SIGNALS.md), [Metrics](docs/METRICS.md), [Baseline](docs/BASELINE.md), [Indicator](docs/INDICATOR.md), [Quest Pro](docs/QUEST_PRO_INTEGRATION.md) |
| Backend (FastAPI, optional Gemini) | `backend/` | [Backend README](backend/README.md), [Runtime plan](docs/BACKEND_RUNTIME_PLAN.md) |
| Forest scene | `unity/Assets/App/Relaxation`, `App/Squirrel`, `Scenes/ForestDemo.unity` | [Forest](docs/FOREST_SCENE.md) |
| Shared JSON contracts and synthetic fixtures | `contracts/v1`, `fixtures/` | [Contracts](contracts/README.md), [Fixtures](fixtures/README.md) |
| Concept and plan | `docs/` | [Concept](docs/CONCEPT.md), [Project plan](docs/PROJECT_PLAN.md), [Quest Pro facts](docs/HARDWARE_QUEST_PRO.md), [Stages](stages.md) |
| Submission | `docs/SUBMISSION.md` | Devpost draft, demo script, AI-use disclosure |

## Status

**Current desktop demo:** Unity replays a synthetic session with a 130-second baseline, a 60-second loaded task and target probes. The signals engine calculates gaze/head/probe metrics, baseline deviations and an experimental indicator from that replay. Default replay speed is 4x. Unity sends the aggregate summary to FastAPI and applies the returned, validated settings to a placeholder `ForestDemo` scene. The UI identifies synthetic replay and distinguishes backend rules from Unity's local fallback.

The backend works offline by default. Interactive check-ins use `cloud_policy: local_only`, including those with synthetic eye samples. The optional Gemini adapter has passed a bounded synthetic-only smoke check; invented test scenarios must explicitly opt in with `synthetic_demo`. The Gemini key stays on the backend.

Remaining work includes the finished forest assets/audio, wiring the Quest Pro capture bridge into the app's hardware provider, and native Editor/network/headset acceptance on the target machines. A working local backend does not establish connectivity from a second laptop. Unity is pinned to **6000.6.7f1**, Meta XR Core SDK to **207.0.1**, and Python to **3.12**. Follow [setup](docs/SETUP.md), [architecture](docs/ARCHITECTURE.md) and the [backend guide](backend/README.md). The [integration review](docs/INTEGRATION_REVIEW.md) records an earlier integration checkpoint; some listed tasks have since been completed.

Automated checks on `dev`: about 290 Unity EditMode tests, PlayMode tests including Unity <-> FastAPI over HTTP, 130 backend tests, and contract fixture checks. Not verified yet: any headset run and an Android build. The demo data are synthetic and labelled as such.

## Quick start

See [docs/SETUP.md](docs/SETUP.md). In short: start the backend (`cd backend && uv sync --locked --extra dev && uv run uvicorn app.main:app --host 127.0.0.1 --port 8000`), open `unity/` in Unity Hub with editor 6000.6.7f1, open `Assets/App/Scenes/Assessment.unity`, press Play.

## Connect Unity on your teammate's laptop

`127.0.0.1` refers to the laptop running the client. If FastAPI runs on your laptop and Unity runs on your teammate's, Unity needs your reachable network IP. `0.0.0.0` tells the server where to listen; it is not a client destination.

### Where to get the token

`VR_BACKEND_ACCESS_TOKEN` is a private password your team creates for this backend. It does not come from Google, and it is different from `GEMINI_API_KEY`. Its value is stored in the backend owner's ignored `backend/.env` file:

```dotenv
VR_BACKEND_ACCESS_TOKEN=<your-shared-backend-token>
```

If it is already configured, copy only the value after `=` from that line and share it privately with the Unity owner. Do not include the variable name or surrounding quotes in the pasted token. Never send the full `.env` file. To create a token on a fresh setup, run this in `backend/`, then paste its output into that line of `.env`:

```sh
uv run python -c "import secrets; print(secrets.token_urlsafe(32))"
```

This prints the new backend token in your own terminal. Do not paste it into Git, screenshots, public chats or Unity assets. If `.env` does not exist, copy `.env.example` first; do not overwrite an existing `.env`. Gemini can stay disabled throughout the connection test.

### Your steps: backend laptop

1. Use the latest `dev` and configure the backend token above.
2. Stop the existing server with **Ctrl+C in its original terminal**. It reads `.env` at startup, so restart after changing the token.
3. Start one LAN receiver from the repository root:

```sh
cd backend
uv sync --locked --extra dev
uv run uvicorn app.main:app --host 0.0.0.0 --port 8000 --access-log
```

4. In a second terminal, find your shared-network IP (macOS Wi-Fi commonly uses this interface):

```sh
ipconfig getifaddr en0
```

5. Give your friend `http://<that-IP>:8000` and the backend token. Keep the server terminal running. If no IP is returned, find the active interface's IP in macOS Network settings.

### Your friend's steps: Unity laptop

1. Save/commit their current work, quit the Unity Editor, and run these commands from their repository folder:

```sh
git fetch origin
git switch dev
git pull --ff-only origin dev
```

2. In a browser **on their laptop**, open `http://<backend-IP>:8000/health`. It must return `status: ok`; this public health endpoint needs no token. If it fails, resolve the shared-network/firewall route first.
3. Launch the Editor with `VR_BACKEND_ACCESS_TOKEN` set in its process environment. Use the exact [macOS or Windows commands in the backend guide](backend/README.md#launch-unity-with-the-shared-token); no token belongs in the Inspector.
4. Open `Assets/App/Scenes/Assessment.unity`. On `SessionController`, set **Analysis = Http** and **Backend Base Url = `http://<backend-IP>:8000`**. Use the address you supplied, not `127.0.0.1` or `0.0.0.0`. Do not commit this scene change; alternatively pass `-backendUrl http://<backend-IP>:8000` on the Editor command line. Plain `http://` to another laptop also needs **Player Settings -> Other Settings -> Allow downloads over HTTP = Allowed in development builds** (set in the repository once #12 is merged); with "Not allowed" Unity only reaches `127.0.0.1`.
5. Press Play and complete the check-in. Your server terminal must show `POST /api/v1/analyze` with **200**, and Unity should say **FALLBACK from backend rules** when cloud use is disabled. A forest reached through **LOCAL FALLBACK** alone does not prove connectivity.

### Troubleshooting

| Result | Action |
| --- | --- |
| Friend cannot reach `/health` | Confirm the server listens on `0.0.0.0`, both laptops have a reachable shared network, and the firewall allows the backend's inbound port. Check VPN routing/client isolation; try a shared team hotspot. |
| HTTP 401 | Use the same backend token and restart both server and Editor so each reads its new configuration. |
| HTTP 422 | Inspect the validation errors and align the Unity request with the v1 contract. Changing the token will not repair invalid JSON/data. |
| `[Errno 48] address already in use` | Stop the original server command before restarting. Run one receiver per port. |
| Unity shows **LOCAL FALLBACK (backend error / connection failed)** and the server logs nothing, although `/health` works in the browser | Unity's HTTP policy blocked the request: set Allow downloads over HTTP as in step 4, restart Play. |
| HTTP 200 and `ai_source: fallback` | Expected for `local_only` check-ins. Check `X-VR-Fallback-Reason`; `local_only` confirms the privacy gate, not a failed connection. |

Keep machine-specific URLs and tokens out of committed scenes/prefabs. A development HTTP bearer token is for the trusted local demo; use HTTPS and proper access controls before public hosting.

## Project guides

- [Project objectives, architecture, feature acceptance, and build order](docs/PROJECT_PLAN.md)
- [Backend setup and Unity/HTTP integration](backend/README.md)
- [Concept: baseline, check-in agent, indicator maths, and scene parameters](docs/CONCEPT.md)
- [Meta Quest Pro: signals, first-hour test, and networking](docs/HARDWARE_QUEST_PRO.md)
- [Versioned JSON contracts](contracts/README.md) and [synthetic fixtures](fixtures/README.md)
- [Signals module: integration, quality policy, and checks](docs/SIGNALS.md)
- [Gaze/head metrics and target-probe calculations](docs/METRICS.md)
- [Personal baseline, window quality and metric comparison](docs/BASELINE.md)
- [Experimental indicator, smoothing and offer-state rules](docs/INDICATOR.md)
- [Quest Pro provider, SDK setup and integrator handoff](docs/QUEST_PRO_INTEGRATION.md)
- [MVP stages and four-person work plan](stages.md)
- [Branching, PRs, review requests, and Unity collaboration](CONTRIBUTING.md)
- [Short shared agent rules](AGENTS.md)

## Working together

Read [AGENTS.md](AGENTS.md) (short rules for people and coding agents) and [CONTRIBUTING.md](CONTRIBUTING.md) (branches, ownership, how to avoid conflicts, checks before a PR). Feature branches start from `dev` and go back into `dev` by PR with a merge commit; `main` is the demo-ready snapshot.

The prototype is experimental and does not provide medical diagnosis or validated fatigue detection.
