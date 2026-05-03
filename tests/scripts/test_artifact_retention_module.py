from __future__ import annotations

import os
import shutil
import subprocess
import tempfile
import unittest
from datetime import datetime, timezone
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
MODULE_PATH = REPO_ROOT / "build" / "scripts" / "lib" / "ArtifactRetention.psm1"


@unittest.skipIf(os.name != "nt", "ArtifactRetention.psm1 behavior is validated on Windows")
class ArtifactRetentionModuleTests(unittest.TestCase):
    def test_prunes_recent_artifacts_beyond_retained_latest(self) -> None:
        powershell = shutil.which("pwsh") or shutil.which("powershell")
        if powershell is None:
            self.skipTest("PowerShell is not available")

        with tempfile.TemporaryDirectory() as temp_dir:
            output_root = Path(temp_dir) / "artifacts" / "publish"
            oldest = self._create_artifact(output_root / "run-1", age_days=3)
            retained = self._create_artifact(output_root / "run-2", age_days=2)
            newest = self._create_artifact(output_root / "run-3", age_days=1)

            result = self._run_retention(
                powershell,
                output_root,
                max_age_days=14,
                retain_latest=2,
            )

            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertFalse(oldest.exists(), result.stdout)
            self.assertTrue(retained.exists(), result.stdout)
            self.assertTrue(newest.exists(), result.stdout)
            self.assertIn("Pruned 1 publish output directory", result.stdout)

    def test_preserves_active_output_even_when_oldest(self) -> None:
        powershell = shutil.which("pwsh") or shutil.which("powershell")
        if powershell is None:
            self.skipTest("PowerShell is not available")

        with tempfile.TemporaryDirectory() as temp_dir:
            output_root = Path(temp_dir) / "artifacts" / "publish"
            active = self._create_artifact(output_root / "active-run", age_days=30)
            stale = self._create_artifact(output_root / "stale-run", age_days=20)
            newest = self._create_artifact(output_root / "newest-run", age_days=1)

            result = self._run_retention(
                powershell,
                output_root,
                active_path=active,
                max_age_days=14,
                retain_latest=1,
            )

            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertTrue(active.exists(), result.stdout)
            self.assertFalse(stale.exists(), result.stdout)
            self.assertTrue(newest.exists(), result.stdout)

    @staticmethod
    def _create_artifact(path: Path, *, age_days: int) -> Path:
        nested_file = path / "win-x64" / "Meridian.Desktop.exe"
        nested_file.parent.mkdir(parents=True, exist_ok=True)
        nested_file.write_bytes(b"x" * 1024)

        timestamp = datetime(2026, 4, 28, tzinfo=timezone.utc).timestamp() - (age_days * 24 * 60 * 60)
        for candidate in (nested_file, nested_file.parent, path):
            os.utime(candidate, (timestamp, timestamp))

        return path

    @staticmethod
    def _run_retention(
        powershell: str,
        output_root: Path,
        *,
        active_path: Path | None = None,
        max_age_days: int,
        retain_latest: int,
    ) -> subprocess.CompletedProcess[str]:
        command = [powershell, "-NoProfile"]
        if Path(powershell).name.lower().startswith("powershell"):
            command.extend(["-ExecutionPolicy", "Bypass"])

        active_arg = f" -ActivePath '{active_path}'" if active_path is not None else ""
        command.extend(
            [
                "-Command",
                (
                    "$ErrorActionPreference = 'Stop'; "
                    f"Import-Module '{MODULE_PATH}' -Force; "
                    "Invoke-MeridianArtifactDirectoryRetention "
                    f"-OutputRoot '{output_root}'"
                    f"{active_arg} "
                    f"-MaxAgeDays {max_age_days} "
                    f"-RetainLatest {retain_latest} "
                    "-Label 'publish output'"
                ),
            ]
        )
        return subprocess.run(command, capture_output=True, text=True)


if __name__ == "__main__":
    unittest.main()
