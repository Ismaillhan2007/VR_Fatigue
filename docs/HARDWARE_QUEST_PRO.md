# Meta Quest Pro: what we can rely on

Target headset: Meta Quest Pro, used fully virtual (no passthrough in the demo). Each statement carries a status:

- **Documented**: from Meta or Unity documentation (recheck against the installed SDK).
- **Observed (lab)**: seen by a team member in earlier lab recordings with another application. Useful as a prior, not proof that *this* app behaves the same. Lab recordings themselves are restricted and must never be added to this repo.
- **Unverified**: must be tested in the first hour with this app.

## 1. Signals

| Signal | Status | Consequence |
| --- | --- | --- |
| Per-eye gaze pose (direction, and a validity flag) via Meta XR Core SDK `OVREyeGaze` (or OpenXR `XR_FB_eye_tracking_social`) | Documented; behaviour in our build unverified | Primary input. Verify class and property names against the installed SDK version. |
| Gaze rate | Observed (lab): a 90 Hz frame-synchronised stream with 11.1 ms timestamps; about 1.7-2.9% repeated-timestamp rows; occasional 22 ms gaps; rare stalls of hundreds of ms. True sensor rate and latency unknown | Never assume a rate. Use measured timestamps, drop repeated ones, treat long gaps as invalid segments. |
| Gaze confidence | Documented as fixed or uninformative; observed constant in lab data | Do **not** filter on confidence. Set `capabilities.confidence_informative = false`. |
| Pupil diameter, eyelid openness | Not exposed (empty in lab data) | Blink and pupil metrics are out of scope. `null` with reason `not_exposed_by_runtime`. |
| Blink via face-tracking weights (e.g. eyes-closed) | Derived and unreliable | Do not use for metrics. |
| Accuracy | Documented in the literature: median about 1 degree, much worse tails, and failures to acquire for some people/tasks | Probe targets >= 6 degrees apart, tolerance 3 degrees; report data loss. |
| Raw eye images, passthrough camera frames | Not available / not needed | Out of scope. |
| Eye tracking over Link / Air Link | Experimental; has broken across updates | Run as a standalone APK on the headset. Desktop play mode uses replay. |

## 2. First-hour checklist (hardware owner)

1. Headset: Settings -> Movement tracking: eye tracking on; run eye calibration; developer mode on; USB debugging authorised for the laptop.
2. Unity: install the Unity editor version that the installed Meta XR SDK lists as supported (a Unity 6 LTS line is expected - verify). Pin the exact versions in `ProjectVersion.txt` and `Packages/manifest.json` in the setup PR.
3. In the scene: `OVRManager` with eye tracking support set to *Supported/Required*; add the eye-tracking permission to the manifest and request it at runtime (`com.oculus.permission.EYE_TRACKING`); add one `OVREyeGaze` per eye. Names and options may differ by SDK version.
4. Log 60 seconds on the headset: timestamp, left/right direction, valid flag, confidence, head pose. Compute: median and histogram of `dt`, fraction of repeated timestamps, longest gap, fraction invalid, whether confidence varies. Paste the numbers (not the raw samples) into the setup PR.
5. Decision after 60 minutes: if eye gaze works, implement `HardwareEyeProvider`. If it is still blocked, ship the replay path and keep the hardware adapter as a separate task (as PROJECT_PLAN says).

**Acceptance:** one 60 s log from the headset with the numbers above; one session through the same metrics code from replay; both labelled in the UI (`data_source`).

## 3. Network from the standalone headset to the laptop

- Same Wi-Fi or a phone hotspot (venue Wi-Fi often isolates clients). The headset needs the laptop's LAN IP, not `localhost`. Allow the backend port in the laptop firewall.
- Unity development builds need cleartext HTTP allowed (Player settings: Allow downloads over HTTP) for a LAN `http://` URL; remove or restrict before any public deployment.
- Fallback to test: `adb reverse tcp:8000 tcp:8000` so the headset can call `http://localhost:8000` over USB (*unverified* on Quest).
- The Unity app must stay usable when the request fails (local report and preset).

## 4. Comfort and safety on the device

Keep sessions short (target under 10 minutes), static camera, no locomotion, no flashing, a visible pause/exit, mute, and a way to take a break at any time. Measure frame timing on the headset and stay within its refresh-rate budget. Quest Pro is no longer sold new; do not plan features that depend on its future SDK support.
