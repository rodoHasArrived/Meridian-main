"""Exercise the real recovery script with controlled PostgreSQL command boundaries.

These tests use real archive encryption, integrity verification, and file restoration.
The PostgreSQL stubs keep this receipt-contract proof separate from database evidence.
"""

import base64
import json
from datetime import datetime, timedelta, timezone
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


REPO_ROOT = Path(__file__).resolve().parents[2]
SCRIPT = REPO_ROOT / "build/scripts/recovery/invoke-production-recovery.ps1"
VALIDATOR = REPO_ROOT / "build/scripts/recovery/validate-recovery-receipt.ps1"


def utc_timestamp(value: datetime) -> str:
    return value.isoformat().replace("+00:00", "Z")


class ProductionRecoveryReceiptTests(unittest.TestCase):
    def setUp(self) -> None:
        self.pwsh = shutil.which("pwsh")
        self.assertIsNotNone(self.pwsh, "PowerShell 7 is required for recovery receipt tests.")
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.source = self.root / "source"
        self.source.mkdir()
        (self.source / "probe.txt").write_text("committed-state", encoding="utf-8")
        self.backups = self.root / "backups"
        self.restored = self.root / "restored"
        self.calls = self.root / "postgres-calls.jsonl"
        self.pg_tool = self.root / "postgres-tool"
        self.pg_tool.write_text(
            "#!/usr/bin/env python3\n"
            "import json, pathlib, sys\n"
            f"calls = pathlib.Path({str(self.calls)!r})\n"
            "args = sys.argv[1:]\n"
            "with calls.open('a') as output:\n"
            "    output.write(json.dumps(args) + '\\n')\n"
            "if '--file' in args:\n"
            "    pathlib.Path(args[args.index('--file') + 1]).write_bytes(b'controlled-database-state')\n"
            "elif '--clean' in args:\n"
            "    if args[args.index('--dbname') + 1] == 'failed_restore':\n"
            "        sys.exit(17)\n"
            "    assert pathlib.Path(args[-1]).read_bytes() == b'controlled-database-state'\n"
            "else:\n"
            "    print('1')\n",
            encoding="utf-8",
        )
        self.pg_tool.chmod(0o755)
        self.commit = "a" * 40

    def invoke(self, mode: str, receipt_name: str = "receipt.json", *extra: str):
        receipt_path = self.root / receipt_name
        extra_arguments = list(extra)
        overrides = {
            "-RestoreConnectionString": "Host=localhost;Database=target;Username=probe",
            "-EncryptionKeyBase64": base64.b64encode(b"a" * 32).decode("ascii"),
        }
        for flag in overrides:
            if flag in extra_arguments:
                index = extra_arguments.index(flag)
                overrides[flag] = extra_arguments[index + 1]
                del extra_arguments[index:index + 2]
        command = [
            self.pwsh, "-NoLogo", "-NoProfile", "-File", str(SCRIPT),
            "-Mode", mode,
            "-ConnectionString", "Host=localhost;Database=source;Username=probe",
            "-DataRoot", str(self.source), "-BackupRoot", str(self.backups),
            "-RestoreConnectionString", overrides["-RestoreConnectionString"],
            "-RestoreDataRoot", str(self.restored), "-AllowDatabaseOverwrite",
            "-EncryptionKeyBase64", overrides["-EncryptionKeyBase64"],
            "-PgDumpPath", str(self.pg_tool), "-PgRestorePath", str(self.pg_tool),
            "-PsqlPath", str(self.pg_tool), "-SourceCommit", self.commit,
            "-ReceiptPath", str(receipt_path), *extra_arguments,
        ]
        result = subprocess.run(command, cwd=REPO_ROOT, capture_output=True, text=True, timeout=30)
        self.assertTrue(receipt_path.is_file(), result.stdout + result.stderr)
        return result, json.loads(receipt_path.read_text(encoding="utf-8-sig")), receipt_path

    def assert_archive_durations(self, receipt):
        for operation in ("backup", "restore"):
            started = datetime.fromisoformat(receipt[f"{operation}StartedAtUtc"])
            completed = datetime.fromisoformat(receipt[f"{operation}CompletedAtUtc"])
            self.assertAlmostEqual(
                (completed - started).total_seconds(), receipt[f"{operation}DurationSeconds"], delta=0.001,
            )

    def test_archive_roundtrip_without_checkpoint_cannot_prove_recovery(self):
        result, receipt, _ = self.invoke("Drill")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("passed", receipt["status"])
        self.assertEqual("unproven", receipt["objectiveStatus"])
        self.assertIsNone(receipt["measuredRpoSeconds"])
        self.assertIsNone(receipt["measuredRtoSeconds"])
        self.assertIsNone(receipt["operatorAcceptedAtUtc"])
        self.assertIsNone(receipt["reconciliationCompletedAtUtc"])
        self.assertEqual("committed-state", (self.restored / "probe.txt").read_text())
        self.assert_archive_durations(receipt)

    def test_known_checkpoint_measures_age_without_automatic_acceptance(self):
        point = utc_timestamp(datetime.now(timezone.utc) - timedelta(seconds=120))
        result, receipt, _ = self.invoke(
            "Drill", "receipt.json", "-LastVerifiedRecoverablePointAtUtc", point,
            "-RecoverablePointEvidence", "controlled-quiesced-checkpoint",
        )
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("unproven", receipt["objectiveStatus"])
        self.assertEqual("proven", receipt["rpoStatus"])
        self.assertGreaterEqual(receipt["measuredRpoSeconds"], 120)
        self.assertIsNone(receipt["measuredRtoSeconds"])
        self.assertLess(receipt["backupDurationSeconds"], receipt["measuredRpoSeconds"])
        self.assertLessEqual(receipt["recoverablePointVerifiedAtUtc"], receipt["simulatedLossAtUtc"])
        self.assert_archive_durations(receipt)

    def test_fast_restore_of_retained_stale_backup_fails_point_budget(self):
        point = utc_timestamp(datetime.now(timezone.utc) - timedelta(hours=2))
        backup_result, backup, _ = self.invoke(
            "Backup", "backup-receipt.json", "-LastVerifiedRecoverablePointAtUtc", point,
            "-RecoverablePointEvidence", "controlled-stale-checkpoint",
        )
        self.assertEqual(0, backup_result.returncode, backup_result.stderr)
        result, receipt, _ = self.invoke("Drill", "drill-receipt.json", "-BackupPath", backup["backupPath"])
        self.assertNotEqual(0, result.returncode)
        self.assertEqual(backup["backupId"], receipt["backupId"])
        self.assertEqual(point, receipt["lastVerifiedRecoverablePointAtUtc"])
        self.assertGreater(receipt["measuredRpoSeconds"], 3600)
        self.assertEqual("breached", receipt["rpoStatus"])
        self.assertEqual("unproven", receipt["objectiveStatus"])
        self.assertLess(receipt["restoreDurationSeconds"], 3600)
        self.assertEqual(1, len(list(self.backups.glob("backup-*"))))
        self.assertEqual("committed-state", (self.restored / "probe.txt").read_text())

    def test_tampered_backup_fails_before_simulated_loss(self):
        backup_result, backup, _ = self.invoke(
            "Backup", "backup-receipt.json", "-LastVerifiedRecoverablePointAtUtc",
            utc_timestamp(datetime.now(timezone.utc) - timedelta(minutes=2)),
            "-RecoverablePointEvidence", "controlled-checkpoint",
        )
        self.assertEqual(0, backup_result.returncode, backup_result.stderr)
        archive = Path(backup["backupPath"]) / "database.dump.enc"
        data = bytearray(archive.read_bytes())
        data[40] ^= 1
        archive.write_bytes(data)
        result, receipt, _ = self.invoke("Drill", "drill-receipt.json", "-BackupPath", backup["backupPath"])
        self.assertNotEqual(0, result.returncode)
        self.assertEqual("failed", receipt["status"])
        self.assertEqual("unproven", receipt["objectiveStatus"])
        self.assertIsNone(receipt["simulatedLossAtUtc"])
        self.assertIsNone(receipt["recoverablePointVerifiedAtUtc"])
        self.assertFalse(self.restored.exists())

    def test_legacy_backup_restores_with_archive_timings_but_no_objective_proof(self):
        backup_result, backup, _ = self.invoke("Backup", "backup-receipt.json")
        self.assertEqual(0, backup_result.returncode, backup_result.stderr)
        manifest_path = Path(backup["backupPath"]) / "manifest.json"
        manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
        manifest["schemaVersion"] = 1
        manifest["createdAtUtc"] = manifest.pop("backupCompletedAtUtc")
        manifest["durationSeconds"] = manifest.pop("backupDurationSeconds")
        for field in ("backupStartedAtUtc", "lastVerifiedRecoverablePointAtUtc", "recoverablePointVerifiedAtUtc", "recoverablePointEvidence"):
            manifest.pop(field)
        manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
        result, receipt, _ = self.invoke("Drill", "legacy-drill.json", "-BackupPath", backup["backupPath"])
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("passed", receipt["status"])
        self.assertEqual("unproven", receipt["objectiveStatus"])
        self.assertEqual(manifest["durationSeconds"], receipt["backupDurationSeconds"])
        self.assertIsNone(receipt["measuredRpoSeconds"])
        self.assertIsNone(receipt["measuredRtoSeconds"])
        self.assertEqual("committed-state", (self.restored / "probe.txt").read_text())

    def test_invalid_manifest_schema_cannot_supply_verified_point_evidence(self):
        backup_result, backup, _ = self.invoke("Backup", "backup-receipt.json")
        self.assertEqual(0, backup_result.returncode, backup_result.stderr)
        manifest_path = Path(backup["backupPath"]) / "manifest.json"
        manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
        for value in (True, "2"):
            with self.subTest(schema_version=value):
                manifest["schemaVersion"] = value
                manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
                result, receipt, _ = self.invoke("Drill", "invalid-schema.json", "-BackupPath", backup["backupPath"])
                self.assertNotEqual(0, result.returncode)
                self.assertEqual("failed", receipt["status"])
                self.assertEqual("unproven", receipt["objectiveStatus"])
                self.assertIsNone(receipt["simulatedLossAtUtc"])

    def test_restore_failure_keeps_operation_timings_and_objectives_unproven(self):
        result, receipt, _ = self.invoke(
            "Drill", "failed-receipt.json", "-RestoreConnectionString",
            "Host=localhost;Database=failed_restore;Username=probe",
        )
        self.assertNotEqual(0, result.returncode)
        self.assertEqual("failed", receipt["status"])
        self.assertEqual("unproven", receipt["objectiveStatus"])
        self.assertIn("17", receipt["error"])
        self.assertIsNone(receipt["operatorAcceptedAtUtc"])
        self.assertIsNotNone(receipt["backupDurationSeconds"])
        self.assertIsNotNone(receipt["restoreDurationSeconds"])
        self.assert_archive_durations(receipt)

    def test_bad_encryption_key_still_leaves_failed_receipt(self):
        result, receipt, _ = self.invoke("Backup", "bad-key.json", "-EncryptionKeyBase64", "invalid")
        self.assertNotEqual(0, result.returncode)
        self.assertEqual("failed", receipt["status"])
        self.assertEqual("unproven", receipt["objectiveStatus"])
        self.assertIn("Base64", receipt["error"])

    def test_operator_completion_can_validate_the_actual_produced_receipt(self):
        point = utc_timestamp(datetime.now(timezone.utc) - timedelta(minutes=2))
        result, receipt, receipt_path = self.invoke(
            "Drill", "receipt.json", "-LastVerifiedRecoverablePointAtUtc", point,
            "-RecoverablePointEvidence", "controlled-quiesced-checkpoint",
        )
        self.assertEqual(0, result.returncode, result.stderr)
        evidence = {key: receipt[key] for key in ("sourceCommit", "backupId", "simulatedLossAtUtc", "lossDeclaredAtUtc")}
        evidence.update(
            reconciliationCompletedAtUtc=utc_timestamp(datetime.now(timezone.utc)),
            reconciliationEvidence="controlled-reconciliation-result",
            operatorAcceptedAtUtc=utc_timestamp(datetime.now(timezone.utc)),
            operatorAcceptedBy="test-operator",
            operatorAcceptanceEvidence="controlled-acceptance-record",
        )
        evidence_path = self.root / "completion.json"
        evidence_path.write_text(json.dumps(evidence), encoding="utf-8")
        output_path = self.root / "evaluated.json"
        validation = subprocess.run(
            [self.pwsh, "-NoLogo", "-NoProfile", "-File", str(VALIDATOR), "-ReceiptPath", str(receipt_path),
             "-RecoveryEvidencePath", str(evidence_path), "-OutputPath", str(output_path)],
            cwd=REPO_ROOT, capture_output=True, text=True, timeout=30,
        )
        self.assertEqual(0, validation.returncode, validation.stderr)
        evaluated = json.loads(output_path.read_text(encoding="utf-8-sig"))
        self.assertEqual("proven", evaluated["objectiveStatus"])
        self.assertGreaterEqual(evaluated["measuredRpoSeconds"], 120)
        self.assertGreater(evaluated["measuredRtoSeconds"], receipt["restoreDurationSeconds"])
        self.assertEqual("unproven", json.loads(receipt_path.read_text())["objectiveStatus"])


if __name__ == "__main__":
    unittest.main()
