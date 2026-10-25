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

Works on desktop (Unity 6000.6.7f1, macOS), end to end and tested:
- **Demo path:** synthetic 130 s replay with probes -> real metrics, deviations and the experimental indicator -> FastAPI backend (offline rules) or local fallback when the backend is unreachable -> sanitised scene config -> forest.
- **Tests:** about 290 Unity EditMode tests, PlayMode tests (including Unity <-> FastAPI over HTTP), 130 backend tests, and contract fixture checks.

Not done or not verified:
- the real forest art (the current ForestDemo is a placeholder);
- the Quest Pro adapter wired into the app flow, and any headset test;
- live Gemini in this build;
- an Android build.

The demo data are synthetic and labelled as such.

## Quick start

See [docs/SETUP.md](docs/SETUP.md). In short: start the backend (`cd backend && uv sync --locked --extra dev && uv run uvicorn app.main:app --host 127.0.0.1 --port 8000`), open `unity/` in Unity Hub with editor 6000.6.7f1, open `Assets/App/Scenes/Assessment.unity`, press Play.

## Working together

Read [AGENTS.md](AGENTS.md) (short rules for people and coding agents) and [CONTRIBUTING.md](CONTRIBUTING.md) (branches, ownership, how to avoid conflicts, checks before a PR). Feature branches start from `dev` and go back into `dev` by PR with a merge commit; `main` is the demo-ready snapshot.
