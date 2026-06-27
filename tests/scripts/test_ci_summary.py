from __future__ import annotations

import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
SCRIPT_PATH = REPO_ROOT / "build" / "scripts" / "ci" / "summarize-ci-artifacts.py"

SPEC = importlib.util.spec_from_file_location("summarize_ci_artifacts", SCRIPT_PATH)
assert SPEC and SPEC.loader
MODULE = importlib.util.module_from_spec(SPEC)
sys.modules["summarize_ci_artifacts"] = MODULE
SPEC.loader.exec_module(MODULE)


class CiSummaryTests(unittest.TestCase):
    def test_parse_steps_reads_ci_step_tsv(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "steps.tsv"
            path.write_text("Restore\t0\t3\nBuild\t1\t12\n", encoding="utf-8")

            steps = MODULE.parse_steps(path)

        self.assertEqual([step.name for step in steps], ["Restore", "Build"])
        self.assertEqual([step.status for step in steps], ["passed", "failed"])

    def test_known_error_classification_detects_common_build_failures(self) -> None:
        findings = MODULE.classify_known_patterns("error NETSDK1100: Windows targeting is disabled")

        self.assertIn("Windows targeting is missing; add EnableWindowsTargeting=true.", findings)

    def test_load_dotnet_summary_reports_failed_slices(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "summary.json"
            path.write_text(
                json.dumps(
                    {
                        "total": 2,
                        "passed": 1,
                        "failed": 1,
                        "results": [
                            {"name": "core", "path": "tests/Meridian.Tests/Meridian.Tests.csproj", "exit_code": 0},
                            {"name": "ui", "path": "tests/Meridian.Ui.Tests/Meridian.Ui.Tests.csproj", "exit_code": 1},
                        ],
                    }
                ),
                encoding="utf-8",
            )

            totals, failed = MODULE.load_dotnet_summary(path)

        self.assertIn("Total .NET slices: 2", totals)
        self.assertEqual(failed, ["ui (tests/Meridian.Ui.Tests/Meridian.Ui.Tests.csproj) exited 1"])

    def test_load_dotnet_summary_handles_malformed_json(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "summary.json"
            path.write_text("{", encoding="utf-8")

            totals, failed = MODULE.load_dotnet_summary(path)

        self.assertEqual(totals, [])
        self.assertEqual(failed, ["Failed to read or parse .NET test summary."])

    def test_load_dotnet_summary_handles_partial_result_entries(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "summary.json"
            path.write_text(
                json.dumps({"results": [{"exit_code": 1}, "invalid", {"path": "tests/example.csproj"}]}),
                encoding="utf-8",
            )

            totals, failed = MODULE.load_dotnet_summary(path)

        self.assertIn("Total .NET slices: 0", totals)
        self.assertEqual(failed, ["Unknown (Unknown) exited 1"])

    def test_build_summary_includes_lane_result_and_failure_lines(self) -> None:
        summary = MODULE.build_summary(
            lane="verify-dotnet",
            exit_code=1,
            steps=[MODULE.StepResult("Build", 1, 10)],
            dotnet_totals=[],
            failed_dotnet=[],
            known_findings=["C# compile error."],
            interesting_lines=["error CS1002: ; expected"],
        )

        self.assertIn("Meridian CI lane: `verify-dotnet`", summary)
        self.assertIn("| Build | failed | 1 | 10 |", summary)
        self.assertIn("C# compile error.", summary)
        self.assertIn("error CS1002", summary)


if __name__ == "__main__":
    unittest.main()
