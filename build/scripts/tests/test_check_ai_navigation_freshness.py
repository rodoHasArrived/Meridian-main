from __future__ import annotations

import importlib.util
import json
import sys
import tempfile
import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path


SCRIPT_PATH = Path(__file__).resolve().parents[1] / "docs" / "check-ai-navigation-freshness.py"
SPEC = importlib.util.spec_from_file_location("check_ai_navigation_freshness", SCRIPT_PATH)
assert SPEC and SPEC.loader
freshness = importlib.util.module_from_spec(SPEC)
sys.modules["check_ai_navigation_freshness"] = freshness
SPEC.loader.exec_module(freshness)


class CheckAiNavigationFreshnessTests(unittest.TestCase):
    def test_artifact_age_days_uses_generated_at(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / "repo-navigation.json"
            generated_at = (datetime.now(timezone.utc) - timedelta(days=3)).strftime("%Y-%m-%dT%H:%M:%SZ")
            path.write_text(json.dumps({"generatedAt": generated_at}), encoding="utf-8")

            age = freshness.artifact_age_days(path)
            self.assertGreaterEqual(age, 2)
            self.assertLessEqual(age, 3)

    def test_main_fails_when_navigation_is_stale(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / "repo-navigation.json"
            generated_at = (datetime.now(timezone.utc) - timedelta(days=30)).strftime("%Y-%m-%dT%H:%M:%SZ")
            path.write_text(json.dumps({"generatedAt": generated_at}), encoding="utf-8")

            original_argv = sys.argv[:]
            try:
                sys.argv = [
                    "check-ai-navigation-freshness.py",
                    "--navigation-json",
                    str(path),
                    "--max-age-days",
                    "14",
                ]
                self.assertEqual(freshness.main(), 1)
            finally:
                sys.argv = original_argv


if __name__ == "__main__":
    unittest.main()
