from __future__ import annotations

import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPT_PATH = Path(__file__).resolve().parents[2] / "scripts" / "check_program_state_consistency.py"
SPEC = importlib.util.spec_from_file_location("check_program_state_consistency", SCRIPT_PATH)
assert SPEC and SPEC.loader
module = importlib.util.module_from_spec(SPEC)
sys.modules["check_program_state_consistency"] = module
SPEC.loader.exec_module(module)

VALID_BLOCK = """<!-- program-state:begin -->
| Wave | Owner | Primary Owner | Backup Owner | Escalation SLA | Dependency Owners | Status | Target Date | Evidence Link |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| W1 | Data Operations + Provider Reliability | Data Confidence and Validation | Trading Workstation | 4 hours / 1 business day | Trading Workstation; Shared Platform Interop; Governance and Ledger | Done | 2026-04-17 | [evidence](#w1) |
<!-- program-state:end -->
"""


class ProgramStateConsistencyTests(unittest.TestCase):
    def test_parse_file_accepts_valid_ownership_metadata(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "PROGRAM_STATE.md"
            path.write_text(VALID_BLOCK, encoding="utf-8")

            parsed = module.parse_file(path)

            self.assertIn("W1", parsed)
            self.assertEqual(parsed["W1"]["Primary Owner"], "Data Confidence and Validation")

    def test_parse_file_rejects_invalid_escalation_sla(self) -> None:
        invalid = VALID_BLOCK.replace("4 hours / 1 business day", "urgent")
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "PROGRAM_STATE.md"
            path.write_text(invalid, encoding="utf-8")

            with self.assertRaises(ValueError):
                module.parse_file(path)


if __name__ == "__main__":
    unittest.main()
