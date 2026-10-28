from __future__ import annotations

import importlib.util
import contextlib
import io
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch


REPO_ROOT = Path(__file__).resolve().parents[2]
SCRIPT = REPO_ROOT / ".codex/skills/meridian-implementation-assurance/scripts/run_evals.py"
SPEC = importlib.util.spec_from_file_location("implementation_assurance_runner", SCRIPT)
runner = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = runner
SPEC.loader.exec_module(runner)

CATEGORIES = ["behavior_correctness", "validation_evidence", "performance_safety", "documentation_sync", "traceable_summary"]
RUBRIC = json.dumps({category: 2 for category in CATEGORIES})


def command_event(command: str, *, kind: str = "item.completed", exit_code: int = 0, item_id: str = "cmd-1", output: str = "") -> dict:
    return {"type": kind, "item": {"id": item_id, "type": "command_execution", "command": command,
                                   "exit_code": exit_code, "aggregated_output": output}}


def valid_events() -> list[dict]:
    return [command_event("dotnet test tests/Example.csproj"),
            {"type": "item.completed", "item": {"type": "agent_message", "text": RUBRIC}},
            {"type": "turn.completed"}]


class ImplementationAssuranceRunnerTests(unittest.TestCase):
    def setUp(self) -> None:
        self.case = runner.EvalCase(1, "A", "test case", "implement requested behavior", ["Behavior matches acceptance criteria"])

    def score(self, events: list[dict]):
        return runner.score_case(self.case, events, runner.commands_run(events))

    def test_started_and_completed_commands_are_counted_once(self) -> None:
        events = [command_event("dotnet test", kind="item.started"), command_event("dotnet test")]
        self.assertEqual(runner.commands_run(events), ["dotnet test"])
        self.assertEqual(len(runner.successful_command_events(events)), 1)

    def test_validation_requires_successful_completed_execution(self) -> None:
        for event in (command_event("dotnet test", kind="item.started"),
                      command_event("dotnet test", exit_code=1),
                      command_event("echo 'dotnet test'"),
                      command_event("python -c 'print(\"validate test\")'")):
            with self.subTest(event=event):
                events = [event, *valid_events()[1:]]
                self.assertEqual(self.score(events).outcome, "FAIL")

    def test_common_validation_commands_and_shell_wrappers_are_recognized(self) -> None:
        commands = ["dotnet build Example.csproj", "python -m unittest discover -s tests/scripts",
                    "python build/scripts/docs/check-codex-skills.py --summary",
                    "python build/scripts/docs/validate-skill-packages.py",
                    "npm --prefix src/Meridian.Ui/dashboard run test",
                    "git diff --check", "bash scripts/ci.sh",
                    "/bin/bash -lc 'cd /workspace && dotnet test Example.csproj'",
                    'pwsh -Command "dotnet test Example.csproj"']
        for command in commands:
            with self.subTest(command=command):
                self.assertTrue(runner.check_ran_build_or_test([command]))
        self.assertFalse(runner.check_ran_build_or_test(["echo 'dotnet test; python validate-example.py'"]))

    def test_passing_trace_still_requires_assertion_review(self) -> None:
        result = self.score(valid_events())
        self.assertEqual(result.outcome, "PASS")
        self.assertEqual(result.assertions_require_review, self.case.assertions)

    def test_process_failure_and_failed_or_missing_turn_cannot_pass(self) -> None:
        result = self.score(valid_events())
        result.exit_code = 1
        self.assertEqual(result.outcome, "FAIL")
        self.assertEqual(self.score(valid_events() + [{"type": "turn.failed"}]).outcome, "FAIL")
        self.assertEqual(self.score(valid_events() + [{"type": "error"}]).outcome, "FAIL")
        self.assertEqual(self.score(valid_events()[:-1]).outcome, "FAIL")

    def test_rubric_mentions_in_prompt_or_command_do_not_count(self) -> None:
        events = [command_event(f"echo '{RUBRIC}'", output=RUBRIC),
                  {"type": "prompt", "text": RUBRIC},
                  {"type": "item.started", "item": {"type": "agent_message", "text": RUBRIC}}]
        self.assertFalse(runner.check_produced_rubric_output(events))
        self.assertFalse(runner.check_produced_rubric_output([
            {"type": "item.completed", "item": {"type": "agent_message", "text": "Behavior Correctness"}}]))

    def test_rubric_from_successful_scorer_output_counts(self) -> None:
        self.assertTrue(runner.check_produced_rubric_output([
            command_event("python scripts/score_eval.py --json", output=RUBRIC)]))
        self.assertFalse(runner.check_produced_rubric_output([
            command_event("python scripts/score_eval.py --json", exit_code=1, output=RUBRIC)]))

    def test_documentation_route_helper_is_advisory(self) -> None:
        self.case.scenario = "B"
        self.assertEqual(self.score(valid_events()).outcome, "PASS")

    def test_malformed_trace_is_rejected(self) -> None:
        for raw in ('{"type":"turn.completed"}\nnot JSON', '[]',
                    '{"type":"item.completed","item":null}',
                    '{"type":"item.started","item":[]}',
                    '{"type":"item.completed","item":{"id":[],"type":"command_execution","command":"dotnet test"}}',
                    '{"type":"item.completed","item":{"type":"command_execution","command":null}}',
                    '{"type":"turn.completed","usage":"invalid"}'):
            with self.subTest(raw=raw), self.assertRaises(ValueError):
                runner.parse_jsonl(raw)

    def test_run_case_records_nonzero_process_exit(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            artifacts = Path(directory)
            def fake_run(prompt, trace_path):
                trace_path.write_text("\n".join(json.dumps(event) for event in valid_events()), encoding="utf-8")
                return 7, "failed"
            with patch.object(runner, "ARTIFACTS_DIR", artifacts), patch.object(runner, "run_codex", side_effect=fake_run):
                result = runner.run_case(self.case)
            self.assertEqual(result.outcome, "FAIL")
            self.assertIn("codex exec exited with status 7", result.failed)

    def test_run_case_reports_malformed_nested_item_without_crashing(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            def fake_run(prompt, trace_path):
                trace_path.write_text('{"type":"item.completed","item":null}', encoding="utf-8")
                return 0, ""
            with patch.object(runner, "ARTIFACTS_DIR", Path(directory)), patch.object(runner, "run_codex", side_effect=fake_run):
                result = runner.run_case(self.case)
            self.assertEqual(result.outcome, "FAIL")
            self.assertIn("must be an object", result.failed[0])

    def test_historical_baseline_checkset_is_not_compared(self) -> None:
        baseline = {"baselines": [{"eval_id": 1, "accepted_pass_rate": 0.8}]}
        result = runner.CaseResult(1, "case", failed=["validation failed"])
        comparison = runner.baseline_comparison(baseline, requested=True)
        self.assertEqual(comparison["status"], "skipped_incompatible_version")
        self.assertFalse(comparison["comparable"])
        self.assertIsNone(comparison["baseline_trace_check_version"])
        self.assertEqual(runner.regression_check([result], baseline), [])
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            runner.print_summary([result], [], comparison)
        self.assertIn("Baseline comparison skipped", output.getvalue())
        self.assertNotIn("No trace-check regressions", output.getvalue())

    def test_matching_baseline_checkset_can_report_regression(self) -> None:
        baseline = {"trace_check_version": runner.TRACE_CHECK_VERSION,
                    "baselines": [{"eval_id": 1, "accepted_pass_rate": 0.8}]}
        comparison = runner.baseline_comparison(baseline, requested=True)
        self.assertEqual(comparison["status"], "compared")
        self.assertTrue(comparison["comparable"])
        result = runner.CaseResult(1, "case", failed=["validation failed"])
        self.assertEqual(len(runner.regression_check([result], baseline)), 1)

    def test_comparable_baseline_is_not_compared_without_request(self) -> None:
        comparison = runner.baseline_comparison({"trace_check_version": runner.TRACE_CHECK_VERSION}, requested=False)
        self.assertEqual(comparison["status"], "not_requested")
        self.assertTrue(comparison["comparable"])
        self.assertEqual(comparison["trace_check_version"], runner.TRACE_CHECK_VERSION)


class ImplementationAssuranceFixtureTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.script = self.root / "scripts/run_evals.py"
        self.script.parent.mkdir()
        shutil.copyfile(SCRIPT, self.script)
        self.evals = self.root / "evals"
        self.evals.mkdir()
        self.manifest = {"skill_name": "meridian-implementation-assurance", "evals": [
            {"id": 1, "scenario": "A", "description": "case", "prompt": "Do work", "assertions": ["Verify behavior"]}]}
        self.write_manifest(self.manifest)
        self.csv = self.evals / "meridian-implementation-assurance.prompts.csv"
        self.csv.write_text("id,should_trigger,prompt\nyes,true,Implement\nno,false,Review only\n", encoding="utf-8")
        (self.evals / "benchmark_baseline.json").write_text(json.dumps({
            "skill_name": "meridian-implementation-assurance", "baselines": [{"eval_id": 1, "accepted_pass_rate": 0.8}]}), encoding="utf-8")

    def write_manifest(self, value) -> None:
        (self.evals / "evals.json").write_text(json.dumps(value), encoding="utf-8")

    def invoke(self, *args: str):
        return subprocess.run([sys.executable, str(self.script), *args], capture_output=True, text=True, timeout=10)

    def test_dry_run_is_fixture_only_and_writes_no_traces(self) -> None:
        result = self.invoke("--all", "--dry-run", "--json")
        self.assertEqual(result.returncode, 0, result.stderr)
        output = json.loads(result.stdout)
        self.assertEqual(output["validation_scope"], "fixture_structure_only")
        self.assertFalse(output["behavior_evaluated"])
        self.assertFalse(output["trigger_selection_evaluated"])
        self.assertEqual(output["assertions_evaluated"], 0)
        self.assertEqual(output["trace_check_version"], runner.TRACE_CHECK_VERSION)
        self.assertEqual(output["baseline_comparison"]["status"], "not_requested")
        self.assertNotIn("score", output)
        self.assertFalse((self.evals / "artifacts").exists())

    def test_zero_eval_id_is_rejected(self) -> None:
        result = self.invoke("--eval-id", "0", "--dry-run")
        self.assertEqual(result.returncode, 2)
        self.assertIn("eval-id 0 not found", result.stderr)

    def test_malformed_eval_fixtures_fail_closed(self) -> None:
        invalid = [[], {"skill_name": "wrong", "evals": []},
                   {**self.manifest, "evals": ["case"]},
                   {**self.manifest, "evals": [{**self.manifest["evals"][0], "id": True}]},
                   {**self.manifest, "evals": self.manifest["evals"] * 2},
                   {**self.manifest, "evals": [{**self.manifest["evals"][0], "assertions": "verify"}]},
                   {**self.manifest, "evals": [{**self.manifest["evals"][0], "prompt": " "}]}]
        for value in invalid:
            with self.subTest(value=value):
                self.write_manifest(value)
                result = self.invoke("--all", "--dry-run")
                self.assertEqual(result.returncode, 2, result.stdout)
                self.assertIn("error:", result.stderr)

    def test_malformed_csv_fixtures_fail_closed(self) -> None:
        invalid = ["id,should_trigger\nx,true\n", "id,should_trigger,prompt\nx,maybe,Do work\n",
                   "id,should_trigger,prompt\nx,true,Do work\nx,false,Review\n",
                   "id,should_trigger,prompt\nx,true,\n", "id,should_trigger,prompt\nx,true,Do work,extra\n",
                   'id,should_trigger,prompt\nx,true,"unterminated\n']
        for value in invalid:
            with self.subTest(value=value):
                self.csv.write_text(value, encoding="utf-8")
                result = self.invoke("--all", "--dry-run")
                self.assertEqual(result.returncode, 2, result.stdout)

    def test_malformed_baseline_is_rejected(self) -> None:
        (self.evals / "benchmark_baseline.json").write_text("[]", encoding="utf-8")
        result = self.invoke("--all", "--dry-run")
        self.assertEqual(result.returncode, 2)
        self.assertIn("Baseline must identify", result.stderr)


if __name__ == "__main__":
    unittest.main()
