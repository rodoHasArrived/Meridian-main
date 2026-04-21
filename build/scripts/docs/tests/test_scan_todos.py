#!/usr/bin/env python3
"""Focused validation for scan-todos.py behavior."""

from __future__ import annotations

import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path


MODULE_PATH = Path(__file__).resolve().parents[1] / "scan-todos.py"
SPEC = importlib.util.spec_from_file_location("scan_todos", MODULE_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError(f"Unable to load scan-todos module from {MODULE_PATH}")

scan_todos = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = scan_todos
SPEC.loader.exec_module(scan_todos)


class ScanTodosTests(unittest.TestCase):
    def test_scan_file_matches_annotation_style_tags_only(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            file_path = root / "src" / "Sample.cs"
            file_path.parent.mkdir(parents=True, exist_ok=True)
            file_path.write_text(
                "\n".join(
                    [
                        'var value = "TODO: string literal should be ignored";',
                        "TODO scanning overview should be ignored",
                        "// TODO: wire up execution path",
                        "1. NOTE: review workstation flow",
                        "HACK: remove this shim later",
                    ]
                ),
                encoding="utf-8",
            )

            items = scan_todos.scan_file(file_path, root, include_notes=True)

            self.assertEqual(["TODO", "NOTE", "HACK"], [item.tag for item in items])
            self.assertEqual(3, len(items))

    def test_iter_files_skips_generated_and_worktree_paths(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            included = root / "src" / "Active.cs"
            excluded_dist = root / "dist" / "bundle.js"
            excluded_worktree = root / ".claude" / "worktrees" / "copy" / "Active.cs"
            excluded_status = root / "docs" / "status" / "TODO.md"

            for path in (included, excluded_dist, excluded_worktree, excluded_status):
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text("// TODO: placeholder\n", encoding="utf-8")

            discovered = {
                path.relative_to(root).as_posix()
                for path in scan_todos.iter_files(root)
            }

            self.assertEqual({"src/Active.cs"}, discovered)

    def test_scan_file_captures_issue_refs_priority_and_context(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            file_path = root / "docs" / "guide.md"
            file_path.parent.mkdir(parents=True, exist_ok=True)
            file_path.write_text(
                "\n".join(
                    [
                        "Before context",
                        "| Item | Value |",
                        "| --- | --- |",
                        "| Work | FIXME: track with issue #42 before release |",
                        "After context",
                    ]
                ),
                encoding="utf-8",
            )

            items = scan_todos.scan_file(file_path, root, include_notes=True)

            self.assertEqual(1, len(items))
            item = items[0]
            self.assertTrue(item.has_issue)
            self.assertEqual(["#42"], item.issue_refs)
            self.assertEqual("high", item.priority)
            self.assertIn("3: | --- | --- |", item.context)
            self.assertIn("4: | Work | FIXME: track with issue #42 before release |", item.context)
            self.assertIn("5: After context", item.context)


if __name__ == "__main__":
    unittest.main()
