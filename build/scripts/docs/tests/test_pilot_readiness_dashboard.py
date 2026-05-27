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
            self.assertEqual(0, artifact["evidence_self_edge_count"])
            self.assertEqual("dataset/pilot/unit", artifact["key_evidence"]["dataset_evidence_id"])
            self.assertEqual("portfolio/unit", artifact["key_evidence"]["portfolio_evidence_id"])
            self.assertEqual("ledger/unit", artifact["key_evidence"]["ledger_evidence_id"])
            self.assertEqual(2, artifact["ledger_artifact_count"])
            self.assertEqual(
                "/api/workstation/runs/run-paper-unit/ledger/journal",
                artifact["ledger_artifact_refs"][0]["route"],
            )
            self.assertEqual("Governed report pack lineage", artifact["stage_gates"][-1]["label"])
            self.assertEqual(["W4"], artifact["stage_gates"][-1]["wave_claims"])
            acceptance_check = next(
                check for check in payload["checks"] if check["id"] == "pilot-acceptance-artifact"
            )
            self.assertEqual("pass", acceptance_check["status"])

    def test_dashboard_rejects_inconsistent_stage_count_claims(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            artifact = build_artifact()
            artifact["readyStageCount"] = 8
            artifact["totalStageCount"] = 8
            artifact["stageGates"] = artifact["stageGates"][:-1]
            artifact_path = root / "artifacts" / "pilot-acceptance" / "latest" / "pilot-readiness.json"
            artifact_path.parent.mkdir(parents=True)
            artifact_path.write_text(json.dumps(artifact), encoding="utf-8")

            payload = pilot_dashboard.build_dashboard(root)

            acceptance_check = next(
                check for check in payload["checks"] if check["id"] == "pilot-acceptance-artifact"
            )
            artifact_summary = payload["pilot_acceptance_artifact"]
            self.assertFalse(artifact_summary["all_stages_ready"])
            self.assertIn("GovernedReportPack", artifact_summary["missing_stages"])
            self.assertIn(
                "consistent stage gates and evidence graph",
                acceptance_check["missing_terms"],
            )
            self.assertEqual("gap", acceptance_check["status"])

    def test_dashboard_flags_evidence_graph_self_edges(self) -> None:
        artifact = build_artifact()
        artifact["evidenceGraph"].append(
            {
                "fromEvidenceId": "run-paper-unit",
                "toEvidenceId": "run-paper-unit",
                "relationship": "summarized-by",
            }
        )

        loaded = pilot_dashboard.load_pilot_acceptance_artifact_from_payload(
            artifact,
            "artifacts/pilot-acceptance/latest/pilot-readiness.json",
        )

        self.assertEqual(1, loaded["evidence_self_edge_count"])
        self.assertFalse(loaded["all_stages_ready"])
        self.assertTrue(
            any("self-edge" in issue for issue in loaded["consistency_issues"])
        )

    def test_dashboard_flags_missing_route_only_ledger_artifacts(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            artifact = build_artifact()
            artifact["ledgerArtifactRefs"] = [
                {
                    "artifactId": "ledger:journal",
                    "kind": "ledger-journal",
                    "path": "workstation/evidence/ledger/journal.json",
                    "route": "/api/workstation/runs/run-paper-unit/ledger/journal",
                    "generatedAt": "2026-04-29T00:00:00Z",
                    "hash": None,
                    "retained": True,
                }
            ]
            artifact_path = root / "artifacts" / "pilot-acceptance" / "latest" / "pilot-readiness.json"
            artifact_path.parent.mkdir(parents=True)
            artifact_path.write_text(json.dumps(artifact), encoding="utf-8")

            payload = pilot_dashboard.build_dashboard(root)

            artifact_summary = payload["pilot_acceptance_artifact"]
            acceptance_check = next(
                check for check in payload["checks"] if check["id"] == "pilot-acceptance-artifact"
            )
            self.assertFalse(artifact_summary["all_stages_ready"])
            self.assertTrue(
                any("Ledger artifact ref" in issue for issue in artifact_summary["consistency_issues"])
            )
            self.assertIn("route-only ledger artifact refs", acceptance_check["missing_terms"])
            self.assertEqual("gap", acceptance_check["status"])

    def test_dashboard_rejects_missing_wave_claims(self) -> None:
        artifact = build_artifact()
        artifact["stageGates"][4]["waveClaims"] = []

        loaded = pilot_dashboard.load_pilot_acceptance_artifact_from_payload(
            artifact,
            "artifacts/pilot-acceptance/latest/pilot-readiness.json",
        )

        self.assertFalse(loaded["all_stages_ready"])
        self.assertTrue(
            any(
                "PaperSession W2-W4 claims are none; expected W2." == issue
                for issue in loaded["consistency_issues"]
            )
        )

    def test_dashboard_requires_blocker_when_claimed_stage_is_not_ready(self) -> None:
        artifact = build_artifact()
        artifact["stageGates"][7]["status"] = "ReviewRequired"
        artifact["stageGates"][7]["blockers"] = []

        loaded = pilot_dashboard.load_pilot_acceptance_artifact_from_payload(
            artifact,
            "artifacts/pilot-acceptance/latest/pilot-readiness.json",
        )

        self.assertFalse(loaded["all_stages_ready"])
        self.assertTrue(
            any(
                "GovernedReportPack carries W2-W4 claims but records no blocker" in issue
                for issue in loaded["consistency_issues"]
            )
        )

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
        self.assertIn("| Governed report pack lineage | W4 | Ready |", rendered)
        self.assertIn("### Ledger Artifact Refs", rendered)
        self.assertIn("ledger-journal", rendered)
        self.assertIn("### Evidence Graph", rendered)
        self.assertIn("feeds-run", rendered)
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
        "ledgerArtifactRefs": [
            {
                "artifactId": "ledger:journal",
                "kind": "ledger-journal",
                "path": None,
                "route": "/api/workstation/runs/run-paper-unit/ledger/journal",
                "generatedAt": "2026-04-29T00:00:00Z",
                "hash": None,
                "retained": True,
            },
            {
                "artifactId": "ledger:trial-balance",
                "kind": "ledger-trial-balance",
                "path": None,
                "route": "/api/workstation/runs/run-paper-unit/ledger/trial-balance",
                "generatedAt": "2026-04-29T00:00:00Z",
                "hash": None,
                "retained": True,
            },
        ],
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
    wave_claims_key = "waveClaims" if camel_case else "wave_claims"
    return [
        {
            "stage": "TrustedData",
            "label": "Trusted provider and dataset evidence",
            "status": "Ready",
            evidence_key: ["provider-evidence/unit", "dataset/pilot/unit"],
            "blockers": [],
            wave_claims_key: ["W2", "W3", "W4"],
            "validation": "Unit artifact loaded.",
        },
        {
            "stage": "ResearchRun",
            "label": "Research run evidence retained",
            "status": "Ready",
            evidence_key: ["run-backtest-unit", "dataset/pilot/unit"],
            "blockers": [],
            wave_claims_key: ["W3"],
            "validation": "Research run loaded.",
        },
        {
            "stage": "RunComparison",
            "label": "Baseline and candidate run comparison",
            "status": "Ready",
            evidence_key: ["run-backtest-unit", "run-paper-unit"],
            "blockers": [],
            wave_claims_key: ["W3"],
            "validation": "Run comparison loaded.",
        },
        {
            "stage": "PaperPromotion",
            "label": "Paper promotion approval audit",
            "status": "Ready",
            evidence_key: ["promotion-audit-unit"],
            "blockers": [],
            wave_claims_key: ["W2", "W3"],
            "validation": "Promotion audit loaded.",
        },
        {
            "stage": "PaperSession",
            "label": "Paper session replay verification",
            "status": "Ready",
            evidence_key: ["PAPER-UNIT", "replay-audit-unit"],
            "blockers": [],
            wave_claims_key: ["W2"],
            "validation": "Replay audit loaded.",
        },
        {
            "stage": "PortfolioLedgerReview",
            "label": "Portfolio and ledger continuity",
            "status": "Ready",
            evidence_key: ["portfolio/unit", "ledger/unit"],
            "blockers": [],
            wave_claims_key: ["W3", "W4"],
            "validation": "Portfolio and ledger evidence loaded.",
        },
        {
            "stage": "Reconciliation",
            "label": "Reconciliation run casework",
            "status": "Ready",
            evidence_key: ["reconciliation-unit"],
            "blockers": [],
            wave_claims_key: ["W3", "W4"],
            "validation": "Reconciliation run loaded.",
        },
        {
            "stage": "GovernedReportPack",
            "label": "Governed report pack lineage",
            "status": "Ready",
            evidence_key: ["report-unit", "run-paper-unit"],
            "blockers": [],
            wave_claims_key: ["W4"],
            "validation": "Report pack links to pilot evidence.",
        },
    ]


if __name__ == "__main__":
    unittest.main()
