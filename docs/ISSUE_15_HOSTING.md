# Issue 15: hosted backend configuration and success criteria

Scope: [issue #15](https://github.com/Ismaillhan2007/VR_Fatigue/issues/15), branch `feat/issue-15-hosted-backend`, based on `dev` at `bf96d63`. The core product is an eye-tracking assessment and experimental wellness report. Hosting removes the shared-Wi-Fi requirement; it still needs internet. Forest participation and Gemini are optional.

## Success criteria

| Criterion | Required evidence | Status |
| --- | --- | --- |
| Preserve current behavior | Baseline and final backend tests, Ruff and shared JSON contract checks pass | Passed: baseline 130, final 150 tests; Ruff and contracts passed |
| Reproducible isolated package | Python 3.12 image installs `uv.lock` with `--locked`, excludes local `.env`, fixtures and dev packages, and runs as nonroot | Passed: local Docker build and packaged checks |
| Platform-compatible startup | Actual packaged server listens on `0.0.0.0` and a nondefault `PORT`; one worker; invalid ports refuse startup without echoing values | Passed: unit tests and packaged HTTP on internal port 8127 |
| Hosted mode fails closed | Missing/blank token refuses startup; missing/wrong bearer credentials yield 401; health remains public | Passed: unit and packaged startup/auth checks |
| Unity wire compatibility | Current synthetic Unity fixture receives 200 with matching provenance and safe fallback; invalid duration receives 422 | Passed: packaged HTTP receiver probe |
| Safe initial staging policy | Gemini disabled; declared live/recorded eye input rejected with 403; local participant path remains available | Passed: local unit/image checks; no provider calls |
| Ready-to-review configuration | Dockerfile, health command, CI smoke and host/rollback runbook committed; no secret in source/image/output | Local checks passed; human review/remote CI tracked in the PR |
| Real staging reachability | Authorized HTTPS deployment passes the probe from two distinct networks; URL, service/environment and observed deployment success recorded | Not deployed; requires deployment authorization and two-network evidence |
| Native Unity/Quest acceptance | Actual Unity HTTP client and headset use the hosted endpoint with runtime credentials | Not run; separate issue #20 and device work |

Do not close issue #15 on local/image checks alone. The two-network staging criterion remains open until observed. Report-only UI completion is tracked in #16 and is not implemented here.

## Current Unity/backend boundary

`HttpAnalysisClient` already accepts an arbitrary base URL and an injected token (or desktop `VR_BACKEND_ACCESS_TOKEN`), sends JSON to `/api/v1/analyze`, and falls back locally on network/auth/contract errors. `SessionController` reads `-backendUrl`. The v1 request/response schemas are unchanged by this work. Tests use the committed synthetic Unity request fixture; they do not execute the Unity engine.

For desktop HTTP integration, use the hosted HTTPS origin as the backend URL and provide the matching bearer token privately to the Editor process. Never use `0.0.0.0` as a client URL, disable TLS verification, embed a token in the URL, or put a secret in scene/prefab assets. A native Unity client does not need browser CORS configuration. A standalone Quest does not inherit the laptop's environment or command line: issue #20 owns its runtime provisioning UI and transfer-consent gate.

## Runtime modes and data handling

- **Local:** `VR_RUNTIME_MODE=local` (the ordinary Python default). Empty backend token preserves same-machine offline development. Existing real/recorded aggregates can use local deterministic rules without Gemini.
- **Hosted:** `VR_RUNTIME_MODE=hosted` (the image default). An empty/whitespace-only backend token prevents application startup. Authenticated analysis accepts only declared synthetic eyes or replay of synthetic eyes. Live/recorded eye input is rejected before analysis. This rejection happens after the HTTP body reaches the server; it is not a client-side privacy boundary.
- Staging is for trusted teammates sending **wholly invented** requests only, including ratings, chips and preferences. A synthetic eye label does not prove check-in content is invented. Do not point participant sessions at staging until the transport-consent/data-handling work in #20 is complete.
- `cloud_policy: local_only` prevents Gemini use. It does not prevent sending a request to a remote backend. A successful staging response with `ai_source: fallback` and `X-VR-Fallback-Reason: local_only` is expected and proves neither live AI nor on-device processing.
- Keep `VR_GEMINI_ENABLED=false` for this rollout. The service's cache/attempt/budget state is in memory; one Uvicorn worker and one service replica are required. Restart/redeploy resets state. These are not durable or account-wide spending limits, so do not enable paid generation as part of connectivity work.

## Build and validate locally

From the repository root:

```sh
docker build --tag vr-fatigue-hosted:issue15 ./backend
cd backend
uv sync --locked --extra dev
uv run --no-sync pytest -q
uv run --no-sync ruff check app tests scripts
uv run --no-sync python ../contracts/validate_examples.py
uv run --no-sync python -m scripts.check_hosted_image --image vr-fatigue-hosted:issue15
```

The image check generates a temporary token in memory, starts its own containers on loopback, and removes them afterward. It verifies missing-token startup refusal, a nondefault internal `PORT`, auth/200/401/422/provenance with the existing receiver probe, rejection of declared participant eye data, absence of dev/fixture/dotenv files, nonroot execution and the actual Docker health command. It prints only fixed check names and booleans. No provider calls or public deployment occur.

The build context is **`backend/`**. The allowlisted `.dockerignore` and explicit Docker COPY instructions omit `.env`, tests, scripts, `fixtures/`, `contracts/` and Unity assets from the image. Run receiver probes from a full repository checkout with dev dependencies; do not try to run repo-root-fixture scripts inside the runtime image.

## Railway staging runbook (execute after authorization)

1. Select the intended project and a **staging** environment. Record the service/environment identity privately; check for an existing backend before creating one. Keep automatic deployment of unrelated branches disabled while validating this branch.
2. Set the Git source to the reviewed branch/commit and service root to `/backend`. Railway detects `backend/Dockerfile` relative to that root. Keep its entry point (`python -m app.serve`); it binds the platform `PORT` and enforces one worker. Do not override it with a multi-worker command. No Unity or repo-root assets are required at runtime.
3. In the host secret store, set a new separate random `VR_BACKEND_ACCESS_TOKEN`. Never reuse `GEMINI_API_KEY`. Set `VR_RUNTIME_MODE=hosted` and `VR_GEMINI_ENABLED=false`; leave Gemini credentials/model unset and budget zero. Do not copy the developer's full `.env` into the host.
4. Configure health path `/health`, startup health timeout 60 seconds, **one replica**, and bounded on-failure restarts (at most three). The Docker HEALTHCHECK is useful locally; also configure the platform health path explicitly. These service settings must be checked during deployment, not inferred from Docker metadata.
5. Generate/select a platform HTTPS domain with its target port matching the server's platform `PORT`. Preserve certificate verification. Observe **SUCCESS** for the exact submitted deployment, then check `/health`. An upload/queued result is not deployment success.
6. From a checkout on each of two different networks, privately configure the same backend token and run:

```sh
cd backend
uv run --no-sync python -m scripts.probe_backend --base-url https://<staging-host>
```

7. Record safe check output, deployment id/commit, domain, both network contexts and UTC timestamps. The probe forces `local_only` and uses synthetic fixtures; it checks actual analyze/auth/schema behavior rather than health alone. Never paste the token, request payloads or provider credentials into an issue/PR.
8. Complete the desktop Unity run separately with its URL/runtime token. Clearly distinguish HTTP success/backend rules from Unity's client-side local fallback. Headset/runtime provisioning remains issue #20.

Railway's current guidance favors Infrastructure as Code over deprecated `railway.json`/`railway.toml`; this change adds neither a legacy manifest nor an unreviewed project-wide IaC apply. The Dockerfile plus explicit service settings above define this initial single-service rollout. Automate a reviewed project plan once the target project/environment is selected.

Sources: [Railway Docker builds](https://docs.railway.com/builds/dockerfiles), [health checks](https://docs.railway.com/deployments/healthchecks), [Infrastructure as Code](https://docs.railway.com/infrastructure-as-code), [uv Docker integration](https://docs.astral.sh/uv/guides/integration/docker/).

## Rollback and checks still requiring hardware/network access

Keep the previous successful deployment id before updating. If startup, auth or contract checks fail, keep the new deployment out of service and roll back to that known image/commit with the hosted token policy intact. If there is no prior staging version, stop the staging service and use Unity's local report fallback. Rotate an exposed token through the secret store and private client configuration; never put the replacement in source.

No pinned Unity editor is installed on this machine. Native Unity EditMode/PlayMode, Android build, Quest networking and device visuals cannot be certified by Python or Docker tests. There is also no two-network HTTPS evidence until a staging deployment is authorized and checked. Keep those results explicitly unrun.

## Execution record

- Before implementation: 130 backend pytest cases passed, Ruff passed, and 14 schemas/35 fixtures validated (7 deliberately invalid examples rejected). No cloud calls. The existing FastAPI TestClient emits a dependency deprecation warning.
- After implementation: **150 backend pytest cases passed**, including 20 new hosted/startup cases; Ruff passed for `app`, `tests` and `scripts`. Contract checks passed: **14 schemas, 35 fixtures, 7 rejected invalid examples**, no actual C# reports regenerated. The baseline dependency deprecation warning remains.
- Docker Desktop on local Linux/arm64: `vr-fatigue-hosted:issue15` built successfully using Python 3.12.13 and uv 0.11.16. All **7 packaged-image checks** passed: missing-token refusal, platform port/health, Unity-wire auth/status/provenance, participant-eye rejection, no token in responses/probe output, nonroot/no-dotenv/no-fixtures/no-dev packages, and the exact Docker health command. Temporary test containers were removed. CI repeats the build/smoke on its Linux runner.
- Documentation links/code fences, changed-file whitespace and the repository private-file scan passed; the scan returned only `backend/.env.example`.
- No public deployment or live Gemini call was made. Railway account discovery returned **Unauthorized**; staging project selection/sign-in and explicit public-deployment authorization are still needed. Native Unity, Quest and two-network HTTPS results remain unrun.
