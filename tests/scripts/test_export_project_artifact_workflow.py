"""Keep artifact exports aligned with the maintained publish workflow.

Generic repository archives were retired in the May 2026 workflow consolidation.
The export safety contract now covers scoped build output and commit-bound evidence.
"""

import importlib.util
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import yaml


REPO_ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "export_workflow_hygiene", REPO_ROOT / "build/scripts/ci/check-workflow-hygiene.py"
)
assert SPEC and SPEC.loader
hygiene = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(hygiene)


class ExportProjectArtifactWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.workflow = yaml.load(
            (REPO_ROOT / ".github/workflows/publish-smoke.yml").read_text(encoding="utf-8"),
            Loader=yaml.BaseLoader,
        )
        cls.steps = cls.workflow["jobs"]["publish"]["steps"]

    def test_generic_repository_export_remains_retired(self) -> None:
        self.assertIn("export-project-artifact.yml", hygiene.OLD_WORKFLOW_FILENAMES)
        failures: list[str] = []
        hygiene.check_removed_workflows_stay_retired(failures)
        self.assertEqual(failures, [])

    def test_hygiene_rejects_reintroduced_retired_workflow(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            workflow_dir = Path(temporary)
            for filename in ("export-project-artifact.yml", "python-package-conda.yml"):
                with self.subTest(filename=filename):
                    restored = workflow_dir / filename
                    restored.write_text("name: Obsolete workflow\n", encoding="utf-8")
                    failures: list[str] = []
                    with patch.object(hygiene, "WORKFLOW_DIR", workflow_dir):
                        hygiene.check_removed_workflows_stay_retired(failures)
                    self.assertEqual(len(failures), 1)
                    self.assertIn(filename, failures[0])
                    restored.unlink()

    def test_publish_validates_selected_project_at_workflow_commit(self) -> None:
        checkout = next(step for step in self.steps if step.get("uses", "").startswith("actions/checkout@"))
        self.assertEqual(checkout["with"]["ref"], "${{ github.sha }}")
        self.assertEqual(checkout["with"]["persist-credentials"], "false")
        publish = next(step for step in self.steps if "build/scripts/publish/publish.ps1" in step.get("run", ""))
        self.assertIn("-Project $env:PROJECT", publish["run"])
        self.assertIn("-OutputDir artifacts/publish/publish-smoke", publish["run"])
        self.assertNotIn("if", publish)
        self.assertNotIn("continue-on-error", publish)
        self.assertLess(self.steps.index(checkout), self.steps.index(publish))

    def test_exports_only_publish_output_and_retains_failure_evidence(self) -> None:
        uploads = [step for step in self.steps if step.get("uses", "").startswith("actions/upload-artifact@")]
        self.assertEqual(len(uploads), 1)
        upload = uploads[0]
        self.assertEqual(upload["with"]["path"], "artifacts/publish/publish-smoke/")
        self.assertEqual(upload["if"], "always()")
        self.assertNotEqual(upload["with"].get("include-hidden-files"), "true")
        self.assertIn("${{ inputs.project }}", upload["with"]["name"])
        self.assertIn("${{ inputs.runtime }}", upload["with"]["name"])
        self.assertIn("${{ github.run_attempt }}", upload["with"]["name"])
        evidence = next(step for step in self.steps if "generate-release-evidence-manifest.py" in step.get("run", ""))
        self.assertIn("--artifact-root artifacts/publish/publish-smoke", evidence["run"])
        self.assertIn("--output artifacts/publish/publish-smoke/release-evidence.json", evidence["run"])
        self.assertNotIn("if", evidence)
        self.assertNotIn("continue-on-error", evidence)
        self.assertLess(self.steps.index(evidence), self.steps.index(upload))

    def test_standalone_export_fails_when_publish_output_is_missing(self) -> None:
        workflow = yaml.load(
            (REPO_ROOT / ".github/workflows/desktop-standalone-publish.yml").read_text(encoding="utf-8"),
            Loader=yaml.BaseLoader,
        )
        steps = workflow["jobs"]["publish-desktop-standalone"]["steps"]
        upload = next(step for step in steps if step.get("uses", "").startswith("actions/upload-artifact@"))
        self.assertEqual(upload["with"]["path"], "artifacts/publish/local-desktop/")
        self.assertEqual(upload["with"]["if-no-files-found"], "error")


if __name__ == "__main__":
    unittest.main()
