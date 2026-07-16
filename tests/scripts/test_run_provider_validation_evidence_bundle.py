from __future__ import annotations

import json
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

from tests.scripts.test_generate_dk1_pilot_parity_packet import (
    _build_packet_review,
    _build_passing_summary,
    _build_ready_packet,
    _build_signed_signoff,
)

REPO_ROOT = Path(__file__).resolve().parents[2]
SCRIPT_PATH = REPO_ROOT / "scripts" / "dev" / "run-provider-validation-evidence-bundle.ps1"
WORKFLOW_PATH = REPO_ROOT / ".github" / "workflows" / "provider-validation.yml"


class RunProviderValidationEvidenceBundleTests(unittest.TestCase):
    def test_workflow_uses_named_splatting_for_pending_signoff_mode(self) -> None:
        workflow = WORKFLOW_PATH.read_text(encoding="utf-8")

        self.assertIn("$bundleArgs = @{", workflow)
        self.assertIn("PrepareOperatorSignoff = $true", workflow)
        self.assertIn("$bundleArgs.SkipWave1 = $true", workflow)
        self.assertNotIn("$bundleArgs += '-PrepareOperatorSignoff'", workflow)

    def test_prepare_operator_signoff_succeeds_without_claiming_dk1_exit(self) -> None:
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
            summary = _build_passing_summary()
            summary["generatedAtUtc"] = "2026-05-01T10:00:00Z"
            summary_path.write_text(json.dumps(summary), encoding="utf-8")

            completed = subprocess.run(
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
                    "-PrepareOperatorSignoff",
                ],
                cwd=REPO_ROOT,
                check=True,
                capture_output=True,
                text=True,
            )

            self.assertIn("ready for operator review", completed.stdout)
            signoff = json.loads((bundle_root / "dk1-operator-signoff.json").read_text(encoding="utf-8-sig"))
            self.assertTrue(signoff["packetReview"]["validForOperatorReview"])
            self.assertTrue(all(approval["decision"] == "pending" for approval in signoff["approvals"]))
            self.assertFalse((bundle_root / "provider-validation-evidence-bundle.json").exists())

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

            summary = _build_passing_summary()
            summary["generatedAtUtc"] = "2026-05-01T10:00:00Z"
            summary_path.write_text(json.dumps(summary), encoding="utf-8")
            packet = _build_ready_packet(summary_path, "2026-05-01T10:01:00Z")
            packet_path.write_text(json.dumps(packet), encoding="utf-8")
            packet_review = _build_packet_review(packet_path, packet)
            signoff_path.write_text(json.dumps(_build_signed_signoff(packet_review)), encoding="utf-8")

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
