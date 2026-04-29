import json
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
REFRESH_SCREENSHOTS_WORKFLOW = REPO_ROOT / ".github" / "workflows" / "refresh-screenshots.yml"
DASHBOARD_LOCKFILE = REPO_ROOT / "src" / "Meridian.Ui" / "dashboard" / "package-lock.json"


class RefreshScreenshotsWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.workflow = REFRESH_SCREENSHOTS_WORKFLOW.read_text(encoding="utf-8")
        cls.dashboard_lock = json.loads(DASHBOARD_LOCKFILE.read_text(encoding="utf-8"))

    def test_web_screenshot_job_installs_optional_native_packages(self) -> None:
        self.assertIn("run: npm ci --include=optional", self.workflow)

        packages = self.dashboard_lock["packages"]
        self.assertIn("node_modules/rollup", packages)
        self.assertIn("node_modules/@rollup/rollup-linux-x64-gnu", packages)
        self.assertIn("@rollup/rollup-linux-x64-gnu", packages["node_modules/rollup"]["optionalDependencies"])

    def test_wpf_screenshot_job_downloads_prebuilt_binaries_under_src(self) -> None:
        self.assertIn("name: wpf-build-binaries", self.workflow)
        self.assertIn("path: src", self.workflow)


if __name__ == "__main__":
    unittest.main()
