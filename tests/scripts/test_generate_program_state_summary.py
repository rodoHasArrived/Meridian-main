from __future__ import annotations

import importlib.util
import re
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPT_PATH = Path(__file__).resolve().parents[2] / "scripts" / "generate_program_state_summary.py"
SPEC = importlib.util.spec_from_file_location("generate_program_state_summary", SCRIPT_PATH)
assert SPEC and SPEC.loader
module = importlib.util.module_from_spec(SPEC)
sys.modules["generate_program_state_summary"] = module
SPEC.loader.exec_module(module)

PROGRAM_STATE = """schema:
  id: meridian.program-state
  version: "1.0.0"
program:
  id: meridian-program
  title: Meridian Evidence-Backed Investment Operations
  owner: Core Team
  snapshot_date: 2026-05-20
  status: active
  current_focus:
    - Trusted data
  active_ui_lane: browser_workstation
  retained_support_lane: wpf
  mobile_lane: closed
"""

ROADMAP_ITEMS = """schema:
  id: meridian.roadmap-items
  version: "1.0.0"
items:
  - id: W1-DATA-001
    title: Provider trust gate and data confidence baseline
    wave: W1
    workspace:
      - Data
    owner_lane: Data Confidence and Validation
    status: done
    health: green
    priority: critical
    evidence_posture: complete
    evidence:
      - type: reference
        path: docs/reference/provider-validation-matrix.md
    last_reviewed: 2026-05-20
"""


def _ordering_item(identifier: str, wave: str, sequence: int | None = None) -> str:
    block = (
        f"  - id: {identifier}\n"
        "    title: Ordering fixture row\n"
        f"    wave: {wave}\n"
    )
    if sequence is not None:
        block += f"    sequence: {sequence}\n"
    return block + "    status: planned\n    health: green\n    owner_lane: Lane\n"


class ProgramStateSummaryGeneratorTests(unittest.TestCase):
    def test_load_program_state_returns_registry_rows(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            data_dir = root / "docs" / "roadmap" / "data"
            data_dir.mkdir(parents=True)
            (data_dir / "program-state.yml").write_text(PROGRAM_STATE, encoding="utf-8")
            (data_dir / "roadmap-items.yml").write_text(ROADMAP_ITEMS, encoding="utf-8")

            program, rows = module.load_program_state(root)

            self.assertEqual("2026-05-20", str(program["snapshot_date"]))
            self.assertEqual(1, len(rows))
            self.assertEqual("W1-DATA-001", rows[0]["ID"])
            self.assertEqual("Data", rows[0]["Workspaces"])
            self.assertEqual("Data Confidence and Validation", rows[0]["Owner Lane"])
            self.assertIn("docs/reference/provider-validation-matrix.md", rows[0]["Evidence"])

    def test_rows_order_by_wave_then_declared_rank(self) -> None:
        # Guards this generator's call site specifically. The shared-key regressions in
        # test_roadmap_source_docs.py would still pass if this script reverted to sorting on the
        # raw identifier, which is exactly the defect this fixture reproduces: lexicographic order
        # puts W10 between W1 and W2 and ignores the sequence rank W10 rows declare.
        roadmap = (
            'schema:\n  id: meridian.roadmap-items\n  version: "1.1.0"\nitems:\n'
            + _ordering_item("W10-CONSOL-001", "W10", sequence=11)
            + _ordering_item("W2-TRD-001", "W2")
            + _ordering_item("W10-MARK-001", "W10", sequence=1)
            + _ordering_item("W9-ASSET-010", "W9")
            + _ordering_item("W9-TRUTH-001", "W9")
        )

        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            data_dir = root / "docs" / "roadmap" / "data"
            data_dir.mkdir(parents=True)
            (data_dir / "program-state.yml").write_text(PROGRAM_STATE, encoding="utf-8")
            (data_dir / "roadmap-items.yml").write_text(roadmap, encoding="utf-8")

            program, rows = module.load_program_state(root)
            markdown = module.render_markdown(program, rows)
            json_payload = module.render_json(program, rows)

        expected = [
            "W2-TRD-001",
            "W9-TRUTH-001",
            "W9-ASSET-010",
            "W10-MARK-001",
            "W10-CONSOL-001",
        ]
        self.assertEqual(expected, [row["ID"] for row in rows])

        # Both rendered outputs are built from the same rows, so assert the order survives into each.
        self.assertEqual(expected, re.findall(r"W\d+[A-Z]*-[A-Z-]+-\d+", markdown)[: len(expected)])
        self.assertEqual(expected, re.findall(r'"ID": "([^"]+)"', json_payload))

    def test_rendered_outputs_reference_roadmap_sources(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            data_dir = root / "docs" / "roadmap" / "data"
            data_dir.mkdir(parents=True)
            (data_dir / "program-state.yml").write_text(PROGRAM_STATE, encoding="utf-8")
            (data_dir / "roadmap-items.yml").write_text(ROADMAP_ITEMS, encoding="utf-8")

            program, rows = module.load_program_state(root)
            markdown = module.render_markdown(program, rows)
            json_payload = module.render_json(program, rows)

            self.assertIn("docs/roadmap/data/program-state.yml", markdown)
            self.assertIn("docs/roadmap/data/roadmap-items.yml", markdown)
            self.assertIn('"schemaVersion": "program-state-summary/v3"', json_payload)
            self.assertIn('"roadmapSource": "docs/roadmap/data/roadmap-items.yml"', json_payload)
            self.assertNotIn("docs/status/PROGRAM_STATE.md", markdown)


if __name__ == "__main__":
    unittest.main()
