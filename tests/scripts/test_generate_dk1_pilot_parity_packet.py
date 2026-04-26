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
            signoff_path.write_text(json.dumps(_build_signed_signoff()), encoding="utf-8")

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
            self.assertEqual([], packet["operatorSignoff"]["missingOwners"])
            self.assertEqual(
                ["Data Operations", "Provider Reliability", "Trading"],
                packet["operatorSignoff"]["signedOwners"],
            )
            self.assertEqual(3, len(packet["operatorSignoff"]["approvals"]))


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


def _build_signed_signoff() -> dict[str, object]:
    return {
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


if __name__ == "__main__":
    unittest.main()
