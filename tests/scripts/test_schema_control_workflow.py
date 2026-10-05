import json
import os
import subprocess
import tempfile
import textwrap
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
WORKFLOW_PATH = REPO_ROOT / ".github" / "workflows" / "schema-control.yml"


class SchemaControlWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.workflow = WORKFLOW_PATH.read_text(encoding="utf-8")

    def test_workflow_uses_repository_pinned_actions(self) -> None:
        self.assertIn("uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1", self.workflow)
        self.assertIn("persist-credentials: false", self.workflow)
        self.assertIn("fetch-depth: 0", self.workflow)
        self.assertIn("uses: actions/setup-python@5fda3b95a4ea91299a34e894583c3862153e4b97", self.workflow)
        self.assertIn('python-version: "3.12"', self.workflow)
        self.assertIn("uses: actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a", self.workflow)

    def test_workflow_runs_postgres_16_service(self) -> None:
        for expected in [
            "image: postgres:16.14-alpine",
            "POSTGRES_USER: meridian",
            "POSTGRES_DB: meridian_schema_control",
            "pg_isready -U meridian -d meridian_schema_control",
            "postgresql://meridian:meridian@localhost:5432/meridian_schema_control",
        ]:
            self.assertIn(expected, self.workflow)

    def test_workflow_is_read_only_and_cancels_superseded_runs(self) -> None:
        self.assertIn("permissions:\n  contents: read", self.workflow)
        self.assertNotIn("contents: write", self.workflow)
        self.assertNotIn("pull-requests: write", self.workflow)
        self.assertIn(
            "group: schema-control-${{ github.event.pull_request.number || github.ref }}",
            self.workflow,
        )
        self.assertIn("cancel-in-progress: true", self.workflow)

    def test_workflow_supports_check_and_snapshot_modes(self) -> None:
        self.assertIn("workflow_dispatch:", self.workflow)
        self.assertIn("type: choice", self.workflow)
        self.assertIn("default: check", self.workflow)
        self.assertIn("          - check", self.workflow)
        self.assertIn("          - snapshot", self.workflow)
        self.assertIn("inputs.mode == 'check'", self.workflow)
        self.assertIn("inputs.mode == 'snapshot'", self.workflow)
        self.assertIn(
            'baseline_ref:\n        description: "Explicit baseline commit SHA or Git ref (resolved once per run)"\n'
            "        required: true\n        type: string",
            self.workflow,
        )

    def test_workflow_resolves_event_baseline_before_tests_and_records_revisions(self) -> None:
        self.assertIn("PR_BASE_SHA: ${{ github.event.pull_request.base.sha }}", self.workflow)
        self.assertIn("MANUAL_BASELINE_REF: ${{ inputs.baseline_ref }}", self.workflow)
        self.assertLess(
            self.workflow.index("- name: Resolve schema control revisions"),
            self.workflow.index("- name: Test schema-control tooling"),
        )
        self.assertIn("build/schema-control/revisions.json", self.workflow)
        self.assertIn("Candidate SHA (checked-out commit)", self.workflow)
        self.assertNotIn('ref: ${{ github.event.pull_request.head.sha }}', self.workflow)

    def test_workflow_watches_schema_contract_and_generated_doc_paths(self) -> None:
        for watched_path in [
            "src/Meridian.Storage/**/Migrations/**",
            "src/Meridian.Storage/Migrations/**",
            "src/Meridian.Storage/**/*MigrationRunner.cs",
            "src/Meridian.Identity/**/Migrations/**",
            "src/Meridian.Identity/**/*.cs",
            "src/Meridian.Contracts/**",
            "tools/schema_control/**",
            "build/scripts/schema-control.py",
            "tests/scripts/test_schema_control_*.py",
            "database/**",
            "docs/reference/database-schema.md",
            "docs/generated/database/**",
            ".github/workflows/schema-control.yml",
        ]:
            self.assertIn(f'"{watched_path}"', self.workflow)

    def test_workflow_installs_and_runs_schema_control_wrapper(self) -> None:
        self.assertIn(
            "python -m pip install --requirement tools/schema_control/requirements.txt",
            self.workflow,
        )
        self.assertIn("test_schema_control*.py", self.workflow)
        self.assertIn("python build/scripts/schema-control.py verify", self.workflow)
        self.assertNotIn('--base-ref "origin/main"', self.workflow)
        self.assertEqual(self.workflow.count('--base-ref "$BASELINE_SHA"'), 2)
        self.assertEqual(
            self.workflow.count("BASELINE_SHA: ${{ steps.revisions.outputs.baseline_sha }}"),
            2,
        )
        self.assertIn("python build/scripts/schema-control.py snapshot", self.workflow)
        self.assertGreaterEqual(
            self.workflow.count("--candidate-root build/schema-control/candidate"),
            2,
        )

    def test_workflow_always_publishes_summary_and_artifacts(self) -> None:
        self.assertIn(
            'summary_path="build/schema-control/candidate/reports/summary.md"',
            self.workflow,
        )
        self.assertIn('cat "$summary_path" >> "$GITHUB_STEP_SUMMARY"', self.workflow)
        self.assertGreaterEqual(self.workflow.count("if: always()"), 2)
        self.assertIn("path: build/schema-control/", self.workflow)
        self.assertIn("if-no-files-found: ignore", self.workflow)
        self.assertIn("retention-days: 14", self.workflow)

    @staticmethod
    def _git(root: Path, *args: str) -> str:
        return subprocess.run(
            ["git", *args], cwd=root, text=True, capture_output=True, check=True
        ).stdout.strip()

    def _init_repository(self, root: Path) -> tuple[str, str]:
        self._git(root, "init")
        self._git(root, "config", "user.name", "Schema control test")
        self._git(root, "config", "user.email", "schema-control@example.invalid")
        self._git(root, "commit", "--allow-empty", "-m", "baseline")
        baseline_sha = self._git(root, "rev-parse", "HEAD")
        self._git(root, "commit", "--allow-empty", "-m", "candidate")
        return baseline_sha, self._git(root, "rev-parse", "HEAD")

    def _run_revision_step(
        self, root: Path, *, event: str, pr_base: str = "", manual_base: str = ""
    ) -> subprocess.CompletedProcess[str]:
        step = self.workflow.split("      - name: Resolve schema control revisions\n", 1)[1]
        step = step.split("\n      - name:", 1)[0]
        script = textwrap.dedent(step.split("        run: |\n", 1)[1])
        output = root / "github-output"
        summary = root / "github-summary"
        output.write_text("", encoding="utf-8")
        summary.write_text("", encoding="utf-8")
        return subprocess.run(
            ["bash", "-e", "-o", "pipefail", "-c", script],
            cwd=root,
            env={
                **os.environ,
                "EVENT_NAME": event,
                "PR_BASE_SHA": pr_base,
                "MANUAL_BASELINE_REF": manual_base,
                "GITHUB_OUTPUT": str(output),
                "GITHUB_STEP_SUMMARY": str(summary),
            },
            text=True,
            capture_output=True,
            check=False,
        )

    def test_pr_revision_evidence_keeps_event_base_when_origin_main_advances(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            baseline_sha, candidate_sha = self._init_repository(root)
            self._git(root, "update-ref", "refs/remotes/origin/main", baseline_sha)

            result = self._run_revision_step(root, event="pull_request", pr_base=baseline_sha)
            self.assertEqual(0, result.returncode, result.stderr)
            evidence_path = root / "build/schema-control/revisions.json"
            before = evidence_path.read_bytes()
            self.assertEqual(
                {
                    "format": "meridian.schema-control-revisions.v1",
                    "baseline_sha": baseline_sha,
                    "candidate_sha": candidate_sha,
                },
                json.loads(before),
            )

            advanced_sha = self._git(
                root, "commit-tree", "HEAD^{tree}", "-p", "HEAD", "-m", "advance main"
            )
            self._git(root, "update-ref", "refs/remotes/origin/main", advanced_sha)
            result = self._run_revision_step(root, event="pull_request", pr_base=baseline_sha)
            self.assertEqual(0, result.returncode, result.stderr)
            self.assertEqual(before, evidence_path.read_bytes())
            self.assertIn(
                f"baseline_sha={baseline_sha}",
                (root / "github-output").read_text(encoding="utf-8"),
            )
            summary = (root / "github-summary").read_text(encoding="utf-8")
            self.assertIn(baseline_sha, summary)
            self.assertIn(candidate_sha, summary)

    def test_manual_revision_step_resolves_explicit_ref_to_commit(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            baseline_sha, candidate_sha = self._init_repository(root)
            self._git(root, "update-ref", "refs/heads/review-baseline", baseline_sha)

            result = self._run_revision_step(
                root,
                event="workflow_dispatch",
                manual_base="review-baseline",
                pr_base=candidate_sha,
            )

            self.assertEqual(0, result.returncode, result.stderr)
            evidence = json.loads(
                (root / "build/schema-control/revisions.json").read_text(encoding="utf-8")
            )
            self.assertEqual(baseline_sha, evidence["baseline_sha"])
            self.assertEqual(candidate_sha, evidence["candidate_sha"])

    def test_manual_revision_step_rejects_missing_and_invalid_baselines(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self._init_repository(root)
            for baseline_ref in ("", "missing-ref", "$(touch injected)", "--help"):
                with self.subTest(baseline_ref=baseline_ref):
                    result = self._run_revision_step(
                        root, event="workflow_dispatch", manual_base=baseline_ref
                    )
                    self.assertNotEqual(0, result.returncode)
                    self.assertFalse((root / "build/schema-control/revisions.json").exists())
                    self.assertFalse((root / "injected").exists())


if __name__ == "__main__":
    unittest.main()
