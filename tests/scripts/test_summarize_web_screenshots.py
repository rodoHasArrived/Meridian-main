import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
SCRIPT = REPO_ROOT / "scripts/dev/summarize-web-screenshots.py"


class SummarizeWebScreenshotsTests(unittest.TestCase):
    def run_summary(self, manifest, *, raw=False, environment=None):
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            manifest_path = root / "manifest.json"
            if manifest is not None:
                manifest_path.write_text(manifest if raw else json.dumps(manifest), encoding="utf-8")
            output = root / "nested" / "summary.md"
            github_summary = root / "github-summary.md"
            github_summary.write_text("Existing summary\n", encoding="utf-8")
            result = subprocess.run([
                sys.executable, str(SCRIPT), "--manifest", str(manifest_path),
                "--output", str(output), "--github-step-summary", str(github_summary),
            ], capture_output=True, text=True, cwd=REPO_ROOT, env={**os.environ, **(environment or {})})
            self.assertEqual(0, result.returncode, result.stderr)
            content = output.read_text(encoding="utf-8")
            self.assertEqual(content, result.stdout)
            self.assertEqual("Existing summary\n" + content, github_summary.read_text(encoding="utf-8"))
            return content

    def test_success_reports_route_attempts_and_duration(self):
        content = self.run_summary({
            "status": "passed", "selectedCaptureCount": 1, "durationSeconds": 4.25,
            "captures": [{"name": "trading", "route": "/trading", "status": "passed", "attempts": 2, "durationSeconds": 3.5}],
        }, environment={
            "CAPTURE_OUTCOME": "success", "VALIDATION_OUTCOME": "success", "PUBLISH_OUTCOME": "skipped",
            "SOURCE_SHA": "abc123", "GITHUB_SERVER_URL": "https://github.com", "GITHUB_REPOSITORY": "owner/repo",
            "GITHUB_RUN_ID": "42", "GITHUB_RUN_ATTEMPT": "2",
        })
        self.assertIn("1 passed · 0 failed · 0 not attempted", content)
        self.assertIn("| trading (/trading) | passed | 2 | 3.50s |", content)
        self.assertIn("Validation: **success**", content)
        self.assertIn("Refresh PR: **skipped**", content)
        self.assertIn("Source commit: abc123", content)
        self.assertIn("https://github.com/owner/repo/actions/runs/42/attempts/2", content)

    def test_partial_run_counts_from_route_results_instead_of_stale_counts(self):
        content = self.run_summary({
            "status": "failed", "selectedCaptureCount": 4, "capturedCount": 99,
            "error": "Browser disconnected\nFull stack trace",
            "captures": [
                {"name": "trading", "route": "/trading", "status": "passed", "attempts": 1},
                {"name": "data", "route": "/data", "status": "failed", "attempts": 2, "error": "Selector timed out\nStack trace"},
            ],
        })
        self.assertIn("1 passed · 1 failed · 2 not attempted", content)
        self.assertIn("Run error: Browser disconnected", content)
        self.assertIn("| data (/data) | failed | 2 | — | Selector timed out |", content)
        self.assertNotIn("Stack trace", content)
        self.assertNotIn("Full stack trace", content)

    def test_missing_manifest_is_diagnostic_and_does_not_claim_capture_success(self):
        content = self.run_summary(None, environment={"CAPTURE_OUTCOME": "skipped"})
        self.assertIn("No capture manifest was produced", content)
        self.assertIn("Capture: **skipped**", content)
        self.assertNotIn("passed ·", content)

    def test_malformed_manifest_is_diagnostic(self):
        for manifest in ("{broken", "[]", '{"captures": [1]}'):
            with self.subTest(manifest=manifest):
                content = self.run_summary(manifest, raw=True)
                self.assertIn("The capture manifest could not be read", content)
                self.assertNotIn("passed ·", content)

    def test_manifest_text_cannot_inject_html_or_markdown_and_is_bounded(self):
        content = self.run_summary({
            "status": "failed", "error": "<script>alert('x')</script>" + "x" * 2000,
            "captures": [{"name": "[link](evil) | `code`", "route": "/<img>", "status": "failed", "error": "a|b **bold**\n| injected |"}],
        })
        self.assertNotIn("<script>", content)
        self.assertNotIn("<img>", content)
        self.assertNotIn("[link]", content)
        self.assertNotIn("`code`", content)
        self.assertIn("&lt;script&gt;", content)
        self.assertIn("a&#124;b &#42;&#42;bold&#42;&#42;", content)
        self.assertNotIn("injected", content)
        self.assertNotIn("x" * 601, content)


if __name__ == "__main__":
    unittest.main()
