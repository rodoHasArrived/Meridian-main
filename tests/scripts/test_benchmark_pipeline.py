"""Exercise the pipeline lane against the real budget validator without timing CI hosts."""
from __future__ import annotations

import contextlib
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import time
import unittest
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[2]
HARNESS = ROOT / "build/scripts/ci/benchmark-pipeline.py"

# Registry export fixtures, including the SIMD exclusion. These are deliberately
# fixed: test data must never relax the production thresholds to make a run pass.
BUDGET_LIMITS = (
    ("DedupKey_CacheHit", 0, 200, False),
    ("DedupKey_CacheMiss", 256, 800, False),
    ("WalChecksum_Small", 0, 400, False),
    ("WalChecksum_Medium_1KB", 0, 600, False),
    ("WalChecksum_Large_4KB", 1024, 1200, False),
    ("NewlineScan_Portable", 0, 50, False),
    ("NewlineScan_Avx2", 0, 20, True),
    ("AlpacaParse_Trade_SourceGenerated", 512, 900, False),
    ("AlpacaParse_Quote_SourceGenerated", 640, 1200, False),
)
STAGES = tuple(stage for stage, _, _, simd in BUDGET_LIMITS if not simd)
FIRST_STAGE = STAGES[0]

FAKE_DOTNET = r'''
import json
import os
from pathlib import Path
import sys

args = sys.argv[1:]
scenario = os.environ["PIPELINE_TEST_SCENARIO"]
with Path(os.environ["PIPELINE_TEST_CALLS"]).open("a") as calls:
    calls.write(json.dumps(args) + "\n")
if args == ["--version"]:
    print("10.0.101" if scenario == "wrong-sdk" else "10.0.100")
elif args == ["--info"]:
    print(".NET SDK:\n Version: 10.0.100\nRuntime Environment:\n OS Name: Linux\n RID: linux-x64")
elif args == ["--list-runtimes"]:
    print("Microsoft.NETCore.App 10.0.0 [/test/shared/Microsoft.NETCore.App]")
elif args and args[0] in ("restore", "build"):
    print(args[0] + " completed (fixture)")
    sys.exit(19 if scenario == args[0] + "-failed" else 0)
elif "--artifacts" in args:
    directory = Path(args[args.index("--artifacts") + 1])
    directory.mkdir(parents=True, exist_ok=True)
    budgets = json.loads(os.environ["PIPELINE_TEST_BUDGETS"])
    if scenario != "missing-budgets":
        (directory / "perf-budgets.json").write_text(json.dumps(budgets))
    if scenario == "no-results":
        sys.exit(0)
    rows = [dict(
        FullName="Meridian.Benchmarks.PipelineBudgetBenchmarks." + budget["stage_name"],
        Method=budget["stage_name"],
        Statistics=dict(Mean=budget["max_mean_nanos_per_event"] / 2),
        Memory=dict(BytesAllocatedPerOperation=budget["max_allocated_bytes_per_event"]),
        Measurements=[dict(IterationMode="Workload", IterationStage="Result",
                           LaunchIndex=1, IterationIndex=1, Operations=1000,
                           Nanoseconds=budget["max_mean_nanos_per_event"] * 500)],
    ) for budget in budgets if not budget["requires_simd"]]
    if scenario == "missing-stage":
        rows.pop(0)
    elif scenario == "missing-statistics":
        rows[0]["Statistics"] = None
    elif scenario == "latency-over-budget":
        rows[0]["Statistics"]["Mean"] = budgets[0]["max_mean_nanos_per_event"] + 1
    elif scenario == "allocation-over-budget":
        rows[0]["Memory"]["BytesAllocatedPerOperation"] = 0.25
    elif scenario == "missing-measurements":
        rows[0]["Measurements"] = []
    elif scenario == "duplicate-stage":
        rows.append(rows[0])
    report = dict(HostEnvironmentInfo=dict(
        BenchmarkDotNetVersion="0.14.0", OsVersion="Linux (fixture)",
        ProcessorName="Recorded fixture CPU", RuntimeVersion=".NET 10.0.0"), Benchmarks=rows)
    if scenario == "missing-host-profile":
        report.pop("HostEnvironmentInfo")
    result_dir = directory / "results"
    result_dir.mkdir()
    result = result_dir / "Meridian.Benchmarks.PipelineBudgetBenchmarks-report-full.json"
    result.write_text("{invalid-json" if scenario == "invalid-json" else json.dumps(report))
    print("Wrote benchmark report (fixture)")
    sys.exit(23 if scenario == "benchmark-failed" else 0)
else:
    print("Unexpected dotnet invocation: " + repr(args), file=sys.stderr)
    sys.exit(64)
'''


@unittest.skipUnless(sys.platform.startswith("linux"), "The pipeline lane records Linux hardware")
class PipelineBenchmarkTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.directory = Path(self.temporary.name)
        self.output = self.directory / "artifacts"
        executable = self.directory / "dotnet"
        executable.write_text(f"#!{sys.executable}\n" + FAKE_DOTNET, encoding="utf-8")
        executable.chmod(0o755)
        self.environment = {
            key: value for key, value in os.environ.items()
            if not key.startswith(("GITHUB_", "RUNNER_"))
        }
        self.environment.update(
            PATH=str(self.directory) + os.pathsep + os.environ.get("PATH", ""),
            PIPELINE_TEST_CALLS=str(self.directory / "dotnet-calls.jsonl"),
            PIPELINE_TEST_BUDGETS=json.dumps([
                dict(stage_name=stage, max_allocated_bytes_per_event=allocation,
                     max_mean_nanos_per_event=latency, requires_simd=simd)
                for stage, allocation, latency, simd in BUDGET_LIMITS
            ]),
        )
        self.commit = subprocess.check_output(
            ["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()

    def run_lane(self, scenario="success"):
        before = set(self.output.glob("*/*/run.json"))
        completed = subprocess.run(
            [sys.executable, str(HARNESS), "--local", "--output-root", str(self.output)],
            cwd=ROOT,
            env=dict(self.environment, PIPELINE_TEST_SCENARIO=scenario),
            capture_output=True,
            text=True,
            timeout=30,
        )
        fresh = set(self.output.glob("*/*/run.json")) - before
        self.assertEqual(1, len(fresh), completed.stdout + completed.stderr)
        manifest = fresh.pop()
        result = json.loads(manifest.read_text())
        self.assertEqual(self.commit, manifest.parent.parent.name)
        self.assertEqual(self.commit, result["commitSha"])
        self.assertTrue(manifest.parent.name.startswith("local-"))
        return completed, manifest.parent, result

    def evidence(self, directory):
        return json.loads((directory / "budget-evidence.json").read_text())

    def load_harness(self):
        spec = importlib.util.spec_from_file_location("pipeline_benchmark_under_test", HARNESS)
        benchmark = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(benchmark)
        return benchmark

    def assert_failed(self, completed, result, validator_code):
        self.assertEqual(1, completed.returncode, completed.stdout + completed.stderr)
        self.assertEqual("failure", result["conclusion"])
        self.assertEqual(validator_code, result["commands"]["validator"]["exitCode"])

    def test_success_retains_commit_bound_profile_budgets_and_full_measurements(self):
        completed, directory, result = self.run_lane()
        self.assertEqual(0, completed.returncode, completed.stdout + completed.stderr)
        self.assertEqual("success", result["conclusion"])
        for name in ("restore", "build", "benchmark", "validator"):
            self.assertEqual(0, result["commands"][name]["exitCode"], name)
        for name in ("profile.json", "dotnet-info.log", "benchmark.log", "budget-validation.log",
                     "budget-evidence.json", "bdn/perf-budgets.json"):
            self.assertTrue((directory / name).is_file(), name)
        profile = json.loads((directory / "profile.json").read_text())
        self.assertEqual(set(STAGES), set(profile["requiredStages"]))
        self.assertEqual("10.0.100", profile["sdkVersion"])
        self.assertEqual("10.0.0", profile["runtimeVersion"])
        self.assertEqual("Linux", result["hardware"]["os"])
        self.assertTrue(result["hardware"]["cpuModels"])
        self.assertGreater(result["hardware"]["logicalCpuCount"], 0)
        self.assertTrue(result["hardware"]["memoryTotal"])
        self.assertEqual("local", result["executionEnvironment"])
        self.assertEqual(profile["environment"], result["runtimeEnvironment"])
        for name, digest in result["artifactSha256"].items():
            self.assertEqual(hashlib.sha256((directory / name).read_bytes()).hexdigest(), digest, name)
        self.assertEqual(1, len(list((directory / "bdn/results").glob("*-report-full.json"))))
        evidence = self.evidence(directory)
        self.assertEqual(8, evidence["measured_count"])
        self.assertEqual(0, evidence["violation_count"])
        self.assertEqual(0, evidence["unmeasured_count"])
        self.assertEqual(0, evidence["waived_unmeasured_count"])
        self.assertEqual(set(STAGES), {
            stage["stage_name"] for stage in evidence["stages"] if stage["measured"]})
        self.assertIn("All budgets measured and within limits",
                      (directory / "budget-validation.log").read_text())
        command = result["commands"]["benchmark"]["command"]
        self.assertIn("--export-budgets", command)
        self.assertIn("--artifacts", command)
        for option, value in (("--fx-version", "10.0.0"), ("--exporters", "fulljson"),
                              ("--filter", profile["benchmarkClass"] + ".*"),
                              ("--launchCount", profile["launchCount"]),
                              ("--warmupCount", profile["warmupCount"]),
                              ("--iterationCount", profile["iterationCount"]),
                              ("--buildTimeout", profile["generatedBuildTimeoutSeconds"]),
                              ("--iterationTime", profile["iterationTimeMs"])):
            self.assertEqual(str(value), command[command.index(option) + 1], option)
        self.assertIn("--fail-on-violation", result["commands"]["validator"]["command"])
        self.assertNotIn("--allow-unmeasured", result["commands"]["validator"]["command"])

    def test_zero_exit_without_results_fails_the_real_validator(self):
        completed, directory, result = self.run_lane("no-results")
        self.assert_failed(completed, result, 2)
        self.assertEqual(0, result["commands"]["benchmark"]["exitCode"])
        self.assertIn("no measurements", (directory / "budget-validation.log").read_text())

    def test_missing_stage_and_missing_statistics_are_unmeasured_failures(self):
        for scenario in ("missing-stage", "missing-statistics"):
            with self.subTest(scenario=scenario):
                completed, directory, result = self.run_lane(scenario)
                self.assert_failed(completed, result, 1)
                evidence = self.evidence(directory)
                self.assertEqual(7, evidence["measured_count"])
                self.assertEqual([FIRST_STAGE], evidence["unmeasured_stages"])

    def test_latency_and_fractional_allocation_violations_propagate(self):
        for scenario in ("latency-over-budget", "allocation-over-budget"):
            with self.subTest(scenario=scenario):
                completed, directory, result = self.run_lane(scenario)
                self.assert_failed(completed, result, 1)
                evidence = self.evidence(directory)
                self.assertEqual(1, evidence["violation_count"])
                self.assertEqual(0, evidence["unmeasured_count"])
                self.assertEqual([FIRST_STAGE], [
                    stage["stage_name"] for stage in evidence["stages"]
                    if stage["status"] == "violation"])

    def test_nonzero_benchmark_is_failure_even_when_validator_passes(self):
        completed, directory, result = self.run_lane("benchmark-failed")
        self.assert_failed(completed, result, 0)
        self.assertEqual(23, result["commands"]["benchmark"]["exitCode"])
        self.assertEqual(8, self.evidence(directory)["measured_count"])

    def test_restore_and_build_failures_still_invoke_validator_and_retain_logs(self):
        for stage in ("restore", "build"):
            with self.subTest(stage=stage):
                completed, directory, result = self.run_lane(stage + "-failed")
                self.assert_failed(completed, result, 2)
                self.assertEqual(19, result["commands"][stage]["exitCode"])
                self.assertNotIn("benchmark", result["commands"])
                self.assertTrue((directory / f"{stage}.log").read_text())
                self.assertIn("budget file not found", (directory / "budget-validation.log").read_text())
                self.assertTrue(any(stage in message.lower() for message in result["errors"]))
                if stage == "restore":
                    self.assertNotIn("build", result["commands"])

    def test_sdk_mismatch_fails_preflight_with_recorded_diagnostics(self):
        completed, directory, result = self.run_lane("wrong-sdk")
        self.assert_failed(completed, result, 2)
        self.assertEqual("10.0.101", (directory / "sdk.log").read_text().strip())
        self.assertNotIn("restore", result["commands"])
        self.assertNotIn("build", result["commands"])
        self.assertNotIn("benchmark", result["commands"])
        self.assertTrue(result["hardware"]["cpuModels"])
        self.assertTrue(any("requires SDK 10.0.100" in message for message in result["errors"]))

    def test_timeout_terminates_benchmark_process_and_its_descendant(self):
        benchmark = self.load_harness()
        parent_pid = self.directory / "parent.pid"
        descendant_pid = self.directory / "descendant.pid"
        descendant_code = (
            "import os, pathlib, time; "
            f"pathlib.Path({str(descendant_pid)!r}).write_text(str(os.getpid())); "
            "time.sleep(30)"
        )
        parent_code = (
            "import os, pathlib, subprocess, sys, time; "
            f"pathlib.Path({str(parent_pid)!r}).write_text(str(os.getpid())); "
            f"subprocess.Popen([sys.executable, '-c', {descendant_code!r}]); "
            "time.sleep(30)"
        )

        def is_alive(pid):
            # Linux can retain a killed grandchild as a zombie until init reaps it.
            try:
                stat = Path(f"/proc/{pid}/stat").read_text()
                return stat.rsplit(") ", 1)[1].split()[0] not in ("Z", "X")
            except FileNotFoundError:
                return False

        try:
            result = benchmark.run_command(
                [sys.executable, "-c", parent_code], self.directory / "timeout.log", 1, os.environ.copy())
            self.assertEqual(124, result["exitCode"])
            self.assertTrue(result["timedOut"])
            self.assertIn("Timed out", (self.directory / "timeout.log").read_text())
            self.assertTrue(parent_pid.is_file(), "Parent did not start before the timeout")
            self.assertTrue(descendant_pid.is_file(), "Descendant did not start before the timeout")
            pids = [int(path.read_text()) for path in (parent_pid, descendant_pid)]
            deadline = time.monotonic() + 2
            while any(is_alive(pid) for pid in pids) and time.monotonic() < deadline:
                time.sleep(0.02)
            self.assertFalse(any(is_alive(pid) for pid in pids), "Timeout left a live benchmark descendant")
        finally:
            for path in (parent_pid, descendant_pid):
                if path.is_file():
                    pid = int(path.read_text())
                    if is_alive(pid):
                        try:
                            os.kill(pid, signal.SIGKILL)
                        except ProcessLookupError:
                            pass

    def test_invalid_json_and_missing_budget_export_fail_validation(self):
        for scenario in ("invalid-json", "missing-budgets"):
            with self.subTest(scenario=scenario):
                completed, directory, result = self.run_lane(scenario)
                self.assert_failed(completed, result, 2)
                self.assertTrue((directory / "budget-validation.log").read_text())

    def test_aggregate_only_or_duplicate_reports_cannot_pass_the_full_measurement_gate(self):
        for scenario in ("missing-measurements", "missing-host-profile", "duplicate-stage"):
            with self.subTest(scenario=scenario):
                completed, directory, result = self.run_lane(scenario)
                self.assert_failed(completed, result, 0)
                self.assertEqual(8, self.evidence(directory)["measured_count"])
                self.assertTrue(result["errors"])

    def test_prior_passing_output_cannot_rescue_an_empty_fresh_run(self):
        passing, old_directory, _ = self.run_lane()
        self.assertEqual(0, passing.returncode, passing.stdout + passing.stderr)
        old_evidence = (old_directory / "budget-evidence.json").read_bytes()
        completed, new_directory, result = self.run_lane("no-results")
        self.assert_failed(completed, result, 2)
        self.assertNotEqual(old_directory, new_directory)
        self.assertEqual(old_evidence, (old_directory / "budget-evidence.json").read_bytes())
        self.assertFalse(list((new_directory / "bdn/results").glob("*-report-full.json")))

    def test_validator_launch_failure_and_success_without_evidence_cannot_pass(self):
        benchmark = self.load_harness()
        real_run_command = benchmark.run_command

        for exit_code in (127, 0):
            with self.subTest(exit_code=exit_code):
                output = self.directory / f"validator-{exit_code}"

                def run_command(command, *args, **kwargs):
                    if any(str(part).endswith("validate_budget.py") for part in command):
                        return dict(command=command, exitCode=exit_code, seconds=0,
                                    startedAt="2026-10-06T00:00:00+00:00", timedOut=False,
                                    error="validator could not launch" if exit_code else None)
                    return real_run_command(command, *args, **kwargs)

                with patch.dict(os.environ, dict(self.environment, PIPELINE_TEST_SCENARIO="success"),
                                clear=True), patch.object(benchmark, "run_command", side_effect=run_command), \
                        contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
                    code = benchmark.main(["--local", "--output-root", str(output)])
                self.assertEqual(1, code)
                manifests = list(output.glob("*/*/run.json"))
                self.assertEqual(1, len(manifests))
                result = json.loads(manifests[0].read_text())
                self.assertEqual("failure", result["conclusion"])
                self.assertEqual(exit_code, result["commands"]["validator"]["exitCode"])
                self.assertTrue(result["errors"])
                self.assertFalse((manifests[0].parent / "budget-evidence.json").exists())


if __name__ == "__main__":
    unittest.main()
