from __future__ import annotations

import json
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
SCRIPT_PATH = REPO_ROOT / "scripts" / "dev" / "prepare-dk1-operator-signoff.ps1"


class PrepareDk1OperatorSignoffTests(unittest.TestCase):
    def test_writes_required_owner_template(self) -> None:
        pwsh = shutil.which("pwsh")
        if pwsh is None:
            self.skipTest("pwsh is not available")

        with tempfile.TemporaryDirectory() as temp_dir:
            output_path = Path(temp_dir) / "dk1-operator-signoff.json"

            subprocess.run(
                [
                    pwsh,
                    "-NoProfile",
                    "-File",
                    str(SCRIPT_PATH),
                    "-OutputPath",
                    str(output_path),
                    "-Json",
                ],
                cwd=REPO_ROOT,
                check=True,
                capture_output=True,
                text=True,
            )

            payload = json.loads(output_path.read_text(encoding="utf-8-sig"))

            self.assertEqual(
                ["Data Operations", "Provider Reliability", "Trading"],
                payload["requiredOwners"],
            )
            self.assertEqual(
                ["Data Operations", "Provider Reliability", "Trading"],
                [row["owner"] for row in payload["approvals"]],
            )
            self.assertEqual(
                ["pending", "pending", "pending"],
                [row["decision"] for row in payload["approvals"]],
            )

    def test_validate_accepts_signed_owner_file(self) -> None:
        pwsh = shutil.which("pwsh")
        if pwsh is None:
            self.skipTest("pwsh is not available")

        with tempfile.TemporaryDirectory() as temp_dir:
            signoff_path = Path(temp_dir) / "dk1-operator-signoff.json"
            signoff_path.write_text(json.dumps(_build_signed_signoff()), encoding="utf-8")

            result = subprocess.run(
                [
                    pwsh,
                    "-NoProfile",
                    "-File",
                    str(SCRIPT_PATH),
                    "-OutputPath",
                    str(signoff_path),
                    "-Validate",
                    "-Json",
                ],
                cwd=REPO_ROOT,
                check=True,
                capture_output=True,
                text=True,
            )

            review = json.loads(result.stdout)

            self.assertEqual("signed", review["status"])
            self.assertTrue(review["validForDk1Exit"])
            self.assertEqual([], review["missingOwners"])
            self.assertEqual(
                ["Data Operations", "Provider Reliability", "Trading"],
                review["signedOwners"],
            )

    def test_validate_rejects_missing_required_owner(self) -> None:
        pwsh = shutil.which("pwsh")
        if pwsh is None:
            self.skipTest("pwsh is not available")

        with tempfile.TemporaryDirectory() as temp_dir:
            signoff_path = Path(temp_dir) / "dk1-operator-signoff.json"
            payload = _build_signed_signoff()
            payload["approvals"] = payload["approvals"][:2]
            signoff_path.write_text(json.dumps(payload), encoding="utf-8")

            result = subprocess.run(
                [
                    pwsh,
                    "-NoProfile",
                    "-File",
                    str(SCRIPT_PATH),
                    "-OutputPath",
                    str(signoff_path),
                    "-Validate",
                    "-Json",
                ],
                cwd=REPO_ROOT,
                check=False,
                capture_output=True,
                text=True,
            )

            self.assertNotEqual(0, result.returncode)
            review = json.loads(result.stdout)
            self.assertEqual("partial", review["status"])
            self.assertFalse(review["validForDk1Exit"])
            self.assertEqual(["Trading"], review["missingOwners"])


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
