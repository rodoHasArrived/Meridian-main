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


@unittest.skipUnless(shutil.which("pwsh") or shutil.which("powershell"), "PowerShell is not available")
class SharedBuildRetentionTests(unittest.TestCase):
    def test_build_retention_preserves_profiles_outside_temporary_run_budgets(self) -> None:
        for policy in ("age", "count", "size"):
            with self.subTest(policy=policy), tempfile.TemporaryDirectory() as temp_dir:
                repo_root = Path(temp_dir)
                profiles = []
                expired_runs = []
                retained_runs = []
                for artifact_kind in ("bin", "obj"):
                    for name, age_days in (("profile-old", 30), ("PROFILE-new", 0)):
                        profiles.append(self._create_artifact(
                            repo_root,
                            f"artifacts/{artifact_kind}/{name}",
                            age_days=age_days,
                            size_bytes=2 * 1024 * 1024,
                        ))
                    expired_runs.append(self._create_artifact(
                        repo_root,
                        f"artifacts/{artifact_kind}/old-run",
                        age_days=30,
                        size_bytes=700 * 1024,
                    ))
                    retained_runs.append(self._create_artifact(
                        repo_root,
                        f"artifacts/{artifact_kind}/latest-run",
                        age_days=1,
                        size_bytes=700 * 1024,
                    ))

                result = self._run_build_retention(
                    repo_root,
                    max_age_days=14 if policy == "age" else 0,
                    retain_latest=1 if policy == "count" else 0,
                    max_root_size_mb=1 if policy == "size" else 0,
                )

                self.assertEqual(result.returncode, 0, result.stderr)
                for path in profiles + retained_runs:
                    self.assertTrue(path.exists(), f"Removed {path}: {result.stdout}")
                for path in expired_runs:
                    self.assertFalse(path.exists(), f"Retained {path}: {result.stdout}")

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

    def test_prunes_recent_workflow_runs_beyond_retained_latest(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            repo_root = Path(temp_dir)
            oldest = self._create_artifact(
                repo_root,
                "artifacts/desktop-workflows/20260507-010000-debug-startup",
                age_days=3,
            )
            retained = self._create_artifact(
                repo_root,
                "artifacts/desktop-workflows/20260507-020000-debug-startup",
                age_days=2,
            )
            newest = self._create_artifact(
                repo_root,
                "artifacts/desktop-workflows/20260507-030000-debug-startup",
                age_days=1,
            )

            result = self._run_workflow_retention(
                repo_root / "artifacts" / "desktop-workflows",
                max_age_days=14,
                retain_latest=2,
            )

            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertFalse(oldest.exists(), result.stdout)
            self.assertTrue(retained.exists(), result.stdout)
            self.assertTrue(newest.exists(), result.stdout)

    def test_workflow_count_retention_runs_when_age_retention_is_disabled(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            repo_root = Path(temp_dir)
            oldest = self._create_artifact(
                repo_root,
                "artifacts/desktop-workflows/20260507-010000-debug-startup",
                age_days=2,
            )
            newest = self._create_artifact(
                repo_root,
                "artifacts/desktop-workflows/20260507-020000-debug-startup",
                age_days=1,
            )

            result = self._run_workflow_retention(
                repo_root / "artifacts" / "desktop-workflows",
                max_age_days=0,
                retain_latest=1,
            )

            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertFalse(oldest.exists(), result.stdout)
            self.assertTrue(newest.exists(), result.stdout)

    def test_workflow_retention_ignores_non_run_directories(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            repo_root = Path(temp_dir)
            checkpoints = self._create_artifact(
                repo_root,
                "artifacts/desktop-workflows/checkpoints",
                age_days=30,
            )
            old_run = self._create_artifact(
                repo_root,
                "artifacts/desktop-workflows/20260401-010000-debug-startup",
                age_days=30,
            )
            newest = self._create_artifact(
                repo_root,
                "artifacts/desktop-workflows/20260507-010000-debug-startup",
                age_days=1,
            )

            result = self._run_workflow_retention(
                repo_root / "artifacts" / "desktop-workflows",
                max_age_days=14,
                retain_latest=1,
            )

            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertTrue(checkpoints.exists(), result.stdout)
            self.assertFalse(old_run.exists(), result.stdout)
            self.assertTrue(newest.exists(), result.stdout)

    @unittest.skipIf(os.name != "nt", "Directory junctions require Windows")
    def test_build_retention_skips_junction_artifact_root(self) -> None:
        powershell = shutil.which("pwsh") or shutil.which("powershell")
        if powershell is None:
            self.skipTest("PowerShell is not available")

        with tempfile.TemporaryDirectory() as temp_dir:
            repo_root = Path(temp_dir) / "repo"
            external_root = Path(temp_dir) / "external-bin"
            (repo_root / "artifacts").mkdir(parents=True)
            stale_external = self._create_artifact(external_root, "old-run", age_days=30)
            junction_path = repo_root / "artifacts" / "bin"
            junction = subprocess.run(
                ["cmd", "/c", "mklink", "/J", str(junction_path), str(external_root)],
                capture_output=True,
                text=True,
            )
            if junction.returncode != 0:
                self.skipTest(f"Directory junctions are not available: {junction.stderr or junction.stdout}")

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
                        "-MaxAgeDays 14 -RetainLatest 1 -MaxRootSizeMB 1"
                    ),
                ]
            )

            result = subprocess.run(command, capture_output=True, text=True)

            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertTrue(stale_external.exists(), result.stdout)
            self.assertIn("reparse point", result.stdout + result.stderr)

    @unittest.skipIf(os.name != "nt", "Directory junctions require Windows")
    def test_build_retention_skips_junction_artifact_parent(self) -> None:
        powershell = shutil.which("pwsh") or shutil.which("powershell")
        if powershell is None:
            self.skipTest("PowerShell is not available")

        with tempfile.TemporaryDirectory() as temp_dir:
            repo_root = Path(temp_dir) / "repo"
            external_artifacts = Path(temp_dir) / "external-artifacts"
            repo_root.mkdir(parents=True)
            stale_external = self._create_artifact(external_artifacts, "bin/old-run", age_days=30)
            junction_path = repo_root / "artifacts"
            junction = subprocess.run(
                ["cmd", "/c", "mklink", "/J", str(junction_path), str(external_artifacts)],
                capture_output=True,
                text=True,
            )
            if junction.returncode != 0:
                self.skipTest(f"Directory junctions are not available: {junction.stderr or junction.stdout}")

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
                        "-MaxAgeDays 14 -RetainLatest 1 -MaxRootSizeMB 1"
                    ),
                ]
            )

            result = subprocess.run(command, capture_output=True, text=True)

            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertTrue(stale_external.exists(), result.stdout)
            self.assertIn("reparse point", result.stdout + result.stderr)

    def _run_build_retention(
        self,
        repo_root: Path,
        *,
        max_age_days: int,
        retain_latest: int,
        max_root_size_mb: int,
    ) -> subprocess.CompletedProcess[str]:
        powershell = shutil.which("pwsh") or shutil.which("powershell")
        if powershell is None:
            self.skipTest("PowerShell is not available")

        command = [powershell, "-NoProfile"]
        if Path(powershell).name.lower().startswith("powershell"):
            command.extend(["-ExecutionPolicy", "Bypass"])
        script_path = str(SCRIPT_PATH).replace("'", "''")
        root_path = str(repo_root).replace("'", "''")
        command.extend([
            "-Command",
            "$ErrorActionPreference = 'Stop'; "
            f". '{script_path}'; "
            f"Invoke-MeridianBuildArtifactRetention -RepoRoot '{root_path}' "
            f"-MaxAgeDays {max_age_days} -RetainLatest {retain_latest} "
            f"-MaxRootSizeMB {max_root_size_mb}",
        ])
        return subprocess.run(command, capture_output=True, text=True)

    def _run_workflow_retention(
        self,
        output_root: Path,
        *,
        max_age_days: int,
        retain_latest: int,
    ) -> subprocess.CompletedProcess[str]:
        powershell = shutil.which("pwsh") or shutil.which("powershell")
        if powershell is None:
            self.skipTest("PowerShell is not available")

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
                    f"Invoke-MeridianWorkflowArtifactRetention -OutputRoot '{output_root}' "
                    f"-MaxAgeDays {max_age_days} -RetainLatest {retain_latest}"
                ),
            ]
        )

        return subprocess.run(command, capture_output=True, text=True)

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

        timestamp = datetime.now(timezone.utc).timestamp() - (age_days * 24 * 60 * 60)
        for candidate in (output_file, nested, path):
            os.utime(candidate, (timestamp, timestamp))

        return path


if __name__ == "__main__":
    unittest.main()
