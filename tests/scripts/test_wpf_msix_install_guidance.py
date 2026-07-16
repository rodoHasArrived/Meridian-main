import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
INSTALL_SCRIPT_PATH = REPO_ROOT / "build" / "scripts" / "install" / "install.ps1"


class WpfMsixInstallGuidanceTests(unittest.TestCase):
    def setUp(self) -> None:
        self.script = INSTALL_SCRIPT_PATH.read_text(encoding="utf-8")

    def test_success_summary_points_to_existing_packaging_guide(self) -> None:
        guide_path = REPO_ROOT / "docs" / "operators" / "deployment-packaging.md"

        self.assertIn("docs/operators/deployment-packaging.md", self.script)
        self.assertNotIn("docs/operations/msix-packaging.md", self.script)
        self.assertTrue(guide_path.exists())

    def test_success_summary_mentions_desktop_shortcut_launch(self) -> None:
        self.assertIn("Available in Start Menu and Desktop shortcut", self.script)
        self.assertIn("Open the 'Meridian' Desktop shortcut or search Start Menu", self.script)

    def test_help_switch_is_supported_by_script(self) -> None:
        self.assertIn("[switch]$Help", self.script)
        self.assertIn(".\\install.ps1 -Help", self.script)
        self.assertIn("if ($Help)", self.script)


if __name__ == "__main__":
    unittest.main()
