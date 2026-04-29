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
            self.assertEqual([], packet["operatorSignoff"]["missingOwners"])
            self.assertEqual(
                ["Data Operations", "Provider Reliability", "Trading"],
                packet["operatorSignoff"]["signedOwners"],
            )
            self.assertEqual(3, len(packet["operatorSignoff"]["approvals"]))

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
                "owner": "Data Operations",
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
        ("DK1 pilot parity runbook", "parity", "docs/status/dk1-pilot-parity-runbook.md"),
        ("DK1 trust rationale mapping", "explainability", "docs/status/dk1-trust-rationale-mapping.md"),
        ("DK1 baseline trust thresholds", "calibration", "docs/status/dk1-baseline-trust-thresholds.md"),
        ("Provider validation matrix", "parity", "docs/status/provider-validation-matrix.md"),
    ]
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
        "validForOperatorReview": True,
    }


if __name__ == "__main__":
    unittest.main()
