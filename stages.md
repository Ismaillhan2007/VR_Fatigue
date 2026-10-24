# Development stages for the HackTX MVP

Build one complete desktop experience first, then connect real eye tracking when a compatible headset is available. The first milestone is:

**Start a sample session -> calculate metrics -> show a summary -> enter a personalized forest.**

This is the team's starting plan, not a claim that these features already exist. Detailed architecture and acceptance criteria live in [docs/PROJECT_PLAN.md](docs/PROJECT_PLAN.md); collaboration rules live in [CONTRIBUTING.md](CONTRIBUTING.md).

## 1. Define the MVP

Ship five connected features:

1. **Assessment screen:** start a short session, show progress, and identify live or synthetic input.
2. **Deterministic measurements:** calculate session duration, tracking coverage, and gaze movement statistics in code.
3. **Gemini explanation:** explain supplied observations and limitations in an experimental wellness summary. Do not claim validated fatigue detection or medical diagnosis.
4. **Personalization controls:** squirrels on/off, daylight/sunset, ambient sound/silence, and gentle/no animal movement.
5. **One working forest:** apply supported settings to existing objects, lighting, and audio. Keep a saved configuration and local report fallback available if Gemini fails.

Defer additional environments, accounts, databases, PDF reports, and runtime world generation until this flow works. Gemini's useful role is explaining observations and translating preferences into supported scene settings.

## 2. Use a small, shared stack

| Part | Recommendation | Reason |
| --- | --- | --- |
| Application | Unity LTS + C# + URP | One project for desktop development and eventual VR |
| VR interaction | OpenXR + XR Interaction Toolkit | Separate headset integration from application logic |
| Assessment logic | Plain C# classes | Test with recorded or synthetic input |
| Backend | Python + FastAPI + Pydantic | Small API with explicit request/response validation |
| AI | Google Gen AI SDK; configurable Flash-family model | One bounded request for an explanation and scene settings |
| Communication | JSON through UnityWebRequest | Send one session summary rather than streaming frames |
| Storage | Memory and synthetic JSON fixtures | Enough for the demo without a database |

Agree on the same Unity editor and compatible package versions before splitting work. Keep the Gemini key on the backend. Select an available stable model after checking its structured-output support, price, and project quota.

Ask Gemini for structured JSON, validate the returned values, and apply only allowlisted scene settings. Develop with mocks first. Bound live API calls, cache suitable nonpersonal responses, and retain the offline fallback. Keep real eye data local in the MVP; use synthetic, nonpersonal payloads for free-tier AI demos.

## 3. Develop without a headset

Develop navigation and eye data separately. Unity's XR Interaction Simulator lets the team exercise headset/controller interactions with keyboard and mouse. Import the compatible simulator and Starter Assets samples for the installed XR Interaction Toolkit version; use the sample's configured XR Origin.

The interaction simulator does not establish that eye tracking works. Put eye input behind a replaceable interface:

```text
IEyeTrackingProvider
  ReplayEyeProvider    -> synthetic samples now
  HardwareEyeProvider  -> actual headset samples later
                         |
                         v
                   same metrics code
```

Create three synthetic fixtures: steady gaze, changing gaze, and tracking gaps. Use them to verify calculations and missing-data handling; they are not evidence of real fatigue. Mouse input may help debug interactions but must remain labeled as simulated input.

Keep input provenance and AI provenance separate: live/replay/synthetic data versus live/cached/fallback AI output. Missing signals stay unavailable rather than becoming invented measurements.

When a headset is available, verify eye-data access, permissions, calibration, frame timing, and comfort. OpenXR gaze-pose support does not guarantee blink, pupil, or raw eye-image access. Do not count head gaze or tracking loss as eye movements or blinks.

## 4. Divide responsibilities between four people

| Person | Owns | First deliverable | Suggested feature branch |
| --- | --- | --- | --- |
| 1: Unity integration | Project setup, assessment/report UI, session flow, backend connection; later headset integration | Start button -> sample summary -> forest transition | `feat/app-flow` |
| 2: Signals and metrics | Replay provider, synthetic fixtures, calculations, data-quality handling, tests | A sample session produces repeatable metrics | `feat/replay-metrics` |
| 3: Backend and Gemini | FastAPI, request/response validation, Gemini prompt, timeout/cache/fallback | Endpoint returns a valid mocked report and scene configuration | `feat/gemini-backend` |
| 4: Environment and experience | Forest scene, squirrel prefab, audio, lighting, preference application, demo capture | Forest works locally and changes through a configuration object | `feat/forest-scene` |

Person 1 owns the assessment scene; person 4 owns the forest scene. Avoid simultaneous edits to the same Unity scene or shared prefab. Each person reviews another person's PR. Split work by deliverables and files so nobody waits for an entire subsystem to be finished.

Before coding independently, agree on example request/response JSON and the minimum shared contracts: `SessionSummary`, `Preferences`, `AnalysisResponse`, and `SceneConfig`. Person 1 connects these boundaries; person 2 supplies metrics, person 3 supplies the explanation/config response, and person 4 consumes the scene configuration. Mocks must use the same shape as the eventual live implementation.

## 5. Work in stages and integrate early

| Stage | Target | Exit condition |
| --- | --- | --- |
| First 30 minutes | Agree on one Unity version, create the project, commit example request/response JSON, assign owners | Everyone can work against the same examples and package versions |
| Next two hours | Work in parallel with synthetic metrics, a mocked backend, and placeholder forest objects | Each person's first deliverable runs independently |
| First integration checkpoint | Connect session -> metrics -> report -> forest on desktop | One complete flow works, even with a ground plane and a few trees |
| After integration | Connect Gemini, improve the forest, add hardware tracking when available, test failure paths | Live improvements preserve the working offline flow |
| Final two hours before submission | Freeze features, test offline behavior, record a backup, rehearse | Reproducible demo build, honest source labels, and a prepared pitch |

Treat these as targets and adjust to the actual time remaining. Confirm headset availability early without blocking the desktop implementation. If tracking access remains blocked after an hour of investigation, finish replay integration and keep the hardware adapter as a separate task.

## 6. Verify the demo before adding stretch features

- Complete the full desktop flow from session start to the forest.
- Confirm the metrics are repeatable and tracking gaps produce an insufficient-data result when appropriate.
- Change each preference and verify the corresponding scene behavior.
- Disable the network and confirm a usable local report and saved scene configuration.
- Test invalid/blocked Gemini output, a missing key, timeout, and rate-limit handling through mocks.
- Run a headset check when hardware becomes available; identify anything still unverified.
- Save a local build and a short backup recording. Tell judges when input is synthetic or a report uses fallback output.

The MVP is complete when this end-to-end experience works reliably. A single demo participant does not establish health improvement or fatigue-detection accuracy.

## References

- [Unity XR Interaction Simulator](https://docs.unity.cn/Packages/com.unity.xr.interaction.toolkit%403.1/manual/xr-interaction-simulator-overview.html): example documentation; match setup instructions to the team's installed package version.
- [Unity OpenXR eye-gaze interface](https://docs.unity3d.com/Packages/com.unity.xr.openxr@1.13/manual/features/eyegazeinteraction.html): gaze-pose support and bindings.
- [Gemini structured outputs](https://ai.google.dev/gemini-api/docs/structured-output): schema-based output and application validation.
- [Gemini API terms](https://ai.google.dev/gemini-api/terms): data handling and medical-use restrictions.
