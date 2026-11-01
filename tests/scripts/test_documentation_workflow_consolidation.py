"""Protect the lean documentation gate and its existing validation ownership.

Run on a bash-enabled tooling host. The repository script-test lane discovers
this suite; it does not introduce another workflow or required check.
"""
from __future__ import annotations

import ast
import os
import subprocess
import tempfile
import unittest
from pathlib import Path

import yaml

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github/workflows/documentation.yml"


def assignment(path: Path, name: str):
    """Inspect the canonical catalog without importing/executing its tooling."""
    for node in ast.parse(path.read_text(encoding="utf-8")).body:
        if isinstance(node, ast.AnnAssign) and isinstance(node.target, ast.Name):
            if node.target.id == name:
                return node.value
        if isinstance(node, ast.Assign):
            if any(isinstance(t, ast.Name) and t.id == name for t in node.targets):
                return node.value
    raise AssertionError(f"Missing catalog {name} in {path}")


class DocumentationWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        # BaseLoader preserves the Actions 'on' key instead of YAML 1.1's boolean.
        cls.workflow = yaml.load(WORKFLOW.read_text(encoding="utf-8"), Loader=yaml.BaseLoader)
        cls.jobs = cls.workflow["jobs"]
        cls.gate = cls.jobs["validate-docs"]
        cls.producer = cls.jobs["regenerate-docs"]
        cls.commands = [s["run"] for s in cls.producer["steps"] if "run" in s]

    def gate_result(self, result: str | None) -> subprocess.CompletedProcess:
        step, = self.gate["steps"]
        with tempfile.TemporaryDirectory() as directory:
            env = {**os.environ, "GITHUB_STEP_SUMMARY": str(Path(directory) / "summary.md")}
            env.pop("DOCS_RESULT", None)
            if result is not None:
                env["DOCS_RESULT"] = result
            return subprocess.run(
                ["bash", "--noprofile", "--norc", "-c", step["run"]],
                env=env, capture_output=True, text=True, timeout=10, check=False,
            )

    def test_existing_check_names_and_events_are_retained(self):
        self.assertEqual(set(self.jobs), {"validate-docs", "regenerate-docs"})
        for name, job in self.jobs.items():
            self.assertEqual(job.get("name", name), name)
        events = self.workflow["on"]
        self.assertEqual(set(events), {"pull_request", "push", "merge_group", "workflow_dispatch"})
        self.assertEqual(events["pull_request"]["types"], ["opened", "synchronize", "reopened"])
        self.assertEqual(events["push"]["branches"], ["main"])
        for event in events.values():
            if isinstance(event, dict):
                self.assertNotIn("paths", event)
                self.assertNotIn("paths-ignore", event)

    def test_same_run_gate_always_evaluates_the_producer(self):
        self.assertEqual(self.gate["needs"], "regenerate-docs")
        self.assertIn(self.gate["if"], ("always()", "${{ always() }}"))
        step, = self.gate["steps"]
        self.assertEqual(step["shell"], "bash")
        self.assertEqual(step["env"]["DOCS_RESULT"], "${{ needs.regenerate-docs.result }}")
        self.assertNotIn("${{", step["run"])

    def test_success_passes(self):
        self.assertEqual(self.gate_result("success").returncode, 0)

    def test_non_success_cannot_turn_green(self):
        for result in ("failure", "cancelled", "skipped", "timed_out", "neutral", "", "success\n"):
            with self.subTest(result=result):
                self.assertNotEqual(self.gate_result(result).returncode, 0)

    def test_missing_result_fails_closed(self):
        self.assertNotEqual(self.gate_result(None).returncode, 0)

    def test_gate_has_no_checkout_install_or_elevated_permissions(self):
        self.assertEqual(self.workflow["permissions"], {"contents": "read"})
        self.assertTrue(all("uses" not in s for s in self.gate["steps"]))
        for job in self.jobs.values():
            self.assertNotIn("permissions", job)
        checkouts = [s for s in self.producer["steps"] if s.get("uses", "").startswith("actions/checkout@")]
        self.assertEqual(len(checkouts), 1)
        self.assertEqual(checkouts[0]["with"]["persist-credentials"], "false")
        self.assertEqual(checkouts[0]["with"]["fetch-depth"], "0")

    def test_unique_rules_and_memory_commands_still_run(self):
        self.assertIn("python3 build/scripts/docs/rules-engine.py --rules build/rules/doc-rules.yaml --summary", self.commands)
        memory = next(c for c in self.commands if "check-codex-memory.py" in c)
        self.assertEqual(memory.strip().splitlines(), [
            "python build/scripts/docs/check-codex-memory.py --summary",
            "python build/scripts/docs/check-codex-memory.py --task .codex/memory/tasks/example.yml --receipt --summary",
            "python build/scripts/docs/check-codex-memory.py --goal .codex/memory/goals/example.yml --receipt --summary",
            "python -m unittest build.scripts.docs.tests.test_check_codex_memory",
        ])
        joined = "\n".join(self.commands)
        for duplicate in ("validate-examples.py", "check-ai-inventory.py", "check-ai-handoff.py", "check-ai-contract-drift.py"):
            self.assertNotIn(duplicate, joined)

    def test_generation_and_freshness_are_still_mandatory(self):
        generation = "python3 build/scripts/run-maintained-generation.py --docs-only --report artifacts/docs/generation.json"
        freshness = "python3 build/scripts/ci/check-generated-output.py"
        self.assertEqual(self.commands.count(generation), 1)
        self.assertEqual(self.commands.count(freshness), 1)
        self.assertLess(self.commands.index(generation), self.commands.index(freshness))
        self.assertIn("git diff --check", self.commands)
        for job in self.jobs.values():
            self.assertNotIn("continue-on-error", job)
            for step in job["steps"]:
                self.assertNotIn("continue-on-error", step)
        for step in self.producer["steps"]:
            if "run" in step:
                self.assertNotIn("if", step)

    def test_dashboard_regression_and_repair_artifact_are_retained(self):
        delta = next(c for c in self.commands if "baseline_available" in c)
        self.assertIn("delta < -10.0", delta)
        self.assertIn("raise SystemExit", delta)
        upload, = [s for s in self.producer["steps"] if s.get("uses", "").startswith("actions/upload-artifact@")]
        self.assertEqual(upload["if"], "always()")
        self.assertEqual(upload["with"]["name"], "documentation-repair-${{ github.run_id }}-${{ github.run_attempt }}")
        self.assertIn("artifacts/docs/", upload["with"]["path"])
        self.assertIn("artifacts/maintained-generation/", upload["with"]["path"])

    def test_manual_runs_are_not_cancelled_by_ordinary_pushes(self):
        concurrency = self.workflow["concurrency"]
        self.assertIn("github.event_name", concurrency["group"])
        self.assertIn("github.run_id", concurrency["group"])
        self.assertEqual(concurrency["cancel-in-progress"], "${{ github.event_name != 'workflow_dispatch' }}")


class CanonicalDocumentationOwnershipTests(unittest.TestCase):
    def test_generation_requires_core_profile(self):
        catalog = assignment(ROOT / "build/scripts/run-maintained-generation.py", "DOCS_STEPS")
        steps = [call for call in catalog.elts if ast.literal_eval(call.args[0]) == "Run docs automation profile"]
        step, = steps
        command = ast.literal_eval(step.args[1])
        self.assertEqual(command[0:4], ("python3", "build/scripts/docs/run-docs-automation.py", "--profile", "core"))
        self.assertLess(len(step.args), 3)  # No optional=True positional escape.
        self.assertFalse(step.keywords)

    def test_core_retains_all_four_removed_validators(self):
        path = ROOT / "build/scripts/docs/run-docs-automation.py"
        profiles = ast.literal_eval(assignment(path, "PROFILE_CONFIG"))
        catalog = assignment(path, "SCRIPT_CONFIG")
        scripts = {ast.literal_eval(k): v for k, v in zip(catalog.keys, catalog.values)}
        expected = {
            "validate-examples": "validate-examples.py",
            "check-ai-inventory": "check-ai-inventory.py",
            "check-ai-handoff-strict": "check-ai-handoff.py",
            "check-ai-contract-drift": "check-ai-contract-drift.py",
        }
        for name, filename in expected.items():
            with self.subTest(name=name):
                self.assertEqual(profiles["core"].count(name), 1)
                self.assertEqual(ast.literal_eval(scripts[name])["script"], filename)
        self.assertIn("--strict", ast.literal_eval(scripts["check-ai-handoff-strict"])["args"])


if __name__ == "__main__":
    unittest.main()
