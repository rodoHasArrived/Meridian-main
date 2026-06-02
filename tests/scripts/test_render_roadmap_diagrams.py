from __future__ import annotations

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


class RenderRoadmapDiagramsTests(unittest.TestCase):
    def test_renderer_labels_nodes_with_registry_wave(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            data_dir = root / "docs" / "roadmap" / "data"
            data_dir.mkdir(parents=True)
            (data_dir / "roadmap-items.yml").write_text(ROADMAP_ITEMS, encoding="utf-8")

            result = subprocess.run(
                [sys.executable, str(SCRIPT_PATH), "--root", str(root), "--summary"],
                check=False,
                capture_output=True,
                text=True,
            )

            self.assertEqual(0, result.returncode, result.stderr)
            diagram = (root / "docs" / "architecture" / "diagrams" / "meridian-development-roadmap.mmd").read_text(encoding="utf-8")
            self.assertIn('W6_BTSTUDIO_001["W6-BTSTUDIO-001\\nW6 - planned / green"]', diagram)
            self.assertIn('W7_LIVE_001["W7-LIVE-001\\nW7 - planned / green"]', diagram)


if __name__ == "__main__":
    unittest.main()
