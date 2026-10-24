# Project rules

## Objective
Build a Unity VR prototype for HackTX: eye-signal capture -> deterministic metrics -> Gemini wellness summary -> a personalized, prebuilt relaxation scene. Ship one reliable forest demo with optional squirrels before adding more environments.

## Development
- Read `docs/PROJECT_PLAN.md` and `CONTRIBUTING.md`; implement one acceptance criterion at a time.
- Use Unity/C# for VR and Python/FastAPI with the Google Gen AI SDK for the backend. Keep the Gemini key on the backend.
- Keep capture, metrics, AI interpretation, and scene configuration separate. Version shared JSON contracts before parallel feature work.
- Preserve Unity `.meta` files and GUIDs. Coordinate ownership of scenes/prefabs; prefer separate prefabs to simultaneous scene edits.
- Keep `main` demo-ready. Use short-lived `feat/`, `fix/`, `docs/`, `test/`, or `chore/` branches and PRs; see `CONTRIBUTING.md`.
- Validate changed behavior and report exactly what ran. Headset-only checks must be identified when unavailable.

## Agents may
- Inspect, implement, refactor within the assigned scope, update docs, and run local checks.
- Create local branches and draft PRs for requested changes; request a teammate's review.
- Add clearly labeled synthetic fixtures and deterministic offline fallbacks.

## Agents must not
- Merge PRs, push directly to `main`, force-push shared branches, or discard teammates' changes without explicit authorization.
- Buy assets, enable billing, increase spending limits, or deploy publicly without explicit authorization.
- Commit credentials, identifying eye data, raw eye images, or personal reports; transmit real eye data to Gemini by default.
- Diagnose conditions, prescribe treatment, invent measurements/accuracy, or describe a demo score as clinically validated.
- Represent head gaze, missing tracking, synthetic data, or cached AI output as live eye measurements or live AI.
- Execute model-generated code, arbitrary tools, asset URLs, or scene scripts. Apply only validated, allowlisted scene settings.
- Replace the working offline demo with an untested live-generation dependency.

## Done
Acceptance criteria met; appropriate checks recorded; data/AI provenance visible; offline demo still works; a human teammate approves before merge.
