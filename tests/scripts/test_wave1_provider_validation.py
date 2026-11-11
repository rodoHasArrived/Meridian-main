"""Exercise provider-validation receipts and the runner with synthetic dotnet results."""

from __future__ import annotations

import json
import os
import re
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
from xml.sax.saxutils import escape


ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "scripts" / "dev"
PWSH = shutil.which("pwsh")


def trx(names: list[str], outcome: str = "Passed") -> str:
    count = len(names)
    passed = count if outcome == "Passed" else 0
    failed = count if outcome == "Failed" else 0
    definitions = "".join(
        f'<UnitTest id="{i}"><TestMethod className="Fixture.{escape(name)}" name="Scenario" /></UnitTest>'
        for i, name in enumerate(names)
    )
    results = "".join(
        f'<UnitTestResult testId="{i}" outcome="{outcome}" />' for i in range(count)
    )
    return (
        '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
        f'<TestDefinitions>{definitions}</TestDefinitions><Results>{results}</Results>'
        f'<ResultSummary><Counters total="{count}" executed="{count}" passed="{passed}" '
        f'failed="{failed}" /></ResultSummary></TestRun>'
    )


@unittest.skipUnless(PWSH, "pwsh is not available")
class ProviderTestEvidenceTests(unittest.TestCase):
    def read(self, xml: str | None, anchors: list[str]) -> dict:
        with tempfile.TemporaryDirectory() as directory:
            temp = Path(directory)
            if xml is not None:
                (temp / "results.trx").write_text(xml)
            (temp / "anchors.json").write_text(json.dumps(anchors))
            harness = temp / "read.ps1"
            harness.write_text(
                "param($Helper, $Trx, $Anchors)\n. $Helper\n"
                "$expected = @(Get-Content -Raw $Anchors | ConvertFrom-Json)\n"
                "Get-MeridianProviderTestEvidence -TrxPath $Trx -ExpectedEvidence $expected | ConvertTo-Json -Depth 8\n"
            )
            result = subprocess.run(
                [PWSH, "-NoProfile", "-File", str(harness), str(SCRIPTS / "ProviderValidationEvidence.ps1"),
                 str(temp / "results.trx"), str(temp / "anchors.json")],
                capture_output=True, text=True, check=True,
            )
            return json.loads(result.stdout)

    def test_receipt_counts_actual_execution_and_each_claim(self):
        receipt = self.read(trx(["HistoryTests", "HistoryTests", "FactoryTests"]), ["HistoryTests", "FactoryTests"])
        self.assertTrue(receipt["success"])
        self.assertEqual(receipt["executed"], 3)
        self.assertEqual(receipt["evidenceCounts"], {"HistoryTests": 2, "FactoryTests": 1})

    def test_zero_tests_are_not_success(self):
        self.assertFalse(self.read(trx([]), ["DoesNotExist"])["success"])

    def test_missing_claim_fails_even_when_other_tests_pass(self):
        receipt = self.read(trx(["HistoryTests"]), ["HistoryTests", "MissingTests"])
        self.assertFalse(receipt["success"])
        self.assertEqual(receipt["evidenceCounts"]["MissingTests"], 0)

    def test_failed_or_skipped_results_are_not_success(self):
        for outcome in ["Failed", "NotExecuted"]:
            with self.subTest(outcome=outcome):
                self.assertFalse(self.read(trx(["HistoryTests"], outcome), ["HistoryTests"])["success"])

    def test_missing_malformed_and_inconsistent_results_fail_closed(self):
        for xml in [None, "<broken", trx(["HistoryTests"]).replace('passed="1"', 'passed="9"')]:
            with self.subTest(xml=xml):
                self.assertFalse(self.read(xml, ["HistoryTests"])["success"])

    def test_run_level_failure_is_not_overruled_by_passed_test_counters(self):
        for summary in [
            '<ResultSummary outcome="Aborted">',
            '<ResultSummary><RunInfos><RunInfo outcome="Error" /></RunInfos>',
        ]:
            with self.subTest(summary=summary):
                xml = trx(["HistoryTests"]).replace("<ResultSummary>", summary)
                self.assertFalse(self.read(xml, ["HistoryTests"])["success"])


@unittest.skipUnless(PWSH and os.name != "nt", "requires pwsh and POSIX fixture executables")
class ProviderValidationRunnerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        dev = self.root / "scripts" / "dev"
        dev.mkdir(parents=True)
        for name in ["run-wave1-provider-validation.ps1", "SharedCheckpoint.ps1", "SharedPreflight.ps1", "ProviderValidationEvidence.ps1"]:
            shutil.copy(SCRIPTS / name, dev / name)
        (dev / "generate-dk1-pilot-parity-packet.ps1").write_text("param($SummaryJsonPath, [switch]$AllowFailedSummary)\n")
        project = self.root / "tests" / "Meridian.Tests" / "Meridian.Tests.csproj"
        project.parent.mkdir(parents=True)
        project.touch()
        bin_dir = self.root / "bin"
        bin_dir.mkdir()
        (bin_dir / "git").write_text("#!/bin/sh\nprintf 'fixture-revision\\n'\n")
        (bin_dir / "git").chmod(0o755)
        # Produce deterministic TRX from the requested filter; simulate missing tests and bad runners.
        (bin_dir / "dotnet").write_text(
            "#!/usr/bin/env python3\n"
            "import json, os, pathlib, sys\n"
            "args = sys.argv[1:]\n"
            "with open(os.environ['FIXTURE_CALLS'], 'a') as f: f.write(json.dumps(args) + '\\n')\n"
            "if args[0] != 'test': sys.exit(0)\n"
            "names = [n.split('~', 1)[1] for n in args[args.index('--filter') + 1].split('|')]\n"
            "mode = os.environ.get('FIXTURE_RESULT', 'passed')\n"
            "if mode == 'zero': names = []\n"
            "if mode == 'missing': names = [n for n in names if n != 'AlpacaHistoricalDataProviderTests']\n"
            "if mode == 'missing-parquet': names = [n for n in names if n != 'ParquetConversionServiceTests']\n"
            "outcome = 'Failed' if mode == 'failed' else 'Passed'\n"
            "count = len(names); passed = count if outcome == 'Passed' else 0\n"
            "defs = ''.join(f'<UnitTest id=\"{i}\"><TestMethod className=\"Fixture.{n}\" name=\"Scenario\" /></UnitTest>' for i, n in enumerate(names))\n"
            "results = ''.join(f'<UnitTestResult testId=\"{i}\" outcome=\"{outcome}\" />' for i in range(count))\n"
            "xml = f'<TestRun xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\"><TestDefinitions>{defs}</TestDefinitions><Results>{results}</Results><ResultSummary><Counters total=\"{count}\" executed=\"{count}\" passed=\"{passed}\" failed=\"{count-passed}\" /></ResultSummary></TestRun>'\n"
            "filename = args[args.index('--logger') + 1].split('LogFileName=')[1]\n"
            "path = pathlib.Path(args[args.index('--results-directory') + 1]) / filename\n"
            "path.write_text(xml)\n"
            "print('Synthetic test process completed')\n"
        )
        (bin_dir / "dotnet").chmod(0o755)
        self.env = dict(os.environ, PATH=str(bin_dir) + os.pathsep + os.environ["PATH"], FIXTURE_CALLS=str(self.root / "calls.jsonl"))

    def run_fixture(self, mode="passed"):
        result = subprocess.run(
            [PWSH, "-NoProfile", "-File", str(self.root / "scripts/dev/run-wave1-provider-validation.ps1"),
             "-DateStamp", "2026-11-11"],
            env=dict(self.env, FIXTURE_RESULT=mode), cwd=self.root, capture_output=True, text=True,
        )
        summary_path = self.root / "artifacts/provider-validation/_automation/2026-11-11/wave1-validation-summary.json"
        self.assertTrue(summary_path.exists(), result.stdout + result.stderr)
        return result, json.loads(summary_path.read_text(encoding="utf-8-sig"))

    def test_success_receipts_cover_all_claimed_provider_evidence(self):
        result, summary = self.run_fixture()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(summary["result"], "passed")
        test_steps = [step for step in summary["steps"] if step["kind"] == "test"]
        for step in test_steps:
            self.assertGreater(step["testEvidence"]["executed"], 0)
        for provider, step in zip(summary["activeProviderRows"], test_steps):
            self.assertEqual(set(provider["offlineEvidence"]), set(step["testEvidence"]["evidenceCounts"]))
            matrix = (ROOT / "docs/reference/provider-validation-matrix.md").read_text()
            row = next(line for line in matrix.splitlines() if line.startswith(f"| {provider['name']} "))
            documented = set(re.findall(r"`([^`]+)`", row.split("|")[2]))
            self.assertEqual(set(provider["offlineEvidence"]), documented)

    def test_zero_missing_or_failed_tests_fail_despite_zero_process_exit(self):
        for mode in ["zero", "missing", "missing-parquet", "failed"]:
            with self.subTest(mode=mode):
                result, summary = self.run_fixture(mode)
                self.assertNotEqual(result.returncode, 0)
                self.assertEqual(summary["result"], "failed")
                self.assertTrue(any(step["status"] == "failed" and step["exitCode"] == 0 for step in summary["steps"]))

    def test_resume_reexecutes_tests_and_rewrites_summary_on_failure(self):
        first, _ = self.run_fixture()
        self.assertEqual(first.returncode, 0, first.stderr)
        second, summary = self.run_fixture("zero")
        self.assertNotEqual(second.returncode, 0)
        self.assertEqual(summary["result"], "failed")
        self.assertTrue(all(step["testEvidence"]["executed"] == 0 for step in summary["steps"] if step["kind"] == "test"))
        calls = [json.loads(line) for line in (self.root / "calls.jsonl").read_text().splitlines()]
        self.assertEqual(sum(call[0] == "build" for call in calls), 2)


if __name__ == "__main__":
    unittest.main()
