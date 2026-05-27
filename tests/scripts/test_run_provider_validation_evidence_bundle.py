from __future__ import annotations

import json
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
SCRIPT_PATH = REPO_ROOT / "scripts" / "dev" / "run-provider-validation-evidence-bundle.ps1"


class RunProviderValidationEvidenceBundleTests(unittest.TestCase):
    def test_skip_wave1_generates_traceable_bundle_with_expected_artifact_paths(self) -> None:
        pwsh = shutil.which("pwsh")
        if pwsh is None:
            self.skipTest("pwsh is not available")

        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            output_root = root / "bundle-output"
            date_stamp = "2026-05-01"
            bundle_root = output_root / date_stamp
            bundle_root.mkdir(parents=True, exist_ok=True)

            summary_path = bundle_root / "wave1-validation-summary.json"
            packet_path = bundle_root / "dk1-pilot-parity-packet.json"
            signoff_path = bundle_root / "dk1-operator-signoff.json"

            summary_path.write_text(json.dumps({"generatedAtUtc": "2026-05-01T10:00:00Z", "result": "passed"}), encoding="utf-8")
            packet_path.write_text(json.dumps({"generatedAtUtc": "2026-05-01T10:01:00Z", "status": "ready-for-operator-review"}), encoding="utf-8")
            signoff_path.write_text(json.dumps({
                "requiredOwners": ["Data Operations", "Provider Reliability", "Trading"],
                "approvals": [
                    {"owner": "Data Operations", "signedBy": "data.ops", "signedAtUtc": "2026-05-01T10:02:00Z", "decision": "approved", "rationale": "ok"},
                    {"owner": "Provider Reliability", "signedBy": "provider.reliability", "signedAtUtc": "2026-05-01T10:03:00Z", "decision": "approved", "rationale": "ok"},
                    {"owner": "Trading", "signedBy": "trading.owner", "signedAtUtc": "2026-05-01T10:04:00Z", "decision": "approved", "rationale": "ok"},
                ],
                "packetReview": {
                    "path": str(packet_path),
                    "generatedAtUtc": "2026-05-01T10:01:00Z",
                    "status": "ready-for-operator-review",
                    "validForOperatorReview": True,
                    "readySampleCount": 4,
                    "validatedEvidenceDocumentCount": 4,
                },
            }), encoding="utf-8")

            subprocess.run(
                [
                    pwsh,
                    "-NoProfile",
                    "-File",
                    str(SCRIPT_PATH),
                    "-DateStamp",
                    date_stamp,
                    "-OutputRoot",
                    str(output_root),
                    "-SkipWave1",
                ],
                cwd=REPO_ROOT,
                check=True,
                capture_output=True,
                text=True,
            )

            bundle = json.loads((bundle_root / "provider-validation-evidence-bundle.json").read_text(encoding="utf-8"))
            self.assertEqual("provider-validation-bundle/v1", bundle["schemaVersion"])
            self.assertEqual("passed", bundle["gateOutcome"]["wave1Result"])
            self.assertEqual("ready-for-operator-review", bundle["gateOutcome"]["dk1Status"])
            self.assertTrue(bundle["gateOutcome"]["operatorSignoffValidForDk1Exit"])
            self.assertEqual("not-run", bundle["promotionPosture"]["status"])

            for key in ["summaryPath", "parityPacketPath", "signoffPath"]:
                artifact_path = Path(bundle[key])
                self.assertTrue(artifact_path.exists(), f"Expected traceable artifact path for {key}: {artifact_path}")


if __name__ == "__main__":
    unittest.main()
