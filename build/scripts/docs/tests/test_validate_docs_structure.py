#!/usr/bin/env python3
"""Focused validation for validate-docs-structure.py behavior."""

from __future__ import annotations

import importlib.util
import io
import sys
import tempfile
import unittest
from contextlib import redirect_stdout
from pathlib import Path


MODULE_PATH = Path(__file__).resolve().parents[1] / "validate-docs-structure.py"
SPEC = importlib.util.spec_from_file_location("validate_docs_structure", MODULE_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError(f"Unable to load validate-docs-structure module from {MODULE_PATH}")

validate_docs_structure = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = validate_docs_structure
SPEC.loader.exec_module(validate_docs_structure)


def write_text(root: Path, rel_path: str, content: str) -> None:
    path = root / rel_path
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")


class ValidateDocsStructureTests(unittest.TestCase):
    def test_top_level_model_allows_supporting_lanes_without_warning(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            docs_dir = Path(tmp) / "docs"
            write_text(Path(tmp), "docs/architecture/README.md", "# Architecture\n")
            write_text(Path(tmp), "docs/security/README.md", "# Security\n")

            with redirect_stdout(io.StringIO()):
                errors, warnings = validate_docs_structure.check_top_level_model(
                    docs_dir,
                    github_actions=False,
                )

            self.assertEqual([], errors)
            self.assertEqual([], warnings)

    def test_top_level_model_warns_for_transitional_lane(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            docs_dir = Path(tmp) / "docs"
            write_text(Path(tmp), "docs/operations/README.md", "# Operations\n")

            output = io.StringIO()
            with redirect_stdout(output):
                errors, warnings = validate_docs_structure.check_top_level_model(
                    docs_dir,
                    github_actions=False,
                )

            self.assertEqual([], errors)
            self.assertEqual(1, len(warnings))
            self.assertIn("Transitional compatibility folder", output.getvalue())

    def test_top_level_model_rejects_retired_lane(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            docs_dir = Path(tmp) / "docs"
            write_text(Path(tmp), "docs/providers/README.md", "# Providers\n")

            output = io.StringIO()
            with redirect_stdout(output):
                errors, warnings = validate_docs_structure.check_top_level_model(
                    docs_dir,
                    github_actions=False,
                )

            self.assertEqual(1, len(errors))
            self.assertEqual([], warnings)
            self.assertIn("Unexpected top-level docs folder", output.getvalue())

    def test_scoped_top_level_front_matter_checks_ignore_other_folders(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            docs_dir = root / "docs"
            write_text(root, "docs/README.md", "# Docs\n")
            write_text(root, "docs/ai/README.md", "# AI\n")
            write_text(root, "docs/architecture/README.md", "# Architecture\n")
            write_text(root, "docs/ai/guide.md", "# Guide\n")
            write_text(root, "docs/architecture/guide.md", "# Guide\n")

            output = io.StringIO()
            with redirect_stdout(output):
                issues = validate_docs_structure.check_front_matter_in_dirs(
                    docs_dir,
                    [docs_dir / "ai"],
                    github_actions=False,
                    strict=False,
                )

            self.assertEqual(3, len(issues))
            self.assertIn("docs\\ai\\guide.md", output.getvalue())
            self.assertNotIn("docs\\architecture\\guide.md", output.getvalue())

    def test_resolve_top_level_scope_reports_missing_folder(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            docs_dir = root / "docs"
            docs_dir.mkdir(parents=True, exist_ok=True)
            write_text(root, "docs/README.md", "# Docs\n")

            with redirect_stdout(io.StringIO()):
                scoped_dirs, errors = validate_docs_structure.resolve_top_level_scope(
                    docs_dir,
                    ["ai"],
                    github_actions=False,
                )

            self.assertEqual([], scoped_dirs)
            self.assertEqual(1, len(errors))
            self.assertIn("docs/ai/", errors[0])
