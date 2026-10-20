"""Exercise generated-output recovery against real conflicted Git indexes."""

from __future__ import annotations

import subprocess
import json
import sys
import tempfile
import unittest
from pathlib import Path


SCRIPT = Path(__file__).resolve().parents[2] / "build/scripts/resolve-generated-merge-conflicts.py"
ASSET = "src/Meridian.Ui/wwwroot/workstation/assets/app [old].js"
DOC = "docs/generated/repository-structure.md"
SOURCE = "src/Example/Example.cs"
README = "src/Example/README.md"
COVERAGE = "docs/status/coverage-report.md"
MARKER = "<!-- auto-sync:coverage -->"


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

    def test_reviewed_hash_conflict_uses_only_parent_entries_matching_final_source(self) -> None:
        manifest_path = "docs/source/generated/source-hash-manifest.json"
        registry = {"modules": [{"id": "Example", "path": "src/Example", "readme": README}]}
        self.write("docs/source/data/source-modules.yml", json.dumps(registry))
        self.write(SOURCE, "// base\n")
        self.write(README, "# Reviewed documentation\n")
        validator = SCRIPT.parent / "docs/validate-doc-hashes.py"
        def refresh():
            subprocess.run([sys.executable, str(validator), "--root", str(self.repo), "--write"], check=True, capture_output=True)
        refresh()
        self.commit("reviewed base")
        self.git("switch", "-c", "feature")
        self.write(SOURCE, "// reviewed feature\n")
        refresh()
        self.commit("feature review")
        self.git("switch", "main")
        self.write("src/Example/Second.cs", "// reviewed incoming\n")
        refresh()
        self.commit("main review")
        self.merge()
        before = (self.repo / manifest_path).read_bytes()
        result = self.run_recovery("--apply")
        self.assertEqual(result.returncode, 1, result.stderr)
        self.assertIn("neither reviewed parent", result.stdout)
        self.assertEqual(before, (self.repo / manifest_path).read_bytes())
        # A user resolves source to one reviewed parent; its existing review may
        # now be reused without ever calculating a new reviewed baseline.
        self.write(SOURCE, "// base\n")
        self.git("add", SOURCE)
        result = self.run_recovery("--apply")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(json.loads((self.repo / manifest_path).read_bytes()),
                         json.loads(self.git("show", f"main:{manifest_path}").stdout))
        self.assertFalse(self.git("ls-files", "--unmerged").stdout)

    def test_repeated_apply_does_not_overwrite_regenerated_output(self) -> None:
        self.start_conflict((ASSET,))
        self.assertEqual(self.run_recovery("--apply").returncode, 0)
        self.write(ASSET, "regenerated merged source\n")
        before = self.snapshot()
        result = self.run_recovery("--apply")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(before, self.snapshot())

    def coverage_conflict(self, ancestor: str, head: str, incoming: str) -> None:
        self.write(COVERAGE, ancestor)
        self.commit("coverage ancestor")
        self.git("switch", "-c", "feature")
        self.write(COVERAGE, head)
        self.commit("coverage feature")
        self.git("switch", "main")
        self.write(COVERAGE, incoming)
        self.commit("coverage main")
        self.merge()

    def test_conflicting_handwritten_suffix_keeps_both_sides_unresolved(self) -> None:
        self.coverage_conflict(
            f"base output\n{MARKER}\nbase prose\n",
            f"feature output\n{MARKER}\nfeature prose\n",
            f"main output\n{MARKER}\nmain prose\n",
        )
        before = self.snapshot()
        result = self.run_recovery("--apply")
        self.assertEqual(result.returncode, 1, result.stderr)
        self.assertIn("Handwritten suffix has conflicts", result.stdout)
        self.assertEqual(before, self.snapshot())
        self.assertIn(b"feature prose", (self.repo / COVERAGE).read_bytes())
        self.assertIn(b"main prose", (self.repo / COVERAGE).read_bytes())
        self.assertTrue(self.git("ls-files", "--unmerged", "--", COVERAGE).stdout)

    def test_generated_prefix_conflict_preserves_independent_handwritten_edits(self) -> None:
        suffix = f"{MARKER}\nfirst\n" + "unchanged\n" * 12 + "last\n"
        self.coverage_conflict(
            "base output\n" + suffix,
            "feature output\n" + suffix.replace("first\n", "feature first\n"),
            "main output\n" + suffix.replace("last\n", "main last\n"),
        )
        before = self.snapshot()
        preview = self.run_recovery()
        self.assertEqual(preview.returncode, 0, preview.stderr)
        self.assertEqual(before, self.snapshot())
        result = self.run_recovery("--apply")
        self.assertEqual(result.returncode, 0, result.stderr)
        merged = (self.repo / COVERAGE).read_text()
        self.assertTrue(merged.startswith("main output\n" + MARKER))
        self.assertIn("feature first\n", merged)
        self.assertIn("main last\n", merged)
        self.assertFalse(self.git("ls-files", "--unmerged").stdout)

    def test_generated_only_coverage_conflict_reuses_identical_prose(self) -> None:
        suffix = f"{MARKER}\nkeep this handwritten guidance\n"
        self.coverage_conflict("base\n" + suffix, "feature\n" + suffix, "incoming\n" + suffix)
        result = self.run_recovery("--apply")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual((self.repo / COVERAGE).read_text(), "incoming\n" + suffix)
        self.assertFalse(self.git("ls-files", "--unmerged").stdout)

    def test_missing_duplicate_and_embedded_suffix_markers_refuse_without_mutation(self) -> None:
        # Each malformed parent must fail even if selecting the other parent's
        # bytes would appear to fix the output.
        for malformed in ("missing marker\n", f"{MARKER}\n{MARKER}\n", f"inline {MARKER}\n"):
            with self.subTest(marker=malformed), tempfile.TemporaryDirectory() as temporary:
                original = self.repo
                self.repo = Path(temporary)
                try:
                    self.git("init", "--initial-branch=main")
                    self.git("config", "user.name", "Merge recovery test")
                    self.git("config", "user.email", "merge-test@example.invalid")
                    self.git("config", "commit.gpgsign", "false")
                    self.coverage_conflict(f"base\n{MARKER}\nprose\n", "feature\n" + malformed,
                                           f"incoming\n{MARKER}\nprose\n")
                    before = self.snapshot()
                    result = self.run_recovery("--apply")
                    self.assertEqual(result.returncode, 1, result.stderr)
                    self.assertEqual(before, self.snapshot())
                    self.assertTrue(self.git("ls-files", "--unmerged").stdout)
                finally:
                    self.repo = original

    def test_every_declared_whole_output_conflict_has_a_resolution_fixture(self) -> None:
        policy = json.loads(SCRIPT.with_name("maintained-generation-outputs.json").read_text())
        paths = tuple(path for path, record in policy["conflict_recovery"]["files"].items()
                      if record["strategy"] == "regenerate")
        self.assertIn("src/Meridian.Ui/dashboard/src/lib/ui-api-routes.generated.ts", paths)
        self.assertIn("docs/source/generated/adapter-readiness-matrix.md", paths)
        self.start_conflict(paths)
        result = self.run_recovery("--apply")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertFalse(self.git("ls-files", "--unmerged").stdout)
        for path in paths:
            with self.subTest(path=path):
                self.assertEqual((self.repo / path).read_text(), "incoming\n")

    def test_candidate_policy_cannot_authorize_handwritten_conflicts(self) -> None:
        self.start_conflict((SOURCE,))
        candidate_policy = {"schema_version": 1, "whole_files": [SOURCE],
                            "conflict_recovery": {"default_strategy": "manual", "files": {
                                SOURCE: {"strategy": "regenerate", "generator": "malicious.py", "required": True}},
                                "trees": {}}}
        self.write("build/scripts/maintained-generation-outputs.json", json.dumps(candidate_policy))
        before = self.snapshot()
        result = self.run_recovery("--apply")
        self.assertEqual(result.returncode, 1, result.stderr)
        self.assertEqual(before, self.snapshot())

    def test_protected_suffix_does_not_follow_worktree_symlinks(self) -> None:
        suffix = f"{MARKER}\nkeep prose\n"
        self.coverage_conflict("base\n" + suffix, "feature\n" + suffix, "incoming\n" + suffix)
        target = self.repo / "unrelated.txt"
        target.write_text("unrelated user content\n")
        destination = self.repo / COVERAGE
        destination.unlink()
        try:
            destination.symlink_to(target)
        except OSError:
            self.skipTest("This host cannot create symbolic links")
        before = self.git("ls-files", "--stage", "-z").stdout
        result = self.run_recovery("--apply")
        self.assertEqual(result.returncode, 1, result.stderr)
        self.assertEqual(before, self.git("ls-files", "--stage", "-z").stdout)
        self.assertTrue(destination.is_symlink())
        self.assertEqual(target.read_text(), "unrelated user content\n")


if __name__ == "__main__":
    unittest.main()
