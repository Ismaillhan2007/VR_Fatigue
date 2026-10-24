# VR_Fatigue

HackTX prototype: Unity eye-signal capture, a Gemini-assisted wellness summary, and a personalized relaxation scene. Start with one reliable forest demo with optional squirrels and a clearly labeled offline/replay path.

The separate [FastAPI backend app](backend/README.md) has a runnable offline analysis endpoint, typed contracts, tests, and an optional Gemini adapter. The Unity application has not been scaffolded yet. Unity is confirmed and the target headset is a Meta Quest Pro; eye-data access in our own build still needs the first-hour check.

- [Project objectives, architecture, feature acceptance, and build order](docs/PROJECT_PLAN.md)
- [Backend setup and Unity/HTTP integration](backend/README.md)
- [Concept: baseline, check-in agent, indicator maths, and scene parameters](docs/CONCEPT.md)
- [Meta Quest Pro: signals, first-hour test, and networking](docs/HARDWARE_QUEST_PRO.md)
- [Versioned JSON contracts](contracts/README.md) and [synthetic fixtures](fixtures/README.md)
- [MVP stages and four-person work plan](stages.md)
- [Branching, PRs, review requests, and Unity collaboration](CONTRIBUTING.md)
- [Short shared agent rules](AGENTS.md)

The prototype is experimental and does not provide medical diagnosis or validated fatigue detection.
