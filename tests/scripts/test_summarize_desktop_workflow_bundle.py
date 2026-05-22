from __future__ import annotations

import json
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
SCRIPT_PATH = REPO_ROOT / "scripts" / "dev" / "summarize-desktop-workflow-bundle.ps1"


class SummarizeDesktopWorkflowBundleTests(unittest.TestCase):
    def test_summary_handles_manifest_without_run_error(self) -> None:
        pwsh = shutil.which("pwsh") or shutil.which("powershell")
        if pwsh is None:
            self.skipTest("PowerShell is not available")

        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            bundle_path = root / "bundle"
            bundle_path.mkdir(parents=True, exist_ok=True)
            manifest_path = bundle_path / "manifest.json"
            stage_status_path = bundle_path / "stage-status.json"

            manifest_path.write_text(
                json.dumps({"workflow": {"name": "manual-overview"}, "run": {"status": "ok"}}),
                encoding="utf-8",
            )
            stage_status_path.write_text("[]", encoding="utf-8")

            command = [pwsh, "-NoProfile"]
            if Path(pwsh).name.lower().startswith("powershell"):
                command.extend(["-ExecutionPolicy", "Bypass"])
            command.extend(
                [
                    "-File",
                    str(SCRIPT_PATH),
                    "-BundlePath",
                    str(bundle_path),
                    "-ManifestPath",
                    str(manifest_path),
                ]
            )

            result = subprocess.run(
                command,
                cwd=REPO_ROOT,
                capture_output=True,
                text=True,
            )

            self.assertEqual(result.returncode, 0, result.stderr)

            summary_path = bundle_path / "bundle-summary.md"
            self.assertTrue(summary_path.exists())
            summary = summary_path.read_text(encoding="utf-8")
            self.assertIn("- Top failure cause: No failure recorded.", summary)


if __name__ == "__main__":
    unittest.main()
