# Unity/backend runtime implementation and test plan

> Historical implementation/test checkpoint. The current product objective is an eye-tracking report with optional scene participation; the old scene-handoff acceptance below does not establish normal report-only completion. See [objectives and current implementation gaps](OBJECTIVES.md).

Scope: the current Assessment scene on the local `feat/unity-backend-runtime` branch. Preserve its synthetic source, HTTP mode, 20-second baseline/task, scene assets and GUIDs.

## Acceptance criteria

1. Exercise check-in -> native UnityWebRequest -> FastAPI -> validated response -> summary/config preview. Cover success, matching/missing/wrong bearer token, backend unavailable and invalid response. Use the actual Assessment scene settings; tests that shorten durations must say so. Editor/headset results require those runtimes; portable C# checks are separate evidence.
2. Configure the existing local Gemini key without printing it. Use `gemini-3.1-flash-lite`, verified text rates ($0.25/$1.50 per million input/output tokens), and at most one synthetic generation with a $0.002 reservation. Keep participant sessions local. Do not change billing/provider limits. Record generation, cache, privacy exclusion and token usage. No live calls in CI.
3. Return an allowlisted `X-VR-Fallback-Reason` header for backend fallbacks. Keep response JSON v1 compatible. Unity consumes only known codes, distinguishes HTTP/auth/validation/transport failures, and presents safe wording. Never echo exceptions, prompts, personal payloads or secrets.
4. Add optional request `cloud_policy: local_only | synthetic_demo`, defaulting to `local_only` for old clients. `AnalyzeRequest.Create` always uses local-only for interactive check-ins, even with synthetic eyes. Only wholly invented test scenarios may opt into `synthetic_demo`; live and recorded eyes remain blocked. Enforce the gate in the service and adapter. Update both clients, schema and fixtures together.
5. Pin Python 3.12 and add a least-privilege GitHub Actions workflow that installs the locked backend dependencies and runs pytest, Ruff and shared contracts without credentials.

## Test matrix

| Boundary | Checks |
| --- | --- |
| Python | Offline response; all reason codes; failures/cache/limits; concurrent request isolation; legacy/local-only and synthetic eligibility; real-data exclusion; strict schemas |
| C# Core | Cloud-policy serialization/default/validation; source labels; response guard; existing local rules |
| HTTP | Real loopback FastAPI server, auth 200/401, malformed request 422, bounded diagnostics and JSON compatibility |
| Unity PlayMode | Current Assessment configuration, actual HTTP client, rendered labels, safe scene handoff, wrong token, refused connection, invalid response |
| Gemini | One authorized synthetic attempt, five-minute cache hit, real and participant-check-in exclusion, bounded usage |
| CI | Python pin/lock install, pytest, lint and fixture checks; no cloud calls |

## Execution record

The user clarified that Unity runs on a teammate's laptop. No local Editor install was started or resumed. The teammate's stated URL is `http://127.0.0.1:8000`; that reaches this backend only if it runs on the same laptop or a tunnel forwards the port. For direct cross-laptop access, use a reachable backend-laptop IP, bind to `0.0.0.0`, and provision the separate runtime bearer token. No machine-specific IP was saved in Unity assets.

Implemented:

- Request-local diagnostics returned as an allowlisted HTTP header, consumed as approved wording by Unity. JSON responses retain their v1 shape; no shared last-error state exists.
- Optional additive cloud policy in JSON Schema, Pydantic and C#. The request fixture and interactive request builder use `local_only`. Explicit invented scenarios exercise `synthetic_demo`. Service and adapter both block implicit cloud use.
- Native Unity PlayMode tests for a full backend session with saved scene durations, matching/missing/wrong token and semantic 422 handling. Separate tests cover rejected provider JSON, safe header wording and the existing refused-connection flow. Backend-dependent Editor tests require opt-in and are ignored otherwise.
- Credential-free backend/contract CI; root Python 3.12 pin; refreshed lockfile and local Python 3.12.13 environment. Dependency versions were retained; obsolete Python 3.11 wheel entries were removed by uv.
- `scripts.probe_backend`: a real-HTTP synthetic receiver probe, forced local-only, with runtime token support and safe output.
- Existing ignored `.env` now contains the selected model and verified rates. Its API key was preserved and never printed. Persistent generation remains disabled with budget zero; the authorized smoke enabled generation only in its own process.

Verification on October 24, 2026:

| Check | Result |
| --- | --- |
| Locked dependency install on Python 3.12.13 | Passed |
| `python -m pytest -q` | 120 passed, including actual loopback sockets, auth and receiver CLI; one dependency deprecation warning |
| Ruff and shared schema/fixture validation | Passed |
| Portable C# Core/Capture/Metrics and EditMode logic | 110/110 passed with Roslyn/NUnit/.NET 9; only Unity Application paths shimmed. This is not an Editor run. A Newtonsoft assembly-version compatibility warning was emitted during compilation. |
| Actual current scene configuration -> C# request -> real HTTP backend -> C# response guard | Passed: synthetic/HTTP, saved URL `127.0.0.1:8000`, baseline 20 s, task 20 s; HTTP 200 and `local_only` header; task duration 20,000 ms, one baseline window, unavailable indicator; accepted scene exactly matched C# local fallback. Synthetic capture advanced using simulated timestamps, not real-time Editor capture. Native UnityWebRequest was not exercised. |
| One authorized live Gemini attempt | Passed on `gemini-3.1-flash-lite`: reserved at most $0.002, 374 prompt + 256 candidate tokens (630 reported total); no provider error. Cache/session rebinding, real-data exclusion, participant-check-in exclusion, preference overrides and response schemas all passed. No second generation was run. |
| Source/Git checks | No scene or build-settings changes; existing GUIDs preserved; no configured API key in any changed/new unignored file; changes left uncommitted on the local branch. |
| Hosted GitHub Actions | Workflow prepared; not run on GitHub because nothing was pushed. |
| Teammate Editor and headset | Pending: teammate runtime unavailable from this machine. Native transport, UI assertions, headset reachability and visuals remain unverified. Use the cross-laptop instructions in `backend/README.md`; do not report these as passed. |

The temporary localhost receiver used for verification was stopped after testing. A direct cross-laptop receiver was not opened without its configured runtime token and a confirmed reachable route.

Pricing/model references checked October 24, 2026: [pricing](https://ai.google.dev/gemini-api/docs/pricing#gemini-3.1-flash-lite), [model and structured output support](https://ai.google.dev/gemini-api/docs/models/gemini-3.1-flash-lite).
