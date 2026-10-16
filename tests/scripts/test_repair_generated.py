"""Isolated repair artifacts include deletes/binaries and preserve caller work."""
from __future__ import annotations

import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[2] / "build/scripts/repair-generated.py"
SPEC = importlib.util.spec_from_file_location("repair_generated_under_test", SCRIPT)
repair = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(repair)
ASSET = "src/Meridian.Ui/wwwroot/workstation/assets/old.js"
NEW_ASSET = "src/Meridian.Ui/wwwroot/workstation/assets/new.js"
DOC = "docs/generated/repository-structure.md"


class RepairGeneratedTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.repo = self.root / "repo"
        self.repo.mkdir()
        self.output = self.root / "evidence"
        self.git("init", "--initial-branch=main")
        self.git("config", "user.name", "Repair test")
        self.git("config", "user.email", "repair-test@example.invalid")
        self.git("config", "commit.gpgsign", "false")
        self.git("config", "core.autocrlf", "false")
        self.write(self.repo, ".gitignore", b"artifacts/\n")
        self.write(self.repo, DOC, b"base\n")
        self.write(self.repo, ASSET, b"base\n")
        self.write(self.repo, "source.txt", b"base source\n")
        self.commit("base")
        self.git("switch", "-c", "feature")
        self.write(self.repo, DOC, b"feature\n")
        self.write(self.repo, ASSET, b"feature\n")
        self.commit("feature generated outputs")
        self.head = self.git("rev-parse", "HEAD").decode().strip()
        self.git("switch", "main")
        self.write(self.repo, DOC, b"main\n")
        self.write(self.repo, "incoming.txt", b"main handwritten source\n")
        self.commit("main generated outputs and source")
        self.base = self.git("rev-parse", "HEAD").decode().strip()
        self.git("switch", "feature")
        self.calls = []

    def git(self, *args):
        return subprocess.check_output(["git", *args], cwd=self.repo, stderr=subprocess.PIPE)

    @staticmethod
    def write(root, name, content):
        path = root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(content)

    def commit(self, message):
        self.git("add", "-A")
        self.git("commit", "-m", message)

    def generator(self, root, output, max_passes):
        self.calls.append((root, max_passes))
        self.assertNotEqual(root, self.repo)
        self.assertEqual((root / "incoming.txt").read_bytes(), b"main handwritten source\n")
        (root / ASSET).unlink()
        self.write(root, NEW_ASSET, b"new\x00binary asset\xff\n")
        self.write(root, DOC, b"final generated documentation\n")
        self.write(root, "artifacts/maintained-generation/report.json", json.dumps({"successful": True, "converged": True, "passes": [{"number": 1}]}).encode())
        self.write(root, "artifacts/maintained-generation/logs/build.log", b"build evidence\n")
        return 0

    def run_repair(self, generator=None):
        return repair.repair(self.repo, self.head, self.base, self.output, generation_runner=generator or self.generator)

    def test_full_patch_contains_merge_deletion_binary_and_generated_patch_scope(self):
        report = self.run_repair()
        self.assertTrue(report["successful"], report)
        patch = (self.output / "repair.patch").read_bytes()
        delta = (self.output / "generated.patch").read_bytes()
        self.assertIn(b"deleted file mode", patch)
        self.assertIn(b"GIT binary patch", patch)
        self.assertIn(b"incoming.txt", patch)
        self.assertNotIn(b"incoming.txt", delta)
        self.assertEqual(report["patch_sha256"], hashlib.sha256(patch).hexdigest())
        self.assertEqual(report["head_sha"], self.head)
        self.assertEqual(report["base_sha"], self.base)
        self.assertEqual((self.output / "logs/build.log").read_bytes(), b"build evidence\n")
        self.assertEqual(json.loads((self.output / "report.json").read_bytes()), report)
        # The exported patch is genuinely applicable to the advertised head.
        self.git("apply", "--check", str(self.output / "repair.patch"))
        self.git("apply", "--index", str(self.output / "repair.patch"))
        self.assertEqual(self.git("write-tree").decode().strip(), report["final_tree_sha"])

    def test_dirty_callers_index_worktree_and_untracked_files_are_preserved(self):
        self.write(self.repo, "staged.txt", b"staged\n")
        self.git("add", "staged.txt")
        self.write(self.repo, "source.txt", b"local user edits\n")
        self.write(self.repo, "untracked.txt", b"local scratch\n")
        before = (self.git("ls-files", "--stage", "-z"), self.git("diff"), self.git("status", "--porcelain", "-z"))
        self.assertTrue(self.run_repair()["successful"])
        self.assertEqual(before, (self.git("ls-files", "--stage", "-z"), self.git("diff"), self.git("status", "--porcelain", "-z")))
        self.assertEqual(self.git("rev-parse", "HEAD").decode().strip(), self.head)
        self.assertEqual(len(self.git("worktree", "list", "--porcelain").decode().split("worktree ")) - 1, 1)

    def test_source_conflicts_stop_before_generation_and_remove_stale_patch(self):
        self.write(self.repo, "source.txt", b"feature source\n")
        self.commit("feature handwritten source")
        self.head = self.git("rev-parse", "HEAD").decode().strip()
        self.git("switch", "main")
        self.write(self.repo, "source.txt", b"incoming source\n")
        self.commit("incoming handwritten source")
        self.base = self.git("rev-parse", "HEAD").decode().strip()
        self.git("switch", "feature")
        self.write(self.output, "repair.patch", b"stale previous success")
        report = self.run_repair()
        self.assertEqual(report["status"], "review_required")
        self.assertEqual(report["conflicts"], ["source.txt"])
        self.assertFalse(self.calls)
        self.assertFalse((self.output / "repair.patch").exists())

    def test_generation_failure_emits_evidence_without_publishable_patch(self):
        def failure(root, output, max_passes):
            self.generator(root, output, max_passes)
            return 1
        report = self.run_repair(failure)
        self.assertEqual(report["status"], "generation_failed")
        self.assertFalse((self.output / "repair.patch").exists())
        self.assertTrue((self.output / "generation-report.json").is_file())
        self.assertEqual(self.git("rev-parse", "HEAD").decode().strip(), self.head)

    def test_generation_mode_changes_are_not_exported_as_ready(self):
        if self.git("config", "core.filemode").strip() == b"false":
            self.skipTest("Git cannot observe executable bits on this filesystem")
        def change_mode(root, output, passes):
            self.generator(root, output, passes)
            (root / "source.txt").chmod(0o755)
            return 0
        report = self.run_repair(change_mode)
        self.assertEqual(report["status"], "generation_failed")
        self.assertEqual(report["protection_violations"], ["source.txt"])
        self.assertFalse((self.output / "repair.patch").exists())

    def test_missing_convergence_evidence_fails_closed(self):
        report = self.run_repair(lambda root, output, passes: 0)
        self.assertEqual(report["status"], "generation_failed")
        self.assertFalse(report["successful"])

    def test_prepare_merge_rejects_wrong_head_without_mutation(self):
        before = self.git("status", "--porcelain", "-z")
        with self.assertRaisesRegex(ValueError, "pinned head"):
            repair.prepare_merge(self.repo, self.base, self.base)
        self.assertEqual(before, self.git("status", "--porcelain", "-z"))

    def test_refs_are_pinned_before_runner_moves_base(self):
        def move_ref(root, output, passes):
            self.git("update-ref", "refs/heads/main", self.head)
            return self.generator(root, output, passes)
        report = repair.repair(self.repo, "feature", "main", self.output, generation_runner=move_ref)
        self.assertTrue(report["successful"])
        self.assertEqual(report["base_sha"], self.base)
        self.assertIn("incoming.txt", report["changed_paths"])

    def register_reviewed_module(self):
        self.write(self.repo, "docs/source/data/source-modules.yml", b'{"modules":[{"id":"A","path":"src/A","readme":"src/A/README.md"}]}')
        self.write(self.repo, "src/A/README.md", b"# Reviewed module\n")
        self.write(self.repo, "src/A/first.cs", b"// first base\n")
        self.write(self.repo, "src/A/second.cs", b"// second base\n")
        merger = repair.load_resolver().load_hash_merger()
        def refresh():
            self.write(self.repo, merger.MANIFEST, json.dumps(merger.actual_manifest(self.repo), indent=2, sort_keys=True).encode())
        refresh()
        self.commit("register reviewed module")
        return refresh

    def test_clean_textual_manifest_merge_stops_when_source_matches_neither_review(self):
        refresh = self.register_reviewed_module()
        common = self.git("rev-parse", "HEAD").decode().strip()
        self.write(self.repo, "src/A/first.cs", b"// reviewed first change\n")
        refresh()
        self.commit("review first file")
        self.head = self.git("rev-parse", "HEAD").decode().strip()
        self.git("switch", "-C", "main", common)
        self.write(self.repo, "src/A/second.cs", b"// second change without review\n")
        self.commit("unreviewed second change")
        self.base = self.git("rev-parse", "HEAD").decode().strip()
        self.git("switch", "feature")
        report = self.run_repair()
        self.assertEqual(report["status"], "review_required")
        self.assertEqual(report["conflicts"], [])
        self.assertIn("A", report["reviewed_hashes"]["review_required"])
        self.assertFalse(self.calls)
        self.assertFalse((self.output / "repair.patch").exists())

    def test_generation_cannot_invent_review_after_readme_content_changes(self):
        self.register_reviewed_module()
        self.head = self.git("rev-parse", "HEAD").decode().strip()
        def change_readme(root, output, passes):
            self.generator(root, output, passes)
            self.write(root, "src/A/README.md", b"# Newly generated README block\n")
            return 0
        report = self.run_repair(change_readme)
        self.assertEqual(report["status"], "review_required")
        self.assertIn("A", report["reviewed_hashes"]["review_required"])
        self.assertFalse((self.output / "repair.patch").exists())

    def test_output_directory_cannot_overwrite_tracked_checkout_files(self):
        with self.assertRaisesRegex(ValueError, "under ignored artifacts"):
            repair.repair(self.repo, self.head, self.base, self.repo)
        self.assertFalse((self.repo / "report.json").exists())

    def test_existing_generated_files_are_regenerated_when_base_is_ancestor(self):
        self.base = self.git("merge-base", self.head, self.base).decode().strip()
        def generator(root, output, passes):
            self.write(root, DOC, b"fresh\n")
            self.write(root, "artifacts/maintained-generation/report.json", b'{"successful":true,"converged":true}')
            return 0
        report = self.run_repair(generator)
        self.assertTrue(report["successful"], report)
        self.assertEqual(report["changed_paths"], [DOC])


if __name__ == "__main__":
    unittest.main()
