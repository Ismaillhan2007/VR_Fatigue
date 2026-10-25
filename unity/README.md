# Signals module and Quest Pro bridge

This module serves an **eye-tracking app** whose primary outcome is an experimental health/fatigue wellness report. The forest is a separately chosen relaxation use case; capture, metrics and report completion must not depend on scene participation. The current app still needs normal report-only completion; see [product objectives and implementation gaps](../docs/OBJECTIVES.md).

Target editor: **Unity 6000.6.7f1** (`ProjectSettings/ProjectVersion.txt`).
`Packages/manifest.json` pins Meta XR Core SDK **207.0.1**, OpenXR **1.18.0**,
XR Management **4.7.0**, Newtonsoft **3.2.2** and Unity Test Framework **1.8.0**.
UPM restores SDK packages; generated `Library/` caches are ignored.

`Assets/Signals/` contains the pure C# `VRFatigue.Signals` assembly and its EditMode tests.
`Assets/QuestPro/` contains the Unity/Meta bridge in a separate assembly. The replay,
quality, metrics, baseline and experimental indicator code needs no Meta APIs.

The `Assets/App/` application scaffold, UI, scenes, XR configuration and package versions
are preserved. The signal module is isolated under `Assets/Signals/`. The app now uses
`SignalsMetricsEngine` and JSON replay through `MetricsEngineFactory`/`ReplayEyeProvider`;
wiring the native Quest bridge into `EyeProviderFactory` remains integrator work.
See [architecture](../docs/ARCHITECTURE.md).
Target-editor import, Android build and live Quest acceptance remain pending.

Start with the [Quest Pro integrator handoff](../docs/QUEST_PRO_INTEGRATION.md), including
SDK setup, controller API, consent, source labels, lifecycle and unrun device acceptance.
Other module documentation:

- [Signals, replay and quality](../docs/SIGNALS.md)
- [Gaze/head/probe metrics](../docs/METRICS.md)
- [Baseline and comparison](../docs/BASELINE.md)
- [Experimental indicator](../docs/INDICATOR.md)
