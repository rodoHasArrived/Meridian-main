#!/usr/bin/env python3
"""Build the Windows graph once and run independent, individually reported slices."""
from __future__ import annotations

import argparse
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import time
import uuid

ROOT = Path(__file__).resolve().parents[3]
spec = importlib.util.spec_from_file_location("windows_dotnet_runner", Path(__file__).with_name("run-dotnet-ci-tests.py"))
runner = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = runner
spec.loader.exec_module(runner)


def selected_projects(slices: list[str], test_filter: str | None = None):
    roster = json.loads((ROOT / "build/ci/windows-test-slices.json").read_text(encoding="utf-8"))
    names = {row["name"] for row in roster}
    if set(slices) - names:
        raise ValueError(f"Unknown Windows slices: {sorted(set(slices) - names)}")
    if test_filter is not None and len(slices) != 1:
        raise ValueError("A filter override requires exactly one explicitly selected slice.")
    return [runner.TestProject(row["name"], row["project"], test_filter or row["filter"])
            for row in roster if not slices or row["name"] in slices]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--slice", action="append", default=[])
    parser.add_argument("--filter")
    parser.add_argument("--build-only", action="store_true")
    parser.add_argument("--results-dir", type=Path, default=Path("artifacts/wpf-validation/windows-desktop-build"))
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()
    if os.name != "nt" and not args.dry_run:
        parser.error("Windows validation requires a native Windows runner.")
    os.chdir(ROOT)
    os.environ["MERIDIAN_REPO_ROOT"] = str(ROOT)
    projects = selected_projects(args.slice, args.filter)
    output = args.results_dir.resolve()
    output.mkdir(parents=True, exist_ok=True)
    slnf = output / "windows-tests.slnf"
    runner.write_build_solution_filter(projects, repo_root=ROOT, filter_path=slnf)
    # One key and graph for all roots; do not override the distinct WPF/setup TFMs.
    properties = ["/p:EnableWindowsTargeting=true", "/p:EnableFullWpfBuild=true",
                  "/p:WindowsPackageType=None", f"/p:MeridianBuildIsolationKey=windows-ci-{uuid.uuid4().hex[:12]}",
                  "/m:1", "/nr:false", "/p:UseSharedCompilation=false"]
    builds = []
    for verb, extra in (("restore", []), ("build", ["-c", "Release", "--no-restore"])):
        command = ["dotnet", verb, str(slnf), *extra, *properties]
        log_path = output / f"{verb}.log"
        started = time.perf_counter()
        print(subprocess.list2cmdline(command), flush=True)
        with log_path.open("w", encoding="utf-8") as log:
            code = 0 if args.dry_run else subprocess.run(command, stdout=log, stderr=subprocess.STDOUT, check=False).returncode
        builds.append(runner.TestResult(verb, str(slnf), code, command, time.perf_counter() - started, str(log_path)))
        if code:
            print(log_path.read_text(encoding="utf-8", errors="replace")[-16384:], file=sys.stderr)
            break
    results = []
    if all(item.exit_code == 0 for item in builds) and not args.build_only:
        # A failure never short-circuits later slices. TRX prefixes/directories are unique.
        results = runner.run_tests(projects, configuration="Release", test_filter="",
                                   results_dir=output, dry_run=args.dry_run, properties=properties)
    runner.write_summaries(results, summary_output=output / "summary.md",
                           json_output=output / "summary.json", build_results=builds)
    if os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as summary:
            summary.write((output / "summary.md").read_text(encoding="utf-8"))
    return int(any(item.exit_code != 0 for item in [*builds, *results]))


if __name__ == "__main__":
    raise SystemExit(main())
