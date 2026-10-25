# Quest backend defaults without typing in VR

This supersedes manual headset URL/token entry for the development demo.
The app loads an operator-provisioned `backend-connection.json` from Android's
**private internal app files directory** at startup. It sets the backend origin,
separate backend bearer token and local-development mode before showing Consent.
It also makes an invented/local-only connection probe automatically, without a
Gemini charge. The Welcome screen shows the connection status when provisioned.

No Google key is included in the setup file or APK. No credential is stored in
the scene, Resources or PlayerPrefs. The setup file cannot grant cloud consent;
each participant still selects the Gemini consent option for their own check-in.
Replay remains replay until **Use native eyes** is explicitly chosen and native
permission/calibration succeed.

## Ismail: prepare the private file

The backend stays running on Ismail's laptop. Run from the repository:

```sh
cd backend
uv run python -m scripts.quest_setup --export ../session-data/quest-backend-private.json --backend-url http://100.64.127.140:8147
```

This reads the separate `VR_BACKEND_ACCESS_TOKEN` from the ignored backend env
file, verifies backend reachability/auth/readiness, and writes a private file
with owner-only permissions. It makes no paid request. Use the current laptop
Wi-Fi address if it changes. Transfer the generated file privately to Alexander,
for example with AirDrop. Do not put it in GitHub, commit it or send it through
a public chat. The setup file contains a credential, so keep it private.

## Alexander: install once from the laptop

1. Close Unity, preserve local work and pull the latest `dev` containing this
   change. Use Unity 6000.6.7f1 with Android Build Support. Run **App > Quest Pro >
   Apply Android Assessment Settings**, allow HTTP in development builds and run
   **App > Quest Pro > Build Assessment Development APK**. Leave forest untouched.
2. Connect Quest by USB, accept USB debugging, and install the new development APK:

```sh
adb install -r builds/quest/VRFatigue-development.apk
```

3. Save Ismail's private file locally, then run from `backend/`:

```sh
uv sync --locked --extra dev
uv run python -m scripts.quest_setup --install ~/Downloads/quest-backend-private.json
```

The script requires exactly one authorized device (or `--serial DEVICE`), locates
adb on PATH or common Unity/macOS SDK paths (or `--adb /path/to/adb`), verifies
that Alexander's laptop can reach/authenticate the backend and writes the token
**over stdin** with `adb shell run-as`. It does not put secrets in command
arguments or print them. The file is stored with private permissions in the
application sandbox. The installed APK must be a Development Build for `run-as`.

4. Close and reopen the VR app. The connection settings are filled automatically;
   **do not type anything in VR**. The configured headset must still be able to
   reach Ismail's backend on the network. Start the session, complete the task,
   select the optional Gemini consent checkbox, View report, and wait for
   **AI: live** before Finish with report. If you missed consent, use the report's
   own consent checkbox and Generate Gemini explanation.

If preflight reports `backend_token_rejected`, obtain a fresh private setup file
from Ismail. If the backend is unreachable, check the laptops' and headset's
network access and laptop firewall before repeating a 190-second session.
Successful operator-laptop preflight does not prove headset network access.

## Lifetime and removal

The operator defaults persist across normal app restarts, Exit and New session,
and ordinarily an in-place APK update. **Exit clears the active in-memory token**;
restarting a session reloads the saved operator setup. Participant measurements,
ratings and cloud consent never go in this file and reset as before. Uninstall
or clear app storage removes the file. To remove only connection defaults:

```sh
uv run python -m scripts.quest_setup --clear
```

Then close/reopen the app. Updating the laptop address/token requires provisioning
again; the app does not discover a laptop or bypass authentication automatically.

## Verification

Backend: 173 tests passed; Ruff passed. Unity EditMode: 407 passed.
Initial and final focused PlayMode runs each passed all four selected tests.
The actual private export completed after authenticated readiness passed, with
no additional Gemini request. The private file is ignored and is not committed.

Tests cover private JSON validation, exclusion of consent/provider keys, fixed
error messages without secrets, owner-only export, stdin-only installation,
unauthorized-device rejection, authenticated readiness without provider calls,
Unity field population, HTTP authentication and reset/reload without consent.
The internal Android directory and `run-as` path need final acceptance on
Alexander's physical Quest. Desktop tests do not claim Android installation.

References: [Android Debug Bridge](https://developer.android.com/tools/adb) and
[Android application debugging](https://chromium.googlesource.com/chromium/src/+/main/docs/android_debugging_instructions.md)
describe authorized devices and `run-as` for debuggable applications.
