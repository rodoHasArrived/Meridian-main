from __future__ import annotations

import re
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


SCRIPT_PATH = Path(__file__).resolve().parents[2] / "build" / "scripts" / "docs" / "render-roadmap-diagrams.py"


ROADMAP_ITEMS = """schema:
  id: meridian.roadmap-items
  version: "1.0.0"
items:
  - id: W6-BTSTUDIO-001
    title: Backtesting studio evidence loop
    wave: W6
    status: planned
    health: green
  - id: W7-LIVE-001
    title: Live-readiness governance
    wave: W7
    status: planned
    health: green
"""


def _item(identifier: str, wave: str) -> str:
    return (
        f"  - id: {identifier}\n"
        f"    title: {identifier}\n"
        f"    wave: {wave}\n"
        "    status: planned\n"
        "    health: green\n"
    )


def _registry(*entries: tuple[str, str]) -> str:
    header = 'schema:\n  id: meridian.roadmap-items\n  version: "1.0.0"\nitems:\n'
    return header + "".join(_item(identifier, wave) for identifier, wave in entries)


class RenderRoadmapDiagramsTests(unittest.TestCase):
    def _render(self, registry: str) -> str:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            data_dir = root / "docs" / "roadmap" / "data"
            data_dir.mkdir(parents=True)
            (data_dir / "roadmap-items.yml").write_text(registry, encoding="utf-8")

            result = subprocess.run(
                [sys.executable, str(SCRIPT_PATH), "--root", str(root), "--summary"],
                check=False,
                capture_output=True,
                text=True,
            )

            self.assertEqual(0, result.returncode, result.stderr)
            return (
                root / "docs" / "architecture" / "diagrams" / "meridian-development-roadmap.mmd"
            ).read_text(encoding="utf-8")

    @staticmethod
    def _node_order(diagram: str) -> list[str]:
        return re.findall(r"^  ([A-Z0-9_]+)\[", diagram, flags=re.MULTILINE)

    def test_renderer_labels_nodes_with_registry_wave(self) -> None:
        diagram = self._render(ROADMAP_ITEMS)
        self.assertIn('W6_BTSTUDIO_001["W6-BTSTUDIO-001\\nW6 - planned / green"]', diagram)
        self.assertIn('W7_LIVE_001["W7-LIVE-001\\nW7 - planned / green"]', diagram)

    def test_double_digit_wave_sorts_after_single_digit_waves(self) -> None:
        # Lexicographic ordering placed "W10-" between "W1-" and "W2-", rendering a planned wave
        # in the middle of delivered work and drawing an edge from it into an earlier wave.
        diagram = self._render(
            _registry(
                ("W10-MARK-001", "W10"),
                ("W2-TRD-001", "W2"),
                ("W1-DATA-001", "W1"),
                ("W9-TRUTH-001", "W9"),
            )
        )
        self.assertEqual(
            ["W1_DATA_001", "W2_TRD_001", "W9_TRUTH_001", "W10_MARK_001"],
            self._node_order(diagram),
        )

    def test_items_order_by_sequence_within_a_wave_not_by_area(self) -> None:
        # DEC-PRIORITY-SLATE-001 records that rank is carried in each W9 item's numeric suffix, so
        # ordering alphabetically by area discards it.
        diagram = self._render(
            _registry(
                ("W9-ASSET-010", "W9"),
                ("W9-DEMO-002", "W9"),
                ("W9-TRUTH-001", "W9"),
                ("W9-ALPACA-004", "W9"),
            )
        )
        self.assertEqual(
            ["W9_TRUTH_001", "W9_DEMO_002", "W9_ALPACA_004", "W9_ASSET_010"],
            self._node_order(diagram),
        )

    def test_lettered_wave_suffix_sorts_after_its_base_wave(self) -> None:
        diagram = self._render(
            _registry(
                ("W5X-CONNECT-001", "W5X"),
                ("W6-BTSTUDIO-001", "W6"),
                ("W5-ACCT-001", "W5"),
            )
        )
        self.assertEqual(
            ["W5_ACCT_001", "W5X_CONNECT_001", "W6_BTSTUDIO_001"],
            self._node_order(diagram),
        )


if __name__ == "__main__":
    unittest.main()
