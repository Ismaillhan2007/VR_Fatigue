# VR_Fatigue

HackTX prototype: Unity eye-signal capture, a Gemini-assisted wellness summary, and a personalized relaxation scene. Start with one reliable forest demo with optional squirrels and a clearly labeled offline/replay path.

The Unity project in `unity/` includes the application session flow, UI, contract DTOs and integration seams (see [setup](docs/SETUP.md) and [architecture](docs/ARCHITECTURE.md)). The signals module adds deterministic replay, quality, gaze/head/probe metrics, personal baseline comparison, an experimental indicator and the Quest Pro SDK bridge. The modules are delivered together; wiring the application seams to these signal APIs remains integrator work. Unity is pinned to **6000.6.7f1** and Meta XR Core SDK to **207.0.1**. Backend, forest and live headset acceptance remain pending.

- [Project objectives, architecture, feature acceptance, and build order](docs/PROJECT_PLAN.md)
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
