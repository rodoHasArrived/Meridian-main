"""Exercise the real recovery script with controlled PostgreSQL command boundaries.

These tests use real archive encryption, integrity verification, and file restoration.
The PostgreSQL stubs keep this receipt-contract proof separate from database evidence.
"""

import base64
import hashlib
import hmac
import json
import os
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
            "-DataRoot": str(self.source),
            "-BackupRoot": str(self.backups),
            "-RestoreDataRoot": str(self.restored),
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
            "-DataRoot", overrides["-DataRoot"], "-BackupRoot", overrides["-BackupRoot"],
            "-RestoreConnectionString", overrides["-RestoreConnectionString"],
            "-RestoreDataRoot", overrides["-RestoreDataRoot"], "-AllowDatabaseOverwrite",
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

    def filesystem_snapshot(self, *excluded: Path):
        """Include empty directories, quarantine paths, PostgreSQL calls, and symlinks."""
        snapshot = {}
        for path in self.root.rglob("*"):
            if path in excluded:
                continue
            relative = str(path.relative_to(self.root))
            if path.is_symlink():
                snapshot[relative] = ("symlink", str(path.readlink()))
            elif path.is_dir():
                snapshot[relative] = ("directory", path.stat().st_mtime_ns)
            else:
                snapshot[relative] = ("file", path.stat().st_mtime_ns, path.read_bytes())
        return snapshot

    def assert_receipt_preflight_rejected(self, mode, receipt_name, *extra):
        before = self.filesystem_snapshot()
        result = subprocess.run(
            self.command(mode, receipt_name, *extra),
            cwd=REPO_ROOT, capture_output=True, text=True, timeout=30,
        )
        self.assertNotEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertIn("receipt", result.stderr.lower())
        self.assertEqual(before, self.filesystem_snapshot(), result.stdout + result.stderr)

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
        malformed_commits = (
            ("missing", None), ("null", None), ("empty", ""), ("short", "a" * 39),
            ("long", "a" * 65), ("nonhex", "g" * 40),
            ("sha1-newline", "a" * 40 + "\n"), ("sha256-newline", "b" * 64 + "\n"),
            ("number", 123), ("boolean", True), ("array", [self.commit]),
            ("object", {"commit": self.commit}),
        )
        self.restored.mkdir()
        (self.restored / "untouched.txt").write_text("existing-target", encoding="utf-8")
        for name, value in malformed_commits:
            with self.subTest(source_commit=name):
                manifest = dict(original)
                if name == "missing":
                    manifest.pop("sourceCommit")
                else:
                    manifest["sourceCommit"] = value
                self.write_signed_manifest(manifest_path, manifest)
                before = self.filesystem_snapshot()
                result, receipt, receipt_path = self.invoke(
                    "Drill", f"malformed-source-{name}.json", "-BackupPath", backup["backupPath"], "-AllowDataOverwrite",
                )
                self.assertNotEqual(0, result.returncode)
                self.assertIn("original sourceCommit", receipt["error"])
                self.assertEqual("failed", receipt["status"])
                self.assertEqual("unproven", receipt["objectiveStatus"])
                self.assertIsNone(receipt["sourceCommit"])
                self.assertIsNone(receipt["simulatedLossAtUtc"])
                self.assertIsNone(receipt["restoreStartedAtUtc"])
                self.assertFalse(receipt["manifestAuthenticated"])
                self.assertEqual(before, self.filesystem_snapshot(receipt_path))

    def test_all_modes_reject_malformed_execution_commit_before_recovery(self):
        backup_result, backup, _ = self.invoke("Backup", "backup-receipt.json")
        self.assertEqual(0, backup_result.returncode, backup_result.stderr)
        self.restored.mkdir()
        (self.restored / "untouched.txt").write_text("existing-target", encoding="utf-8")
        for mode, retained in (("Backup", False), ("Drill", False), ("Restore", True), ("Drill", True)):
            for name, commit in (
                ("empty", ""), ("whitespace", " \t "),
                ("nonhex", "g" * 40), ("short", "a" * 39), ("long", "a" * 65),
                ("sha1-newline", "a" * 40 + "\n"), ("sha256-newline", "b" * 64 + "\n"),
            ):
                with self.subTest(mode=mode, retained=retained, source_commit=name):
                    before = self.filesystem_snapshot()
                    extra = ("-BackupPath", backup["backupPath"]) if retained else ()
                    result, receipt, receipt_path = self.invoke(
                        mode, f"bad-commit-{mode}-{retained}-{name}.json", *extra,
                        "-SourceCommit", commit, "-AllowDataOverwrite",
                    )
                    self.assertNotEqual(0, result.returncode)
                    self.assertIn("commit", receipt["error"].lower())
                    self.assertEqual("failed", receipt["status"])
                    self.assertEqual("unproven", receipt["objectiveStatus"])
                    self.assertIsNone(receipt["simulatedLossAtUtc"])
                    self.assertIsNone(receipt["backupStartedAtUtc"])
                    self.assertIsNone(receipt["restoreStartedAtUtc"])
                    self.assertEqual(before, self.filesystem_snapshot(receipt_path))

    def test_omitted_source_commit_can_use_the_current_git_commit(self):
        command = self.command("Backup")
        commit_index = command.index("-SourceCommit")
        del command[commit_index:commit_index + 2]
        result = subprocess.run(
            command, cwd=REPO_ROOT, capture_output=True, text=True, timeout=30,
            env={key: value for key, value in os.environ.items() if key != "GITHUB_SHA"},
        )
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        expected_commit = subprocess.run(
            ["git", "rev-parse", "HEAD"], cwd=REPO_ROOT, capture_output=True, text=True, check=True,
        ).stdout.strip()
        receipt = json.loads((self.root / "receipt.json").read_bytes())
        self.assertEqual(expected_commit, receipt["sourceCommit"])
        self.assertEqual(expected_commit, receipt["drillSourceCommit"])
        self.assertTrue(receipt["manifestAuthenticated"])

    def test_full_sha1_and_sha256_commits_support_proven_drills_in_either_case(self):
        for commit in ("abcdef0123" * 4, "ABCDEF0123" * 4, "abcdef01" * 8, "ABCDEF01" * 8):
            name = f"{len(commit)}-{'upper' if commit.isupper() else 'lower'}"
            with self.subTest(commit=name):
                result, receipt, receipt_path = self.invoke(
                    "Drill", f"valid-{name}.json", "-SourceCommit", commit,
                    "-BackupRoot", str(self.backups / name), "-RestoreDataRoot", str(self.restored / name),
                    "-LastVerifiedRecoverablePointAtUtc", utc_timestamp(datetime.now(timezone.utc) - timedelta(minutes=2)),
                    "-RecoverablePointEvidence", "controlled-checkpoint",
                )
                self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                self.assertEqual(commit, receipt["sourceCommit"])
                self.assertEqual(commit, receipt["drillSourceCommit"])
                validation, output = self.validate_completion(receipt_path, self.completion_evidence(receipt), name)
                self.assertEqual(0, validation.returncode, validation.stdout + validation.stderr)
                self.assertEqual("proven", json.loads(output.read_bytes())["objectiveStatus"])

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

    def test_explicit_receipts_cannot_overlap_source_or_restore_roots(self):
        result, backup, _ = self.invoke("Backup", "backup-receipt.json")
        self.assertEqual(0, result.returncode, result.stderr)
        self.restored.mkdir()
        (self.restored / "untouched.txt").write_text("existing-target", encoding="utf-8")
        for mode, retained in (("Backup", False), ("Drill", False), ("Restore", True), ("Drill", True)):
            extra = ("-BackupPath", backup["backupPath"]) if retained else ()
            for protected_root in (self.source, self.restored):
                for receipt in (protected_root, protected_root / "new-receipt-parent" / "receipt.json"):
                    with self.subTest(mode=mode, retained=retained, receipt=receipt):
                        self.assert_receipt_preflight_rejected(mode, str(receipt), *extra, "-AllowDataOverwrite")

    def test_receipts_cannot_create_missing_recovery_roots_or_their_ancestors(self):
        result, backup, _ = self.invoke("Backup", "backup-receipt.json")
        self.assertEqual(0, result.returncode, result.stderr)
        for mode, retained in (("Backup", False), ("Drill", False), ("Restore", True), ("Drill", True)):
            extra = ("-BackupPath", backup["backupPath"]) if retained else ()
            for root_flag in ("-DataRoot", "-RestoreDataRoot"):
                for receipt_is_ancestor in (False, True):
                    receipt = self.root / "missing-parent" / "receipt.json"
                    protected_root = receipt / "data" if receipt_is_ancestor else receipt
                    with self.subTest(mode=mode, retained=retained, root=root_flag, ancestor=receipt_is_ancestor):
                        self.assert_receipt_preflight_rejected(
                            mode, str(receipt), *extra, root_flag, str(protected_root), "-AllowDataOverwrite",
                        )

    def test_default_receipts_inside_recovery_roots_have_no_side_effects(self):
        result, backup, _ = self.invoke("Backup", "backup-receipt.json")
        self.assertEqual(0, result.returncode, result.stderr)
        self.restored.mkdir()
        (self.restored / "untouched.txt").write_text("existing-target", encoding="utf-8")
        for mode, retained in (("Backup", False), ("Drill", False), ("Restore", True), ("Drill", True)):
            extra = ("-BackupPath", backup["backupPath"]) if retained else ()
            for protected_root in (self.source, self.restored):
                with self.subTest(mode=mode, retained=retained, root=protected_root):
                    self.assert_receipt_preflight_rejected(
                        mode, None, *extra, "-BackupRoot", str(protected_root / "new-receipt-parent"),
                        "-AllowDataOverwrite",
                    )

    def test_restore_receipt_preflight_uses_data_root_when_restore_root_is_omitted(self):
        result, backup, _ = self.invoke("Backup", "backup-receipt.json")
        self.assertEqual(0, result.returncode, result.stderr)
        for receipt in (str(self.source / "new-receipt-parent" / "receipt.json"), None):
            with self.subTest(receipt=receipt):
                extra = ("-BackupRoot", str(self.source / "new-receipt-parent")) if receipt is None else ()
                self.assert_receipt_preflight_rejected(
                    "Restore", receipt, "-BackupPath", backup["backupPath"], "-RestoreDataRoot", "",
                    "-AllowDataOverwrite", *extra,
                )

    def test_receipt_conflicts_are_checked_after_path_normalization(self):
        for root_flag, protected_root in (("-DataRoot", self.source), ("-RestoreDataRoot", self.restored)):
            with self.subTest(root=root_flag):
                receipt = str(protected_root / ".." / protected_root.name / "new-receipt-parent" / "receipt.json")
                self.assert_receipt_preflight_rejected(
                    "Drill", receipt, root_flag, str(protected_root) + "/", "-AllowDataOverwrite",
                )

    def test_receipt_conflicts_follow_directory_symlinks(self):
        result, backup, _ = self.invoke("Backup", "backup-receipt.json")
        self.assertEqual(0, result.returncode, result.stderr)
        self.restored.mkdir()
        (self.restored / "untouched.txt").write_text("existing-target", encoding="utf-8")
        source_alias, restore_alias = self.root / "source-alias", self.root / "restore-alias"
        source_alias.symlink_to(self.source, target_is_directory=True)
        restore_alias.symlink_to(self.restored, target_is_directory=True)
        for mode, retained in (("Backup", False), ("Drill", False), ("Restore", True), ("Drill", True)):
            extra = ("-BackupPath", backup["backupPath"]) if retained else ()
            for root_flag, protected_root, alias in (
                ("-DataRoot", self.source, source_alias), ("-RestoreDataRoot", self.restored, restore_alias),
            ):
                for aliased_receipt in (False, True):
                    with self.subTest(mode=mode, retained=retained, root=root_flag, receipt_alias=aliased_receipt):
                        receipt_root = alias if aliased_receipt else protected_root
                        recovery_root = protected_root if aliased_receipt else alias
                        self.assert_receipt_preflight_rejected(
                            mode, str(receipt_root / "new-receipt-parent" / "receipt.json"), *extra,
                            root_flag, str(recovery_root), "-AllowDataOverwrite",
                        )

    def test_receipts_inside_roots_cannot_escape_through_outward_symlinks(self):
        result, backup, _ = self.invoke("Backup", "backup-receipt.json")
        self.assertEqual(0, result.returncode, result.stderr)
        self.restored.mkdir()
        outside = self.root / "outside"
        outside.mkdir()
        for protected_root in (self.source, self.restored):
            (protected_root / "alias").symlink_to(outside, target_is_directory=True)
            (self.root / f"{protected_root.name}-alias").symlink_to(protected_root, target_is_directory=True)
        for mode, retained in (("Backup", False), ("Drill", False), ("Restore", True), ("Drill", True)):
            extra = ("-BackupPath", backup["backupPath"]) if retained else ()
            for protected_root in (self.source, self.restored):
                with self.subTest(mode=mode, retained=retained, root=protected_root):
                    self.assert_receipt_preflight_rejected(
                        mode, str(protected_root / "alias" / "receipt.json"), *extra, "-AllowDataOverwrite",
                    )
                    self.assert_receipt_preflight_rejected(
                        mode, str(self.root / f"{protected_root.name}-alias" / "alias" / "receipt.json"),
                        *extra, "-AllowDataOverwrite",
                    )

    def test_receipt_conflicts_follow_symlinks_in_other_link_targets(self):
        nested_source = self.source / "nested"
        nested_source.mkdir()
        source_alias = self.root / "source-alias"
        source_alias.symlink_to(self.source, target_is_directory=True)
        chained_alias = self.root / "chained-alias"
        chained_alias.symlink_to(source_alias / "nested", target_is_directory=True)
        new_source = self.source / "new"
        new_source.mkdir()
        outer = self.root / "outer"
        outer.mkdir()
        (outer / "pivot").symlink_to(nested_source, target_is_directory=True)
        traversal_alias = self.root / "traversal-alias"
        traversal_alias.symlink_to("outer/pivot/../new", target_is_directory=True)
        for alias, real_directory in ((chained_alias, nested_source), (traversal_alias, new_source)):
            for aliased_receipt in (False, True):
                with self.subTest(alias=alias.name, receipt_alias=aliased_receipt):
                    receipt_root = alias if aliased_receipt else real_directory
                    recovery_root = self.source if aliased_receipt else alias
                    self.assert_receipt_preflight_rejected(
                        "Drill", str(receipt_root / "new-receipt-parent" / "receipt.json"),
                        "-DataRoot", str(recovery_root),
                    )

    def test_safe_relative_directory_symlink_receipt_remains_valid(self):
        outer = self.root / "outer"
        outer.mkdir()
        safe_receipts = self.root / "safe-receipts"
        safe_receipts.mkdir()
        alias = outer / "safe-alias"
        alias.symlink_to("../safe-receipts", target_is_directory=True)
        result, receipt, _ = self.invoke("Backup", str(alias / "receipt.json"))
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertEqual("passed", receipt["status"])
        self.assertTrue((safe_receipts / "receipt.json").is_file())

    def test_receipt_paths_with_sibling_prefixes_remain_valid(self):
        receipt_path = self.root / "source-restored-receipts" / "receipt.json"
        result, receipt, _ = self.invoke(
            "Drill", str(receipt_path), "-BackupRoot", str(self.root / "source-backups"),
            "-RestoreDataRoot", str(self.root / "source-restored"),
        )
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertEqual("passed", receipt["status"])
        self.assertEqual("committed-state", (self.root / "source-restored" / "probe.txt").read_text())
        default_result = subprocess.run(
            self.command("Backup", None, "-BackupRoot", str(self.root / "source-default-backups")),
            cwd=REPO_ROOT, capture_output=True, text=True, timeout=30,
        )
        self.assertEqual(0, default_result.returncode, default_result.stdout + default_result.stderr)
        self.assertEqual(1, len(list((self.root / "source-default-backups").glob("*-receipt.json"))))

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
