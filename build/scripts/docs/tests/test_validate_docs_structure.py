#!/usr/bin/env python3
"""Focused validation for validate-docs-structure.py behavior."""

from __future__ import annotations

import importlib.util
import io
import sys
import tempfile
import unittest
from contextlib import redirect_stdout
from datetime import date, timedelta
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
            self.assertIn("docs/ai/guide.md", output.getvalue())
            self.assertNotIn("docs/architecture/guide.md", output.getvalue())

    def test_body_and_examples_cannot_supply_lifecycle_fields(self) -> None:
        examples = [
            "## Example\n**Owner:** example-team\n**Reviewed:** 2026-09-01\n",
            "```markdown\n**Owner:** example-team\n**Reviewed:** 2026-09-01\n```\n",
            "~~~markdown\n**Owner:** example-team\n**Reviewed:** 2026-09-01\n~~~\n",
            "```markdown\n**Owner:** example-team\n**Reviewed:** 2026-09-01\n",
            "```yaml\n---\nowner: example-team\nreviewed: 2026-09-01\n---\n",
            "This guide describes ownership.\n**Owner:** example-team\n**Reviewed:** 2026-09-01\n",
            "This guide describes ownership.\n---\nowner: example-team\nreviewed: 2026-09-01\n---\n",
        ]
        for body in examples:
            with self.subTest(body=body), tempfile.TemporaryDirectory() as tmp:
                root = Path(tmp)
                write_text(root, "docs/development/guide.md", "# Guide\n\n**Status:** active\n\n" + body)
                with redirect_stdout(io.StringIO()):
                    issues = validate_docs_structure.check_front_matter(root / "docs", False, False)
                self.assertEqual(2, len(issues))
                self.assertTrue(any("**Owner:**" in issue for issue in issues))
                self.assertTrue(any("**Reviewed:**" in issue for issue in issues))

    def test_lifecycle_dates_report_invalid_and_stale_values(self) -> None:
        stale = (date.today() - timedelta(days=181)).isoformat()
        for reviewed, expected in [
            ("YYYY-MM-DD", "Invalid Reviewed date"),
            ("2026-02-30", "Invalid Reviewed date"),
            ("2026-9-1", "Invalid Reviewed date"),
            (stale, "Stale document"),
        ]:
            with self.subTest(reviewed=reviewed), tempfile.TemporaryDirectory() as tmp:
                root = Path(tmp)
                write_text(root, "docs/reference/guide.md",
                           f"# Guide\n\n**Status:** active\n**Owner:** core-team\n**Reviewed:** {reviewed}\n")
                with redirect_stdout(io.StringIO()):
                    issues = validate_docs_structure.check_front_matter(root / "docs", False, False)
                self.assertEqual(1, len(issues))
                self.assertIn(expected, issues[0])

    def test_valid_header_metadata_is_accepted(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_text(root, "docs/reference/guide.md",
                       f"# Guide\n\n**Version:** 2\n**Status:** active\n**Owner:** core-team\n"
                       f"**Reviewed:** {date.today().isoformat()}\n\n## Procedure\n")
            with redirect_stdout(io.StringIO()):
                issues = validate_docs_structure.check_front_matter(root / "docs", False, False)
            self.assertEqual([], issues)

    def test_empty_field_cannot_borrow_the_following_line(self) -> None:
        content = "# Guide\n\n**Status:** active\n**Owner:**\n**Reviewed:** 2026-09-01\n"
        self.assertIsNone(validate_docs_structure.extract_field(content, "Owner"))
        self.assertEqual("2026-09-01", validate_docs_structure.extract_field(content, "Reviewed"))

    def test_yaml_header_is_accepted_with_quoted_and_date_scalars(self) -> None:
        for reviewed in [date.today().isoformat(), f'"{date.today().isoformat()}"']:
            with self.subTest(reviewed=reviewed), tempfile.TemporaryDirectory() as tmp:
                root = Path(tmp)
                write_text(root, "docs/reference/guide.md",
                           f'---\nstatus: active\nOwner: "Core: Team"\nreviewed: {reviewed}\n---\n# Guide\n')
                with redirect_stdout(io.StringIO()):
                    issues = validate_docs_structure.check_front_matter(root / "docs", False, False)
                self.assertEqual([], issues)

    def test_empty_yaml_values_are_missing_metadata(self) -> None:
        for owner in ["", '""', "null", "[]", "{}"]:
            with self.subTest(owner=owner), tempfile.TemporaryDirectory() as tmp:
                root = Path(tmp)
                write_text(root, "docs/reference/guide.md",
                           f"---\nstatus: active\nowner: {owner}\nreviewed: {date.today().isoformat()}\n---\n# Guide\n")
                with redirect_stdout(io.StringIO()):
                    issues = validate_docs_structure.check_front_matter(root / "docs", False, False)
                self.assertEqual(1, len(issues))
                self.assertIn("**Owner:**", issues[0])

    def test_malformed_yaml_header_is_reported(self) -> None:
        for content in ["---\nowner: team\n", "---\nowner: [team\n---\n", "---\n- team\n---\n"]:
            with self.subTest(content=content), tempfile.TemporaryDirectory() as tmp:
                root = Path(tmp)
                write_text(root, "docs/reference/guide.md", content)
                with redirect_stdout(io.StringIO()):
                    issues = validate_docs_structure.check_front_matter(root / "docs", False, False)
                self.assertEqual(1, len(issues))
                self.assertIn("Invalid lifecycle header", issues[0])

    def test_generated_reports_are_separate_from_hand_authored_status_documents(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_text(root, "docs/status/runbook.md", "# Runbook\n\nOperator procedure.\n")
            write_text(root, "docs/status/header-report.md", "# Report\n\n> Auto-generated by `report.py`.\n")
            write_text(root, "docs/status/contract-report.md",
                       "<!--\ngenerated: true\ngenerator: report.py\n-->\n# Report\n")
            write_text(root, "docs/status/yaml-report.md",
                       "---\ngenerated: true\ngenerator: report.py\n---\n# Report\n")
            write_text(root, "docs/status/comment-report.md",
                       "# Report\n\n<!-- Generated by report.py. Do not edit by hand. -->\n")
            write_text(root, "docs/status/program-state.md",
                       "# Program State\n\nThis file is generated from `docs/roadmap/data/program-state.yml`.\n")
            write_text(root, "docs/generated/report.md", "# Generated\n")
            write_text(root, "docs/status/README.md", "# Status\n")
            output = io.StringIO()
            with redirect_stdout(output):
                issues = validate_docs_structure.check_front_matter(root / "docs", False, False)
            self.assertEqual(3, len(issues))
            self.assertTrue(all("runbook.md" in issue for issue in issues))
            self.assertIn("1 hand-authored document(s) checked", output.getvalue())
            self.assertIn("6 generated output(s) excluded", output.getvalue())

    def test_generated_examples_do_not_exempt_a_hand_authored_guide(self) -> None:
        for body in [
            "```markdown\n> Auto-generated by `report.py`.\n```\n",
            "## Example\n<!--\ngenerated: true\ngenerator: report.py\n-->\n",
            "Instructions:\nThis file is generated from `docs/roadmap/data/program-state.yml`.\n",
        ]:
            with self.subTest(body=body), tempfile.TemporaryDirectory() as tmp:
                root = Path(tmp)
                write_text(root, "docs/status/guide.md", "# Guide\n\n" + body)
                with redirect_stdout(io.StringIO()):
                    issues = validate_docs_structure.check_front_matter(root / "docs", False, False)
                self.assertEqual(3, len(issues))

    def test_registry_outputs_are_read_without_executing_the_runner(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            write_text(root, "build/scripts/docs/run-docs-automation.py", '''
raise RuntimeError("Must never execute registry code")
SCRIPT_CONFIG: dict = {
    "report": {"output": "docs/status/report.md", "args": []},
    "packet": {"output": "docs/status/packet.json", "args": ["--output", "docs/status/packet.md"]},
    "computed": {"output": COMPUTED_PATH},
}
''')
            for name in ["report", "packet", "runbook"]:
                write_text(root, f"docs/status/{name}.md", f"# {name}\n")
            with redirect_stdout(io.StringIO()):
                issues = validate_docs_structure.check_front_matter(root / "docs", False, False)
            self.assertEqual(3, len(issues))
            self.assertTrue(all("runbook.md" in issue for issue in issues))

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
