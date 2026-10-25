# Unity/backend integration check — October 24, 2026

> This is a historical verification record of the scene-oriented flow. The core eye-tracking report and optional relaxation objective is in [OBJECTIVES](OBJECTIVES.md); successful report-only completion remains a separate implementation gap, not a result of these checks.

Integrated Alexandr's Unity app-flow changes from PR #5 (`origin/main` at `4665da0`) with the FastAPI backend on `dev`. The original `feat/app-flow` tip (`cafd696`) has the same tree as that squash commit. Merge conflicts in the root README and the contract README/fixture validator were resolved by retaining both the backend setup and Unity's new request contract and checks.

## Reconciled boundaries

- The request accepts `preferences: null` when the participant lets the app choose. Non-null free text is rejected, including empty strings; Unity computes safety locally before removing text.
- `HttpAnalysisClient` supports the separate runtime backend bearer token via a constructor argument or `VR_BACKEND_ACCESS_TOKEN`. No scene/prefab stores it; the Gemini key stays backend-only.
- Both local mappings use the same forest preset and CONCEPT table order, then explicit preferences. `want_quiet` means volume 0.2, explicit silence zeros every layer, and stop-session settings cannot be reenabled by preference sanitization. The flow blocks scene entry for `stop_session`.
- The Unity stub's `duration_ms` now describes the task, with baseline duration separate and rounded quality durations summing exactly to task duration. Previously it added baseline time while quality covered only the task, causing backend 422 responses.
- Missing measurements no longer produce positive observations or `within_baseline` in the C# fallback. Python/C# observation ordering and duplicate speed codes are aligned.
- Unity scenes, prefabs, package pins, project settings and all imported `.meta` GUIDs are preserved.

## Verification

- Python: 96 pytest cases, Ruff and all shared schema/fixture checks passed. Includes the actual Unity request fixture, null preferences, authenticated requests, D1 text rejection, mocked Gemini/cache behavior and seven shared scene-rule cases.
- Portable C#: compiled Core/Capture/Metrics plus the EditMode NUnit logic tests using Roslyn 4.14, Newtonsoft.Json 13.0.3, NUnit/NUnitLite 3.14 and the available .NET 9 runtime. Only `UnityEngine.Application` file paths were shimmed. **109/109 passed**. This was not a Unity Editor test run.
- Cross-language wire checks: actual C# `SyntheticEyeProvider` -> `StubMetricsEngine` -> `AnalyzeRequest.Create` produced a 20,000 ms task with a 120,000 ms baseline. Seven requests ran through FastAPI's in-process HTTP test client, then back through C# `ContractJson` and `AnalysisResponseGuard`. All returned 200, all responses were accepted, and complete Python/C# local scene configurations matched within numeric tolerance.
- Source/meta inspection and merge/whitespace checks passed. No real eye data or live/paid Gemini requests were used.

## Remaining runtime checks

Unity Hub reports no installed editor on this machine. The original Unity PlayMode smoke test and the new native HTTP-header tests were not executed in Unity; neither were scene visuals, Android builds or headset connectivity. Install the pinned editor and run EditMode/PlayMode as described in [SETUP.md](SETUP.md), then run a desktop Unity session against the backend with and without its access token, and repeat with the backend/network unavailable. The forest, real metrics and Quest adapter remain their owners' separate work.
