# HackTX 2026 submission draft (Devpost)

Draft for the team to edit. Every claim below must stay within [AGENTS.md](../AGENTS.md): wellness prototype, no diagnosis, the indicator is experimental, and synthetic/replay/cached/fallback is never presented as live.

## Project name and tagline

**VR Fatigue** - a personal check-in that compares your eye movements with your own baseline, asks how you feel, and builds a calm forest from what you say.

## Problem

People who spend long sessions in VR rarely notice gradual visual strain until it is uncomfortable. Population thresholds for "fatigue" from gaze do not work well across people, and the headset cannot tell you how you feel. We wanted something honest: compare a person with themselves, put their own words first, and offer a calm break they can shape.

## What we built

- A Unity (6000.6.7f1) app for the Meta Quest Pro, currently run on desktop, with the session flow: consent -> baseline -> task -> check-in (two 1-7 sliders, chips, optional text) -> summary -> calming forest -> re-rating. Pause and exit work at every step.
- A **probe task**: a head-locked target jumps every 2 s through a fixed layout in both the baseline and the task, so the two phases are measured under the same conditions.
- Deterministic signal processing in C#: data quality (repeated timestamps dropped, stalls and gaps handled, measured time steps, confidence never used as a filter), gaze and head speed, probe latency, accuracy and misses, per-metric deviation from the personal baseline (robust z), and an **experimental baseline-deviation indicator** (fixed a-priori weights, smoothing, hysteresis).
- A FastAPI backend that validates every request and response against versioned JSON contracts and returns a short explanation plus a bounded scene configuration. It runs offline by default with deterministic rules; Gemini is optional and only selects from approved explanation codes and bounded scene settings.
- A parametric forest whose lighting, fog, sound layers, particles, breathing orb and optional squirrel are set only from validated, clamped values. The participant's explicit choices always win; quiet and animal-free choices override everything.

## How it works

1. Eye samples come from a provider: a synthetic generator, a replay file, or (later) the Quest Pro adapter. Each provider declares what it really offers; only a headset provider may claim native eye gaze.
2. The metrics bridge turns the baseline and task into one session on the sensor clock and computes quality, metrics, deviations and the experimental indicator. Unavailable values are `null` with a reason, never 0.
3. The app computes the reconciliation between words and signals and the safety notice locally. Free text never leaves the device.
4. The backend (or local rules if it is unreachable) returns the explanation and a scene configuration. Unity rejects any response that relabels provenance, changes the reconciliation or lowers safety, then clamps the scene settings again.
5. The forest applies the settings with slow fades; the camera never moves.

## What is real and what is not (say this out loud)

| Part | Status in the demo |
| --- | --- |
| Eye data | **Synthetic replay** (`fixtures/replay/demo_session.json`, generated, labelled "REPLAY of synthetic data (not live)"). No headset recording is used or committed. |
| Metrics, deviations, indicator | Really computed by our code from that synthetic data. The indicator is **experimental**, not validated, not a fatigue score. |
| AI explanation | **Offline rules** unless Gemini is explicitly configured; the UI shows "FALLBACK from backend rules" or "LOCAL FALLBACK". Live Gemini output, if shown, is labelled "live" with the model id; cached output is labelled "cached". |
| Forest | A placeholder scene (primitive trees, generated squirrel) until the forest owner's scene lands. |
| Quest Pro | Adapter code compiled against Meta XR Core SDK 207.0.1; **not yet tested on a headset**. |

## Built with

Unity 6, C#, URP, XR Interaction Toolkit, OpenXR, Meta XR Core SDK, Newtonsoft Json, NUnit; Python 3.12, FastAPI, Pydantic, pytest, uv; Google Gen AI SDK (Gemini, optional); JSON Schema.

## AI use disclosure

- **Claude Code** (Anthropic) was used to write and review code, tests and documentation; all of it was written during the event and checked by the team.
- **Gemini** is used at runtime only for bounded selection: approved explanation codes and scene settings within fixed ranges, for synthetic input only. It never computes measurements, never sees free text or identities, and its output is validated and clamped before use. Without a key the app runs fully offline.

## Honest limitations

- Experimental wellness prototype. **Not a medical device, not a diagnosis, not validated fatigue detection.** The indicator's constants are initial guesses.
- No headset test yet: eye-data access, timing and comfort on the Quest Pro are unverified. The demo uses synthetic replay data.
- Baseline is a single short session; a baseline taken while tired is flagged and the claims shrink.
- Pupil size and eyelid openness are not available on the Quest Pro, so there are no blink or pupil metrics.
- No user study. A single before/after rating is not evidence that the scene helps.
- Not tested: Android build, live Gemini in this build, headset networking.

## 3-minute demo script

| Time | Show | Say (exact moments in **bold**) |
| --- | --- | --- |
| 0:00-0:20 | Title, consent screen | "Long VR sessions cause gradual strain people don't notice. We compare you with your own baseline and put your words first. This is an experimental wellness prototype, not a diagnosis." |
| 0:20-0:45 | Baseline with the yellow probe target; point at the top bar | "**The eye data in this demo is a synthetic replay, labelled here: REPLAY of synthetic data, not live.** The target jumps every two seconds; we measure how fast and how accurately gaze reaches it." |
| 0:45-1:05 | Task phase, then check-in sliders and chips | "Same probe layout under load. Now the participant says how they feel. Their text stays on the device." |
| 1:05-1:45 | Summary: per-metric deviations, probe trials, experimental indicator, reconciliation | "These are deviations from this person's own baseline. **The indicator is experimental and not a fatigue score.** Words and signals agree here; if they disagreed, the words would decide the scene. **The explanation is from our offline rules (fallback), not live AI** - the label says so." |
| 1:45-2:30 | Forest; press Mute; show pause/exit | "The scene is assembled only from bounded settings: dim, warm, quiet, no flashing, no camera movement. The participant's choices always win. **This forest is a placeholder scene.**" |
| 2:30-3:00 | Re-rating, then the architecture slide | "We'd test this on the Quest Pro next: the adapter is written but **not yet tested on the headset**. Vision: a background layer and multi-day baselines, with consent." |

If live Gemini is configured and shown, replace the fallback sentence with: "**This explanation is live Gemini output, constrained to approved codes and bounded settings, from synthetic input.**" If it came from the cache, say "cached".

## Pre-submit checklist

- [ ] Repository is public (or judges have access); `main` contains the merged, reviewed demo; no secrets (`git ls-files | grep -iE "DevAgentSettings|\.env$"` returns only `backend/.env.example`).
- [ ] README status matches reality; `docs/SETUP.md` steps work on a fresh clone.
- [ ] Demo video (about 3 minutes) recorded from the desktop build, with the synthetic/fallback labels visible and narrated.
- [ ] Devpost: description from this file, screenshots (summary screen with labels, forest), built-with list, AI-use disclosure, team members.
- [ ] Tracks / prizes selected (check the HackTX track list; do not claim medical or clinical categories).
- [ ] In-person judging: laptop charged, backend started (`uv run uvicorn app.main:app --host 127.0.0.1 --port 8000`), Unity scene open, offline fallback tested with the backend stopped, Quest Pro charged if the headset check happens.
- [ ] Final claims check against AGENTS.md: no "fatigue score", no diagnosis, nothing synthetic presented as live.
