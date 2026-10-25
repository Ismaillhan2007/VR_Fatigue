# Backend: FastAPI + Gemini

This is a separate Python app inside the shared repository. Unity remains a separate C# app. They exchange HTTP requests and JSON; neither app needs to run the other's language. No database is required.

The backend supports an **eye-tracking app** whose core outcome is a health/fatigue report presented as an experimental wellness summary. A scene is one optional relaxation use case. V1 currently bundles the report with required `scene_config` and `scene_rationale` fields, even when `preferences` is null; null means no overrides, not a participation decision. A returned config must not imply consent to enter a forest. The current Unity flow still lacks normal report-only completion; see [objectives and implementation gaps](../docs/OBJECTIVES.md). This docs change preserves the wire contract; making scene output optional requires coordinated schema/client/fixture changes.

```text
Unity / C#                         FastAPI / Python                   Gemini
capture + metrics + check-in  ->   validate aggregates + choices  ->  select codes/settings
report + forest scene        <-   validated AnalysisResponse     <-  structured JSON
```

The backend is offline by default. It returns a deterministic explanation and scene configuration immediately, so Unity integration can start without an API key or a headset. An optional Gemini adapter can select approved explanation codes and scene settings from synthetic input. The server renders the explanation from vetted text rather than accepting unrestricted medical prose.

## Run locally

Prerequisites: Python 3.12 (pinned in the root `.python-version`) and [uv](https://docs.astral.sh/uv/). From the repository root:

```sh
cd backend
uv sync --extra dev
uv run uvicorn app.main:app --host 127.0.0.1 --port 8000 --reload
```

Open [interactive API docs](http://127.0.0.1:8000/docs), [OpenAPI JSON](http://127.0.0.1:8000/openapi.json), or [health](http://127.0.0.1:8000/health).

Use `uv sync --locked --extra dev` for reproducible installs after the lockfile exists. The app reads `backend/.env` when present. `.env.example` is safe to copy; do not commit `.env` or paste a key into Unity.

## Endpoints and the Unity boundary

| Endpoint | Purpose |
| --- | --- |
| `GET /health` | Local service status and whether Gemini configuration is complete; does not call Google |
| `POST /api/v1/analyze` | Validate a session summary, preserve its provenance, return a report and scene settings |
| `GET /docs` | Interactive request/response documentation generated from the actual Pydantic models |

The analysis request wraps the existing shared contracts:

```json
{
  "schema_version": "1.0",
  "session_summary": { "...": "complete SessionSummary from contracts/v1" },
  "preferences": { "...": "complete Preferences from contracts/v1" },
  "reconciliation": "agree_high",
  "safety_notice": "none"
}
```

That snippet illustrates the envelope, not a valid complete request. Generate a working synthetic request from the shared fixtures:

```sh
uv run python scripts/demo_request.py > /tmp/vr-fatigue-request.json
curl -sS http://127.0.0.1:8000/api/v1/analyze \
  -H 'Content-Type: application/json' \
  --data-binary @/tmp/vr-fatigue-request.json
```

Unity sends the same JSON with `UnityWebRequest` and `Content-Type: application/json`, then deserializes the returned `AnalysisResponse`. Apply only `scene_config`; show `data_source`, `replay_origin`, and `ai_source` separately. When `safety_notice` is `stop_session`, show the exit/take-a-break UI instead of automatically entering the forest. On HTTP/network failure, Unity must still use its own local report/preset.

The additive v1 request field `cloud_policy` defaults to `local_only`. `AnalyzeRequest.Create` always uses that value for interactive check-ins, even when the eye source is synthetic. Only an invented test scenario may explicitly use `synthetic_demo`; its self-report and preferences must also be invented. Both the service and Gemini prompt builder enforce the gate. An omitted field is safe for older clients. Upgrade the backend first because older backends reject unknown request fields.

Fallbacks keep the existing response JSON and return the optional `X-VR-Fallback-Reason` header. Its finite codes are `local_only`, `non_synthetic_input`, `safety_stop`, `insufficient_data`, `gemini_disabled`, `gemini_not_configured`, `attempt_limit`, `budget_limit`, `timeout`, `provider_rate_limited`, `provider_unavailable`, `provider_error`, and `invalid_output`. Live/cached responses omit the header. Unity maps only known codes to approved wording; an absent/unknown header means “backend local rules.” HTTP 401/422 and connection failures are separate client diagnostics. No exception bodies or personal input appear in these headers.

Unity owns capture, baseline/metrics/indicator calculation, deterministic reconciliation and local discomfort rules. It computes `safety_notice` from the full local self-report before removing free text. The HTTP request must send `self_report.free_text: null` and `preferences.text: null`; the backend rejects non-null text, including empty strings. `preferences` itself may be null when the participant lets the app choose. The backend preserves reconciliation and the supplied safety notice; it does not recompute eye movements or decide whether a person is fatigued.

An observation requires both a non-null measurement and its non-null deviation for a supported metric. Missing values are never treated as zero. If no supported pairs exist, even with `quality.status: usable`, the backend returns HTTP 200 with `data_quality_limited` and a local report/scene, without a Gemini call, attempt reservation, or cache entry. Partial comparisons describe only available pairs. Supplied quality, provenance, and reconciliation remain unchanged; callers must still use an unavailable indicator/reconciliation when required by the request's semantic rules. A missing or short baseline retains the more specific `no_baseline` observation.

## Compatibility with the current app

The backend follows the shared `contracts/v1/analyze_request.schema.json` and `analysis_response.schema.json` boundary. Unity's DTOs and HTTP client live under `unity/Assets/App/`. The local report version is `local-0.3`; existing response fields and enums are unchanged. Request preferences are nullable, and request free text must stay null to match Unity's D1 policy.

| App part | Current backend boundary | Verification / remaining dependency |
| --- | --- | --- |
| Capture and metrics | Accepts aggregate `SessionSummary`; leaves measurements, quality and reconciliation under Unity ownership. | `MetricsEngineFactory` uses `SignalsMetricsEngine`. The default synthetic replay has a 130 s baseline, 60 s task and probes. Its C# request passed real HTTP and response-guard checks; valid + gap durations sum to task duration. Hardware capture remains separate acceptance work. |
| Report UI | Returns approved observations, narrative, limitations, safety notice and independent data/AI source labels. | Responses checked against the shared JSON Schema; UI rendering untested. Keep the original summary for numeric values and join by `session_id`. |
| Forest engine | Returns bounded `SceneConfig`; reapplies mute, animal-free and motion-free restrictions. | Config validated through the response schema and API tests; Unity asset application unavailable. |
| Gemini | Optional synthetic-only enhancement; real and recorded inputs stay on the local path. | Mocked SDK/failure tests plus a successful manual `gemini-3.1-flash-lite` generation/cache check; other models unverified. |
| Runtime | Separate local Python service with in-memory cache/counters. | Quest Pro LAN reachability and the client's own network-failure fallback remain untested. |

Compatibility checks cover the shared request fixture, null/explicit preferences, text rejection, authenticated requests, mocked Gemini/cache responses, and scene-rule cases shared by Python and C#. Pydantic adds semantic checks beyond the draft schemas (for example, baseline/probe requirements and duration totals), so schema-valid inputs may still receive 422. A real Unity editor-to-backend session, scene application and headset checks still require their respective runtimes. Shared-token authentication is available for the local demo; production access control, rate limiting and durable multi-worker spending limits remain public-hosting work.

## Connect other teammates and the headset

- Same laptop: Unity calls `http://127.0.0.1:8000`.
- Another laptop or standalone Quest Pro: run Uvicorn with `--host 0.0.0.0`, then use `http://<backend-laptop-LAN-IP>:8000` in Unity. `0.0.0.0` is a bind address, not the URL to enter on the headset.
- Both devices need a reachable local network; venue Wi-Fi may isolate clients. A team hotspot may be easier. Configure the local firewall and development-only HTTP settings as described in [the hardware guide](../docs/HARDWARE_QUEST_PRO.md).
- Native Unity HTTP requests do not need browser CORS middleware. A future browser/WebGL client will need an explicit allowed-origin configuration.
- Configure `VR_BACKEND_ACCESS_TOKEN` with a separate random secret before sharing the backend on a LAN; clients then send `Authorization: Bearer <token>`. Never reuse the Google API key. Empty/unset preserves the local offline starter's unauthenticated behavior. `/health` and API documentation remain public but do not expose either secret. A bearer token over local HTTP is a development convenience; public deployment requires HTTPS and further controls.
- Unity's `HttpAnalysisClient` accepts the token as a runtime constructor argument, or reads `VR_BACKEND_ACCESS_TOKEN` from its process environment. Set the same demo token for the backend and desktop Unity process. It is not a serialized scene/prefab field, is not logged, and is never sent to Google. Runtime provisioning for a standalone headset is separate deployment work.

## Security boundaries

- Request bodies are limited to 16 KiB before JSON parsing (`VR_MAX_REQUEST_BYTES`, bounded to 4-64 KiB), including chunked requests and dishonest `Content-Length`. Oversized bodies return 413; malformed lengths return 400.
- Requests reject unknown/executable fields, unsupported enums, duplicate chips/particles, overlong text, nonfinite numbers and invalid provenance. Validation errors omit input values and redact unknown field names.
- Model JSON is untrusted: strict Pydantic validation still runs after generation, then observation allowlists and participant overrides run again. No URLs, scripts, downloads or model-selected tools are executed. Invalid provider output and SDK errors return a local report without exposing provider error bodies.
- The cloud deadline includes time waiting for the generation lock. Timed-out waiters do not reserve attempts or spend budget. Counters remain process-local, not a global billing guarantee.
- Keep `.env` ignored and owner-readable only (for example, `chmod 600 .env`); do not enable HTTP/body debug logging. Restrict the key to the Gemini API in [AI Studio](https://ai.google.dev/gemini-api/docs/api-key#restricting-and-securing-your-keys), and apply source-IP restrictions where practical. Provider restrictions are not configured by this code.
- Before public hosting, add deployment access control, rate/request limits, TLS, durable shared counters and monitoring. A caller's `synthetic` label is a declaration, not proof; keep the service limited to trusted demo clients and keep real data on the local path.

## Optional Gemini mode

1. Copy `.env.example` to `.env` locally.
2. Set `GEMINI_API_KEY` and a supported `GEMINI_MODEL` without committing the values. `gemini-3.1-flash-lite` passed the manual synthetic smoke check below. The adapter uses `generate_content` with `response_json_schema`, converts singleton `const` values to enums, and validates the result against the original strict models. Gemini 3 uses minimal thinking; legacy models use `thinking_budget=0`. An incompatible model fails back locally.
3. Set verified input/output prices per million tokens. For verified unpaid quota, explicitly set both rates to `0`. For paid calls, set a positive `VR_GEMINI_BUDGET_USD` within the team's authorized budget.
4. Set a separate backend access token before LAN sharing, then set `VR_GEMINI_ENABLED=true` and restart the server. The manual smoke check below can verify the key/model without changing the saved configuration.

Only `synthetic` sessions or replay explicitly marked `synthetic`, with `cloud_policy: synthetic_demo`, can reach Gemini. Real/live and recorded participant sessions always use the local response. Interactive participant sliders/chips also stay local, even with synthetic eyes. Free text, preferences text, session IDs, and raw streams are excluded from the cloud prompt. A synthetic declaration is for trusted test clients, not proof of nonpersonal data. Do not label participant data synthetic to bypass this boundary.

One analysis attempt performs a token-count request followed by at most one generation request. The token-count check includes the prompt and schema text conservatively. The generation has a 1,000-token output/thinking cap, SDK retries disabled, and a deadline of at most 15 seconds including lock wait. Input counts above 2,000 skip generation. Empty, blocked, invalid, timed-out, unsupported, or rate-limited output falls back locally. The adapter retains only numeric token-usage metadata for inspection, not provider response text or prompts. Validate the chosen model's accounting and pricing before paid use.

The service reserves worst-case configured token cost before each attempt and never refunds a failed attempt. Attempts are capped per UTC day, and total reserved dollars are capped for the process lifetime. **Counters/cache are in memory and reset on restart**; run one worker for the demo. This is not a durable or multi-worker spending limit. Keep provider-side limits as an additional control.

Successful synthetic results have a bounded five-minute cache; cached responses get the current request's session ID and `ai_source: cached`. Missing configuration, exhausted limits, real input, insufficient data, or explicit discomfort uses `ai_source: fallback` with `model_id: null`.

## Scene precedence

Python and Unity start from the same saved forest preset and apply CONCEPT 7.3 chip rows in order (later rows win), then apply non-null explicit preferences. `want_quiet` lowers volume to 0.2; explicit `sound: silent` zeros the master and every layer. `tired_eyes` sets motion speed to zero; a later `want_movement` chip may set it to 0.3, while an explicit `motion: none` always sets it back to zero. `overstimulated` disables animals/particles and leaves the pad layer; explicit animal choices still win. A `stop_session` notice finally disables motion, animals, particles and the orb regardless of preferences; Unity blocks scene entry. These are deterministic demo choices, not health-benefit claims. Shared cases in `fixtures/integration/scene_rules.json` exercise the same rules in both test suites.

## Checks and ownership

```sh
uv run pytest
uv run ruff check app tests scripts
uv run python ../contracts/validate_examples.py
```

Tests cover HTTP validation, compatibility with shared response schemas, offline mapping, real-data cloud exclusion, source labels, caching, safety notices, timeout/error fallbacks, and spending/attempt cutoffs. All provider behavior is mocked. No test requires a headset, a real key, or paid calls.

Security tests also cover authorization, oversized/chunked bodies, malformed headers, private validation errors, malicious model settings, actual SDK error types, and queued-request deadlines.

Manual live check (requires `--extra dev`, a local key, verified prices and an authorized budget; never runs in pytest):

```sh
uv run python -m scripts.smoke_gemini --model gemini-3.1-flash-lite \
  --input-price 0.25 --output-price 1.50 --budget-usd 0.002
```

This makes at most one synthetic attempt, checks a cache hit and real-data cloud exclusion, validates all responses against the shared contract, and prints only checks, numeric usage and sanitized error codes. It temporarily enables Gemini in that process; it does not edit `.env`. Repeated runs restart process-local counters, so do not treat the command's budget as cumulative across invocations. Rates above were checked against [Google's pricing](https://ai.google.dev/gemini-api/docs/pricing#gemini-3.1-flash-lite) on October 24, 2026; recheck before reuse.

The successful October 24 check used 375 prompt tokens, 265 candidate tokens and 640 total tokens, with no reported thinking-token count. Live output, caching/session rebinding, real-data exclusion, source labels, shared schemas and preference overrides passed. Earlier probes exposed and led to fixes for legacy SDK schema encoding and singleton literal enforcement. Including a sandbox-blocked connectivity attempt, six smoke attempts across separate processes reserved at most $0.0092 in total against a human-authorized $0.01 ceiling; this is a conservative bound, not a billing statement. The saved local configuration remained disabled.

The same review checked 44 installed Python packages against the public OSV database with no reported advisories, and found no occurrence of the configured key in versioned/unignored files or Git history. This is a point-in-time check of known advisories and the current key, not a penetration-test certification or a scan for every possible credential.

- `app/main.py`: app creation, endpoints, safe validation errors.
- `app/models.py`: typed HTTP and shared-contract models; stricter semantic validation than the draft JSON schemas.
- `app/fallback.py`: deterministic observations, scene mapping, and response text.
- `app/gemini.py`: the only Google SDK integration; bounded structured output.
- `app/service.py`: eligibility, provenance, deadline, budget reservations, and cache.
- `app/config.py`: environment settings and secret handling.

The backend branch includes the draft `feat/contracts-v1` dependency. Review/merge that shared-contract PR before merging backend work into `main`, and keep Pydantic models aligned with contract changes. No changes to Unity scenes are needed to work on this app.

## Remaining backend work

1. Review the shared-contract dependency with the team; keep the Pydantic models, Unity DTOs, and fixtures aligned as the contracts change.
2. Use the pinned Python 3.12 with `uv sync --locked --extra dev`; keep the lockfile current when dependencies change.
3. Repeat the manual smoke check when changing the key, model, SDK or pricing. The initial synthetic live check passed on `gemini-3.1-flash-lite`; it does not establish other models' compatibility or production reliability.
4. Confirm which self-report data, if any, may leave the device before expanding the synthetic-only cloud boundary. Keep real eye data on the local path until that decision is explicit.
5. Integrate a real Unity request/response, then verify the standalone Quest Pro can reach the backend over the demo network. Check source labels, explicit preference overrides, and Unity's own network-failure fallback.
6. The backend/contract CI workflow is in `.github/workflows/backend.yml` and never calls Gemini. For any later public deployment, implement authentication, request/rate limits, and durable shared spending counters first; current counters are process-local.

## Cross-laptop receiver and Unity tests

`127.0.0.1` always means the machine running the client. A teammate's Unity can use that URL only if the backend also runs there or a tunnel forwards the port. When Python runs on a different laptop, set Unity's `SessionController.BackendBaseUrl` (or `-backendUrl`) to `http://<backend-laptop-reachable-IP>:8000` and bind Uvicorn to `0.0.0.0`. Confirm the two laptops can reach each other; venue Wi-Fi or VPN routing can prevent this. Never enter `0.0.0.0` as the Unity destination.

Before LAN sharing, configure a separate `VR_BACKEND_ACCESS_TOKEN` in `backend/.env` and give the same token to the trusted Unity process through its environment. Restart both processes after changing settings. Do not share `GEMINI_API_KEY` or the complete `.env` file. No token belongs in a scene or prefab.

Stop the existing backend with Ctrl+C in its original terminal before restarting with `uv run uvicorn app.main:app --host 0.0.0.0 --port 8000 --access-log`. `[Errno 48] address already in use` means an earlier process still owns the port; activating a virtual environment does not free it. On macOS, `lsof -nP -iTCP:8000 -sTCP:LISTEN` identifies the listener. A reloader can have a parent/child pair sharing one socket; stop the original server command rather than starting another instance.

### Launch Unity with the shared token

Get the token from the backend owner's `backend/.env`: copy only the value of `VR_BACKEND_ACCESS_TOKEN`. It is generated locally, not obtained from Google. `GEMINI_API_KEY` stays on the backend. The [root README](../README.md#where-to-get-the-token) includes fresh-setup token creation.

Unity must inherit the token when its process starts. Quit the existing Editor and launch the Editor binary directly from the terminal used below. Replace the project path (and Editor install path if different).

**macOS (zsh/bash):** run this command by itself:

```sh
read -r -s VR_BACKEND_ACCESS_TOKEN
```

Paste the shared backend token, then press Enter. Input is hidden. Then run:

```sh
export VR_BACKEND_ACCESS_TOKEN
/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity -projectPath "/path/to/VR_Fatigue/unity"
```

**Windows (PowerShell):** run the first line, paste the token at the hidden prompt, then run the remaining lines:

```powershell
$backendTokenInput = Read-Host "Backend token" -AsSecureString
$env:VR_BACKEND_ACCESS_TOKEN = [System.Net.NetworkCredential]::new("", $backendTokenInput).Password
& "C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe" -projectPath "C:\path\to\VR_Fatigue\unity"
```

A Hub process that was already running may have an older environment; launching the Editor binary directly avoids that ambiguity. These commands do not put the token in the Editor's command-line arguments or print it. Authentication failures return 401 and Unity uses its local fallback, so confirm the server's POST status rather than relying only on scene entry.

With the receiver running, this cloud-disabled synthetic probe checks real HTTP, provenance, the fallback header, 422 validation, and 401 authentication when a token is configured:

```sh
cd backend
uv run python -m scripts.probe_backend --base-url http://<backend-laptop-reachable-IP>:8000
```

On the teammate's Editor machine, set `VR_RUN_BACKEND_TESTS=1`, `VR_UNITY_BACKEND_TEST_URL` to the reachable URL, and `VR_BACKEND_ACCESS_TOKEN` to the matching demo token **before starting the Editor**. In Test Runner, run the `AssessmentFlowSmokeTest` PlayMode tests. The configured-scene test preserves the saved 130-second baseline / 60-second task replay at 4x speed (about 48 seconds plus check-in time); a separate test checks native HTTP authentication and 422 handling. Another shortened-session backend test uses `VR_SMOKE_BACKEND_URL` as its opt-in. The invalid-response and refused-connection tests use local test endpoints and require no cloud access. Without opt-in, backend-dependent tests are explicitly ignored, rather than reported as verified. These tests do not establish headset reachability or forest visuals.

See [the earlier implementation/test plan and evidence](../docs/BACKEND_RUNTIME_PLAN.md) for the 20/20-second stub-era checkpoint; it is a historical record. Current defaults are in [SETUP.md](../docs/SETUP.md). Keep machine-specific network addresses and credentials out of committed scenes/prefabs.

References: [FastAPI application structure](https://fastapi.tiangolo.com/tutorial/bigger-applications/), [FastAPI testing](https://fastapi.tiangolo.com/tutorial/testing/), [Google Gen AI SDK](https://github.com/googleapis/python-genai).
