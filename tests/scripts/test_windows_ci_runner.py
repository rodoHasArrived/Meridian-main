import importlib.util
from contextlib import redirect_stdout
import io
import json
from pathlib import Path
import re
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("windows_ci_runner", ROOT / "build/scripts/ci/run-windows-ci-tests.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class WindowsCiRunnerTests(unittest.TestCase):
    @staticmethod
    def write_result_fixture(directory):
        path = Path(directory) / "full_1.trx"
        path.write_text('''<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <TestDefinitions>
            <UnitTest id="theory"><TestMethod className="Example.WriterTests" name="Theory" /></UnitTest>
            <UnitTest id="fact"><TestMethod className="Example.OtherTests" name="Fact" /></UnitTest>
          </TestDefinitions>
          <Results>
            <UnitTestResult testId="theory" testName="Theory(1)" outcome="Passed" />
            <UnitTestResult testId="theory" testName="Theory(2)" outcome="Failed" />
            <UnitTestResult testId="theory" testName="Theory(3)" outcome="NotExecuted" />
            <UnitTestResult testId="fact" testName="Fact" outcome="Passed" />
            <UnitTestResult testId="missing" testName="Unresolved" outcome="Timeout" />
          </Results>
        </TestRun>''', encoding="utf-8")
        return path

    def test_class_outcomes_count_actual_theory_rows_and_preserve_all_outcomes(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = self.write_result_fixture(tmp)
            self.assertEqual(module.class_outcomes([str(path)]), {
                "Example.WriterTests": dict(passed=1, failed=1, skipped=1, other=0),
                "Example.OtherTests": dict(passed=1, failed=0, skipped=0, other=0),
                "<unresolved class>": dict(passed=0, failed=0, skipped=0, other=1),
            })

    def test_console_report_uses_only_the_same_collected_trx_evidence(self):
        with tempfile.TemporaryDirectory() as tmp:
            self.write_result_fixture(tmp)
            evidence = module.runner.collect_trx(Path(tmp), "full")
            # An unrelated file left after collection must not become invented execution proof.
            (Path(tmp) / "full_stale.trx").write_text("not current evidence", encoding="utf-8")
            result = module.runner.TestResult("full", "tests.csproj", 1, [], evidence=evidence)
            output = io.StringIO()
            with redirect_stdout(output):
                module.print_class_outcomes([result])
            rows = [json.loads(line.split(": ", 1)[1]) for line in output.getvalue().splitlines()]
            writer = next(row for row in rows if row["class"] == "Example.WriterTests")
            self.assertEqual(writer, {
                "class": "Example.WriterTests", "passed": 1, "failed": 1, "skipped": 1, "other": 0, "total": 3
            })
            self.assertEqual(sum(row["total"] for row in rows), 5)
            self.assertEqual(result.exit_code, 1)

    def test_missing_invalid_or_changed_evidence_never_reports_class_success(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = self.write_result_fixture(tmp)
            evidence = module.runner.collect_trx(Path(tmp), "full")
            result = module.runner.TestResult("full", "tests.csproj", 1, [], evidence=evidence)
            original = path.read_text(encoding="utf-8")
            for content in ("<invalid", "<TestRun />", original.replace("Example.WriterTests", "Forged.WriterTests")):
                path.write_text(content, encoding="utf-8")
                output = io.StringIO()
                with redirect_stdout(output):
                    module.print_class_outcomes([result])
                self.assertIn("TRX class evidence unavailable [full]", output.getvalue())
                self.assertNotIn("TRX class outcomes", output.getvalue())
                self.assertEqual(result.exit_code, 1)
            output = io.StringIO()
            with redirect_stdout(output):
                module.print_class_outcomes([module.runner.TestResult("build-only", "tests.csproj", 0, [])])
            self.assertEqual(output.getvalue(), "")

    def test_every_existing_filter_and_windows_project_remains_selected(self):
        rows = {p.name: p for p in module.selected_projects([])}
        self.assertEqual(set(rows), {"full", "dev-loop", "position-blotter-route", "operator-inbox-route", "supervisor", "setup"})
        for name in ("position-blotter-route", "operator-inbox-route"):
            old = (ROOT / f"scripts/dev/validate-{name}.ps1").read_text(encoding="utf-8")
            self.assertEqual(rows[name].filter_expression, re.search(r'\[string\]\$Filter = "([^"]+)"', old)[1])
        self.assertEqual(rows["full"].filter_expression, "Category!=Integration&FullyQualifiedName!~Integration")
        self.assertEqual(rows["dev-loop"].filter_expression, "FullyQualifiedName~DesktopWorkflowScriptTests")
        self.assertEqual(rows["setup"].filter_expression, "")
        self.assertEqual({p.path for p in rows.values()}, set(module.runner.WINDOWS_ONLY_TEST_PROJECTS))

    def test_override_is_scoped_and_unknown_slice_is_rejected(self):
        with self.assertRaises(ValueError):
            module.selected_projects([], "FullyQualifiedName~Example")
        with self.assertRaises(ValueError):
            module.selected_projects(["missing"])
        self.assertEqual(module.selected_projects(["dev-loop"], "MyFilter")[0].filter_expression, "MyFilter")

    def test_build_once_precedes_all_slices_and_propagates_failures(self):
        calls = []
        def command(cmd, **kwargs):
            calls.append(cmd)
            return type("Completed", (), {"returncode": 0})()
        def test_slices(projects, **kwargs):
            self.assertEqual([c[1] for c in calls], ["restore", "build"])
            self.assertIn("--no-restore", calls[1])
            graph = json.loads(Path(calls[0][2]).read_text())["solution"]["projects"]
            self.assertEqual(len(graph), 3)
            self.assertEqual(len(projects), 6)
            self.assertEqual(kwargs["properties"], calls[0][3:])
            return [module.runner.TestResult(p.name, p.path, int(i == 1), ["dotnet", "test"])
                    for i, p in enumerate(projects)]
        with tempfile.TemporaryDirectory() as tmp, patch.object(sys, "argv", ["runner", "--results-dir", tmp]):
            with patch.object(module.platform, "system", return_value="Windows"), patch.object(module.subprocess, "run", side_effect=command), patch.object(module.runner, "run_tests", side_effect=test_slices):
                self.assertEqual(module.main(), 1)

    def test_automatic_paths_include_every_manual_slice_dependency(self):
        workflow = (ROOT / ".github/workflows/windows-desktop-build.yml").read_text()
        for path in ("scripts/dev/desktop-workflows.json", "scripts/dev/validate-position-blotter-route.ps1",
                     "scripts/dev/validate-operator-inbox-route.ps1", "tests/Meridian.Setup.Tests/**"):
            self.assertEqual(workflow.count(f'"{path}"'), 2)
        for name in ("wpf-dev-validation.yml", "wpf-route-validation.yml"):
            text = (ROOT / ".github/workflows" / name).read_text()
            self.assertNotIn("  pull_request:", text)
            self.assertNotIn("  push:", text)
            self.assertIn("run-windows-ci-tests.py", text)
