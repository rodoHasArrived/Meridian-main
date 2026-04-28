from __future__ import annotations

import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path


SCRIPT_PATH = Path(__file__).resolve().parents[2] / "scripts" / "compare_run_contract.py"
SPEC = importlib.util.spec_from_file_location("compare_run_contract", SCRIPT_PATH)
assert SPEC and SPEC.loader
comparator = importlib.util.module_from_spec(SPEC)
sys.modules["compare_run_contract"] = comparator
SPEC.loader.exec_module(comparator)


class CompareRunContractTests(unittest.TestCase):
    def _workflow(self, *, gate_status: str = "pass", metric_value: float = 98.0) -> dict:
        return {
            "workflowId": "wave1-validation",
            "displayName": "Wave 1 validation",
            "gateStatus": gate_status,
            "invariantCheckpoints": [
                {"id": "fixtures-loaded", "description": "Fixtures loaded", "status": "pass"},
                {"id": "provider-matrix", "description": "Provider matrix complete", "status": "pass"},
            ],
            "keyMetrics": [
                {
                    "id": "provider-pass-rate",
                    "name": "Provider pass rate",
                    "value": metric_value,
                    "unit": "percent",
                    "direction": "higher_is_better",
                }
            ],
            "acceptedToleranceWindows": [
                {
                    "metricId": "provider-pass-rate",
                    "warningDegradationPercent": 2.0,
                    "failureDegradationPercent": 5.0,
                }
            ],
            "requiredArtifactPaths": [
                {
                    "id": "validation-summary",
                    "path": "artifacts/provider-validation/_automation/2026-04-28/wave1-validation-summary.json",
                    "required": True,
                }
            ],
        }

    def test_compare_contracts_flags_missing_checkpoint(self) -> None:
        baseline = {"workflows": [self._workflow()]}
        current_workflow = self._workflow()
        current_workflow["invariantCheckpoints"] = current_workflow["invariantCheckpoints"][:1]
        current = {"workflows": [current_workflow]}

        regressions = comparator.compare_contracts(baseline, current)

        self.assertTrue(any(item.category == "missing_step" for item in regressions))

    def test_compare_contracts_flags_metric_degradation_failure(self) -> None:
        baseline = {"workflows": [self._workflow(metric_value=100.0)]}
        current = {"workflows": [self._workflow(metric_value=90.0)]}

        regressions = comparator.compare_contracts(baseline, current)

        self.assertTrue(any(item.category == "degraded_metric" and item.severity == "failure" for item in regressions))

    def test_compare_contracts_flags_gate_status_regression(self) -> None:
        baseline = {"workflows": [self._workflow(gate_status="pass")]}
        current = {"workflows": [self._workflow(gate_status="warning")]}

        regressions = comparator.compare_contracts(baseline, current)

        self.assertTrue(any(item.category == "gate_status" for item in regressions))

    def test_main_writes_artifacts_and_markdown_sections(self) -> None:
        baseline_contract = {
            "contractVersion": "v1.0",
            "generatedAtUtc": "2026-04-28T00:00:00Z",
            "workflows": [self._workflow(metric_value=100.0)],
        }
        current_contract = {
            "contractVersion": "v1.0",
            "generatedAtUtc": "2026-04-28T01:00:00Z",
            "workflows": [self._workflow(metric_value=99.5)],
        }

        with tempfile.TemporaryDirectory() as temp_dir:
            temp_path = Path(temp_dir)
            baseline_path = temp_path / "baseline.json"
            current_path = temp_path / "current.json"
            output_dir = temp_path / "artifacts" / "run-contract"

            baseline_path.write_text(json.dumps(baseline_contract), encoding="utf-8")
            current_path.write_text(json.dumps(current_contract), encoding="utf-8")

            exit_code = comparator.main_args(
                [
                    "--baseline",
                    str(baseline_path),
                    "--current",
                    str(current_path),
                    "--output-dir",
                    str(output_dir),
                ]
            )

            self.assertEqual(0, exit_code)

            json_report = json.loads((output_dir / "run-contract-comparator.json").read_text(encoding="utf-8"))
            markdown_report = (output_dir / "run-contract-comparator.md").read_text(encoding="utf-8")

            self.assertEqual("passed", json_report["status"])
            self.assertIn("## Operator Sign-off Packet Summary", markdown_report)
            self.assertIn("## Roadmap Cadence Review Summary", markdown_report)


if __name__ == "__main__":
    unittest.main()
