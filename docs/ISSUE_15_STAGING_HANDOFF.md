# Hosted connectivity checkpoint — October 25, 2026

Issue #15, sole owner @Ismaillhan2007. This checkpoint continues the existing
[hosting runbook](ISSUE_15_HOSTING.md); it does not replace its historical results.

## Verified software and deployment proposal

Source: current `origin/dev`, `4aa9beaa9a3dae87a71b91c92173726b24b7707b`.
The backend image and probe already landed in PR #25; no backend implementation
change is needed to deploy that source. PR #30 supplies the separate Unity runtime
settings, invented-session upload controls and connection check for #20.

The local image was rebuilt from `backend/` with Docker Desktop, Linux/arm64:

```sh
docker build --tag vr-fatigue-hosted:issue15 ./backend
cd backend
python -m scripts.check_hosted_image --image vr-fatigue-hosted:issue15
```

Result: all seven image checks passed. Missing token refuses startup; platform
PORT and health work; invented analyze preserves provenance with 200/401/422;
declared participant eye input is rejected; responses/probe output contain no
token; runtime is nonroot and excludes dotenv/fixtures/dev dependencies; the
actual Docker health command passes. Temporary containers were removed.

Local image config ID:
`sha256:ed95b3496d6704b04033e2c9f3589a5913a03fa99c8a7b56eb4eae025b3e7ca0`.
This is a local image identity, not a hosted deployment identifier.

Railway sign-in and project discovery succeeded. No VR project/service exists in
the visible account. No existing unrelated service was changed. Proposed scope,
pending explicit public-deployment approval:

| Setting | Proposed value |
| --- | --- |
| Project / environment / service | New `vr-fatigue` / `staging` / `vr-fatigue-backend` |
| Source | Reviewed dev `4aa9bea`, backend directory only |
| Build | `backend/Dockerfile`; service root `/backend` for Git-source deployment |
| Start | Image entry point `python -m app.serve`; platform PORT; one worker |
| Runtime | `VR_RUNTIME_MODE=hosted`, `VR_GEMINI_ENABLED=false` |
| Credentials | New random backend token in Railway secret store; no Gemini key |
| Health / replicas / restart | `/health`, 60-second startup window, one replica, at most three failure restarts |
| Domain | Platform-managed HTTPS, normal certificate verification |
| Data | Wholly invented eyes and check-ins only; `cloud_policy=local_only` |
| Billing | Use the existing account plan; no billing enablement or spending-limit increases |

Deployment can consume hosting resources under the existing account plan. If the
platform requires new billing or a higher limit, stop for a separate decision.
Observe SUCCESS for the exact deployment before claiming it is running. Record
the actual deployment ID, source revision, HTTPS origin and safe probe output.

## Verification after an authorized deployment

1. Check public `/health`, then run `python -m scripts.probe_backend --base-url
   https://<staging-host>` from a full checkout with the token supplied privately
   through `VR_BACKEND_ACCESS_TOKEN` or ignored local dotenv. Never put it in the
   URL or command arguments. A health response alone does not pass acceptance.
2. Record invented analyze 200, missing/wrong token 401, invalid duration 422,
   matching session/data/quality provenance and expected backend-rules fallback.
3. Build/install the reviewed #20 APK after Alexander's #18 device setup. In
   Backend settings enter the HTTPS origin and masked token, keep local
   development off, and run Check with invented data. Record the exact APK/commit.
4. Repeat against the same service on a second independent network, including
   the standalone Quest Pro. Record only generic network contexts and UTC times.
   Same-LAN or USB-reverse success alone does not pass the two-network criterion.
5. On Quest disable networking, run an invented demo, and finish a usable local
   report with the client-fallback label. Reset during a pending request and
   confirm credentials/status do not return after completion.
6. Keep participant sessions on-device with upload declined. `local_only` prevents
   Gemini forwarding; it does not prevent HTTP upload to the backend.

## Stop, rollback and remaining acceptance

For this first deployment there is no prior VR staging release to restore. If
startup/auth/contract checks fail, stop this service and use the working local
report path. For later updates, record the prior successful deployment ID and
restore it with the hosted token policy intact. Record shutdown/rollback results;
do not infer them from a command submission. A stopped service can be restarted
for the next agreed test window. Keep secret values and full logs private.

Still unrun: public deployment, HTTPS probe, two independent networks, Android
APK rebuild/install, native Quest keyboard/controller input and device offline
fallback. Unity 6000.6.7f1 desktop verification now exists in PR #30 (375 EditMode,
35 PlayMode tests); it does not satisfy these physical-device criteria. Keep
#15 and #20 open until their remaining acceptance is observed.

Decision after review of this proposal: the user chose **keep local only for now**.
Do not create or deploy the proposed public service without a later instruction.
