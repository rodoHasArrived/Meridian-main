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

    def test_explicit_html_anchor_remains_supported(self) -> None:
        anchors = repair_links._extract_anchors('# Guide\n\n<a id="kept--anchor"></a>\n')
        self.assertIn("kept--anchor", anchors)


if __name__ == "__main__":
    unittest.main()
