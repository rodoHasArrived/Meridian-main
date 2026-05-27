import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
DASHBOARD_ROOT = ROOT / "src" / "Meridian.Ui" / "dashboard"


class DashboardPackageLockTests(unittest.TestCase):
    def test_dashboard_checks_in_lockfile(self) -> None:
        self.assertTrue((DASHBOARD_ROOT / "package-lock.json").exists())

    def test_dashboard_package_json_is_valid_json(self) -> None:
        package = json.loads((DASHBOARD_ROOT / "package.json").read_text(encoding="utf-8"))
        self.assertIsInstance(package, dict)


if __name__ == "__main__":
    unittest.main()
