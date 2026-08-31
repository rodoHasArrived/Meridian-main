import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
WORKFLOW_PATH = REPO_ROOT / ".github" / "workflows" / "schema-control.yml"


class SchemaControlWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.workflow = WORKFLOW_PATH.read_text(encoding="utf-8")

    def test_workflow_uses_repository_pinned_actions(self) -> None:
        self.assertIn("uses: actions/checkout@v6.0.2", self.workflow)
        self.assertIn("persist-credentials: false", self.workflow)
        self.assertIn("fetch-depth: 0", self.workflow)
        self.assertIn("uses: actions/setup-python@v7.0.0", self.workflow)
        self.assertIn('python-version: "3.12"', self.workflow)
        self.assertIn("uses: actions/upload-artifact@v7.0.1", self.workflow)

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
        self.assertGreaterEqual(self.workflow.count('--base-ref "origin/main"'), 2)
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


if __name__ == "__main__":
    unittest.main()
