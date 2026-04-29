from __future__ import annotations

import os
import shutil
import subprocess
import tempfile
import unittest
from datetime import datetime, timezone
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
SCRIPT_PATH = REPO_ROOT / "scripts" / "dev" / "SharedBuild.ps1"


@unittest.skipIf(os.name != "nt", "SharedBuild.ps1 retention behavior is validated on Windows")
class SharedBuildRetentionTests(unittest.TestCase):
    def test_prunes_recent_artifacts_beyond_retained_latest(self) -> None:
        powershell = shutil.which("pwsh") or shutil.which("powershell")
        if powershell is None:
            self.skipTest("PowerShell is not available")

        with tempfile.TemporaryDirectory() as temp_dir:
            repo_root = Path(temp_dir)
            oldest_bin = self._create_artifact(repo_root, "artifacts/bin/run-1", age_days=3)
            retained_bin = self._create_artifact(repo_root, "artifacts/bin/run-2", age_days=2)
            newest_bin = self._create_artifact(repo_root, "artifacts/bin/run-3", age_days=1)

            command = [
                powershell,
                "-NoProfile",
            ]
            if Path(powershell).name.lower().startswith("powershell"):
                command.extend(["-ExecutionPolicy", "Bypass"])
            command.extend(
                [
                    "-Command",
                    (
                        "$ErrorActionPreference = 'Stop'; "
                        f". '{SCRIPT_PATH}'; "
                        f"Invoke-MeridianBuildArtifactRetention -RepoRoot '{repo_root}' -MaxAgeDays 14 -RetainLatest 2"
                    ),
                ]
            )

            result = subprocess.run(command, capture_output=True, text=True)

            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertFalse(oldest_bin.exists(), result.stdout)
            self.assertTrue(retained_bin.exists(), result.stdout)
            self.assertTrue(newest_bin.exists(), result.stdout)

    def test_prunes_oldest_artifacts_when_root_size_cap_is_exceeded(self) -> None:
        powershell = shutil.which("pwsh") or shutil.which("powershell")
        if powershell is None:
            self.skipTest("PowerShell is not available")

        with tempfile.TemporaryDirectory() as temp_dir:
            repo_root = Path(temp_dir)
            oldest_bin = self._create_artifact(
                repo_root,
                "artifacts/bin/run-1",
                age_days=3,
                size_bytes=700 * 1024,
            )
            middle_bin = self._create_artifact(
                repo_root,
                "artifacts/bin/run-2",
                age_days=2,
                size_bytes=700 * 1024,
            )
            newest_bin = self._create_artifact(
                repo_root,
                "artifacts/bin/run-3",
                age_days=1,
                size_bytes=700 * 1024,
            )

            command = [
                powershell,
                "-NoProfile",
            ]
            if Path(powershell).name.lower().startswith("powershell"):
                command.extend(["-ExecutionPolicy", "Bypass"])
            command.extend(
                [
                    "-Command",
                    (
                        "$ErrorActionPreference = 'Stop'; "
                        f". '{SCRIPT_PATH}'; "
                        f"Invoke-MeridianBuildArtifactRetention -RepoRoot '{repo_root}' "
                        "-MaxAgeDays 14 -RetainLatest 10 -MaxRootSizeMB 1"
                    ),
                ]
            )

            result = subprocess.run(command, capture_output=True, text=True)

            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertFalse(oldest_bin.exists(), result.stdout)
            self.assertFalse(middle_bin.exists(), result.stdout)
            self.assertTrue(newest_bin.exists(), result.stdout)

    @staticmethod
    def _create_artifact(
        repo_root: Path,
        relative_path: str,
        *,
        age_days: int,
        size_bytes: int = 1,
    ) -> Path:
        path = repo_root / relative_path
        nested = path / "Project"
        nested.mkdir(parents=True, exist_ok=True)
        output_file = nested / "output.dll"
        output_file.write_bytes(b"x" * size_bytes)

        timestamp = datetime(2026, 4, 28, tzinfo=timezone.utc).timestamp() - (age_days * 24 * 60 * 60)
        for candidate in (output_file, nested, path):
            os.utime(candidate, (timestamp, timestamp))

        return path


if __name__ == "__main__":
    unittest.main()
