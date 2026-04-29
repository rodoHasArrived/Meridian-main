#!/usr/bin/env python3
"""Regression tests for pilot readiness dashboard artifact rendering."""

from __future__ import annotations

import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path


DOCS_SCRIPT_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(DOCS_SCRIPT_DIR))


def load_module(name: str, filename: str):
    spec = importlib.util.spec_from_file_location(name, DOCS_SCRIPT_DIR / filename)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Unable to load {filename}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


pilot_dashboard = load_module(
    "generate_pilot_readiness_dashboard_under_test",
    "generate-pilot-readiness-dashboard.py",
)


class PilotReadinessDashboardTests(unittest.TestCase):
    def test_dashboard_loads_pilot_acceptance_stage_artifact(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            artifact_path = root / "artifacts" / "pilot-acceptance" / "latest" / "pilot-readiness.json"
            artifact_path.parent.mkdir(parents=True)
            artifact_path.write_text(json.dumps(build_artifact()), encoding="utf-8")

            payload = pilot_dashboard.build_dashboard(root)

            artifact = payload["pilot_acceptance_artifact"]
            self.assertEqual("loaded", artifact["status"])
            self.assertTrue(artifact["all_stages_ready"])
            self.assertEqual(8, artifact["ready_stage_count"])
            self.assertEqual(2, artifact["evidence_edge_count"])
            self.assertEqual("dataset/pilot/unit", artifact["key_evidence"]["dataset_evidence_id"])
            self.assertEqual("portfolio/unit", artifact["key_evidence"]["portfolio_evidence_id"])
            self.assertEqual("ledger/unit", artifact["key_evidence"]["ledger_evidence_id"])
            self.assertEqual("Governed report pack lineage", artifact["stage_gates"][-1]["label"])
            acceptance_check = next(
                check for check in payload["checks"] if check["id"] == "pilot-acceptance-artifact"
            )
            self.assertEqual("pass", acceptance_check["status"])

    def test_dashboard_marks_missing_pilot_acceptance_artifact_without_failing(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            payload = pilot_dashboard.build_dashboard(Path(tmp))

            artifact = payload["pilot_acceptance_artifact"]
            self.assertEqual("not_generated", artifact["status"])
            self.assertEqual([], artifact["stage_gates"])
            acceptance_check = next(
                check for check in payload["checks"] if check["id"] == "pilot-acceptance-artifact"
            )
            self.assertEqual("gap", acceptance_check["status"])
            self.assertIn(
                "artifacts/pilot-acceptance/latest/pilot-readiness.json",
                acceptance_check["missing_patterns"],
            )
            self.assertLess(payload["score_percent"], 100.0)

    def test_markdown_renders_stage_gate_section_from_artifact(self) -> None:
        payload = {
            "title": "Pilot Readiness Dashboard",
            "description": "Tracks pilot readiness.",
            "generated_at": "2026-04-29T00:00:00Z",
            "score_percent": 100.0,
            "summary": {
                "passed_checks": 1,
                "gap_checks": 0,
                "missing_source_count": 0,
                "missing_term_count": 0,
            },
            "checks": [],
            "pilot_acceptance_artifact": pilot_dashboard.load_pilot_acceptance_artifact_from_payload(
                build_artifact(),
                "artifacts/pilot-acceptance/latest/pilot-readiness.json",
            ),
        }

        rendered = pilot_dashboard.render_pilot_readiness_dashboard_markdown(payload)

        self.assertIn("## Pilot Acceptance Artifact", rendered)
        self.assertIn("Governed report pack lineage", rendered)
        self.assertIn("No stage blockers were recorded", rendered)


def build_artifact() -> dict:
    return {
        "generatedAtUtc": "2026-04-29T00:00:00Z",
        "providerEvidenceId": "provider-evidence/unit",
        "datasetEvidenceId": "dataset/pilot/unit",
        "researchRunId": "run-backtest-unit",
        "comparedRunIds": ["run-backtest-unit", "run-paper-unit"],
        "promotionAuditId": "promotion-audit-unit",
        "paperSessionId": "PAPER-UNIT",
        "replayVerificationAuditId": "replay-audit-unit",
        "reconciliationRunId": "reconciliation-unit",
        "continuityRunId": "run-paper-unit",
        "portfolioEvidenceId": "portfolio/unit",
        "ledgerEvidenceId": "ledger/unit",
        "reportPackId": "report-unit",
        "reportPackRelatedRunIds": ["run-backtest-unit", "run-paper-unit"],
        "readyStageCount": 8,
        "totalStageCount": 8,
        "allStagesReady": True,
        "stageGates": build_expected_stage_gates(camel_case=True),
        "evidenceGraph": [
            {
                "fromEvidenceId": "dataset/pilot/unit",
                "toEvidenceId": "run-backtest-unit",
                "relationship": "feeds-run",
            },
            {
                "fromEvidenceId": "run-paper-unit",
                "toEvidenceId": "report-unit",
                "relationship": "summarized-by",
            },
        ],
    }


def build_expected_stage_gates(camel_case: bool = False) -> list[dict]:
    evidence_key = "evidenceIds" if camel_case else "evidence_ids"
    return [
        {
            "stage": "TrustedData",
            "label": "Trusted provider and dataset evidence",
            "status": "Ready",
            evidence_key: ["provider-evidence/unit", "dataset/pilot/unit"],
            "blockers": [],
            "validation": "Unit artifact loaded.",
        },
        {
            "stage": "GovernedReportPack",
            "label": "Governed report pack lineage",
            "status": "Ready",
            evidence_key: ["report-unit", "run-paper-unit"],
            "blockers": [],
            "validation": "Report pack links to pilot evidence.",
        },
    ]


if __name__ == "__main__":
    unittest.main()
