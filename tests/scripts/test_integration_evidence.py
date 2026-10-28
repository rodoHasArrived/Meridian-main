"""Exercise same-run integration receipt reuse against actual files and job gates."""
from __future__ import annotations

import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

import yaml


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "build/scripts/ci/integration_evidence.py"
SPEC = importlib.util.spec_from_file_location("integration_evidence", SCRIPT)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)


class IntegrationEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.results = self.root / "results"
        self.results.mkdir()
        self.identity = {
            "commit": "a" * 40, "repository": "owner/repo", "runId": "123", "runAttempt": "2",
            "callerWorkflowRef": "owner/repo/.github/workflows/desktop-installer-packaging.yml@refs/tags/v1.0.0",
        }
        workflow = self.root / MODULE.WORKFLOW
        workflow.parent.mkdir(parents=True)
        workflow.write_text("trusted workflow revision")
        for prefix in MODULE.SUITES:
            suite = self.results / prefix
            suite.mkdir()
            (suite / (prefix + ".trx")).write_text(
                '<TestRun><Results><UnitTestResult testName="LedgerWrites" '
                f'testId="{prefix}-1" outcome="Passed"/></Results></TestRun>')
            coverage = self.results / prefix / "coverage.cobertura.xml"
            coverage.write_text('<coverage line-rate="0.75"/>')
        for name in MODULE.SCHEMA_FILES:
            (self.results / name).write_text("schema evidence\n")
        (self.results / "skip-evidence.json").write_text(json.dumps({
            "certifiable": True, "totals": {"passed": 2, "failed": 0, "skipped": 0, "other": 0},
        }))
        self.name = "meridian-ci-integrations-20-2"
        self.receipt, self.sha256 = MODULE.create(self.results, self.name, self.identity, self.root)

    def verify(self, **kwargs):
        inputs = dict(root=self.results, expected_name=self.name, expected_sha256=self.sha256,
                      expected_identity=self.identity, repository_root=self.root,
                      source_result="success", source_artifact_id="456")
        inputs.update(kwargs)
        return MODULE.verify(**inputs)

    def test_complete_same_commit_run_attempt_is_reusable(self):
        receipt = self.verify()
        self.assertEqual(receipt["totals"]["passed"], 2)
        self.assertEqual(set(receipt["suites"]), set(MODULE.SUITES))
        self.assertIn("database-schema.sql", receipt["files"])
        self.assertEqual(receipt["identity"], self.identity)

    def test_each_identity_boundary_is_mandatory(self):
        for key, value in self.identity.items():
            with self.subTest(key=key):
                changed = dict(self.identity, **{key: value + "changed"})
                with self.assertRaisesRegex(ValueError, "not bound"):
                    self.verify(expected_identity=changed)

    def test_wrong_artifact_name_digest_id_or_failed_producer_is_rejected(self):
        cases = (
            {"expected_name": "production-certification-20-2"},
            {"expected_sha256": "b" * 64},
            {"expected_sha256": ""},
            {"source_artifact_id": ""},
            {"source_artifact_id": "../456"},
        )
        for kwargs in cases:
            with self.subTest(kwargs=kwargs), self.assertRaises(ValueError):
                self.verify(**kwargs)
        for result in ("failure", "cancelled", "skipped", ""):
            with self.subTest(result=result), self.assertRaisesRegex(ValueError, "did not succeed"):
                self.verify(source_result=result)

    def test_changed_or_missing_raw_file_cannot_use_a_valid_receipt(self):
        path = self.results / "database-schema.sql"
        path.write_text("changed schema")
        with self.assertRaisesRegex(ValueError, "bytes changed"):
            self.verify()
        path.unlink()
        with self.assertRaisesRegex(ValueError, "missing or empty"):
            self.verify()

    def test_additional_file_or_suite_change_invalidates_receipt(self):
        extra = self.results / "extra.txt"
        extra.write_text("not in certified inventory")
        with self.assertRaisesRegex(ValueError, "bytes changed"):
            self.verify()
        extra.unlink()
        trx = self.results / "meridian-integrations/meridian-integrations.trx"
        trx.write_text(trx.read_text().replace("LedgerWrites", "DifferentTest"))
        with self.assertRaisesRegex(ValueError, "roster"):
            self.verify()

    def test_current_workflow_must_match_producer_revision(self):
        (self.root / MODULE.WORKFLOW).write_text("other workflow")
        with self.assertRaisesRegex(ValueError, "not bound"):
            self.verify()

    def test_links_are_rejected_even_when_contents_match(self):
        path = self.results / "database-schema.sql"
        external = self.root / "outside.sql"
        external.write_bytes(path.read_bytes())
        path.unlink()
        path.symlink_to(external)
        with self.assertRaisesRegex(ValueError, "link"):
            self.verify()

    def test_missing_failed_skipped_empty_and_unknown_test_results_are_rejected(self):
        path = self.results / "meridian-integrations/meridian-integrations.trx"
        original = path.read_text()
        for outcome in ("Failed", "NotExecuted", "Timeout"):
            with self.subTest(outcome=outcome):
                path.write_text(original.replace("Passed", outcome))
                with self.assertRaises(ValueError):
                    MODULE.create(self.results, self.name, self.identity, self.root)
        path.write_text("<TestRun/>")
        with self.assertRaisesRegex(ValueError, "zero discovered|no passing"):
            MODULE.create(self.results, self.name, self.identity, self.root)
        path.unlink()
        with self.assertRaisesRegex(ValueError, "required TRX suite"):
            MODULE.create(self.results, self.name, self.identity, self.root)

    def test_missing_or_invalid_coverage_rejects_certification(self):
        path = self.results / "meridian-integrations/coverage.cobertura.xml"
        path.write_text('<coverage line-rate="nan"/>')
        with self.assertRaisesRegex(ValueError, "coverage"):
            MODULE.create(self.results, self.name, self.identity, self.root)
        path.unlink()
        with self.assertRaisesRegex(ValueError, "must retain its own coverage"):
            MODULE.create(self.results, self.name, self.identity, self.root)

    def test_duplicate_coverage_from_one_suite_cannot_replace_missing_other_suite(self):
        (self.results / "direct-lending-integrations/coverage.cobertura.xml").unlink()
        duplicate = self.results / "meridian-integrations/extra/coverage.cobertura.xml"
        duplicate.parent.mkdir()
        duplicate.write_text('<coverage line-rate="0.75"/>')
        with self.assertRaisesRegex(ValueError, "direct-lending-integrations must retain"):
            MODULE.create(self.results, self.name, self.identity, self.root)

    def test_stored_summary_cannot_hide_raw_failure_or_change_counts(self):
        (self.results / "skip-evidence.json").write_text('{"certifiable": true, "totals": {"passed": 999}}')
        with self.assertRaisesRegex(ValueError, "summary disagrees"):
            self.verify()

    def test_environment_identity_requires_real_commit_run_and_workflow(self):
        with patch.dict(os.environ, {}, clear=True):
            with self.assertRaisesRegex(ValueError, "identity"):
                MODULE.identity_from_environment()

    def test_cli_reuse_preserves_origin_and_rebinds_retained_artifact(self):
        output = self.root / "output"
        environment = dict(os.environ, GITHUB_SHA=self.identity["commit"],
                           GITHUB_REPOSITORY=self.identity["repository"],
                           GITHUB_RUN_ID="123", GITHUB_RUN_ATTEMPT="2",
                           GITHUB_WORKFLOW_REF=self.identity["callerWorkflowRef"],
                           GITHUB_OUTPUT=str(output), SOURCE_ARTIFACT_ID="456",
                           SOURCE_ARTIFACT_NAME=self.name, SOURCE_RECEIPT_SHA256=self.sha256,
                           SOURCE_RESULT="success")
        destination = "production-certification-20-2"
        result = subprocess.run(
            [sys.executable, str(SCRIPT), "reuse", "--results-dir", str(self.results),
             "--artifact-name", destination, "--repository-root", str(self.root)],
            env=environment, capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        receipt = json.loads((self.results / MODULE.RECEIPT).read_text())
        self.assertEqual(receipt["artifactName"], destination)
        self.assertEqual(receipt["reusedFrom"], {
            "artifactId": "456", "artifactName": self.name, "receiptSha256": self.sha256,
            "receiptPath": f"integration-provenance/{self.sha256}.json",
        })
        upstream = self.results / receipt["reusedFrom"]["receiptPath"]
        self.assertEqual(MODULE.digest(upstream.read_bytes()), self.sha256)
        outputs = dict(line.split("=", 1) for line in output.read_text().splitlines())
        self.verify(expected_name=destination, expected_sha256=outputs["receipt-sha256"])


class IntegrationEvidenceGateTests(unittest.TestCase):
    def test_job_gate_requires_exactly_one_successful_complete_evidence_path(self):
        workflow = yaml.load((ROOT / MODULE.WORKFLOW).read_text(), Loader=yaml.BaseLoader)
        gate = workflow["jobs"]["evidence-gate"]
        self.assertEqual(gate["if"], "always()")
        self.assertEqual(gate["needs"], ["deterministic-integrations", "reuse-integrations"])
        script = gate["steps"][0]["run"]
        outputs = {"artifact-id": "12", "artifact-name": "evidence-12-1", "receipt-sha256": "a" * 64}
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "output"
            for fresh, reuse in (("success", "skipped"), ("skipped", "success"),
                                 ("skipped", "skipped"), ("success", "success"),
                                 ("failure", "skipped"), ("skipped", "failure"),
                                 ("cancelled", "skipped"), ("skipped", "cancelled")):
                for evidence in (outputs, {}):
                    with self.subTest(fresh=fresh, reuse=reuse, outputs=bool(evidence)):
                        result = subprocess.run(
                            ["bash", "-euo", "pipefail", "-c", script], text=True, capture_output=True,
                            env=dict(os.environ, GITHUB_OUTPUT=str(path), FRESH_RESULT=fresh,
                                     REUSE_RESULT=reuse, FRESH_OUTPUTS=json.dumps(evidence),
                                     REUSE_OUTPUTS=json.dumps(evidence)))
                        accepted = (fresh, reuse) in (("success", "skipped"), ("skipped", "success")) and bool(evidence)
                        self.assertEqual(result.returncode == 0, accepted, result.stderr)


if __name__ == "__main__":
    unittest.main()
