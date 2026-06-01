#!/usr/bin/env python3
"""Regression tests for repository structure generation."""

from __future__ import annotations

import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path


DOCS_SCRIPT_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(DOCS_SCRIPT_DIR))


def load_module(name: str, filename: str):
    spec = importlib.util.spec_from_file_location(name, DOCS_SCRIPT_DIR / filename)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Unable to load {filename}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


generate_structure_docs = load_module(
    "generate_structure_docs_under_test",
    "generate-structure-docs.py",
)


class GenerateStructureDocsTests(unittest.TestCase):
    def test_render_tree_skips_local_artifacts_and_keeps_canonical_data_docs(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / "src").mkdir()
            (root / "src" / "Meridian.cs").write_text("namespace Meridian;\n", encoding="utf-8")
            (root / "docs" / "source" / "data").mkdir(parents=True)
            (root / "docs" / "source" / "data" / "source-modules.yml").write_text(
                "modules: []\n",
                encoding="utf-8",
            )
            (root / "data").mkdir()
            (root / "data" / "meridian-20260530.log").write_text("local log\n", encoding="utf-8")
            (root / "build" / "scripts" / "__pycache__").mkdir(parents=True)
            (root / "build" / "scripts" / "__pycache__" / "tool.cpython-312.pyc").write_bytes(b"0")
            (root / "config").mkdir()
            (root / "config" / "appsettings.json.backup-20260529-235335").write_text(
                "{}\n",
                encoding="utf-8",
            )
            (root / ".nuget" / "packages").mkdir(parents=True)
            (root / ".nuget" / "packages" / "package.nupkg").write_bytes(b"0")
            (root / "artifacts" / "wpf").mkdir(parents=True)
            (root / "artifacts" / "wpf" / "build.log").write_text("local log\n", encoding="utf-8")

            rendered = generate_structure_docs.render_tree(root)

            self.assertIn("Meridian.cs", rendered)
            self.assertIn("source-modules.yml", rendered)
            self.assertNotIn("meridian-20260530.log", rendered)
            self.assertNotIn("__pycache__", rendered)
            self.assertNotIn("appsettings.json.backup", rendered)
            self.assertNotIn(".nuget", rendered)
            self.assertNotIn("artifacts", rendered)

    def test_render_tree_skips_symlinked_entries(self) -> None:
        with tempfile.TemporaryDirectory() as tmp, tempfile.TemporaryDirectory() as external:
            root = Path(tmp)
            external_root = Path(external)
            (external_root / "outside.txt").write_text("outside\n", encoding="utf-8")
            link = root / "external-link"

            try:
                link.symlink_to(external_root, target_is_directory=True)
            except OSError as exc:
                self.skipTest(f"symlink creation is unavailable: {exc}")

            rendered = generate_structure_docs.render_tree(root)

            self.assertNotIn("external-link", rendered)
            self.assertNotIn("outside.txt", rendered)


if __name__ == "__main__":
    unittest.main()
