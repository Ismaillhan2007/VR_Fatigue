# VR_Fatigue

HackTX prototype: Unity eye-signal capture, a Gemini-assisted wellness summary, and a personalized relaxation scene. Start with one reliable forest demo with optional squirrels and a clearly labeled offline/replay path.

**Status (integration branch):** the desktop demo runs end to end with synthetic eye data: Unity session flow -> real signals metrics (quality, gaze/head metrics, baseline deviations) -> FastAPI backend (offline rules by default) or local fallback -> sanitised `SceneConfig` -> placeholder forest. Every step shows its data source and AI source. Not done yet: a probe task (so the experimental indicator is still `unavailable` in the app), the real forest scene, the Quest Pro adapter wired into the app, live Gemini, and any headset or Android check. Unity **6000.6.7f1**, Meta XR Core SDK **207.0.1**. Run it with [setup](docs/SETUP.md); seams in [architecture](docs/ARCHITECTURE.md); open issues per owner in [integration review](docs/INTEGRATION_REVIEW.md).

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

The prototype is experimental and does not provide medical diagnosis or validated fatigue detection.
