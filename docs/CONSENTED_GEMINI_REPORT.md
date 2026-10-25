# Immediate reports and optional consented Gemini explanations

## Current product decision

The user explicitly requested real VR measurements and ratings explained by
Gemini on October 25, superseding the earlier synthetic-only cloud plan. This
does not make cloud access a prerequisite for a report or authorize raw eye
streams, images, participant free text or identifying data in model prompts.
All measurements and baseline comparisons remain deterministic on-device.
Gemini selects vetted factual observations and bounded optional scene settings;
the app renders approved wording and never treats the result as a diagnosis.

## Why Gemini previously did not run

The running local backend reported Gemini disabled; its ignored configuration
contained a key/model but generation was off and budget was zero. Unity created
interactive requests with local_only, the native adapter unconditionally used
local analysis, and backend/prompt guards rejected non-synthetic input. Approving
the existing smoke-test PR alone could not connect participant results to Gemini.

## New flow and contract

Check-in -> immediate local report -> optional Gemini update -> Finish with report.
The check-in panel has a separate optional, initially unchecked choice describing
the transfer of aggregate measurements and ratings to the configured backend and
Google Gemini. It resets every session. Raw samples and free text stay on-device.
Google processes cloud content under its own Gemini terms; the application makes
no guarantee about provider training/retention settings. Review the account's
service tier before using participant input.

The additive v1 cloud_policy enum now supports consented_summary. The default
remains local_only; synthetic_demo remains for wholly invented scenarios. The
client requires the actual check-in choice as well as the consented request
policy before transport. It never relabels native eyes as synthetic. This choice
is separate from the pre-capture invented-demo upload controls.

The backend also requires VR_ALLOW_PARTICIPANT_CLOUD=true. Hosted participant
requests otherwise remain rejected; local unconsented participant input still
uses rules. Gemini must be enabled/configured and pass the existing budget,
attempt, quality and safety gates. Participant-consented requests are never
cached, including those whose eye source happens to be synthetic. Prompts include
only measurements/deviations, quality, ratings/chips and approved preferences;
session identity, free text, raw gaze and device identifiers are excluded.

Schema, C# enum, Pydantic model and invented fixture are updated together. The
consented fixture is explicitly synthetic; tests mutate source fields in memory
to exercise live/recorded policy without claiming actual headset evidence.

## Report availability fixes

Previously SubmitCheckIn awaited optional HTTP before installing the analysis,
and Finish with report stayed disabled during that request. The controller now
installs a complete local report first. The report can be finished immediately;
doing so cancels optional analysis and late responses cannot replace the retained
report or revive a reset session. A successful response can update the report
while it remains on the Summary screen, with honest live/fallback labels.

Metric results are validated at the computation boundary. An invalid result or
wrong provenance is converted to a valid insufficient-data summary with original
provenance and check-in, instead of throwing later outside the analysis catch.
This addresses a confirmed exception-handling gap; it is not a claim that the
unavailable headset log proved that gap caused the user's particular symptom.
Duplicate check-in submits are ignored after the first transition.

## Local/LAN setup

Use this updated source with Unity 6000.6.7f1. The APK must be rebuilt; an old APK
or the old dev branch does not acquire these fixes from backend configuration.
Configure a reachable backend origin and its separate backend access token in
runtime Backend settings before capture. For trusted development HTTP, explicitly
enable local-development mode and the reviewed Android HTTP setting. Loopback
addresses refer to the headset itself and cannot reach the laptop.

On the backend, retain the existing ignored key/model and set explicit verified
prices, generation budget/attempt cap, VR_GEMINI_ENABLED=true and
VR_ALLOW_PARTICIPANT_CLOUD=true before restart. Do not commit the credential file.
The user approved at most $0.05 and three generation attempts for this verification,
using invented input only. This is not open-ended spend authorization. The local
verification process uses those limits; it does not deploy a public service.

Official references checked October 25: [Gemini 3.1 Flash-Lite pricing](https://ai.google.dev/gemini-api/docs/pricing#gemini-3.1-flash-lite)
lists $0.25/M text input and $1.50/M output including thinking; [Gemini terms](https://ai.google.dev/gemini-api/terms)
describe different data handling for unpaid and paid services. These are backend
configuration references, not claims about this account's billing or privacy tier.

## Approval and remaining hardware evidence

Approve the tested code/contract integration as a development candidate; keep
physical acceptance separate. Alexander builds the combined APK and verifies
View report, Finish with report, cloud-consent readability/controller interaction,
backend reachability, lifecycle and performance. Michael verifies native eyes,
permissions/calibration, known targets and eligible baseline/task measurements.
Ismail owns backend/Gemini/auth/transport/fallback and this contract update.
The optional forest PR still needs its own Unity generation/visual/device checks.
Do not accept fabricated live labels, raw/free-text uploads, unknown credentials,
unbounded spending or claims that desktop tests establish hardware performance.
