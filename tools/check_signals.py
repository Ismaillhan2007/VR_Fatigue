"""Compile/run the C# signal tests with an installed macOS Unity toolchain.

No Unity project, scene, editor launch or API calls. Artifacts go to a temporary directory.
"""
import argparse
import os
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--unity-editor", required=True, type=Path, help="Path to Unity.app")
    parser.add_argument("--replay", type=Path, help="Print the quality JSON of a local session instead of running tests")
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--metrics", action="store_true", help="With --replay, print metrics instead of quality only")
    mode.add_argument("--baseline-plan", type=Path, help="With --replay, print baseline/comparison JSON for this plan")
    parser.add_argument("--indicator", action="store_true", help="With --baseline-plan, also evaluate the experimental indicator")
    parser.add_argument("--reports-dir", type=Path, help="Write actual fixture quality reports for schema validation")
    args = parser.parse_args()
    if (args.metrics or args.baseline_plan) and not args.replay:
        parser.error("--metrics and --baseline-plan require --replay")
    if args.indicator and not args.baseline_plan:
        parser.error("--indicator requires --baseline-plan")
    resources = args.unity_editor / "Contents/Resources"
    mono = resources / "Scripting/MonoBleedingEdge/bin/mono"
    compiler = resources / "Scripting/MonoBleedingEdge/lib/mono/4.5/csc.exe"
    netstandard = resources / "Scripting/MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll"
    newtonsoft = resources / "Scripting/Managed/Newtonsoft.Json.dll"
    nunit_package = resources / "PackageManager/BuiltInPackages/com.unity.ext.nunit"
    # Unity 6000.3 bundles net40; 6000.5 bundles net472. Prefer the newer profile when both exist.
    nunit = next((path for profile in ("net472", "net40")
                  if (path := nunit_package / profile / "unity-custom/nunit.framework.dll").is_file()), None)
    for path in (mono, compiler, netstandard, newtonsoft):
        if not path.is_file():
            parser.error(f"Missing toolchain component: {path}")
    if nunit is None:
        parser.error(f"Missing NUnit framework in {nunit_package} (expected net472 or net40/unity-custom)")
    env = os.environ.copy()
    env["MONO_PATH"] = os.pathsep.join([str(newtonsoft.parent), str(nunit.parent)])
    env["VR_FATIGUE_ROOT"] = str(ROOT)
    # Do not inherit a stale report output directory from a caller.
    env.pop("VR_FATIGUE_REPORTS_DIR", None)
    if args.reports_dir:
        args.reports_dir.mkdir(parents=True, exist_ok=True)
        env["VR_FATIGUE_REPORTS_DIR"] = str(args.reports_dir.resolve())
    app = ROOT / "unity/Assets/Signals"
    sources = sorted([*app.joinpath("Capture").rglob("*.cs"), *app.joinpath("Metrics").rglob("*.cs"),
                      *app.joinpath("Tests/EditMode").rglob("*.cs"), ROOT / "tools/SignalTestRunner.cs"])
    with tempfile.TemporaryDirectory(prefix="vr-fatigue-signals-") as directory:
        assembly = Path(directory) / "SignalsChecks.exe"
        subprocess.run([str(mono), str(compiler), "/nologo", "/langversion:7.3", "/warnaserror+", "/target:exe",
                        f"/out:{assembly}", f"/reference:{netstandard}", f"/reference:{newtonsoft}", f"/reference:{nunit}", *map(str, sources)], check=True, env=env)
        replay_args = [str(args.replay.resolve())] if args.replay else []
        if args.metrics:
            replay_args.append("--metrics")
        if args.baseline_plan:
            replay_args.extend(["--baseline-plan", str(args.baseline_plan.resolve())])
        if args.indicator:
            replay_args.append("--indicator")
        subprocess.run([str(mono), str(assembly), *replay_args], check=True, env=env)


if __name__ == "__main__":
    main()
