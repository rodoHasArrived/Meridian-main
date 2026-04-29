from __future__ import annotations

import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
SCRIPT_PATH = REPO_ROOT / "scripts" / "dev" / "cleanup-generated.ps1"


@unittest.skipIf(os.name != "nt", "cleanup-generated.ps1 cleanup behavior is validated on Windows")
class CleanupGeneratedScriptTests(unittest.TestCase):
    def test_includes_isolated_and_publish_artifacts_without_tracked_content(self) -> None:
        powershell = shutil.which("pwsh") or shutil.which("powershell")
        if powershell is None:
            self.skipTest("PowerShell is not available")

        with tempfile.TemporaryDirectory() as temp_dir:
            repo_root = Path(temp_dir)
            script_copy = repo_root / "scripts" / "dev" / "cleanup-generated.ps1"
            script_copy.parent.mkdir(parents=True)
            shutil.copy2(SCRIPT_PATH, script_copy)

            self._write_file(repo_root / "tracked.txt", "tracked")
            self._write_file(repo_root / "artifacts" / "provider-validation" / "keep.json", "{}")
            self._write_file(repo_root / "artifacts" / "bin" / "run-a" / "output.dll", "bin")
            self._write_file(repo_root / "artifacts" / "obj" / "run-a" / "output.obj", "obj")
            self._write_file(repo_root / "artifacts" / "publish" / "run-a" / "app.exe", "publish")

            subprocess.run(["git", "init"], cwd=repo_root, check=True, capture_output=True, text=True)
            subprocess.run(
                ["git", "add", "tracked.txt", "artifacts/provider-validation/keep.json"],
                cwd=repo_root,
                check=True,
                capture_output=True,
                text=True,
            )

            preview = self._run_script(powershell, script_copy, repo_root)

            self.assertEqual(preview.returncode, 0, preview.stderr)
            self.assertIn("Isolated MSBuild output", preview.stdout)
            self.assertIn("Isolated MSBuild intermediate output", preview.stdout)
            self.assertIn("Generated publish output", preview.stdout)
            self.assertIn("Preview only", preview.stdout)

            execute = self._run_script(powershell, script_copy, repo_root, "-Execute")

            self.assertEqual(execute.returncode, 0, execute.stderr)
            self.assertFalse((repo_root / "artifacts" / "bin" / "run-a").exists())
            self.assertFalse((repo_root / "artifacts" / "obj" / "run-a").exists())
            self.assertFalse((repo_root / "artifacts" / "publish" / "run-a").exists())
            self.assertTrue((repo_root / "artifacts" / "provider-validation" / "keep.json").exists())

    @staticmethod
    def _write_file(path: Path, content: str) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")

    @staticmethod
    def _run_script(
        powershell: str,
        script_path: Path,
        cwd: Path,
        *script_args: str,
    ) -> subprocess.CompletedProcess[str]:
        command = [powershell, "-NoProfile"]
        if Path(powershell).name.lower().startswith("powershell"):
            command.extend(["-ExecutionPolicy", "Bypass"])
        command.extend(["-File", str(script_path), *script_args])
        return subprocess.run(command, cwd=cwd, capture_output=True, text=True)


if __name__ == "__main__":
    unittest.main()
