from __future__ import annotations

import importlib.util
import os
import tempfile
import unittest
from datetime import datetime, timezone
from pathlib import Path


BUILDCTL_PATH = Path(__file__).resolve().parents[2] / "build" / "python" / "cli" / "buildctl.py"


def load_buildctl():
    spec = importlib.util.spec_from_file_location("buildctl_under_test", BUILDCTL_PATH)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Unable to load buildctl.py from {BUILDCTL_PATH}")

    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class IsolatedBuildArtifactRetentionTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.buildctl = load_buildctl()

    def test_prunes_old_isolated_build_artifacts(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            repo_root = Path(temp_dir)
            old_bin = self._create_artifact(repo_root, "artifacts/bin/old-run", age_days=20)
            old_obj = self._create_artifact(repo_root, "artifacts/obj/old-run", age_days=20)
            recent_bin = self._create_artifact(repo_root, "artifacts/bin/recent-run", age_days=2)

            deleted_count, freed_bytes = self.buildctl._prune_isolated_build_artifacts(
                repo_root,
                max_age_days=14,
                active_isolation_key="current-run",
                now=datetime(2026, 4, 28, tzinfo=timezone.utc),
            )

            self.assertEqual(deleted_count, 2)
            self.assertGreaterEqual(freed_bytes, 2)
            self.assertFalse(old_bin.exists())
            self.assertFalse(old_obj.exists())
            self.assertTrue(recent_bin.exists())

    def test_retains_active_isolation_key_even_when_stale(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            repo_root = Path(temp_dir)
            active_bin = self._create_artifact(repo_root, "artifacts/bin/current-run", age_days=45)
            old_obj = self._create_artifact(repo_root, "artifacts/obj/old-run", age_days=45)

            deleted_count, _ = self.buildctl._prune_isolated_build_artifacts(
                repo_root,
                max_age_days=14,
                active_isolation_key="current-run",
                now=datetime(2026, 4, 28, tzinfo=timezone.utc),
            )

            self.assertEqual(deleted_count, 1)
            self.assertTrue(active_bin.exists())
            self.assertFalse(old_obj.exists())

    def test_prunes_recent_artifacts_beyond_retained_latest(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            repo_root = Path(temp_dir)
            oldest_bin = self._create_artifact(repo_root, "artifacts/bin/run-1", age_days=3)
            retained_bin = self._create_artifact(repo_root, "artifacts/bin/run-2", age_days=2)
            newest_bin = self._create_artifact(repo_root, "artifacts/bin/run-3", age_days=1)

            deleted_count, freed_bytes = self.buildctl._prune_isolated_build_artifacts(
                repo_root,
                max_age_days=14,
                retain_latest=2,
                active_isolation_key=None,
                now=datetime(2026, 4, 28, tzinfo=timezone.utc),
            )

            self.assertEqual(deleted_count, 1)
            self.assertGreaterEqual(freed_bytes, 1)
            self.assertFalse(oldest_bin.exists())
            self.assertTrue(retained_bin.exists())
            self.assertTrue(newest_bin.exists())

    def test_prunes_oldest_artifacts_when_root_size_cap_is_exceeded(self) -> None:
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

            deleted_count, freed_bytes = self.buildctl._prune_isolated_build_artifacts(
                repo_root,
                max_age_days=14,
                retain_latest=10,
                max_root_size_mb=1,
                active_isolation_key=None,
                now=datetime(2026, 4, 28, tzinfo=timezone.utc),
            )

            self.assertEqual(deleted_count, 2)
            self.assertGreaterEqual(freed_bytes, 1_400 * 1024)
            self.assertFalse(oldest_bin.exists())
            self.assertFalse(middle_bin.exists())
            self.assertTrue(newest_bin.exists())

    def test_size_cap_preserves_active_isolation_key(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            repo_root = Path(temp_dir)
            old_bin = self._create_artifact(
                repo_root,
                "artifacts/bin/old-run",
                age_days=3,
                size_bytes=700 * 1024,
            )
            active_bin = self._create_artifact(
                repo_root,
                "artifacts/bin/current-run",
                age_days=2,
                size_bytes=700 * 1024,
            )

            deleted_count, _ = self.buildctl._prune_isolated_build_artifacts(
                repo_root,
                max_age_days=14,
                retain_latest=10,
                max_root_size_mb=1,
                active_isolation_key="current-run",
                now=datetime(2026, 4, 28, tzinfo=timezone.utc),
            )

            self.assertEqual(deleted_count, 1)
            self.assertFalse(old_bin.exists())
            self.assertTrue(active_bin.exists())

    def test_non_positive_retention_disables_pruning(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            repo_root = Path(temp_dir)
            old_bin = self._create_artifact(repo_root, "artifacts/bin/old-run", age_days=45)

            deleted_count, freed_bytes = self.buildctl._prune_isolated_build_artifacts(
                repo_root,
                max_age_days=0,
                retain_latest=0,
                max_root_size_mb=0,
                active_isolation_key=None,
                now=datetime(2026, 4, 28, tzinfo=timezone.utc),
            )

            self.assertEqual(deleted_count, 0)
            self.assertEqual(freed_bytes, 0)
            self.assertTrue(old_bin.exists())

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
