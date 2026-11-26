#!/usr/bin/env python3
"""Regression coverage for GitHub heading links in documentation."""

from __future__ import annotations

import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path


MODULE_PATH = Path(__file__).resolve().parents[1] / "repair-links.py"
SPEC = importlib.util.spec_from_file_location("repair_links", MODULE_PATH)
assert SPEC is not None and SPEC.loader is not None
repair_links = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = repair_links
SPEC.loader.exec_module(repair_links)


class RepairLinksTests(unittest.TestCase):
    def test_github_heading_links_preserve_adjacent_hyphens(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            target = root / "acceptance.md"
            target.write_text("# Acceptance\n\n## W9-TRUTH-001 owner exception — 2026-09-26\n", encoding="utf-8")
            source = root / "tracker.md"
            original = (
                "# Tracker\n\n"
                "[Valid](acceptance.md#w9-truth-001-owner-exception--2026-09-26)\n"
                "[Invalid](acceptance.md#w9-truth-001-owner-exception-2026-09-26)\n"
            )
            source.write_text(original, encoding="utf-8")
            result = repair_links.ScanResult()
            repairs = repair_links._scan_file(source, root, {}, False, result)
            self.assertEqual(2, result.total_links_checked)
            self.assertEqual(1, len(result.broken_links))
            self.assertEqual("Invalid", result.broken_links[0].location.link_text)
            self.assertEqual([], repairs)
            self.assertEqual(original, source.read_text(encoding="utf-8"))

    def test_skill_uris_are_external_but_missing_local_paths_still_fail(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            source = root / "receipt.md"
            original = (
                "# Receipt\n\n"
                "[Skill](skill://user/example/SKILL.md)\n"
                "[Skill case](SKILL://user/example/SKILL.md)\n"
                "[Missing](missing-local-guide.md)\n"
            )
            source.write_text(original, encoding="utf-8")
            result = repair_links.ScanResult()
            repairs = repair_links._scan_file(source, root, {}, True, result)
            self.assertEqual(1, result.total_links_checked)
            self.assertEqual(1, len(result.broken_links))
            self.assertEqual("Missing", result.broken_links[0].location.link_text)
            self.assertEqual([], repairs)
            self.assertEqual(original, source.read_text(encoding="utf-8"))

    def test_source_line_anchors_check_bounds_ranges_and_markdown_headings(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / "source.cs").write_text("line one\nline two\nline three\n")
            (root / "guide.md").write_text("# Heading\n\nParagraph.\n")
            source = root / "reference.md"
            links = [
                ("First", "source.cs#L1"), ("Last", "source.cs#L3"),
                ("Range", "source.cs#L1-L3"), ("Zero", "source.cs#L0"),
                ("Overflow", "source.cs#L4"), ("Reversed", "source.cs#L3-L2"),
                ("Range overflow", "source.cs#L1-L4"), ("Malformed", "source.cs#L1-L"),
                ("Source heading", "source.cs#missing-heading"),
                ("Markdown heading", "guide.md#heading"),
                ("Markdown line", "guide.md#L1"),
            ]
            source.write_text("# Reference\n" + "".join(f"[{text}]({target})\n" for text, target in links))
            result = repair_links.ScanResult()
            cache = {}
            repair_links._scan_file(source, root, {}, False, result, cache)
            self.assertEqual(len(links), result.total_links_checked)
            self.assertEqual(
                {"Zero", "Overflow", "Reversed", "Range overflow", "Malformed", "Source heading", "Markdown line"},
                {item.location.link_text for item in result.broken_links},
            )
            self.assertEqual(2, len(cache))

    def test_explicit_html_anchor_remains_supported(self) -> None:
        anchors = repair_links._extract_anchors('# Guide\n\n<a id="kept--anchor"></a>\n')
        self.assertIn("kept--anchor", anchors)


if __name__ == "__main__":
    unittest.main()
