from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from tools.schema_control.diffing import build_manifest_diff, compare_artifact_trees


class SchemaDiffTests(unittest.TestCase):
    def test_duplicate_short_names_do_not_overwrite_structural_changes(self) -> None:
        base = {
            "triggers": [
                {"relation": "first", "name": "updated_at", "timing": "before"},
                {"relation": "second", "name": "updated_at", "timing": "before"},
            ]
        }
        current = {
            "triggers": [
                {"relation": "first", "name": "updated_at", "timing": "before"},
                {"relation": "second", "name": "updated_at", "timing": "after"},
            ]
        }

        diff = build_manifest_diff(base, current)

        self.assertEqual(1, diff["counts"]["changed"])
        self.assertEqual("$.triggers", diff["changes"][0]["path"])

    def test_keys_object_lists_and_reports_added_removed_changed(self) -> None:
        base = {"tables": [{"name": "a", "hash": "1"}, {"name": "gone", "hash": "x"}]}
        current = {"tables": [{"name": "a", "hash": "2"}, {"name": "new", "hash": "y"}]}

        result = build_manifest_diff(base, current)

        self.assertEqual({"added": 1, "removed": 1, "changed": 1}, result["counts"])
        self.assertEqual(64, len(result["fingerprint"]))

    def test_artifact_tree_comparison_is_deterministic(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            expected = root / "expected"
            candidate = root / "candidate"
            expected.mkdir()
            candidate.mkdir()
            (expected / "same.json").write_text("{}\n", encoding="utf-8")
            (candidate / "same.json").write_text("{}\n", encoding="utf-8")
            (expected / "changed.md").write_text("old\n", encoding="utf-8")
            (candidate / "changed.md").write_text("new\n", encoding="utf-8")
            (candidate / "added.mmd").write_text("graph TD\n", encoding="utf-8")

            first = compare_artifact_trees(expected, candidate)
            second = compare_artifact_trees(expected, candidate)

            self.assertEqual(first, second)
            self.assertFalse(first["clean"])
            self.assertEqual(["added.mmd"], first["added"])
            self.assertEqual(["changed.md"], first["changed"])


if __name__ == "__main__":
    unittest.main()
