"""Exercise generated-output recovery against real conflicted Git indexes."""

from __future__ import annotations

import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


SCRIPT = Path(__file__).resolve().parents[2] / "build/scripts/resolve-generated-merge-conflicts.py"
ASSET = "src/Meridian.Ui/wwwroot/workstation/assets/app [old].js"
DOC = "docs/generated/repository-structure.md"
SOURCE = "src/Example/Example.cs"
README = "src/Example/README.md"


class GeneratedMergeRecoveryTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.repo = Path(self.temp.name)
        self.git("init", "--initial-branch=main")
        self.git("config", "user.name", "Merge recovery test")
        self.git("config", "user.email", "merge-test@example.invalid")
        self.git("config", "core.autocrlf", "false")
        self.git("config", "commit.gpgsign", "false")

    def git(self, *args: str, check: bool = True) -> subprocess.CompletedProcess:
        return subprocess.run(
            ["git", "--literal-pathspecs", *args], cwd=self.repo,
            capture_output=True, check=check,
        )

    def write(self, path: str, content: str) -> None:
        target = self.repo / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content, encoding="utf-8", newline="\n")

    def commit(self, message: str) -> None:
        self.git("add", "--all")
        self.git("commit", "-m", message)

    def start_conflict(self, paths: tuple[str, ...]) -> None:
        for path in paths:
            self.write(path, "base\n")
        self.commit("base")
        self.git("switch", "-c", "feature")
        for path in paths:
            self.write(path, "feature\n")
        self.commit("feature changes")
        self.git("switch", "main")
        for path in paths:
            self.write(path, "incoming\n")
        self.commit("main changes")
        self.merge()

    def merge(self) -> None:
        self.git("switch", "feature")
        result = self.git("merge", "--no-commit", "--no-ff", "main", check=False)
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)

    def run_recovery(self, *args: str) -> subprocess.CompletedProcess:
        return subprocess.run(
            [sys.executable, str(SCRIPT), "--repo", str(self.repo), "--main-ref", "main", *args],
            capture_output=True, text=True, encoding="utf-8", check=False,
        )

    def snapshot(self) -> tuple[bytes, bytes]:
        return self.git("ls-files", "--stage", "-z").stdout, self.git("diff").stdout

    def test_preview_leaves_worktree_and_index_unchanged(self) -> None:
        self.start_conflict((ASSET, DOC))
        before = self.snapshot()
        result = self.run_recovery()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("2 generated conflict(s)", result.stdout)
        self.assertEqual(before, self.snapshot())

    def test_apply_resolves_generated_only_and_preserves_manual_conflicts(self) -> None:
        paths = (ASSET, DOC, SOURCE, README, "docs/generated/README.md",
                 "docs/source/generated/source-hash-manifest.json", "docs/HELP.md")
        self.start_conflict(paths)
        original_manual = {path: (self.repo / path).read_bytes() for path in paths[2:]}
        self.write("untracked.txt", "keep untracked work\n")
        result = self.run_recovery("--apply")
        self.assertEqual(result.returncode, 1, result.stderr)
        self.assertIn("5 conflict(s) require review", result.stdout)
        for path in (ASSET, DOC):
            self.assertEqual((self.repo / path).read_text(), "incoming\n")
            self.assertEqual(self.git("show", f":{path}").stdout, b"incoming\n")
        for path, before in original_manual.items():
            self.assertEqual((self.repo / path).read_bytes(), before)
            self.assertTrue(self.git("ls-files", "--unmerged", "--", path).stdout)
        self.assertEqual((self.repo / "untracked.txt").read_text(), "keep untracked work\n")

    def test_modify_delete_removes_output_deleted_on_main(self) -> None:
        self.write(ASSET, "base\n")
        self.commit("base")
        self.git("switch", "-c", "feature")
        self.write(ASSET, "feature\n")
        self.commit("modify output")
        self.git("switch", "main")
        self.git("rm", "--", ASSET)
        self.commit("remove obsolete output")
        self.merge()
        result = self.run_recovery("--apply")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertFalse((self.repo / ASSET).exists())
        self.assertFalse(self.git("ls-files", "--", ASSET).stdout)

    def test_delete_modify_restores_incoming_output(self) -> None:
        self.write(ASSET, "base\n")
        self.commit("base")
        self.git("switch", "-c", "feature")
        self.git("rm", "--", ASSET)
        self.commit("remove output")
        self.git("switch", "main")
        self.write(ASSET, "incoming\n")
        self.commit("main output")
        self.merge()
        result = self.run_recovery("--apply")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual((self.repo / ASSET).read_text(), "incoming\n")
        self.assertFalse(self.git("ls-files", "--unmerged").stdout)

    def test_add_add_conflict_is_resolved(self) -> None:
        self.write(SOURCE, "base\n")
        self.commit("base")
        self.git("switch", "-c", "feature")
        self.write(ASSET, "feature\n")
        self.commit("feature output")
        self.git("switch", "main")
        self.write(ASSET, "incoming\n")
        self.commit("main output")
        self.merge()
        result = self.run_recovery("--apply")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual((self.repo / ASSET).read_text(), "incoming\n")

    def test_rename_rename_keeps_only_incoming_asset_name(self) -> None:
        feature_name = ASSET.replace("[old]", "[feature]")
        main_name = ASSET.replace("[old]", "[main]")
        self.write(ASSET, "base bundle\n" * 40)
        self.commit("base")
        self.git("switch", "-c", "feature")
        self.git("mv", "--", ASSET, feature_name)
        self.commit("feature rename")
        self.git("switch", "main")
        self.git("mv", "--", ASSET, main_name)
        self.commit("main rename")
        self.merge()
        result = self.run_recovery("--apply")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertFalse((self.repo / ASSET).exists())
        self.assertFalse((self.repo / feature_name).exists())
        self.assertTrue((self.repo / main_name).is_file())
        self.assertFalse(self.git("ls-files", "--unmerged").stdout)

    def test_wrong_incoming_parent_is_rejected_without_mutation(self) -> None:
        self.start_conflict((ASSET,))
        before = self.snapshot()
        result = self.run_recovery("--main-ref", "feature", "--apply")
        self.assertEqual(result.returncode, 2, result.stderr)
        self.assertIn("does not match", result.stderr)
        self.assertEqual(before, self.snapshot())

    def test_no_merge_is_rejected_without_touching_local_changes(self) -> None:
        self.write(ASSET, "base\n")
        self.commit("base")
        self.write(ASSET, "local changes\n")
        before = self.snapshot()
        result = self.run_recovery("--apply")
        self.assertEqual(result.returncode, 2, result.stderr)
        self.assertIn("No merge is in progress", result.stderr)
        self.assertEqual(before, self.snapshot())

    def test_repeated_apply_does_not_overwrite_regenerated_output(self) -> None:
        self.start_conflict((ASSET,))
        self.assertEqual(self.run_recovery("--apply").returncode, 0)
        self.write(ASSET, "regenerated merged source\n")
        before = self.snapshot()
        result = self.run_recovery("--apply")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(before, self.snapshot())


if __name__ == "__main__":
    unittest.main()
