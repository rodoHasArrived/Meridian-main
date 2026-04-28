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

PROGRAM_STATE = """# Canonical\n<!-- program-state:begin -->
| Wave | Owner | Primary Owner | Backup Owner | Escalation SLA | Dependency Owners | Status | Target Date | Evidence Link |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| W1 | Data Operations + Provider Reliability | Data Confidence and Validation | Trading Workstation | 4 hours / 1 business day | Trading Workstation; Shared Platform Interop; Governance and Ledger | Done | 2026-04-17 | [evidence](#w1) |
<!-- program-state:end -->
"""


class ProgramStateSummaryGeneratorTests(unittest.TestCase):
    def test_parse_program_state_table_returns_wave_rows(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "PROGRAM_STATE.md"
            path.write_text(PROGRAM_STATE, encoding="utf-8")

            rows = module.parse_program_state_table(path)

            self.assertEqual(len(rows), 1)
            self.assertEqual(rows[0]["Backup Owner"], "Trading Workstation")


if __name__ == "__main__":
    unittest.main()
