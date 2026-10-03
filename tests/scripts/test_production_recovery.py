"""Exercise the real recovery script with controlled PostgreSQL command boundaries.

These tests use real archive encryption, integrity verification, and file restoration.
The PostgreSQL stubs keep this receipt-contract proof separate from database evidence.
"""

import base64
import hashlib
import hmac
import json
from datetime import datetime, timedelta, timezone
from pathlib import Path
import shutil
import subprocess
import tempfile
import time
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

    def command(self, mode: str, receipt_name: str | None = "receipt.json", *extra: str):
        extra_arguments = list(extra)
        overrides = {
            "-RestoreConnectionString": "Host=localhost;Database=target;Username=probe",
            "-EncryptionKeyBase64": base64.b64encode(b"a" * 32).decode("ascii"),
            "-SourceCommit": self.commit,
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
            "-PsqlPath", str(self.pg_tool), "-SourceCommit", overrides["-SourceCommit"],
            *(["-ReceiptPath", str(self.root / receipt_name)] if receipt_name is not None else []),
            *extra_arguments,
        ]
        return command

    def invoke(self, mode: str, receipt_name: str = "receipt.json", *extra: str):
        receipt_path = self.root / receipt_name
        command = self.command(mode, receipt_name, *extra)
        result = subprocess.run(command, cwd=REPO_ROOT, capture_output=True, text=True, timeout=30)
        self.assertTrue(receipt_path.is_file(), result.stdout + result.stderr)
        return result, json.loads(receipt_path.read_text(encoding="utf-8-sig")), receipt_path

    def write_signed_manifest(self, manifest_path: Path, manifest):
        """Construct malformed but authenticated metadata to test producer validation."""
        data = json.dumps(manifest).encode("utf-8")
        salt = bytes(range(16))
        key = hmac.digest(b"a" * 32, salt + b"meridian-recovery-v1:manifest-authentication", "sha256")
        manifest_path.write_bytes(data)
        manifest_path.with_name("manifest.hmac").write_bytes(salt + hmac.digest(key, data, "sha256"))

    def completion_evidence(self, receipt):
        evidence = {key: receipt[key] for key in (
            "sourceCommit", "drillSourceCommit", "manifestSha256", "backupId", "simulatedLossAtUtc", "lossDeclaredAtUtc",
        )}
        evidence.update(
            reconciliationCompletedAtUtc=utc_timestamp(datetime.now(timezone.utc)),
            reconciliationEvidence="controlled-reconciliation-result",
            operatorAcceptedAtUtc=utc_timestamp(datetime.now(timezone.utc)),
            operatorAcceptedBy="test-operator",
            operatorAcceptanceEvidence="controlled-acceptance-record",
        )
        return evidence

    def validate_completion(self, receipt_path: Path, evidence, name="evaluated"):
        evidence_path = self.root / f"{name}-completion.json"
        evidence_path.write_text(json.dumps(evidence), encoding="utf-8")
        output_path = self.root / f"{name}.json"
        validation = subprocess.run(
            [self.pwsh, "-NoLogo", "-NoProfile", "-File", str(VALIDATOR), "-ReceiptPath", str(receipt_path),
             "-RecoveryEvidencePath", str(evidence_path), "-OutputPath", str(output_path)],
            cwd=REPO_ROOT, capture_output=True, text=True, timeout=30,
        )
        return validation, output_path

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

    def test_checkpoint_provenance_and_archive_metadata_are_authenticated(self):
        backup_result, backup, _ = self.invoke(
            "Backup", "backup-receipt.json", "-LastVerifiedRecoverablePointAtUtc",
            utc_timestamp(datetime.now(timezone.utc) - timedelta(hours=2)),
            "-RecoverablePointEvidence", "controlled-stale-checkpoint",
        )
        self.assertEqual(0, backup_result.returncode, backup_result.stderr)
        manifest_path = Path(backup["backupPath"]) / "manifest.json"
        original = manifest_path.read_bytes()
        original_calls = self.calls.read_bytes()
        for field, value in (
            ("lastVerifiedRecoverablePointAtUtc", utc_timestamp(datetime.now(timezone.utc))),
            ("recoverablePointEvidence", "forged-checkpoint"),
            ("recoverablePointVerifiedAtUtc", utc_timestamp(datetime.now(timezone.utc))),
            ("sourceCommit", "b" * 40),
            ("backupId", "forged-backup-id"),
            ("backupStartedAtUtc", utc_timestamp(datetime.now(timezone.utc))),
            ("database", {"archive": "database.dump.enc", "encryptedSha256": "f" * 64, "plaintextSha256": "f" * 64}),
        ):
            with self.subTest(field=field):
                manifest = json.loads(original)
                manifest[field] = value
                manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
                result, receipt, _ = self.invoke("Drill", f"tampered-{field}.json", "-BackupPath", backup["backupPath"])
                self.assertNotEqual(0, result.returncode)
                self.assertIn("manifest authentication failed", receipt["error"])
                self.assertIsNone(receipt["simulatedLossAtUtc"])
                self.assertIsNone(receipt["recoverablePointVerifiedAtUtc"])
                self.assertIsNone(receipt["sourceCommit"])
                self.assertFalse(receipt["manifestAuthenticated"])
                self.assertFalse(self.restored.exists())
                self.assertEqual(original_calls, self.calls.read_bytes())

    def test_manifest_authentication_is_required_and_checked_before_restore_effects(self):
        backup_result, backup, _ = self.invoke("Backup", "backup-receipt.json")
        self.assertEqual(0, backup_result.returncode, backup_result.stderr)
        tag_path = Path(backup["backupPath"]) / "manifest.hmac"
        original_tag = tag_path.read_bytes()
        original_calls = self.calls.read_bytes()
        self.restored.mkdir()
        sentinel = self.restored / "untouched.txt"
        sentinel.write_text("existing-target", encoding="utf-8")
        for name, tag in (("missing", None), ("truncated", original_tag[:30]), ("wrong", b"z" * 48)):
            with self.subTest(authentication=name):
                if tag is None:
                    tag_path.unlink()
                else:
                    tag_path.write_bytes(tag)
                result, receipt, _ = self.invoke(
                    "Restore", f"authentication-{name}.json", "-BackupPath", backup["backupPath"], "-AllowDataOverwrite",
                )
                self.assertNotEqual(0, result.returncode)
                self.assertIn("manifest authentication", receipt["error"])
                self.assertEqual("existing-target", sentinel.read_text())
                self.assertFalse(list(self.root.glob("restored.pre-restore-*")))
                self.assertEqual(original_calls, self.calls.read_bytes())

    def test_authenticated_manifest_requires_original_source_commit(self):
        backup_result, backup, _ = self.invoke("Backup", "backup-receipt.json")
        self.assertEqual(0, backup_result.returncode, backup_result.stderr)
        manifest_path = Path(backup["backupPath"]) / "manifest.json"
        original = json.loads(manifest_path.read_bytes())
        original_calls = self.calls.read_bytes()
        for index, value in enumerate((None, "", "not-a-commit")):
            with self.subTest(source_commit=value):
                manifest = dict(original)
                if value is None:
                    manifest.pop("sourceCommit")
                else:
                    manifest["sourceCommit"] = value
                self.write_signed_manifest(manifest_path, manifest)
                result, receipt, _ = self.invoke("Drill", f"missing-source-{index}.json", "-BackupPath", backup["backupPath"])
                self.assertNotEqual(0, result.returncode)
                self.assertIn("original sourceCommit", receipt["error"])
                self.assertIsNone(receipt["sourceCommit"])
                self.assertIsNone(receipt["simulatedLossAtUtc"])
                self.assertFalse(self.restored.exists())
                self.assertEqual(original_calls, self.calls.read_bytes())

    def test_retained_backup_preserves_commit_and_completion_cannot_relabel_it(self):
        backup_result, backup, _ = self.invoke(
            "Backup", "backup-receipt.json", "-LastVerifiedRecoverablePointAtUtc",
            utc_timestamp(datetime.now(timezone.utc) - timedelta(minutes=2)),
            "-RecoverablePointEvidence", "controlled-retained-checkpoint",
        )
        self.assertEqual(0, backup_result.returncode, backup_result.stderr)
        execution_commit = "b" * 40
        result, receipt, receipt_path = self.invoke(
            "Drill", "retained-drill.json", "-BackupPath", backup["backupPath"], "-SourceCommit", execution_commit,
        )
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(self.commit, receipt["sourceCommit"])
        self.assertEqual(execution_commit, receipt["drillSourceCommit"])
        self.assertTrue(receipt["manifestAuthenticated"])
        self.assertEqual(
            hashlib.sha256((Path(backup["backupPath"]) / "manifest.json").read_bytes()).hexdigest(),
            receipt["manifestSha256"],
        )
        wrong_evidence = self.completion_evidence(receipt)
        wrong_evidence["sourceCommit"] = execution_commit
        validation, output = self.validate_completion(receipt_path, wrong_evidence, "wrong-source")
        self.assertNotEqual(0, validation.returncode)
        self.assertIn("sourceCommit", validation.stderr)
        self.assertFalse(output.exists())
        validation, output = self.validate_completion(receipt_path, self.completion_evidence(receipt), "correct-source")
        self.assertEqual(0, validation.returncode, validation.stderr)
        self.assertEqual("proven", json.loads(output.read_bytes())["objectiveStatus"])

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
        manifest_path.with_name("manifest.hmac").unlink()
        result, receipt, _ = self.invoke("Drill", "legacy-drill.json", "-BackupPath", backup["backupPath"])
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("passed", receipt["status"])
        self.assertEqual("unproven", receipt["objectiveStatus"])
        self.assertEqual(manifest["durationSeconds"], receipt["backupDurationSeconds"])
        self.assertIsNone(receipt["measuredRpoSeconds"])
        self.assertIsNone(receipt["measuredRtoSeconds"])
        self.assertFalse(receipt["manifestAuthenticated"])
        self.assertIsNone(receipt["manifestSha256"])
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
                result, receipt, _ = self.invoke("Drill", f"invalid-schema-{value}.json", "-BackupPath", backup["backupPath"])
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

    def test_explicit_receipt_reuse_fails_before_backup_and_preserves_evidence(self):
        result, _, receipt_path = self.invoke("Backup")
        self.assertEqual(0, result.returncode, result.stderr)
        original = receipt_path.read_bytes()
        original_calls = self.calls.read_bytes()
        repeated = subprocess.run(self.command("Drill"), cwd=REPO_ROOT, capture_output=True, text=True, timeout=30)
        self.assertNotEqual(0, repeated.returncode)
        self.assertIn("receipt already exists", repeated.stderr)
        self.assertEqual(original, receipt_path.read_bytes())
        self.assertEqual(original_calls, self.calls.read_bytes())
        self.assertFalse(self.restored.exists())
        self.assertEqual(1, len(list(self.backups.glob("backup-*"))))

    def test_default_receipts_are_unique_even_when_operations_fail(self):
        for _ in range(2):
            result = subprocess.run(
                self.command("Backup", None, "-EncryptionKeyBase64", "invalid"),
                cwd=REPO_ROOT, capture_output=True, text=True, timeout=30,
            )
            self.assertNotEqual(0, result.returncode)
        receipts = list(self.backups.glob("recovery-backup-*-receipt.json"))
        self.assertEqual(2, len(receipts))
        self.assertTrue(all(json.loads(path.read_bytes())["status"] == "failed" for path in receipts))

    def test_concurrent_receipt_writer_is_rejected_before_postgres_operations(self):
        ready, release = self.root / "ready", self.root / "release"
        stub = self.pg_tool.read_text()
        stub = stub.replace("args = sys.argv[1:]", (
            "import time\n"
            f"pathlib.Path({str(ready)!r}).touch()\n"
            "deadline = time.monotonic() + 20\n"
            f"while not pathlib.Path({str(release)!r}).exists():\n"
            "    if time.monotonic() > deadline: sys.exit(19)\n"
            "    time.sleep(0.01)\n"
            "args = sys.argv[1:]"
        ))
        self.pg_tool.write_text(stub)
        process = subprocess.Popen(self.command("Backup"), cwd=REPO_ROOT, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        try:
            deadline = time.monotonic() + 15
            while not ready.exists() and process.poll() is None and time.monotonic() < deadline:
                time.sleep(0.01)
            self.assertTrue(ready.exists(), "First writer did not reach controlled PostgreSQL boundary.")
            competing = subprocess.run(self.command("Backup"), cwd=REPO_ROOT, capture_output=True, text=True, timeout=15)
            self.assertNotEqual(0, competing.returncode)
            self.assertIn("receipt already exists", competing.stderr)
            self.assertFalse(self.calls.exists())
        finally:
            release.touch()
            stdout, stderr = process.communicate(timeout=25)
        self.assertEqual(0, process.returncode, stdout + stderr)
        self.assertEqual(1, len(self.calls.read_text().splitlines()))
        self.assertEqual("passed", json.loads((self.root / "receipt.json").read_bytes())["status"])

    def test_operator_completion_can_validate_the_actual_produced_receipt(self):
        point = utc_timestamp(datetime.now(timezone.utc) - timedelta(minutes=2))
        result, receipt, receipt_path = self.invoke(
            "Drill", "receipt.json", "-LastVerifiedRecoverablePointAtUtc", point,
            "-RecoverablePointEvidence", "controlled-quiesced-checkpoint",
        )
        self.assertEqual(0, result.returncode, result.stderr)
        validation, output_path = self.validate_completion(receipt_path, self.completion_evidence(receipt))
        self.assertEqual(0, validation.returncode, validation.stderr)
        evaluated = json.loads(output_path.read_text(encoding="utf-8-sig"))
        self.assertEqual("proven", evaluated["objectiveStatus"])
        self.assertGreaterEqual(evaluated["measuredRpoSeconds"], 120)
        self.assertGreater(evaluated["measuredRtoSeconds"], receipt["restoreDurationSeconds"])
        self.assertEqual("unproven", json.loads(receipt_path.read_text())["objectiveStatus"])


if __name__ == "__main__":
    unittest.main()
