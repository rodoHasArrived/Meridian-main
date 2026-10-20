import os
import json
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

import yaml

from tests.scripts.workflow_assertions import assert_pinned_action


REPO_ROOT = Path(__file__).resolve().parents[2]
DOCUMENTATION_WORKFLOW = REPO_ROOT / ".github" / "workflows" / "documentation.yml"


class DocumentationWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.workflow = DOCUMENTATION_WORKFLOW.read_text(encoding="utf-8")

    def test_dashboard_diff_gate_skips_severe_failure_when_baseline_is_unavailable(self) -> None:
        self.assertIn("baseline_available = bool(previous.strip())", self.workflow)
        self.assertIn("'baseline_available': baseline_available", self.workflow)
        self.assertIn("'missing_previous_files': missing_previous", self.workflow)
        self.assertIn(
            "Baseline dashboard source was unavailable; severe regression gating was skipped",
            self.workflow,
        )
        self.assertIn("severe = baseline_available and (", self.workflow)

    def test_regenerate_docs_job_fetches_history_for_dashboard_diff(self) -> None:
        checkout = assert_pinned_action(self, self.workflow, "regenerate-docs", "actions/checkout")
        self.assertEqual(checkout.get("with", {}).get("persist-credentials"), "false")
        self.assertEqual(checkout.get("with", {}).get("fetch-depth"), "0")
        steps = yaml.load(self.workflow, Loader=yaml.BaseLoader)["jobs"]["regenerate-docs"]["steps"]
        comparisons = [
            step for step in steps
            if step.get("name") == "Compare dashboard readiness deltas vs previous commit"
        ]
        self.assertEqual(len(comparisons), 1, "Expected exactly one history comparison step")
        self.assertLess(steps.index(checkout), steps.index(comparisons[0]))

    def test_diagram_dependencies_use_root_lockfile(self) -> None:
        self.assertIn('"package-lock.json"', self.workflow)
        self.assertIn("npm ci --no-fund --no-audit", self.workflow)
        self.assertNotIn("npm install --no-fund --no-audit", self.workflow)

    def test_whole_repository_inputs_and_merge_groups_receive_drift_validation(self) -> None:
        events = yaml.load(self.workflow, Loader=yaml.BaseLoader)["on"]
        self.assertIn("merge_group", events)
        for event in ("pull_request", "push"):
            self.assertNotIn("paths", events[event])
            self.assertNotIn("paths-ignore", events[event])

    def test_drift_check_follows_convergence_and_exports_failure_evidence(self) -> None:
        steps = yaml.load(self.workflow, Loader=yaml.BaseLoader)["jobs"]["regenerate-docs"]["steps"]
        converge = next(step for step in steps if step.get("name") == "Converge maintained documentation")
        gate = next(step for step in steps if step.get("name") == "Check generated docs are current")
        upload = next(step for step in steps if step.get("name") == "Upload documentation repair evidence")
        self.assertLess(steps.index(converge), steps.index(gate))
        self.assertIn("--docs-only", converge["run"])
        self.assertIn("check-generated-output.py", gate["run"])
        self.assertEqual("always()", upload["if"])
        self.assertIn("artifacts/docs/", upload["with"]["path"])

    def test_consumers_share_generation_and_complete_drift_check(self) -> None:
        ci = (REPO_ROOT / "scripts/ci.sh").read_text(encoding="utf-8")
        jobs = yaml.load(self.workflow, Loader=yaml.BaseLoader)["jobs"]
        steps = jobs["regenerate-docs"]["steps"]
        runs = "\n".join(step.get("run", "") for step in steps)
        for consumer in (ci, runs):
            self.assertEqual(1, consumer.count("build/scripts/run-maintained-generation.py"))
            self.assertIn("--docs-only --report artifacts/docs/generation.json", consumer)
            self.assertEqual(1, consumer.count("build/scripts/ci/check-generated-output.py"))
            self.assertNotIn("render-roadmap-docs.py", consumer)
            self.assertNotIn("render-source-docs.py", consumer)
        validators = "\n".join(step.get("run", "") for step in jobs["validate-docs"]["steps"])
        for name in ("rules-engine.py", "validate-examples.py", "check-ai-inventory.py",
                     "check-codex-memory.py", "check-ai-handoff.py", "check-ai-contract-drift.py"):
            self.assertIn(name, validators)

    def test_manual_runs_are_isolated_from_push_and_from_other_dispatches(self) -> None:
        workflow = yaml.load(self.workflow, Loader=yaml.BaseLoader)
        concurrency = workflow["concurrency"]
        for identity in ("github.event_name", "github.event.pull_request.number", "github.run_id"):
            self.assertIn(identity, concurrency["group"])
        self.assertEqual("${{ github.event_name != 'workflow_dispatch' }}", concurrency["cancel-in-progress"])

    def test_lane_metadata_matches_hosted_documentation_timeout_and_evidence(self) -> None:
        manifest = json.loads((REPO_ROOT / "build/ci/lane-manifest.json").read_text())
        lane = next(lane for lane in manifest["lanes"] if lane["id"] == "verify-docs")
        workflow = yaml.load((REPO_ROOT / ".github/workflows/meridian-ci.yml").read_text(),
                             Loader=yaml.BaseLoader)
        job = workflow["jobs"]["verify-docs"]
        self.assertEqual(lane["timeoutMinutes"], int(job["timeout-minutes"]))
        upload = next(step for step in job["steps"]
                      if step.get("uses", "").startswith("actions/upload-artifact@"))
        self.assertEqual(set(lane["artifacts"]), set(upload["with"]["path"].splitlines()))

    def test_roadmap_keeps_schema_and_phase_validation_and_manual_standalone_generation(self) -> None:
        workflow = yaml.load(
            (REPO_ROOT / ".github/workflows/roadmap-source-docs.yml").read_text(),
            Loader=yaml.BaseLoader,
        )
        self.assertIn("Enforce phase scope", [
            step.get("name") for step in workflow["jobs"]["scope-gate"]["steps"]
        ])
        steps = workflow["jobs"]["schema"]["steps"]
        for name in ("Validate roadmap registry", "Validate source READMEs", "Scan source TODOs"):
            self.assertNotIn("if", next(step for step in steps if step.get("name") == name))
        for name in ("Converge maintained documentation", "Check generated docs are current"):
            step = next(step for step in steps if step.get("name") == name)
            self.assertEqual("github.event_name == 'workflow_dispatch'", step["if"])
        # Automatic generation can only be delegated to a workflow that runs on
        # every corresponding event, including non-main PRs and merge groups.
        events = yaml.load(self.workflow, Loader=yaml.BaseLoader)["on"]
        self.assertIn("merge_group", events)
        for event in ("pull_request", "push"):
            self.assertNotIn("paths", events[event])
            self.assertNotIn("paths-ignore", events[event])
        self.assertNotIn("branches", events["pull_request"])

    def test_canonical_lane_fails_for_generation_failure_or_freshness_drift(self) -> None:
        # Run the actual shell orchestrator with inert tools. This proves a failed
        # producer cannot reach a green freshness gate, and a stale-output verdict
        # cannot be swallowed by a later successful validator or EXIT summary.
        for failure in ("run-maintained-generation.py", "check-generated-output.py"):
            with self.subTest(failure=failure), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                (root / "scripts").mkdir()
                shutil.copyfile(REPO_ROOT / "scripts/ci.sh", root / "scripts/ci.sh")
                tools = root / "test-tools"
                tools.mkdir()
                stub = """#!/bin/bash
printf '%s\\n' "$*" >> "$CI_TEST_COMMAND_LOG"
if [[ "$*" == *"$CI_TEST_FAIL_COMMAND"* ]]; then exit 19; fi
exit 0
"""
                for executable in ("python", "python3", "node", "npm"):
                    path = tools / executable
                    path.write_text(stub)
                    path.chmod(0o755)
                log = root / "commands.log"
                result = subprocess.run(
                    ["bash", str(root / "scripts/ci.sh"), "--lane", "verify-docs"],
                    cwd=root, text=True, capture_output=True,
                    env={**os.environ, "PATH": str(tools) + os.pathsep + os.environ["PATH"],
                         "CI_TEST_COMMAND_LOG": str(log), "CI_TEST_FAIL_COMMAND": failure},
                )
                self.assertEqual(19, result.returncode, result.stdout + result.stderr)
                commands = log.read_text()
                self.assertIn("run-maintained-generation.py --docs-only", commands)
                if failure == "run-maintained-generation.py":
                    self.assertNotIn("check-generated-output.py", commands)
                else:
                    self.assertIn("check-generated-output.py", commands)


if __name__ == "__main__":
    unittest.main()
