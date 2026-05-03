import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
DASHBOARD_ROOT = ROOT / "src" / "Meridian.Ui" / "dashboard"


class DashboardPackageLockTests(unittest.TestCase):
    def test_lockfile_keeps_linux_rollup_native_package_for_ci_builds(self) -> None:
        lock = json.loads((DASHBOARD_ROOT / "package-lock.json").read_text(encoding="utf-8"))
        packages = lock["packages"]

        rollup = packages["node_modules/rollup"]
        linux_native = packages.get("node_modules/@rollup/rollup-linux-x64-gnu")

        self.assertIsNotNone(linux_native)
        self.assertEqual(
            linux_native["version"],
            rollup["optionalDependencies"]["@rollup/rollup-linux-x64-gnu"],
        )
        self.assertTrue(linux_native["optional"])
        self.assertEqual(linux_native["os"], ["linux"])
        self.assertEqual(linux_native["cpu"], ["x64"])

    def test_dashboard_lockfile_does_not_pull_repo_root_as_dependency(self) -> None:
        lock = json.loads((DASHBOARD_ROOT / "package-lock.json").read_text(encoding="utf-8"))
        package = json.loads((DASHBOARD_ROOT / "package.json").read_text(encoding="utf-8"))
        root_package = lock["packages"][""]

        self.assertNotIn("meridian-tools", package.get("dependencies", {}))
        self.assertNotIn("meridian-tools", root_package.get("dependencies", {}))
        self.assertNotIn("../../..", lock["packages"])
        self.assertNotIn("node_modules/meridian-tools", lock["packages"])


if __name__ == "__main__":
    unittest.main()
