import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
WORKFLOW_PATH = REPO_ROOT / ".github" / "workflows" / "export-project-artifact.yml"


class ExportProjectArtifactWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.workflow = WORKFLOW_PATH.read_text(encoding="utf-8")

    def test_archives_from_git_object_instead_of_live_worktree(self) -> None:
        self.assertIn(
            'git archive --format=tar.gz --output="${{ steps.metadata.outputs.base_name }}.tar.gz" "$GITHUB_SHA"',
            self.workflow,
        )
        self.assertIn(
            'git archive --format=zip --output="${{ steps.metadata.outputs.base_name }}.zip" "$GITHUB_SHA"',
            self.workflow,
        )
        self.assertNotIn("tar --exclude='.git' -czf", self.workflow)
        self.assertNotIn("zip -r", self.workflow)

    def test_upload_step_still_accepts_selected_archive_format(self) -> None:
        self.assertIn("${{ steps.metadata.outputs.base_name }}.tar.gz", self.workflow)
        self.assertIn("${{ steps.metadata.outputs.base_name }}.zip", self.workflow)
        self.assertIn("if-no-files-found: error", self.workflow)


if __name__ == "__main__":
    unittest.main()
