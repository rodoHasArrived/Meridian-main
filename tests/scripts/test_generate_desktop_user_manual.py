from __future__ import annotations

import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
SCRIPT_PATH = REPO_ROOT / "scripts" / "dev" / "generate-desktop-user-manual.ps1"


class GenerateDesktopUserManualTests(unittest.TestCase):
    def test_each_workflow_builds_unless_the_caller_requests_skip_build(self) -> None:
        script = SCRIPT_PATH.read_text(encoding="utf-8")

        skip_build_guard = """if ($SkipBuild) {
        $runnerArguments.SkipBuild = $true
    }"""

        self.assertIn(skip_build_guard, script)
        self.assertEqual(script.count("$runnerArguments.SkipBuild = $true"), 1)
        self.assertNotIn("$buildSkipped = $true", script)


if __name__ == "__main__":
    unittest.main()
