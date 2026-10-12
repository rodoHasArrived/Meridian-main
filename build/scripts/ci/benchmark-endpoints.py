#!/usr/bin/env python3
"""Compare serial and concurrent endpoint tests in one prebuilt test assembly."""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import platform
import statistics
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

from test_evidence import collect_trx


ROOT = Path(__file__).resolve().parents[3]
NAMESPACE = "Meridian.Tests.Integration.EndpointTests."
# This deliberately excludes process-startup and process-global state tests.
CLASSES = (
    "HealthEndpointTests",
    "NegativePathEndpointTests",
    "ProviderEndpointTests",
    "StatusEndpointTests",
    "StorageEndpointTests",
    "SymbolEndpointTests",
)
FILTER = "|".join(f"FullyQualifiedName~{NAMESPACE}{name}." for name in CLASSES)


def timestamp(value: str) -> float:
    return datetime.fromisoformat(value.replace("Z", "+00:00")).timestamp()


def timing_evidence(path: Path) -> dict:
    """Use test timestamps to prove that distinct test classes actually overlapped."""
    root = ET.parse(path).getroot()
    namespace = {"t": root.tag.partition("}")[0].lstrip("{")}
    methods = {
        test.attrib["id"]: test.find("t:TestMethod", namespace).attrib["className"]
        for test in root.findall("t:TestDefinitions/t:UnitTest", namespace)
    }
    intervals: dict[str, tuple[float, float]] = {}
    for result in root.findall("t:Results/t:UnitTestResult", namespace):
        name = methods[result.attrib["testId"]]
        start = timestamp(result.attrib["startTime"])
        end = timestamp(result.attrib["endTime"])
        previous = intervals.get(name, (start, end))
        intervals[name] = (min(previous[0], start), max(previous[1], end))
    expected = {NAMESPACE + name for name in CLASSES}
    if set(intervals) != expected:
        raise ValueError(f"Unexpected benchmark classes: {sorted(intervals)}")
    events = [(start, 1) for start, end in intervals.values() if end > start]
    events += [(end, -1) for start, end in intervals.values() if end > start]
    active = maximum = 0
    for _, change in sorted(events):
        active += change
        maximum = max(maximum, active)
    times = root.find("t:Times", namespace)
    if times is None:
        raise ValueError("TRX has no run timing evidence")
    return {
        "testSeconds": timestamp(times.attrib["finish"]) - timestamp(times.attrib["start"]),
        "overlappingClasses": maximum,
    }


def validate_sample(evidence: dict, expected_digest: str | None) -> str:
    counts = evidence["counts"]
    if not counts["passed"] or any(counts[key] for key in ("failed", "skipped", "other")):
        raise ValueError(f"Benchmark requires every selected test to pass: {counts}")
    digest = evidence["testIdentityDigest"]
    if expected_digest is not None and digest != expected_digest:
        raise ValueError("Test identities differ between benchmark runs")
    return digest


def run_sample(args: argparse.Namespace, mode: str, label: str) -> dict:
    directory = args.output / label
    directory.mkdir(parents=True)
    settings = directory / "benchmark.runsettings"
    settings.write_text(
        "<RunSettings><xUnit>"
        f"<ParallelizeTestCollections>{str(mode == 'parallel').lower()}</ParallelizeTestCollections>"
        f"<MaxParallelThreads>{args.threads if mode == 'parallel' else 1}</MaxParallelThreads>"
        "</xUnit></RunSettings>\n",
        encoding="utf-8",
    )
    command = [
        args.dotnet, "vstest", str(args.assembly), f"--Settings:{settings}",
        f"--TestCaseFilter:{FILTER}", "--Logger:trx;LogFileName=endpoint.trx",
        f"--ResultsDirectory:{directory}",
    ]
    started = time.perf_counter()
    with (directory / "test.log").open("w", encoding="utf-8") as log:
        result = subprocess.run(
            command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT,
            timeout=args.timeout, check=False,
        )
    wall_seconds = time.perf_counter() - started
    if result.returncode:
        raise ValueError(f"Test command failed ({result.returncode}); see {directory / 'test.log'}")
    evidence = collect_trx(directory, "endpoint")
    evidence.pop("testIdentities")
    return dict(evidence, mode=mode, label=label, wallSeconds=wall_seconds,
                **timing_evidence(directory / "endpoint.trx"))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--assembly", type=Path, required=True, help="Already-built Meridian.Tests.dll")
    parser.add_argument("--output", type=Path, required=True, help="New evidence directory")
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--pairs", type=int, default=5)
    parser.add_argument("--warmups", type=int, default=1, help="Untimed runs of each mode")
    parser.add_argument("--threads", type=int, default=2)
    parser.add_argument("--timeout", type=int, default=300, help="Per-run timeout in seconds")
    args = parser.parse_args()
    if args.pairs < 2 or args.warmups < 1 or args.threads < 2 or args.timeout < 1:
        parser.error("Use at least two pairs, one warmup, two threads, and a positive timeout")
    args.assembly = args.assembly.resolve()
    args.output = args.output.resolve()
    if not args.assembly.is_file():
        parser.error(f"Test assembly does not exist: {args.assembly}")
    if args.output.exists():
        parser.error("Output must be a new directory so old results cannot count as evidence")
    args.output.mkdir(parents=True)
    report = {
        "createdUtc": datetime.now(timezone.utc).isoformat(),
        "assemblySha256": hashlib.sha256(args.assembly.read_bytes()).hexdigest(),
        "platform": platform.platform(), "cpuCount": os.cpu_count(),
        "parallelThreads": args.threads, "filter": FILTER, "warmups": [], "samples": [],
        "complete": False,
    }
    try:
        report["sdk"] = subprocess.check_output([args.dotnet, "--version"], text=True).strip()
        report["commit"] = subprocess.check_output(
            ["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
        expected_digest = None
        for warmup in range(args.warmups):
            for mode in ("serial", "parallel"):
                label = f"warmup-{warmup + 1}-{mode}"
                print(f"Running {label}", flush=True)
                sample = run_sample(args, mode, label)
                expected_digest = validate_sample(sample, expected_digest)
                report["warmups"].append(sample)
        for pair in range(args.pairs):
            modes = ("serial", "parallel") if pair % 2 == 0 else ("parallel", "serial")
            for mode in modes:
                label = f"pair-{pair + 1}-{mode}"
                print(f"Running {label}", flush=True)
                sample = run_sample(args, mode, label)
                expected_digest = validate_sample(sample, expected_digest)
                report["samples"].append(sample)
        if any(s["overlappingClasses"] > 1 for s in report["samples"] if s["mode"] == "serial"):
            raise ValueError("Serial samples overlapped; the runner did not honor serial settings")
        if not any(s["overlappingClasses"] > 1 for s in report["samples"] if s["mode"] == "parallel"):
            raise ValueError("No independent classes overlapped; remove their shared serial collection before benchmarking")
        if hashlib.sha256(args.assembly.read_bytes()).hexdigest() != report["assemblySha256"]:
            raise ValueError("Test assembly changed during the benchmark")
        report["medians"] = {
            mode: {
                metric: statistics.median(s[metric] for s in report["samples"] if s["mode"] == mode)
                for metric in ("wallSeconds", "testSeconds")
            }
            for mode in ("serial", "parallel")
        }
        serial = report["medians"]["serial"]["wallSeconds"]
        parallel = report["medians"]["parallel"]["wallSeconds"]
        report.update(complete=True, wallSpeedup=serial / parallel,
                      wallReductionPercent=100 * (serial - parallel) / serial)
        print(json.dumps(report["medians"], indent=2))
        print(f"Concurrent wall-time reduction: {report['wallReductionPercent']:.1f}%")
    except (OSError, ValueError, subprocess.SubprocessError) as error:
        report["error"] = str(error)
        print(str(error), file=sys.stderr)
    finally:
        (args.output / "summary.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
        print(f"Evidence: {args.output / 'summary.json'}", flush=True)
    return 0 if report["complete"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
