# Gemini verification checkpoint — October 25, 2026

Issue #23, sole owner @Ismaillhan2007. Gemini remains optional; the first Quest
Pro session should complete its report on-device with backend transfer declined.

## Changes and exact boundary

The existing manual `scripts.smoke_gemini` checks live generation, cache/session
rebinding, contracts and preference overrides with one generation attempt. Its
old output names `real_data_stays_local` and `participant_checkin_stays_local`
could be read as an HTTP transport guarantee. The new output says precisely that
these requests are **not sent to Gemini**. Backend transport is controlled by the
Unity #20 upload gate; `cloud_policy=local_only` alone does not block HTTP upload.

The smoke now also verifies recorded-eye exclusion and that invented free text
is rejected during request validation before analysis. The interactive-policy
case continues to use null free text, as required by the wire contract. It checks
fixed fallback reasons and verifies the provider-attempt count remains one.
None of the additional cases can trigger another provider generation.

Two credential-free tests exercise the complete smoke with a fake adapter:
validated live/cache output passes, while invalid provider output returns a
failing smoke result rather than claiming live AI. Both verify the privacy gates,
reservation and schemas, and ensure neither the invented key nor free-text marker
appears in output. These are offline tests, not additional live-provider evidence.

No service, adapter, model, dependency lock, shared schema or Unity code changes
are part of this branch. It starts from dev `4aa9bea` and can be reviewed separately
from #20 (PR #30) and the #15 staging plan (PR #31).

## Evidence reused and checks run

The [earlier runtime execution record](BACKEND_RUNTIME_PLAN.md) reports one
authorized invented-data generation on October 24, using
`gemini-3.1-flash-lite`, historical checked rates $0.25/$1.50 per million
input/output tokens, and a maximum $0.002 reservation. It recorded 374 prompt
and 256 candidate tokens (630 total), no provider error, cache/session rebinding,
real-eye and interactive-policy exclusion, preference overrides and schemas.

`git diff 217ee65 origin/dev -- backend/app/gemini.py backend/app/service.py
backend/scripts/smoke_gemini.py backend/uv.lock` was empty before this branch's
smoke-only extension. The provider adapter, service and lock remain unchanged.
That historical result applies to this provider implementation; it does not prove
current quota/model availability, a public endpoint or physical Quest behavior.
No new provider call or fresh price/quota check was needed or performed.

Today's safe local configuration inspection found local runtime, generation
disabled and budget zero. No configured values were modified and no key/token
was printed. Keep generation disabled for headset debugging.

From `backend/`, using the existing locked Python 3.12 development environment:

```sh
python -m pytest -q
ruff check app tests scripts
python ../contracts/validate_examples.py
```

Results: **152 tests passed**, Ruff passed, **14 schemas and 35 fixtures validated,
7 intentionally invalid examples rejected**. The suite includes configuration
and limit refusal, timeout/429/5xx/invalid output, quality/safety gates, recorded
and live-eye exclusion, free-text rejection, local-only requests, cache and honest
diagnostics. Three actual loopback HTTP tests required the normal socket
permission after the sandbox denied binding; the full rerun passed. A preexisting
FastAPI/TestClient dependency deprecation warning remains.

The historical bounded live command, if a future separately approved repeat is
needed, was equivalent to:

```sh
python -m scripts.smoke_gemini --model gemini-3.1-flash-lite \
  --input-price 0.25 --output-price 1.50 --budget-usd 0.002
```

Do not run this as a current price recommendation. Verify official model support,
rates and quota again and obtain an explicit budget before another live attempt.
Token counting is a separate SDK call. Limits/cache/reservations are process
memory, reset on restart and are not durable account-wide spending controls.

## Remaining device acceptance

- Record the reviewed APK/commit, Unity/SDK and Quest OS versions; use Alexander's
  #18 setup and Michael's #21 real-eye validation as their respective handoffs.
- Run report-only completion with upload declined, AI disabled, backend absent,
  and network failure. A complete local report is a successful product flow.
- Verify eye-data labels (live/synthetic/replay) separately from explanation labels
  (live/cached/backend rules/client fallback). Invented data never proves live
  eye tracking; a fallback explanation never proves live Gemini.
- Interactive requests must remain local_only even with synthetic/replayed eyes.
  #20 requires a separate wholly invented-session acknowledgement and upload
  permission before any session HTTP transfer. Live/recorded input is blocked
  before that transfer. Do not relabel a participant session to obtain AI output.
- The connection-check button uses only its bundled invented request, always
  local_only. Expect backend rules rather than live Gemini there.
- Keep scene entry optional after the report. Check no AI or scene setting can
  imply participant consent or bypass the validated allowlist.

Physical Quest UI/source labels and offline fallback, an Android build/install,
and hosted/two-network evidence remain **unrun**. The user chose local-only work
for now; no public service was deployed. Keep issue #23 open for its device
criteria. Existing evidence supports backend behavior without spending on a
redundant generation.
