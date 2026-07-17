#!/usr/bin/env python3
"""Regression tests for repository structure generation."""

from __future__ import annotations

import importlib.util
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from pathlib import PurePosixPath
from unittest.mock import patch


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
    def test_git_visible_files_merge_case_colliding_index_paths_and_filesystem_outputs(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / ".github").mkdir()
            (root / ".github" / "pull_request_template.md").write_text("template\n", encoding="utf-8")
            (root / "docs" / "status").mkdir(parents=True)
            (root / "docs" / "status" / "todo-scan-results.json").write_text("{}\n", encoding="utf-8")
            git_result = subprocess.CompletedProcess(
                args=["git", "ls-files"],
                returncode=0,
                stdout=(
                    b".github/PULL_REQUEST_TEMPLATE.md\0"
                    b".github/pull_request_template.md\0"
                ),
                stderr=b"",
            )

            with patch.object(generate_structure_docs.subprocess, "run", return_value=git_result):
                visible = generate_structure_docs._git_visible_files(root)

            self.assertIsNotNone(visible)
            self.assertEqual(
                {
                    ".github/PULL_REQUEST_TEMPLATE.md",
                    ".github/pull_request_template.md",
                    "docs/status/todo-scan-results.json",
                },
                {path.as_posix() for path in visible or []},
            )

    def test_render_tree_preserves_case_colliding_git_paths(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            visible_files = [
                PurePosixPath(".github/PULL_REQUEST_TEMPLATE.md"),
                PurePosixPath(".github/pull_request_template.md"),
                PurePosixPath(".github/pull_request_template_desktop.md"),
            ]

            with patch.object(generate_structure_docs, "_git_visible_files", return_value=visible_files):
                rendered = generate_structure_docs.render_tree(root)

            self.assertIn("PULL_REQUEST_TEMPLATE.md", rendered)
            self.assertIn("pull_request_template.md", rendered)
            self.assertLess(
                rendered.index("PULL_REQUEST_TEMPLATE.md"),
                rendered.index("pull_request_template.md"),
            )

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
            (root / "config" / "appsettings.json.backup-20260101-000000").write_text(
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

    def test_render_tree_skips_nested_worktree_checkouts(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / "src").mkdir()
            (root / "src" / "Meridian.cs").write_text("namespace Meridian;\n", encoding="utf-8")
            (root / ".claude" / "settings.json").parent.mkdir(parents=True)
            (root / ".claude" / "settings.json").write_text("{}\n", encoding="utf-8")
            # A git-ignored worktree holds a second checkout of the repository.
            worktree_src = root / ".claude" / "worktrees" / "task-branch" / "src"
            worktree_src.mkdir(parents=True)
            (worktree_src / "WorktreeCopy.cs").write_text("namespace Meridian;\n", encoding="utf-8")

            rendered = generate_structure_docs.render_tree(root)

            self.assertIn("Meridian.cs", rendered)
            self.assertIn("settings.json", rendered)
            self.assertNotIn("worktrees", rendered)
            self.assertNotIn("WorktreeCopy.cs", rendered)

    def test_parse_args_defaults_output_to_canonical_path_per_mode(self) -> None:
        cases = [
            ([], "docs/generated/repository-structure.md"),
            (["--workflows-only"], "docs/generated/workflows-overview.md"),
            (["--providers-only"], "docs/generated/provider-registry.md"),
        ]
        for argv, expected in cases:
            with self.subTest(argv=argv):
                with patch.object(sys, "argv", ["generate-structure-docs.py", *argv]):
                    args = generate_structure_docs.parse_args()
                self.assertIsNone(args.output)

                mode = "structure"
                if args.workflows_only:
                    mode = "workflows"
                elif args.providers_only:
                    mode = "providers"
                self.assertEqual(expected, generate_structure_docs.DEFAULT_OUTPUTS[mode])

    def test_parse_args_honors_explicit_output_override(self) -> None:
        with patch.object(
            sys, "argv", ["generate-structure-docs.py", "--workflows-only", "--output", "custom.md"]
        ):
            args = generate_structure_docs.parse_args()
        self.assertEqual("custom.md", args.output)

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
