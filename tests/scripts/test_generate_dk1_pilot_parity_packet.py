from __future__ import annotations

import json
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
SCRIPT_PATH = REPO_ROOT / "scripts" / "dev" / "generate-dk1-pilot-parity-packet.ps1"


class GenerateDk1PilotParityPacketTests(unittest.TestCase):
    def test_operator_signoff_path_marks_all_required_owners_signed(self) -> None:
        pwsh = shutil.which("pwsh")
        if pwsh is None:
            self.skipTest("pwsh is not available")

        with tempfile.TemporaryDirectory() as temp_dir:
            temp_path = Path(temp_dir)
            summary_path = temp_path / "wave1-validation-summary.json"
            signoff_path = temp_path / "dk1-operator-signoff.json"

            summary_path.write_text(json.dumps(_build_passing_summary()), encoding="utf-8")
            packet_review = _write_reviewed_packet(temp_path, summary_path)
            signoff_path.write_text(json.dumps(_build_signed_signoff(packet_review)), encoding="utf-8")

            subprocess.run(
                [
                    pwsh,
                    "-NoProfile",
                    "-File",
                    str(SCRIPT_PATH),
                    "-SummaryJsonPath",
                    str(summary_path),
                    "-OperatorSignoffPath",
                    str(signoff_path),
                ],
                cwd=REPO_ROOT,
                check=True,
                capture_output=True,
                text=True,
            )

            packet = json.loads((temp_path / "dk1-pilot-parity-packet.json").read_text(encoding="utf-8"))

            self.assertEqual("ready-for-operator-review", packet["status"])
            self.assertEqual("signed", packet["operatorSignoff"]["status"])
            self.assertTrue(packet["operatorSignoff"]["validForDk1Exit"])
            self.assertEqual("valid", packet["operatorSignoff"]["packetBindingStatus"])
            self.assertEqual(packet["operatorSignoff"]["packetReview"]["generatedAtUtc"], packet["generatedAtUtc"])
            search_dependency_review = packet["searchDependencyReview"]
            self.assertEqual("represented", search_dependency_review["status"])
            self.assertEqual(2, search_dependency_review["requiredCount"])
            self.assertEqual(2, search_dependency_review["representedCount"])
            self.assertEqual(
                {"OpenFIGI", "EDGAR"},
                {row["provider"] for row in search_dependency_review["dependencies"]},
            )
            self.assertEqual(
                2,
                packet["operatorSignoff"]["packetReview"]["representedSearchDependencyCount"],
            )
            self.assertEqual([], packet["operatorSignoff"]["missingOwners"])
            self.assertEqual(
                ["Data", "Provider Reliability", "Trading"],
                packet["operatorSignoff"]["signedOwners"],
            )
            self.assertEqual(3, len(packet["operatorSignoff"]["approvals"]))


    def test_single_run_writes_exactly_one_packet_with_restart_continuity_evidence(self) -> None:
        pwsh = shutil.which("pwsh")
        if pwsh is None:
            self.skipTest("pwsh is not available")

        with tempfile.TemporaryDirectory() as temp_dir:
            temp_path = Path(temp_dir)
            summary_path = temp_path / "wave1-validation-summary.json"
            signoff_path = temp_path / "dk1-operator-signoff.json"

            summary_path.write_text(json.dumps(_build_passing_summary()), encoding="utf-8")
            packet_review = _write_reviewed_packet(temp_path, summary_path)
            signoff_path.write_text(json.dumps(_build_signed_signoff(packet_review)), encoding="utf-8")

            subprocess.run(
                [
                    pwsh,
                    "-NoProfile",
                    "-File",
                    str(SCRIPT_PATH),
                    "-SummaryJsonPath",
                    str(summary_path),
                    "-OperatorSignoffPath",
                    str(signoff_path),
                ],
                cwd=REPO_ROOT,
                check=True,
                capture_output=True,
                text=True,
            )

            packets = [
                path
                for path in sorted(temp_path.glob("dk1-pilot-parity-packet*.json"))
                if ".checkpoint" not in path.name
            ]
            self.assertEqual(
                1,
                len(packets),
                f"Expected exactly one packet artifact for a single run, found {len(packets)}: {packets}",
            )

            packet = json.loads(packets[0].read_text(encoding="utf-8"))
            operator_signoff = packet.get("operatorSignoff", {})
            packet_review_node = operator_signoff.get("packetReview", {})

            self.assertTrue(str(packet.get("generatedAtUtc", "")).strip())
            self.assertTrue(str(packet_review_node.get("generatedAtUtc", "")).strip())
            self.assertTrue(str(packet_review_node.get("sourceSummary", "")).strip())
            self.assertTrue(str(packet_review_node.get("path", "")).strip())
            self.assertTrue(str(packet_review_node.get("status", "")).strip())
            self.assertEqual("represented", packet_review_node.get("searchDependencyReviewStatus"))

            self.assertEqual(str(summary_path), packet_review_node.get("sourceSummary"))
            self.assertEqual(str(packets[0]), packet_review_node.get("path"))

            evidence_documents = packet.get("evidenceDocuments", [])
            self.assertGreater(len(evidence_documents), 0)
            self.assertTrue(
                all(str(document.get("path", "")).strip() for document in evidence_documents),
                "Expected evidence document references for restart continuity review.",
            )

            sample_review = packet.get("sampleReview", {})
            samples = sample_review.get("samples", [])
            self.assertGreater(len(samples), 0)
            self.assertTrue(
                all(str(sample.get("id", "")).strip() for sample in samples),
                "Expected sample identifiers to remain linked for replay continuity verification.",
            )

    def test_operator_signoff_path_rejects_stale_packet_binding(self) -> None:
        pwsh = shutil.which("pwsh")
        if pwsh is None:
            self.skipTest("pwsh is not available")

        with tempfile.TemporaryDirectory() as temp_dir:
            temp_path = Path(temp_dir)
            summary_path = temp_path / "wave1-validation-summary.json"
            signoff_path = temp_path / "dk1-operator-signoff.json"

            summary_path.write_text(json.dumps(_build_passing_summary()), encoding="utf-8")
            packet_review = _write_reviewed_packet(temp_path, summary_path)
            stale_review = dict(packet_review)
            stale_review["path"] = str(temp_path / "copied-from-other-run" / "dk1-pilot-parity-packet.json")
            stale_review["generatedAtUtc"] = "2026-04-25T20:28:38Z"
            signoff_path.write_text(json.dumps(_build_signed_signoff(stale_review)), encoding="utf-8")

            result = subprocess.run(
                [
                    pwsh,
                    "-NoProfile",
                    "-File",
                    str(SCRIPT_PATH),
                    "-SummaryJsonPath",
                    str(summary_path),
                    "-OperatorSignoffPath",
                    str(signoff_path),
                ],
                cwd=REPO_ROOT,
                check=False,
                capture_output=True,
                text=True,
            )

            self.assertNotEqual(0, result.returncode)
            self.assertIn("packet binding requirements", result.stderr + result.stdout)

            packet = json.loads((temp_path / "dk1-pilot-parity-packet.json").read_text(encoding="utf-8"))
            self.assertEqual("invalid", packet["operatorSignoff"]["status"])
            self.assertFalse(packet["operatorSignoff"]["validForDk1Exit"])
            self.assertEqual("invalid", packet["operatorSignoff"]["packetBindingStatus"])
            self.assertIn("packetPath", packet["operatorSignoff"]["packetBindingMissingRequirements"])
            self.assertIn("packetGeneratedAtUtc", packet["operatorSignoff"]["packetBindingMissingRequirements"])

    def test_operator_signoff_path_rejects_stale_search_dependency_binding(self) -> None:
        pwsh = shutil.which("pwsh")
        if pwsh is None:
            self.skipTest("pwsh is not available")

        with tempfile.TemporaryDirectory() as temp_dir:
            temp_path = Path(temp_dir)
            summary_path = temp_path / "wave1-validation-summary.json"
            signoff_path = temp_path / "dk1-operator-signoff.json"

            summary_path.write_text(json.dumps(_build_passing_summary()), encoding="utf-8")
            packet_review = _write_reviewed_packet(temp_path, summary_path)
            stale_review = dict(packet_review)
            stale_review["representedSearchDependencyCount"] = 1
            signoff_path.write_text(json.dumps(_build_signed_signoff(stale_review)), encoding="utf-8")

            result = subprocess.run(
                [
                    pwsh,
                    "-NoProfile",
                    "-File",
                    str(SCRIPT_PATH),
                    "-SummaryJsonPath",
                    str(summary_path),
                    "-OperatorSignoffPath",
                    str(signoff_path),
                ],
                cwd=REPO_ROOT,
                check=False,
                capture_output=True,
                text=True,
            )

            self.assertNotEqual(0, result.returncode)
            packet = json.loads((temp_path / "dk1-pilot-parity-packet.json").read_text(encoding="utf-8"))
            self.assertEqual("invalid", packet["operatorSignoff"]["packetBindingStatus"])
            self.assertIn(
                "packetRepresentedSearchDependencyCount",
                packet["operatorSignoff"]["packetBindingMissingRequirements"],
            )


def _build_passing_summary() -> dict[str, object]:
    return {
        "dateStamp": "unit-ready",
        "result": "passed",
        "steps": [
            {"name": "Alpaca core provider confidence", "status": "passed"},
            {"name": "Robinhood supported surface", "status": "passed"},
            {"name": "Yahoo historical-only core provider", "status": "passed"},
        ],
        "pilotReplaySampleSet": [
            {
                "id": "DK1-ALPACA-QUOTE-GOLDEN",
                "provider": "Alpaca",
                "automationStep": "Alpaca core provider confidence",
                "lane": "parity",
                "sampleWindow": "2026-03-19T14:30:00Z",
                "sampleUniverse": ["AAPL"],
                "evidenceAnchors": [
                    "tests/Meridian.Tests/TestData/Golden/alpaca-quote-pipeline.json",
                    "AlpacaQuotePipelineGoldenTests",
                ],
                "acceptanceCheck": "Golden quote pipeline fixture matches.",
            },
            {
                "id": "DK1-ALPACA-PARSER-EDGE-CASES",
                "provider": "Alpaca",
                "automationStep": "Alpaca core provider confidence",
                "lane": "parity",
                "sampleWindow": "2024-06-15",
                "sampleUniverse": ["AAPL", "MSFT", "QQQ", "SPY"],
                "evidenceAnchors": [
                    "AlpacaMessageParsingTests",
                    "AlpacaQuoteRoutingTests",
                    "AlpacaCredentialAndReconnectTests",
                ],
                "acceptanceCheck": "Parser and routing edge cases pass.",
            },
            {
                "id": "DK1-ROBINHOOD-SUPPORTED-SURFACE",
                "provider": "Robinhood",
                "automationStep": "Robinhood supported surface",
                "lane": "parity",
                "sampleWindow": "2026-04-09",
                "sampleUniverse": ["AAPL", "MSFT"],
                "evidenceAnchors": [
                    "RobinhoodMarketDataClientTests",
                    "RobinhoodBrokerageGatewayTests",
                    "artifacts/provider-validation/robinhood/2026-04-09/manifest.json",
                ],
                "acceptanceCheck": "Supported offline and bounded runtime surfaces pass.",
            },
            {
                "id": "DK1-YAHOO-HISTORICAL-FALLBACK",
                "provider": "Yahoo",
                "automationStep": "Yahoo historical-only core provider",
                "lane": "parity",
                "sampleWindow": "2026-04-09",
                "sampleUniverse": ["AAPL", "SPY"],
                "evidenceAnchors": [
                    "YahooFinanceHistoricalDataProviderTests",
                    "YahooFinanceIntradayContractTests",
                ],
                "acceptanceCheck": "Historical and fallback fixtures pass.",
            },
        ],
    }


def _build_signed_signoff(packet_review: dict[str, object] | None = None) -> dict[str, object]:
    signoff: dict[str, object] = {
        "approvals": [
            {
                "owner": "Data",
                "signedBy": "data.ops",
                "signedAtUtc": "2026-04-26T15:58:00Z",
                "decision": "approved",
                "rationale": "Provider packet reviewed.",
            },
            {
                "owner": "Provider Reliability",
                "signedBy": "provider.reliability",
                "signedAtUtc": "2026-04-26T16:00:00Z",
                "decision": "approved",
                "rationale": "Threshold and evidence checks accepted.",
            },
            {
                "owner": "Trading",
                "signedBy": "trading.owner",
                "signedAtUtc": "2026-04-26T16:02:00Z",
                "decision": "approved",
                "rationale": "Cockpit readiness gate accepted.",
            },
        ]
    }
    if packet_review is not None:
        signoff["packetReview"] = packet_review

    return signoff


def _write_reviewed_packet(
    temp_path: Path,
    summary_path: Path,
    generated_at: str = "2026-04-26T17:00:00Z",
) -> dict[str, object]:
    packet_path = temp_path / "dk1-pilot-parity-packet.json"
    packet = _build_ready_packet(summary_path, generated_at)
    packet_path.write_text(json.dumps(packet), encoding="utf-8")
    return _build_packet_review(packet_path, packet)


def _build_ready_packet(summary_path: Path, generated_at: str) -> dict[str, object]:
    samples = [
        "DK1-ALPACA-QUOTE-GOLDEN",
        "DK1-ALPACA-PARSER-EDGE-CASES",
        "DK1-ROBINHOOD-SUPPORTED-SURFACE",
        "DK1-YAHOO-HISTORICAL-FALLBACK",
    ]
    docs = [
        ("DK1 pilot parity runbook", "parity", "docs/status/evidence/dk1-pilot-parity-runbook.md"),
        ("DK1 trust rationale mapping", "explainability", "docs/status/evidence/dk1-trust-rationale-mapping.md"),
        ("DK1 baseline trust thresholds", "calibration", "docs/status/evidence/dk1-baseline-trust-thresholds.md"),
        ("Provider validation matrix", "parity", "docs/reference/provider-validation-matrix.md"),
    ]
    search_dependencies = _build_search_dependencies()
    return {
        "generatedAtUtc": generated_at,
        "sourceSummary": str(summary_path),
        "sourceResult": "passed",
        "status": "ready-for-operator-review",
        "sampleReview": {
            "requiredCount": 4,
            "samples": [
                {
                    "id": sample_id,
                    "status": "ready",
                    "missingRequirements": [],
                }
                for sample_id in samples
            ],
        },
        "searchDependencyReview": {
            "requiredCount": len(search_dependencies),
            "representedCount": len(search_dependencies),
            "status": "represented",
            "dependencies": search_dependencies,
        },
        "trustRationaleContract": {
            "status": "validated",
            "missingRequirements": [],
        },
        "baselineThresholdContract": {
            "status": "validated",
            "missingRequirements": [],
        },
        "evidenceDocuments": [
            {
                "name": name,
                "gate": gate,
                "status": "validated",
                "path": path,
                "missingRequirements": [],
            }
            for name, gate, path in docs
        ],
        "blockers": [],
    }


def _build_packet_review(packet_path: Path, packet: dict[str, object]) -> dict[str, object]:
    sample_review = packet["sampleReview"]
    assert isinstance(sample_review, dict)
    samples = sample_review["samples"]
    assert isinstance(samples, list)
    documents = packet["evidenceDocuments"]
    assert isinstance(documents, list)
    search_review = packet["searchDependencyReview"]
    assert isinstance(search_review, dict)
    search_dependencies = search_review["dependencies"]
    assert isinstance(search_dependencies, list)
    trust_contract = packet["trustRationaleContract"]
    baseline_contract = packet["baselineThresholdContract"]
    assert isinstance(trust_contract, dict)
    assert isinstance(baseline_contract, dict)

    return {
        "path": str(packet_path),
        "status": packet["status"],
        "generatedAtUtc": packet["generatedAtUtc"],
        "sourceSummary": packet["sourceSummary"],
        "sourceResult": packet["sourceResult"],
        "requiredSampleCount": sample_review["requiredCount"],
        "readySampleCount": sum(1 for sample in samples if sample["status"] == "ready"),
        "evidenceDocumentCount": len(documents),
        "validatedEvidenceDocumentCount": sum(
            1 for document in documents if document["status"] == "validated"
        ),
        "trustRationaleContractStatus": trust_contract["status"],
        "baselineThresholdContractStatus": baseline_contract["status"],
        "requiredSearchDependencyCount": search_review["requiredCount"],
        "representedSearchDependencyCount": sum(
            1 for dependency in search_dependencies if dependency["status"] == "represented"
        ),
        "searchDependencyReviewStatus": search_review["status"],
        "validForOperatorReview": True,
    }


def _build_search_dependencies() -> list[dict[str, object]]:
    return [
        {
            "provider": "OpenFIGI",
            "dependency": "Identifier mapping API",
            "risk": "Quota, uptime, and mapping ambiguity can degrade symbol-search confidence.",
            "governanceAction": "Represent in DK1 packet searchDependencyReview.",
            "evidenceAnchors": ["OpenFigiClientTests", "OpenFigiClientAmbiguityTests"],
            "status": "represented",
            "missingRequirements": [],
        },
        {
            "provider": "EDGAR",
            "dependency": "SEC company ticker and reference-data endpoints",
            "risk": "Public endpoint availability can degrade reference-data lookup quality.",
            "governanceAction": "Represent in DK1 packet searchDependencyReview.",
            "evidenceAnchors": ["EdgarSymbolSearchProviderTests", "EdgarReferenceDataProviderTests"],
            "status": "represented",
            "missingRequirements": [],
        },
    ]


if __name__ == "__main__":
    unittest.main()
