from __future__ import annotations

import importlib.util
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
            self.assertIn('"schemaVersion": "program-state-summary/v2"', json_payload)
            self.assertIn('"roadmapSource": "docs/roadmap/data/roadmap-items.yml"', json_payload)
            self.assertNotIn("docs/status/PROGRAM_STATE.md", markdown)


if __name__ == "__main__":
    unittest.main()
