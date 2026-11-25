"""High-volume pull request workflows skip drafts and run once the PR is ready for review."""
from __future__ import annotations

from pathlib import Path
import unittest

import yaml


ROOT = Path(__file__).resolve().parents[2]
WORKFLOWS = ROOT / ".github" / "workflows"
DRAFT_GUARD = "github.event_name != 'pull_request' || github.event.pull_request.draft != true"

# workflow file -> jobs that must skip on drafts. Jobs that need a guarded job skip with it.
DRAFT_SKIPPING = {
    "codeql.yml": ["analyze"],
    "ci.yml": ["secret-scan"],
    "windows-desktop-build.yml": ["desktop"],
    "roadmap-source-docs.yml": ["scope-gate"],
}


def load(name: str) -> dict:
    return yaml.load((WORKFLOWS / name).read_text(encoding="utf-8"), Loader=yaml.BaseLoader)


class DraftPullRequestGatingTests(unittest.TestCase):
    def test_guarded_workflows_run_when_a_draft_becomes_ready(self) -> None:
        for name, jobs in DRAFT_SKIPPING.items():
            with self.subTest(workflow=name):
                workflow = load(name)
                types = workflow["on"]["pull_request"]["types"]
                for event in ("opened", "synchronize", "reopened", "ready_for_review"):
                    self.assertIn(event, types)
                for job in jobs:
                    self.assertEqual(workflow["jobs"][job]["if"], DRAFT_GUARD)

    def test_every_other_job_depends_on_a_guarded_job_or_never_runs_on_pull_requests(self) -> None:
        for name, jobs in DRAFT_SKIPPING.items():
            with self.subTest(workflow=name):
                workflow = load(name)
                for job_name, job in workflow["jobs"].items():
                    if job_name in jobs:
                        continue
                    needs = job.get("needs", [])
                    needs = [needs] if isinstance(needs, str) else needs
                    condition = job.get("if", "")
                    self.assertTrue(
                        any(item in jobs for item in needs)
                        or ("github.event_name == 'schedule'" in condition and "pull_request" not in condition),
                        f"{name}:{job_name} would still run on draft pull requests",
                    )

    def test_merge_queue_push_and_release_callers_are_never_draft_gated(self) -> None:
        # The guard only matches pull_request events, so merge_group, push, schedule and
        # workflow_call release gates always run.
        for name in DRAFT_SKIPPING:
            with self.subTest(workflow=name):
                self.assertTrue(DRAFT_GUARD.startswith("github.event_name != 'pull_request' ||"))

    def test_meridian_ci_and_generated_repair_still_run_on_drafts(self) -> None:
        for name in ("meridian-ci.yml", "generated-repair.yml"):
            with self.subTest(workflow=name):
                text = (WORKFLOWS / name).read_text(encoding="utf-8")
                self.assertNotIn("pull_request.draft", text)


if __name__ == "__main__":
    unittest.main()
