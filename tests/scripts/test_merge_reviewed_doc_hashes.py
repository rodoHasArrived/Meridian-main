"""Reviewed hash merges reuse proven parent records; they never mint reviews."""
from __future__ import annotations

import copy
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[2] / "build/scripts/merge-reviewed-doc-hashes.py"
SPEC = importlib.util.spec_from_file_location("reviewed_doc_hashes_under_test", SCRIPT)
hashes = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(hashes)


def entry(identity, digit="a"):
    return {"id": identity, "path": f"src/{identity}", "readme": f"src/{identity}/README.md",
            "source_hash": digit * 64, "readme_hash": "f" * 64}


def manifest(*entries):
    return {"schema": dict(hashes.SCHEMA), "generator": dict(hashes.GENERATOR),
            "contract": "Reviewed source and documentation", "modules": list(entries)}


class ReviewedHashMergeTests(unittest.TestCase):
    def test_independent_module_changes_merge_by_id(self):
        base = manifest(entry("A"), entry("B"))
        head = manifest(entry("A", "b"), entry("B"))
        incoming = manifest(entry("B", "c"), entry("A"))
        actual = manifest(entry("A", "b"), entry("B", "c"))
        merged, report = hashes.merge_manifests(base, head, incoming, actual)
        self.assertTrue(report["successful"])
        self.assertEqual(merged, actual)
        self.assertEqual(report["decisions"], {"A": "head_parent", "B": "base_parent"})

    def test_identical_parent_changes_are_accepted(self):
        current = manifest(entry("A", "c"))
        self.assertTrue(hashes.merge_manifests(manifest(entry("A")), current, current, current)[1]["successful"])

    def test_different_parent_changes_accept_only_matching_reviewed_parent(self):
        base, head, incoming = (manifest(entry("A", digit)) for digit in "abc")
        self.assertTrue(hashes.merge_manifests(base, head, incoming, incoming)[1]["successful"])
        _, report = hashes.merge_manifests(base, head, incoming, manifest(entry("A", "d")))
        self.assertFalse(report["successful"])
        self.assertIn("neither reviewed parent", report["review_required"]["A"])

    def test_stale_unchanged_entry_is_not_refreshed(self):
        reviewed = manifest(entry("A"))
        _, report = hashes.merge_manifests(reviewed, reviewed, reviewed, manifest(entry("A", "b")))
        self.assertFalse(report["successful"])

    def test_addition_and_deletion_follow_registry_and_parent_review(self):
        base = manifest(entry("A"), entry("B"))
        head = manifest(entry("B"))
        incoming = manifest(entry("A"), entry("B"), entry("C", "c"))
        actual = manifest(entry("B"), entry("C", "c"))
        merged, report = hashes.merge_manifests(base, head, incoming, actual)
        self.assertEqual(merged, actual)
        self.assertEqual(report["decisions"]["A"], "deleted")
        self.assertTrue(report["successful"])
        self.assertFalse(hashes.merge_manifests(base, head, incoming, manifest(entry("B"), entry("D")))[1]["successful"])

    def test_delete_modify_requires_matching_remaining_review(self):
        base, head, incoming = manifest(entry("A")), manifest(), manifest(entry("A", "b"))
        self.assertFalse(hashes.merge_manifests(base, head, incoming, manifest())[1]["successful"])
        self.assertTrue(hashes.merge_manifests(base, head, incoming, incoming)[1]["successful"])

    def test_metadata_conflict_is_not_silently_selected(self):
        base = manifest(entry("A"))
        head, incoming = copy.deepcopy(base), copy.deepcopy(base)
        head["contract"], incoming["contract"] = "left", "right"
        self.assertIn("$manifest", hashes.merge_manifests(base, head, incoming, base)[1]["review_required"])

    def test_malformed_unknown_schema_duplicate_ids_and_keys_fail_closed(self):
        bad = [b"not json", b'{"modules":[],"modules":[]}']
        duplicate = manifest(entry("A"), entry("A"))
        unknown = copy.deepcopy(manifest(entry("A")))
        unknown["schema"]["version"] = "999"
        traversal = manifest(entry("A"))
        traversal["modules"][0]["path"] = "../outside"
        for value in (duplicate, unknown, traversal, {**manifest(), "modules": {}}, {**manifest(), "extra": 1}):
            bad.append(json.dumps(value).encode())
        for content in bad:
            with self.subTest(content=content), self.assertRaises(ValueError):
                hashes.parse_manifest(content, "fixture")


class ReviewedHashCheckoutTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.repo = Path(self.temp.name)
        self.git("init", "--initial-branch=main")
        self.git("config", "user.name", "Hash test")
        self.git("config", "user.email", "hash-test@example.invalid")
        self.git("config", "commit.gpgsign", "false")
        self.git("config", "core.autocrlf", "false")
        self.write(hashes.REGISTRY, json.dumps({"modules": [{"id": identity, "path": f"src/{identity}", "readme": f"src/{identity}/README.md"} for identity in ("A", "B")]}))
        for identity in ("A", "B"):
            self.write(f"src/{identity}/README.md", f"# {identity}\nReviewed docs\n")
            self.write(f"src/{identity}/first.cs", "// base\n")
            self.write(f"src/{identity}/second.cs", "// base\n")
        self.refresh()
        self.commit("base")
        self.base = self.git("rev-parse", "HEAD").decode().strip()

    def git(self, *args):
        return subprocess.check_output(["git", *args], cwd=self.repo, stderr=subprocess.PIPE)

    def write(self, name, content):
        path = self.repo / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")

    def refresh(self):
        self.write(hashes.MANIFEST, json.dumps(hashes.actual_manifest(self.repo), indent=2, sort_keys=True) + "\n")

    def commit(self, message):
        self.git("add", "-A")
        self.git("commit", "-m", message)

    def branches(self, same_module=False):
        self.git("switch", "-c", "feature")
        self.write("src/A/first.cs", "// feature reviewed\n")
        self.refresh()
        self.commit("feature reviewed")
        self.head = self.git("rev-parse", "HEAD").decode().strip()
        self.git("switch", "main")
        self.write(f"src/{'A' if same_module else 'B'}/second.cs", "// incoming reviewed\n")
        self.refresh()
        self.commit("main reviewed")
        self.incoming = self.git("rev-parse", "HEAD").decode().strip()
        self.git("switch", "feature")
        subprocess.run(["git", "merge", "--no-commit", "--no-ff", "main"], cwd=self.repo, capture_output=True)

    def test_checkout_combines_independent_modules_and_stages_valid_manifest(self):
        self.branches()
        report = hashes.merge_checkout(self.repo, self.base, self.head, self.incoming, apply=True)
        self.assertTrue(report["successful"])
        self.assertEqual(json.loads((self.repo / hashes.MANIFEST).read_bytes()), hashes.actual_manifest(self.repo))
        self.assertFalse(self.git("ls-files", "--unmerged"))

    def test_checkout_same_module_combination_keeps_manifest_unresolved(self):
        self.branches(same_module=True)
        before = (self.repo / hashes.MANIFEST).read_bytes()
        report = hashes.merge_checkout(self.repo, self.base, self.head, self.incoming, apply=True)
        self.assertFalse(report["successful"])
        self.assertIn("A", report["review_required"])
        self.assertEqual(before, (self.repo / hashes.MANIFEST).read_bytes())

    def test_removed_manifest_and_empty_registry_are_not_recreated(self):
        self.git("switch", "-c", "feature")
        self.write(hashes.REGISTRY, '{"modules": []}')
        (self.repo / hashes.MANIFEST).unlink()
        self.commit("remove reviewed module registry")
        head = self.git("rev-parse", "HEAD").decode().strip()
        report = hashes.merge_checkout(self.repo, self.base, head, self.base, apply=True)
        self.assertTrue(report["successful"])
        self.assertFalse((self.repo / hashes.MANIFEST).exists())
        self.assertFalse(self.git("status", "--porcelain"))

    def test_registry_duplicates_and_outside_paths_are_rejected(self):
        for modules in ([{"id": "A", "path": "src/A", "readme": "src/A/README.md"}] * 2,
                        [{"id": "A", "path": "../outside", "readme": "src/A/README.md"}]):
            self.write(hashes.REGISTRY, json.dumps({"modules": modules}))
            with self.assertRaises(ValueError):
                hashes.actual_manifest(self.repo)

    def test_ignored_dependency_symlinks_do_not_affect_source_hashes(self):
        before = hashes.actual_manifest(self.repo)
        bin_path = self.repo / "src/A/node_modules/.bin"
        bin_path.mkdir(parents=True)
        (bin_path / "tool").symlink_to(self.repo / "src/A/first.cs")
        self.assertEqual(before, hashes.actual_manifest(self.repo))

    def test_symlinked_source_is_rejected(self):
        (self.repo / "src/A/link.cs").symlink_to(self.repo / "src/A/first.cs")
        with self.assertRaisesRegex(ValueError, "symlinks"):
            hashes.actual_manifest(self.repo)


if __name__ == "__main__":
    unittest.main()
