from __future__ import annotations

import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path

GENERATOR_PATH = Path(__file__).resolve().parents[2] / "scripts" / "generate_program_state_summary.py"
GENERATOR_SPEC = importlib.util.spec_from_file_location("generate_program_state_summary", GENERATOR_PATH)
assert GENERATOR_SPEC and GENERATOR_SPEC.loader
generator = importlib.util.module_from_spec(GENERATOR_SPEC)
sys.modules["generate_program_state_summary"] = generator
GENERATOR_SPEC.loader.exec_module(generator)

SCRIPT_PATH = Path(__file__).resolve().parents[2] / "scripts" / "check_program_state_consistency.py"
SPEC = importlib.util.spec_from_file_location("check_program_state_consistency", SCRIPT_PATH)
assert SPEC and SPEC.loader
module = importlib.util.module_from_spec(SPEC)
sys.modules["check_program_state_consistency"] = module
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


class ProgramStateConsistencyTests(unittest.TestCase):
    def test_validate_generated_outputs_accepts_current_registry_render(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = _write_registry(Path(tmp))
            program, rows = generator.load_program_state(root)
            _write_outputs(root, program, rows)

            errors = module.validate_generated_outputs(root)

            self.assertEqual([], errors)

    def test_validate_generated_outputs_rejects_stale_summary(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = _write_registry(Path(tmp))
            output_dir = root / "docs" / "status"
            output_dir.mkdir(parents=True)
            (output_dir / "program-state-summary.json").write_text("{}", encoding="utf-8")
            (output_dir / "program-state-summary.md").write_text("stale", encoding="utf-8")

            errors = module.validate_generated_outputs(root)

            self.assertIn("generated JSON summary is out of date: docs/status/program-state-summary.json", errors)
            self.assertIn("generated Markdown summary is out of date: docs/status/program-state-summary.md", errors)


def _write_registry(root: Path) -> Path:
    data_dir = root / "docs" / "roadmap" / "data"
    data_dir.mkdir(parents=True)
    (data_dir / "program-state.yml").write_text(PROGRAM_STATE, encoding="utf-8")
    (data_dir / "roadmap-items.yml").write_text(ROADMAP_ITEMS, encoding="utf-8")
    return root


def _write_outputs(root: Path, program: dict, rows: list[dict[str, str]]) -> None:
    output_dir = root / "docs" / "status"
    output_dir.mkdir(parents=True)
    (output_dir / "program-state-summary.json").write_text(generator.render_json(program, rows), encoding="utf-8")
    (output_dir / "program-state-summary.md").write_text(generator.render_markdown(program, rows), encoding="utf-8")


if __name__ == "__main__":
    unittest.main()
