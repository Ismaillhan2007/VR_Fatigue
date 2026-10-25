# Running consented Gemini reports on Quest

The backend owns the Google key. Quest uses a separate backend bearer token.
Every participant explicitly chooses cloud transfer for their own session.
Replay remains labelled synthetic, even when Gemini explains it. Select
**Use native eyes**, grant permission and confirm calibration before capture
when real measurements are required; Gemini does not collect eye measurements.

## Laptop launch

Use the latest development candidate with Unity 6000.6.7f1. From the repository:

```sh
cd backend
uv sync --locked --extra dev
uv run python -m app.quest_demo --port 8147
```

Keep this terminal running and the laptop awake. The launcher listens on the
LAN, requires the separate token, configured Gemini and a persistent allowance,
and fails with a fixed reason if the allowance is exhausted. Stop an older
receiver on port 8147 before launching this one. Port 8000 may still be an
unrelated offline receiver: do not use it for this demo.

The private `backend/.env` needs the following non-secret settings, plus the
existing `GEMINI_API_KEY`, `GEMINI_MODEL` and `VR_BACKEND_ACCESS_TOKEN`:

```dotenv
VR_GEMINI_ENABLED=true
VR_ALLOW_PARTICIPANT_CLOUD=true
VR_INPUT_PRICE_PER_MILLION=0.25
VR_OUTPUT_PRICE_PER_MILLION=1.50
VR_GEMINI_BUDGET_USD=0.50
VR_GEMINI_MAX_ATTEMPTS_PER_DAY=100
VR_GEMINI_MAX_TOTAL_ATTEMPTS=100
VR_GEMINI_LEDGER_PATH=/absolute/private/path/gemini-allowance.sqlite3
```

These prices are for the verified `gemini-3.1-flash-lite` configuration; recheck
prices when changing models. Ismail approved a new $0.50 / 100-attempt allowance
on October 25, 2026. The SQLite ledger stores only dates and conservative cost
reservations, including failed attempts. It survives process restarts and day
changes; keep the same file and do not delete it to replenish the allowance.
The daily cap is additional to the total cap. This is a single-machine demo,
not a multi-host or public deployment.

Check configuration, authentication and remaining allowance without paying:

```sh
uv run python -m scripts.quest_status
```

Expect `ready: true` and `persistent_allowance: true`. This check does not
contact Google or prove the key works. A report labelled **AI: live** is the
confirmation of actual provider generation.

## Headset build and setup

Alexander: close Unity and preserve your local work, fetch/switch to the current
candidate, then rebuild the Assessment APK using Android Build Support. Git
pull alone does not update the installed APK. Enable the reviewed development
HTTP setting for a trusted LAN test. Install with Build And Run or:

```sh
adb install -r /absolute/path/to/the/new.apk
```

On the initial Consent screen, open **Backend settings**. Enter
`http://<laptop-Wi-Fi-IP>:8147`, the `VR_BACKEND_ACCESS_TOKEN` from the private env
file, and enable **Local development**. Do not use the Google key as the token.
On macOS, find the Wi-Fi IP with `ipconfig getifaddr en0`. The current development
network uses RFC 6598 shared addresses (100.64.0.0/10), which this candidate
explicitly accepts only in development mode. Do not enter 0.0.0.0 or headset
localhost. Both devices must be able to reach each other on the network.

Use **Check connection**, then save. This authenticates an invented local-only
probe; it consumes no Gemini attempt. **New session** retains the operator's
connection settings in memory, while clearing all participant data and choices.
**Exit** clears the token, as does closing the app; re-enter it after that. Tokens are never
embedded in the APK, committed or stored in PlayerPrefs.

## After completing the task

On **How do you feel?**, enter ratings and select the optional Gemini consent
box before **View report**. The local report appears immediately. Stay on the
report screen until **AI: live (gemini-3.1-flash-lite)** appears. Then select
**Finish with report**. The finished report retains that explanation until Exit
or New session. Finishing while Gemini is pending cancels the request; the
button explicitly says so.

If you missed the first checkbox or generation failed, remain on the report,
select its Gemini consent box and press **Generate Gemini explanation**.
This sends the existing aggregate summary and ratings; it does not repeat
capture or send free text. The choice starts off unchecked each session.
Retries are explicit and counted; there are no automatic paid retries.

`AI: LOCAL FALLBACK (...local mode)` means no HTTP request was made.
`AI: FALLBACK from backend rules` plus an attempt/budget/provider reason means
the backend replied but Gemini did not supply an accepted explanation.
Safety-stop and insufficient-measurement sessions keep their local report;
the app does not fabricate a Gemini success. Internet/provider availability
and physical headset connectivity must be checked on the device.

## Verification for this change

Backend: 162 tests passed, including persistent reservations after restart/day
change and concurrent services; Ruff passed. Unity EditMode: 401 passed.
The broader report/backend/native-flow regression passed 18 tests with two
manual paid tests intentionally skipped. After adding New session connection
reuse, the three focused consent/retry/reset tests passed again.
Focused report-only PlayMode: 3 passed, including missed-consent recovery and
pending-request Finish. The subsequent three-test run passed through the
laptop's Wi-Fi address with one actual Gemini request for invented data:
completed replay -> consent -> Gemini -> report -> Finish. See the
[actual generated report screenshot](images/completed-gemini-report.png).

The full 130-second baseline and 60-second task ran at accelerated replay speed
while retaining 190 seconds of sensor data. The result preserved synthetic
replay provenance and was retained after Finish with report. This is desktop
software evidence; an installed Quest APK, live eyes and headset performance
still need physical acceptance. Forest assets and code are outside this change.
