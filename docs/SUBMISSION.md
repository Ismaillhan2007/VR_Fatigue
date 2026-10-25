# HackTX 2026 submission draft (Devpost)

Draft for the team to edit. Every claim below must stay within [AGENTS.md](../AGENTS.md): wellness prototype, no diagnosis, the indicator is experimental, and synthetic/replay/cached/fallback is never presented as live.

## Project name and tagline

**VR Fatigue** - an eye-tracking app that compares your eye movements with your own baseline, asks how you feel, and gives you an experimental wellness report, with optional personalized relaxation.

## Problem

People using VR need a way to review changes in eye movement and how they feel. Gaze alone cannot establish fatigue or replace what a participant reports. Our core objective is a useful health/fatigue report presented as an experimental wellness summary: compare a person with themselves, put their own words first, and show limitations. The participant can choose a calm break as an additional use case; they should also be able to finish with the report alone.

**Current desktop build:** Finish with report completes normally without a forest load, scene preferences or post-scene feedback. Reports remain in memory until Exit or New session. Optional relaxation has a separate preview/entry choice and skippable feedback. See [implementation and verification](REPORT_ONLY_FLOW.md). Headset operation remains unverified.

## What we built

- A Unity (6000.6.7f1) app for the Meta Quest Pro, currently run on desktop, with the session flow: consent -> baseline -> task -> check-in (two 1-7 sliders, chips, optional text) -> summary -> calming forest -> re-rating. Pause and exit work at every step.
- A **probe task**: a head-locked target jumps every 2 s through a fixed layout in both the baseline and the task, so the two phases are measured under the same conditions.
- Deterministic signal processing in C#: data quality (repeated timestamps dropped, stalls and gaps handled, measured time steps, confidence never used as a filter), gaze and head speed, probe latency, accuracy and misses, per-metric deviation from the personal baseline (robust z), and an **experimental baseline-deviation indicator** (fixed a-priori weights, smoothing, hysteresis).
- A FastAPI backend that validates every request and response against versioned JSON contracts and returns a short explanation plus a bounded scene configuration. It runs offline by default with deterministic rules; Gemini is optional and only selects from approved explanation codes and bounded scene settings.
- A parametric forest for the optional relaxation use case, whose lighting, fog, sound layers, particles, breathing orb and optional squirrel are set only from validated, clamped values. The participant's explicit choices always win; quiet and animal-free choices override everything. The current completion flow still needs separation from this feature.

## How it works

1. Eye samples come from a provider: a synthetic generator, a replay file, or (later) the Quest Pro adapter. Each provider declares what it really offers; only a headset provider may claim native eye gaze.
2. The metrics bridge turns the baseline and task into one session on the sensor clock and computes quality, metrics, deviations and the experimental indicator. Unavailable values are `null` with a reason, never 0.
3. The app computes the reconciliation between words and signals and the safety notice locally. Free text never leaves the device.
4. The backend (or local rules if it is unreachable) returns the explanation and a scene configuration. Unity rejects any response that relabels provenance, changes the reconciliation or lowers safety, then clamps the scene settings again.
5. If the participant clicks scene entry, the forest applies the settings with slow fades; the camera never moves. Participants can instead finish with their report without entering a scene.

## What is real and what is not (say this out loud)

| Part | Status in the demo |
| --- | --- |
| Eye data | **Synthetic replay** (`fixtures/replay/demo_session.json`, generated, labelled "REPLAY of synthetic data (not live)"). No headset recording is used or committed. |
| Metrics, deviations, indicator | Really computed by our code from that synthetic data. The indicator is **experimental**, not validated, not a fatigue score. |
| AI explanation | **Offline rules** unless Gemini is explicitly configured; the UI shows "FALLBACK from backend rules" or "LOCAL FALLBACK". Live Gemini output, if shown, is labelled "live" with the model id; cached output is labelled "cached". |
| Forest | A placeholder scene (primitive trees, generated squirrel) until the forest owner's scene lands. |
| Report-only completion | Required product behavior, **not implemented**. The summary can be read without entering the forest, but Exit clears it; normal Done requires scene/re-rating. |
| Quest Pro | Adapter code compiled against Meta XR Core SDK 207.0.1; **not yet tested on a headset**. |

## Built with

Unity 6, C#, URP, XR Interaction Toolkit, OpenXR, Meta XR Core SDK, Newtonsoft Json, NUnit; Python 3.12, FastAPI, Pydantic, pytest, uv; Google Gen AI SDK (Gemini, optional); JSON Schema.

## AI use disclosure

- **Claude Code** (Anthropic) was used to write and review code, tests and documentation; all of it was written during the event and checked by the team.
- **Gemini** is used at runtime only for bounded selection: approved explanation codes and scene settings within fixed ranges, for synthetic input only. It never computes measurements, never sees free text or identities, and its output is validated and clamped before use. Without a key the app runs fully offline.

## Honest limitations

- The desktop UI supports report-only completion and explicit optional relaxation with skippable feedback. V1 still bundles scene settings into every response; the report-only UI ignores them. Headset and native-eye acceptance remain pending.
- Experimental wellness prototype. **Not a medical device, not a diagnosis, not validated fatigue detection.** The indicator's constants are initial guesses.
- No headset test yet: eye-data access, timing and comfort on the Quest Pro are unverified. The demo uses synthetic replay data.
- Baseline is a single short session; a baseline taken while tired is flagged and the claims shrink.
- Pupil size and eyelid openness are not available on the Quest Pro, so there are no blink or pupil metrics.
- No user study. A single before/after rating is not evidence that the scene helps.
- Not tested: Android build, live Gemini in this build, headset networking.

## 3-minute demo script

| Time | Show | Say (exact moments in **bold**) |
| --- | --- | --- |
| 0:00-0:20 | Title, consent screen | "This is an eye-tracking app for an experimental wellness report about eye-movement changes and how you feel. The forest is an optional use case. This is not a diagnosis." |
| 0:20-0:45 | Baseline with the yellow probe target; point at the top bar | "**The eye data in this demo is a synthetic replay, labelled here: REPLAY of synthetic data, not live.** The target jumps every two seconds; we measure how fast and how accurately gaze reaches it." |
| 0:45-1:05 | Task phase, then check-in sliders and chips | "Same probe layout under load. Now the participant says how they feel. Their text stays on the device." |
| 1:05-1:45 | Summary: per-metric deviations, probe trials, experimental indicator, reconciliation | "This report is the core result. These are deviations from this person's own baseline. **The indicator is experimental and not a fatigue score. The explanation is from offline rules (fallback), not live AI.** Report-only completion is our objective; this build still needs that finish action, and Exit clears the report." |
| 1:45-2:30 | Deliberately choose scene entry; press Mute; show pause/exit | "We are choosing the relaxation use case for this demo. The scene uses bounded settings, slow fades and no camera movement. Participant preferences win. **This forest is a placeholder scene.**" |
| 2:30-3:00 | Re-rating, then the architecture slide | "We'd test this on the Quest Pro next: the adapter is written but **not yet tested on the headset**. Vision: a background layer and multi-day baselines, with consent." |

If live Gemini is configured and shown, replace the fallback sentence with: "**This explanation is live Gemini output, constrained to approved codes and bounded settings, from synthetic input.**" If it came from the cache, say "cached".

## Pre-submit checklist

- [ ] Repository is public (or judges have access); `main` contains the merged, reviewed demo; no secrets (`git ls-files | grep -iE "DevAgentSettings|\.env$"` returns only `backend/.env.example`).
- [ ] README status matches reality; `docs/SETUP.md` steps work on a fresh clone.
- [ ] Describe eye tracking and the report as the product; describe the forest as optional. Use the recorded desktop report-only evidence and disclose the remaining headset/native-eye limitations.
- [ ] Demo video (about 3 minutes) recorded from the desktop build, with the synthetic/fallback labels visible and narrated.
- [ ] Devpost: description from this file, screenshots (summary screen with labels, forest), built-with list, AI-use disclosure, team members.
- [ ] Tracks / prizes selected (check the HackTX track list; do not claim medical or clinical categories).
- [ ] In-person judging: laptop charged, backend started (`uv run uvicorn app.main:app --host 127.0.0.1 --port 8000`), Unity scene open, offline fallback tested with the backend stopped, Quest Pro charged if the headset check happens.
- [ ] Final claims check against AGENTS.md: no "fatigue score", no diagnosis, nothing synthetic presented as live.
