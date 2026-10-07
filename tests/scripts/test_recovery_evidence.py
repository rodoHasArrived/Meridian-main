"""Exercise the real PowerShell receipt evaluator and CLI with adversarial evidence."""

import json
import os
import shutil
import subprocess
import tempfile
import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
RECOVERY = ROOT / "build" / "scripts" / "recovery"
VALIDATOR = RECOVERY / "validate-recovery-receipt.ps1"
HELPER = RECOVERY / "recovery-evidence.ps1"


def complete_receipt():
    return {
        "schemaVersion": 2,
        "mode": "Drill",
        "status": "passed",
        "sourceCommit": "a" * 40,
        "drillSourceCommit": "b" * 40,
        "manifestAuthenticated": True,
        "manifestSha256": "c" * 64,
        "backupId": "backup-20250101T001000Z",
        "startedAtUtc": "2025-01-01T00:10:00Z",
        "completedAtUtc": "2025-01-01T00:30:04Z",
        "backupStartedAtUtc": "2025-01-01T00:10:00Z",
        "backupCompletedAtUtc": "2025-01-01T00:10:01Z",
        "backupDurationSeconds": 1,
        "restoreStartedAtUtc": "2025-01-01T00:30:02Z",
        "restoreCompletedAtUtc": "2025-01-01T00:30:03Z",
        "restoreDurationSeconds": 1,
        "lastVerifiedRecoverablePointAtUtc": "2025-01-01T00:00:00Z",
        "recoverablePointVerifiedAtUtc": "2025-01-01T00:10:02Z",
        "recoverablePointEvidence": "probe/checkpoint-42.json#backup-checksums",
        "simulatedLossAtUtc": "2025-01-01T00:30:00Z",
        "lossDeclaredAtUtc": "2025-01-01T00:30:01Z",
        "reconciliationCompletedAtUtc": "2025-01-01T00:35:00Z",
        "reconciliationEvidence": "review/reconciliation-42.json",
        "operatorAcceptedAtUtc": "2025-01-01T00:40:00Z",
        "operatorAcceptedBy": "operator-42",
        "operatorAcceptanceEvidence": "review/acceptance-42.json",
        "maximumRpoSeconds": 3600,
        "maximumRtoSeconds": 7200,
    }


class RecoveryEvidenceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.pwsh = os.environ.get("PWSH_PATH") or shutil.which("pwsh")
        if not cls.pwsh:
            raise RuntimeError("PowerShell 7 (pwsh) is required to run recovery evidence tests.")

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.directory = Path(self.temp.name)

    def validate(self, receipt, completion=None, extra=(), output=None):
        receipt_path = self.directory / "receipt.json"
        receipt_path.write_text(json.dumps(receipt), encoding="utf-8")
        source_bytes = receipt_path.read_bytes()
        command = [self.pwsh, "-NoProfile", "-NonInteractive", "-File", str(VALIDATOR), "-ReceiptPath", str(receipt_path)]
        if completion is not None:
            completion_path = self.directory / "completion.json"
            completion_path.write_text(json.dumps(completion), encoding="utf-8")
            command += ["-RecoveryEvidencePath", str(completion_path)]
        if output is not None:
            command += ["-OutputPath", str(output)]
        result = subprocess.run(command + list(extra), capture_output=True, text=True, timeout=30, cwd=ROOT)
        self.assertEqual(receipt_path.read_bytes(), source_bytes, "validator mutated immutable source receipt")
        evaluated = json.loads(result.stdout) if result.stdout.strip() else None
        return result, evaluated

    def assert_unproven(self, receipt, message=None):
        result, evaluated = self.validate(receipt)
        self.assertNotEqual(result.returncode, 0, result.stderr)
        self.assertIsNotNone(evaluated, result.stderr)
        self.assertEqual(evaluated["objectiveStatus"], "unproven")
        self.assertTrue(evaluated["objectiveErrors"])
        if message:
            self.assertTrue(any(message in error for error in evaluated["objectiveErrors"]), evaluated["objectiveErrors"])
        return evaluated

    def assert_breached(self, receipt, objective):
        result, evaluated = self.validate(receipt)
        self.assertNotEqual(result.returncode, 0, result.stderr)
        self.assertEqual(evaluated["objectiveStatus"], "breached")
        self.assertEqual(evaluated[objective + "Status"], "breached")
        return evaluated

    def test_complete_evidence_uses_checkpoint_age_and_operator_acceptance(self):
        result, evaluated = self.validate(complete_receipt())
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(evaluated["objectiveStatus"], "proven")
        self.assertEqual(evaluated["measuredRpoSeconds"], 1800)
        self.assertEqual(evaluated["measuredRtoSeconds"], 599)
        self.assertEqual(evaluated["backupDurationSeconds"], 1)
        self.assertEqual(evaluated["restoreDurationSeconds"], 1)
        self.assertEqual(evaluated["objectiveErrors"], [])

    def test_stale_checkpoint_cannot_be_hidden_by_one_second_archives(self):
        receipt = complete_receipt()
        receipt["lastVerifiedRecoverablePointAtUtc"] = "2024-12-31T00:00:00Z"
        evaluated = self.assert_breached(receipt, "rpo")
        self.assertEqual(evaluated["measuredRpoSeconds"], 88200)
        self.assertEqual(evaluated["rtoStatus"], "proven")

    def test_slow_reconciliation_is_part_of_rto(self):
        receipt = complete_receipt()
        receipt["reconciliationCompletedAtUtc"] = "2025-01-01T03:30:00Z"
        receipt["operatorAcceptedAtUtc"] = "2025-01-01T03:40:00Z"
        evaluated = self.assert_breached(receipt, "rto")
        self.assertEqual(evaluated["measuredRtoSeconds"], 11399)

    def test_detection_time_is_rpo_age_and_rto_starts_at_declaration(self):
        receipt = complete_receipt()
        receipt["lossDeclaredAtUtc"] = "2025-01-01T00:34:00Z"
        receipt["restoreStartedAtUtc"] = "2025-01-01T00:34:01Z"
        receipt["restoreCompletedAtUtc"] = "2025-01-01T00:34:02Z"
        receipt["completedAtUtc"] = "2025-01-01T00:34:03Z"
        result, evaluated = self.validate(receipt)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(evaluated["measuredRpoSeconds"], 1800)
        self.assertEqual(evaluated["measuredRtoSeconds"], 360)

    def test_missing_operator_acceptance_leaves_rto_and_objective_unproven(self):
        receipt = complete_receipt()
        receipt["operatorAcceptedAtUtc"] = None
        evaluated = self.assert_unproven(receipt, "operatorAcceptedAtUtc")
        self.assertIsNone(evaluated["measuredRtoSeconds"])
        self.assertEqual(evaluated["rtoStatus"], "unproven")

    def test_missing_acceptance_does_not_erase_known_rpo_breach(self):
        receipt = complete_receipt()
        receipt["lastVerifiedRecoverablePointAtUtc"] = "2024-12-31T00:00:00Z"
        receipt["operatorAcceptedAtUtc"] = None
        evaluated = self.assert_unproven(receipt)
        self.assertEqual(evaluated["rpoStatus"], "breached")
        self.assertTrue(any("exceeds RPO" in error for error in evaluated["objectiveErrors"]))

    def test_missing_evidence_references_and_acceptor_each_fail_closed(self):
        for field in ("recoverablePointEvidence", "reconciliationEvidence", "operatorAcceptedBy", "operatorAcceptanceEvidence", "sourceCommit", "backupId"):
            with self.subTest(field=field):
                receipt = complete_receipt()
                receipt[field] = "  "
                self.assert_unproven(receipt, field)

    def test_every_required_timestamp_is_required(self):
        for field in (key for key in complete_receipt() if key.endswith("AtUtc")):
            with self.subTest(field=field):
                receipt = complete_receipt()
                del receipt[field]
                self.assert_unproven(receipt, field)

    def test_invalid_non_utc_or_non_string_timestamps_fail_closed(self):
        for value in ("invalid", "2025-02-30T00:00:00Z", "2025-01-01T00:00:00", "2025-01-01T00:00:00-07:00", "2025-01-01T00:00:00+01:00", "01/01/2025", 1735689600, True):
            with self.subTest(value=value):
                receipt = complete_receipt()
                receipt["lastVerifiedRecoverablePointAtUtc"] = value
                self.assert_unproven(receipt, "lastVerifiedRecoverablePointAtUtc")

    def test_utc_offset_and_fractional_seconds_remain_valid_strings(self):
        receipt = complete_receipt()
        receipt["lastVerifiedRecoverablePointAtUtc"] = "2025-01-01T00:00:00.1234567+00:00"
        result, evaluated = self.validate(receipt)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertAlmostEqual(evaluated["measuredRpoSeconds"], 1799.8765433, places=7)
        self.assertEqual(evaluated["lastVerifiedRecoverablePointAtUtc"], receipt["lastVerifiedRecoverablePointAtUtc"])

    def test_future_milestone_fails_closed(self):
        receipt = complete_receipt()
        receipt["operatorAcceptedAtUtc"] = (datetime.now(timezone.utc) + timedelta(days=1)).isoformat().replace("+00:00", "Z")
        evaluated = self.assert_unproven(receipt, "future")
        self.assertIsNone(evaluated["measuredRtoSeconds"])

    def test_reversed_milestones_fail_closed(self):
        cases = {
            "completedAtUtc": "2025-01-01T00:09:00Z",
            "backupStartedAtUtc": "2025-01-01T00:11:00Z",
            "recoverablePointVerifiedAtUtc": "2025-01-01T00:09:00Z",
            "lastVerifiedRecoverablePointAtUtc": "2025-01-01T00:20:00Z",
            "simulatedLossAtUtc": "2025-01-01T00:09:00Z",
            "lossDeclaredAtUtc": "2025-01-01T00:29:00Z",
            "restoreStartedAtUtc": "2025-01-01T00:30:00Z",
            "restoreCompletedAtUtc": "2025-01-01T00:30:01Z",
            "reconciliationCompletedAtUtc": "2025-01-01T00:30:02Z",
            "operatorAcceptedAtUtc": "2025-01-01T00:34:00Z",
        }
        for field, value in cases.items():
            with self.subTest(field=field):
                receipt = complete_receipt()
                receipt[field] = value
                self.assert_unproven(receipt, "must not be after")

    def test_exact_budgets_pass_without_rounding_objectives(self):
        receipt = complete_receipt()
        receipt.update({
            "simulatedLossAtUtc": "2025-01-01T01:00:00Z",
            "lossDeclaredAtUtc": "2025-01-01T01:00:01Z",
            "restoreStartedAtUtc": "2025-01-01T01:00:02Z",
            "restoreCompletedAtUtc": "2025-01-01T01:00:03Z",
            "completedAtUtc": "2025-01-01T01:00:04Z",
            "reconciliationCompletedAtUtc": "2025-01-01T03:00:00Z",
            "operatorAcceptedAtUtc": "2025-01-01T03:00:01Z",
        })
        result, evaluated = self.validate(receipt)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(evaluated["measuredRpoSeconds"], 3600)
        self.assertEqual(evaluated["measuredRtoSeconds"], 7200)

    def test_fractional_rpo_breach_is_not_rounded_into_budget(self):
        receipt = complete_receipt()
        receipt["lastVerifiedRecoverablePointAtUtc"] = "2024-12-31T23:29:59.9999999Z"
        evaluated = self.assert_breached(receipt, "rpo")
        self.assertGreater(evaluated["measuredRpoSeconds"], 3600)

    def test_fractional_rto_breach_is_not_rounded_into_budget(self):
        receipt = complete_receipt()
        receipt["operatorAcceptedAtUtc"] = "2025-01-01T02:30:01.0000001Z"
        evaluated = self.assert_breached(receipt, "rto")
        self.assertGreater(evaluated["measuredRtoSeconds"], 7200)

    def test_claimed_measures_and_status_are_always_recomputed(self):
        receipt = complete_receipt()
        receipt.update({"objectiveStatus": "proven", "rpoStatus": "proven", "rtoStatus": "proven", "measuredRpoSeconds": 0, "measuredRtoSeconds": 0})
        receipt["lastVerifiedRecoverablePointAtUtc"] = "2024-12-31T00:00:00Z"
        evaluated = self.assert_breached(receipt, "rpo")
        self.assertEqual(evaluated["measuredRpoSeconds"], 88200)
        self.assertEqual(evaluated["measuredRtoSeconds"], 599)

    def test_failed_archive_operations_cannot_prove_objectives(self):
        receipt = complete_receipt()
        receipt["status"] = "failed"
        evaluated = self.assert_unproven(receipt, "Archive operation")
        self.assertEqual(evaluated["rpoStatus"], "unproven")
        self.assertEqual(evaluated["rtoStatus"], "unproven")

    def test_non_string_operation_status_cannot_coerce_to_a_pass(self):
        for status in (True, False, [], ["passed"], None):
            with self.subTest(status=status):
                receipt = complete_receipt()
                receipt["status"] = status
                self.assert_unproven(receipt, "Archive operation")

    def test_archive_mode_cannot_claim_drill_objectives(self):
        for mode in ("Backup", "Restore"):
            with self.subTest(mode=mode):
                receipt = complete_receipt()
                receipt["mode"] = mode
                self.assert_unproven(receipt, "Drill receipt")

    def test_recoverable_point_after_archive_snapshot_is_invalid(self):
        receipt = complete_receipt()
        receipt["lastVerifiedRecoverablePointAtUtc"] = "2025-01-01T00:10:00.0000001Z"
        self.assert_unproven(receipt, "backupStartedAtUtc")

    def test_archive_completion_cannot_precede_restore_completion(self):
        receipt = complete_receipt()
        receipt["completedAtUtc"] = "2025-01-01T00:30:02Z"
        self.assert_unproven(receipt, "restoreCompletedAtUtc")

    def test_retained_backups_can_precede_current_drill_start(self):
        receipt = complete_receipt()
        receipt["startedAtUtc"] = "2025-01-01T00:10:02Z"
        result, evaluated = self.validate(receipt)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(evaluated["objectiveStatus"], "proven")

    def test_archive_durations_must_match_their_own_timestamps(self):
        for field, value in (("backupDurationSeconds", 0), ("restoreDurationSeconds", 7200), ("backupDurationSeconds", -1), ("restoreDurationSeconds", "1"), ("backupDurationSeconds", True)):
            with self.subTest(field=field, value=value):
                receipt = complete_receipt()
                receipt[field] = value
                self.assert_unproven(receipt, field)

    def test_millisecond_rounding_is_allowed_only_for_archive_durations(self):
        receipt = complete_receipt()
        receipt["backupCompletedAtUtc"] = "2025-01-01T00:10:01.0004Z"
        result, evaluated = self.validate(receipt)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(evaluated["backupDurationSeconds"], 1)

    def test_receipt_cannot_inflate_independent_policy_budgets(self):
        receipt = complete_receipt()
        receipt["maximumRpoSeconds"] = 86400 * 7
        receipt["maximumRtoSeconds"] = 86400 * 7
        receipt["lastVerifiedRecoverablePointAtUtc"] = "2024-12-31T00:00:00Z"
        evaluated = self.assert_breached(receipt, "rpo")
        self.assertEqual(evaluated["effectiveMaximumRpoSeconds"], 3600)
        self.assertEqual(evaluated["effectiveMaximumRtoSeconds"], 7200)

    def test_invalid_budget_fails_closed(self):
        for value in (None, 0, -1, "3600", True):
            with self.subTest(value=value):
                receipt = complete_receipt()
                receipt["maximumRpoSeconds"] = value
                self.assert_unproven(receipt, "maximumRpoSeconds")

    def test_stricter_cli_policy_is_enforced(self):
        result, evaluated = self.validate(complete_receipt(), extra=("-MaximumRpoSeconds", "1799", "-MaximumRtoSeconds", "598"))
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(evaluated["rpoStatus"], "breached")
        self.assertEqual(evaluated["rtoStatus"], "breached")

    def test_stricter_receipt_budget_is_enforced(self):
        receipt = complete_receipt()
        receipt["maximumRpoSeconds"] = 1799
        self.assert_breached(receipt, "rpo")

    def test_archive_only_legacy_receipt_is_not_objective_proof(self):
        self.assert_unproven({"schemaVersion": 1, "mode": "Drill", "status": "passed", "measuredRpoSeconds": 1, "measuredRtoSeconds": 1}, "legacy")

    def test_unauthenticated_schema_two_receipt_cannot_prove_objectives(self):
        for value in (None, False, "true", 1):
            with self.subTest(value=value):
                receipt = complete_receipt()
                receipt["manifestAuthenticated"] = value
                self.assert_unproven(receipt, "manifestAuthenticated")

    def test_manifest_digest_and_drill_commit_are_required(self):
        for field, value in (("manifestSha256", None), ("manifestSha256", "c" * 63),
                             ("manifestSha256", "x" * 64), ("drillSourceCommit", "")):
            with self.subTest(field=field, value=value):
                receipt = complete_receipt()
                receipt[field] = value
                self.assert_unproven(receipt, field)

    def test_both_commit_identifiers_require_full_hex_strings(self):
        invalid = (
            None, True, 123, [], ["a" * 40], {"commit": "a" * 40},
            "", "  ", "abcdef0", "not-a-commit", "g" * 40, "g" * 64,
            "a" * 39, "a" * 41, "a" * 63, "a" * 65,
            " " + "a" * 40, "a" * 40 + " ", "a" * 40 + "\n", "a" * 64 + "\n",
        )
        for field in ("sourceCommit", "drillSourceCommit"):
            for value in invalid:
                with self.subTest(field=field, value=value):
                    receipt = complete_receipt()
                    receipt[field] = value
                    evaluated = self.assert_unproven(receipt, field)
                    self.assertEqual(evaluated["rpoStatus"], "unproven")
                    self.assertEqual(evaluated["rtoStatus"], "unproven")

    def test_matching_completion_cannot_prove_malformed_commit_identifiers(self):
        completion_fields = (
            "reconciliationCompletedAtUtc", "reconciliationEvidence", "operatorAcceptedAtUtc",
            "operatorAcceptedBy", "operatorAcceptanceEvidence",
        )
        for field in ("sourceCommit", "drillSourceCommit"):
            for value in ("abcdef0", "not-a-commit", "g" * 40, "a" * 41, "a" * 40 + " ",
                          "a" * 40 + "\n", "a" * 64 + "\n"):
                with self.subTest(field=field, value=value):
                    receipt = complete_receipt()
                    receipt[field] = value
                    completion = {key: receipt[key] for key in (
                        "sourceCommit", "drillSourceCommit", "manifestSha256", "backupId",
                        "simulatedLossAtUtc", "lossDeclaredAtUtc",
                    ) + completion_fields}
                    for key in completion_fields:
                        receipt[key] = None
                    result, evaluated = self.validate(receipt, completion)
                    self.assertNotEqual(result.returncode, 0, result.stderr)
                    self.assertIsNotNone(evaluated, result.stderr)
                    self.assertEqual(evaluated["objectiveStatus"], "unproven")
                    self.assertEqual(evaluated["rpoStatus"], "unproven")
                    self.assertEqual(evaluated["rtoStatus"], "unproven")
                    self.assertTrue(any(field in error for error in evaluated["objectiveErrors"]))

    def test_matching_completion_cannot_legalize_non_string_commit_identifiers(self):
        for field in ("sourceCommit", "drillSourceCommit"):
            for value in (None, True, 123, [], ["a" * 40], {"commit": "a" * 40}):
                with self.subTest(field=field, value=value):
                    receipt = complete_receipt()
                    receipt[field] = value
                    completion = {key: receipt[key] for key in (
                        "sourceCommit", "drillSourceCommit", "manifestSha256", "backupId",
                        "simulatedLossAtUtc", "lossDeclaredAtUtc",
                    )}
                    result, evaluated = self.validate(receipt, completion)
                    self.assertNotEqual(result.returncode, 0, result.stderr)
                    self.assertIsNone(evaluated)
                    self.assertIn(field + " must exactly match", result.stderr)

    def test_full_sha1_and_sha256_commit_identifiers_accept_either_hex_case(self):
        for commit in ("a1" * 20, "A1" * 20, "b2" * 32, "B2" * 32):
            with self.subTest(commit=commit):
                receipt = complete_receipt()
                receipt["sourceCommit"] = commit
                receipt["drillSourceCommit"] = commit
                result, evaluated = self.validate(receipt)
                self.assertEqual(result.returncode, 0, result.stderr)
                self.assertEqual(evaluated["objectiveStatus"], "proven")
                self.assertEqual(evaluated["sourceCommit"], commit)
                self.assertEqual(evaluated["drillSourceCommit"], commit)

    def test_completion_cannot_relabel_retained_backup_as_drill_commit(self):
        receipt = complete_receipt()
        completion = {key: receipt[key] for key in ("sourceCommit", "drillSourceCommit", "manifestSha256", "backupId", "simulatedLossAtUtc", "lossDeclaredAtUtc")}
        completion["sourceCommit"] = receipt["drillSourceCommit"]
        result, evaluated = self.validate(receipt, completion)
        self.assertNotEqual(result.returncode, 0)
        self.assertIsNone(evaluated)
        self.assertIn("sourceCommit must exactly match", result.stderr)

    def test_completion_cannot_add_manifest_authentication_to_old_receipt(self):
        receipt = complete_receipt()
        del receipt["manifestAuthenticated"]
        completion = {key: receipt[key] for key in ("sourceCommit", "drillSourceCommit", "manifestSha256", "backupId", "simulatedLossAtUtc", "lossDeclaredAtUtc")}
        completion["manifestAuthenticated"] = True
        result, evaluated = self.validate(receipt, completion)
        self.assertNotEqual(result.returncode, 0)
        self.assertIsNone(evaluated)
        self.assertIn("immutable receipt field: manifestAuthenticated", result.stderr)

    def test_bound_completion_finishes_evidence_without_rewriting_source(self):
        receipt = complete_receipt()
        fields = ("reconciliationCompletedAtUtc", "reconciliationEvidence", "operatorAcceptedAtUtc", "operatorAcceptedBy", "operatorAcceptanceEvidence")
        completion = {field: receipt[field] for field in ("sourceCommit", "drillSourceCommit", "manifestSha256", "backupId", "simulatedLossAtUtc", "lossDeclaredAtUtc") + fields}
        completion["schemaVersion"] = 2
        for field in fields:
            receipt[field] = None
        output = self.directory / "evaluated.json"
        result, evaluated = self.validate(receipt, completion, output=output)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(evaluated["objectiveStatus"], "proven")
        self.assertEqual(json.loads(output.read_text(encoding="utf-8")), evaluated)

    def test_completion_cannot_attach_to_different_commit_backup_or_loss(self):
        for field in ("sourceCommit", "drillSourceCommit", "manifestSha256", "backupId", "simulatedLossAtUtc", "lossDeclaredAtUtc"):
            with self.subTest(field=field):
                receipt = complete_receipt()
                completion = {key: receipt[key] for key in ("sourceCommit", "drillSourceCommit", "manifestSha256", "backupId", "simulatedLossAtUtc", "lossDeclaredAtUtc")}
                completion[field] = "different"
                result, evaluated = self.validate(receipt, completion)
                self.assertNotEqual(result.returncode, 0)
                self.assertIsNone(evaluated)
                self.assertIn(field, result.stderr)

    def test_completion_cannot_replace_immutable_fields_or_existing_acceptance(self):
        for field, value in (("lastVerifiedRecoverablePointAtUtc", "2025-01-01T00:20:00Z"), ("maximumRpoSeconds", 86400), ("operatorAcceptedAtUtc", "2025-01-01T00:39:00Z")):
            with self.subTest(field=field):
                receipt = complete_receipt()
                completion = {key: receipt[key] for key in ("sourceCommit", "drillSourceCommit", "manifestSha256", "backupId", "simulatedLossAtUtc", "lossDeclaredAtUtc")}
                completion[field] = value
                result, evaluated = self.validate(receipt, completion)
                self.assertNotEqual(result.returncode, 0)
                self.assertIsNone(evaluated)
                self.assertIn(field, result.stderr)

    def test_completion_cannot_legalize_non_string_source_evidence(self):
        receipt = complete_receipt()
        completion = {key: receipt[key] for key in ("sourceCommit", "drillSourceCommit", "manifestSha256", "backupId", "simulatedLossAtUtc", "lossDeclaredAtUtc", "reconciliationCompletedAtUtc")}
        receipt["reconciliationCompletedAtUtc"] = [receipt["reconciliationCompletedAtUtc"]]
        result, evaluated = self.validate(receipt, completion)
        self.assertNotEqual(result.returncode, 0)
        self.assertIsNone(evaluated)
        self.assertIn("cannot replace existing reconciliationCompletedAtUtc", result.stderr)

    def test_completion_schema_version_must_be_numeric_two(self):
        for version in (True, "2", 2.1, None):
            with self.subTest(version=version):
                receipt = complete_receipt()
                completion = {key: receipt[key] for key in ("sourceCommit", "drillSourceCommit", "manifestSha256", "backupId", "simulatedLossAtUtc", "lossDeclaredAtUtc")}
                completion["schemaVersion"] = version
                result, evaluated = self.validate(receipt, completion)
                self.assertNotEqual(result.returncode, 0)
                self.assertIsNone(evaluated)
                self.assertIn("schemaVersion", result.stderr)

    def test_incomplete_completion_is_still_unproven(self):
        receipt = complete_receipt()
        receipt["operatorAcceptedAtUtc"] = None
        completion = {key: receipt[key] for key in ("sourceCommit", "drillSourceCommit", "manifestSha256", "backupId", "simulatedLossAtUtc", "lossDeclaredAtUtc")}
        result, evaluated = self.validate(receipt, completion)
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(evaluated["objectiveStatus"], "unproven")

    def test_validator_refuses_source_overwrite_and_existing_output(self):
        for output in (self.directory / "receipt.json", self.directory / "existing.json"):
            with self.subTest(output=output):
                if output.name == "existing.json":
                    output.write_text("existing evidence", encoding="utf-8")
                result, evaluated = self.validate(complete_receipt(), output=output)
                self.assertNotEqual(result.returncode, 0)
                self.assertIsNone(evaluated)

    def test_parser_preserves_strings_and_shared_helper_runs_directly(self):
        receipt = complete_receipt()
        path = self.directory / "direct.json"
        path.write_text(json.dumps(receipt), encoding="utf-8")
        script = self.directory / "evaluate.ps1"
        script.write_text(
            "param($HelperPath, $ReceiptPath)\n"
            ". $HelperPath\n"
            "$receipt = Read-RecoveryJson $ReceiptPath\n"
            "if ($receipt.lastVerifiedRecoverablePointAtUtc -isnot [string]) { throw 'Timestamp was coerced.' }\n"
            "Get-RecoveryObjectiveEvidence -Receipt $receipt | ConvertTo-Json -Depth 8\n",
            encoding="utf-8",
        )
        result = subprocess.run([self.pwsh, "-NoProfile", "-NonInteractive", "-File", str(script), str(HELPER), str(path)], capture_output=True, text=True, timeout=30)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(json.loads(result.stdout)["objectiveStatus"], "proven")

    def test_duplicate_json_properties_are_rejected(self):
        receipt = complete_receipt()
        path = self.directory / "duplicate.json"
        path.write_text(json.dumps(receipt)[:-1] + ', "status": "passed"}', encoding="utf-8")
        result = subprocess.run([self.pwsh, "-NoProfile", "-NonInteractive", "-File", str(VALIDATOR), "-ReceiptPath", str(path)], capture_output=True, text=True, timeout=30)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Duplicate recovery JSON property", result.stderr)


if __name__ == "__main__":
    unittest.main()
