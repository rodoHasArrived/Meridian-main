#!/usr/bin/env python3
"""Regression tests for generated Markdown lint compatibility."""

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


dashboard_rendering = load_module("dashboard_rendering_under_test", "dashboard_rendering.py")
generate_health_dashboard = load_module(
    "generate_health_dashboard_under_test",
    "generate-health-dashboard.py",
)
scan_todos = load_module("scan_todos_under_test", "scan-todos.py")
update_claude_md = load_module("update_claude_md_under_test", "update-claude-md.py")


class MarkdownGenerationLintTests(unittest.TestCase):
    def test_ai_structure_sync_keeps_blank_line_before_next_heading(self) -> None:
        original = "# Guide\n\n- Previous list item\n## Repository Structure\n\n```text\nold\n```\n## Standard Workflow\n\nBody\n"
        replacement = "```text\nnew\n```"

        updated = update_claude_md.update_target(original, replacement)

        self.assertIn("- Previous list item\n\n## Repository Structure", updated)
        self.assertIn("```text\nnew\n```\n\n## Standard Workflow", updated)

    def test_todo_scan_table_uses_configured_markdownlint_spacing(self) -> None:
        item = scan_todos.TodoItem(
            file="src/Sample.cs",
            line=10,
            tag="TODO",
            text="// TODO: wire this up",
            has_issue=False,
            issue_refs=[],
            priority="normal",
            context="10: // TODO: wire this up",
        )

        with tempfile.TemporaryDirectory() as tmp:
            output = Path(tmp) / "TODO.md"
            scan_todos.write_markdown(output, [item])

            self.assertIn("| --- | ---: | --- | :---: | --- |", output.read_text(encoding="utf-8"))

    def test_dashboard_rendering_starts_with_heading_before_metadata(self) -> None:
        rendered = dashboard_rendering.render_markdown_from_json(
            json_payload={"generated_at": "2026-04-29T00:00:00Z"},
            data_sources=["unit"],
            render_body=lambda _: "# Demo Dashboard\n\nBody",
        )

        self.assertTrue(rendered.startswith("# Demo Dashboard\n\n_Auto-generated"))

    def test_health_dashboard_output_uses_lint_friendly_tables_and_footer(self) -> None:
        rendered = generate_health_dashboard.generate_markdown_from_json_payload(
            {
                "generated_at": "2026-04-29T00:00:00Z",
                "total_files": 1,
                "total_lines": 25,
                "orphaned_count": 0,
                "no_heading_count": 0,
                "stale_count": 0,
                "todo_count": 0,
                "average_lines": 25.0,
                "health_score": 100,
                "orphaned_files": [],
                "no_heading_files": [],
                "stale_files": [],
                "scan_time": "2026-04-29T00:00:00Z",
                "root_dir": "repo",
                "all_files": [],
            }
        )

        self.assertTrue(rendered.startswith("# Documentation Health Dashboard"))
        self.assertIn("| -------- | ------- |", rendered)
        self.assertIn("_This file is auto-generated. Do not edit manually._", rendered)

    def test_health_dashboard_output_normalizes_windows_paths(self) -> None:
        rendered = generate_health_dashboard.generate_markdown_from_json_payload(
            {
                "generated_at": "2026-04-29T00:00:00Z",
                "total_files": 1,
                "total_lines": 25,
                "orphaned_count": 1,
                "no_heading_count": 1,
                "stale_count": 1,
                "todo_count": 0,
                "average_lines": 25.0,
                "health_score": 60,
                "orphaned_files": [r".claude\skills\demo.md"],
                "no_heading_files": [r"docs\generated\empty.md"],
                "stale_files": [r"docs\old\stale.md"],
                "scan_time": "2026-04-29T00:00:00Z",
                "root_dir": "repo",
                "all_files": [],
            }
        )

        self.assertIn("`.claude/skills/demo.md`", rendered)
        self.assertIn("`docs/generated/empty.md`", rendered)
        self.assertIn("`docs/old/stale.md`", rendered)
        self.assertNotIn(r".claude\skills\demo.md", rendered)


if __name__ == "__main__":
    unittest.main()
