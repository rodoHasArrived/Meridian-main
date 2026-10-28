import copy
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("promotion", ROOT / "build/scripts/ci/verify-release-promotion.py")
promotion = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(promotion)


class EvidenceApi:
    def __init__(self):
        self.run = dict(id=42, run_attempt=2, run_number=7, head_sha="abc",
                        path=".github/workflows/desktop-installer-packaging.yml")
        self.rows = []
        self.archives = {}
        for i, name in enumerate(sorted(promotion.required_evidence("42", "7", "2")), 1):
            raw = io.BytesIO()
            with zipfile.ZipFile(raw, "w") as archive:
                archive.writestr("report.json", json.dumps({"report": name}))
            content = raw.getvalue()
            self.archives[i] = content
            self.rows.append(dict(id=i, name=name, expired=False, size_in_bytes=len(content),
                                  digest="sha256:" + hashlib.sha256(content).hexdigest(),
                                  workflow_run=dict(id=42, head_sha="abc")))

    def json(self, endpoint):
        if "attempts" in endpoint:
            return copy.deepcopy(self.run)
        return dict(total_count=len(self.rows), artifacts=copy.deepcopy(self.rows))

    def download(self, artifact, destination):
        destination.write_bytes(self.archives[artifact["id"]])


class ReleaseAuditEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.output = Path(self.tmp.name) / promotion.AUDIT_BUNDLE
        self.api = EvidenceApi()
        self.needs = {name: dict(result="success") for name in promotion.GATES}

    def collect(self):
        return promotion.collect_upstream_evidence(
            self.output, repository="test/repo", commit="abc", run_id="42",
            number="7", attempt="2", needs=self.needs, api=self.api)

    def test_complete_original_archives_survive_in_digest_bound_bundle(self):
        rows = self.collect()
        with zipfile.ZipFile(self.output) as archive:
            manifest = json.loads(archive.read("manifest.json"))
            self.assertEqual(manifest["commitSha"], "abc")
            self.assertEqual(manifest["runAttempt"], "2")
            self.assertEqual(manifest["validations"], self.needs)
            self.assertEqual(len(rows), len(self.api.rows))
            self.assertEqual(len(archive.namelist()), len(rows) + 1)
            for row in rows:
                original = self.api.archives[row["artifactId"]]
                retained = archive.read(f'artifacts/{row["name"]}.zip')
                self.assertEqual(retained, original)
                self.assertEqual(hashlib.sha256(retained).hexdigest(), row["sha256"])
        first_digest = promotion.digest(self.output)
        self.collect()
        self.assertEqual(promotion.digest(self.output), first_digest)

    def test_missing_or_previous_attempt_reports_refuse_bundle(self):
        original = copy.deepcopy(self.api.rows)
        self.api.rows.pop()
        with self.assertRaisesRegex(ValueError, "Missing required"):
            self.collect()
        self.api.rows = original
        self.api.rows[0]["name"] = self.api.rows[0]["name"].removesuffix("-2") + "-1"
        with self.assertRaisesRegex(ValueError, "Missing required"):
            self.collect()

    def test_wrong_run_commit_attempt_or_workflow_refuses_before_download(self):
        original = copy.deepcopy(self.api.run)
        for key, wrong in (("id", 43), ("run_attempt", 1), ("run_number", 8),
                           ("head_sha", "wrong"), ("path", ".github/workflows/evil.yml")):
            with self.subTest(key=key):
                self.api.run = original | {key: wrong}
                with self.assertRaisesRegex(ValueError, "coordinator attempt"):
                    self.collect()
        self.assertFalse(self.output.exists())

    def test_unbound_expired_digestless_artifacts_refuse_bundle(self):
        original = copy.deepcopy(self.api.rows[0])
        for changes in ({"expired": True}, {"digest": None},
                        {"workflow_run": {"id": 43, "head_sha": "abc"}},
                        {"workflow_run": {"id": 42, "head_sha": "wrong"}}):
            with self.subTest(changes=changes):
                self.api.rows[0] = original | changes
                with self.assertRaisesRegex(ValueError, "Unbound"):
                    self.collect()

    def test_corrupt_or_size_mismatched_archive_refuses_bundle(self):
        original = self.api.archives[1]
        self.api.archives[1] = original + b"changed"
        with self.assertRaisesRegex(ValueError, "digest differs"):
            self.collect()
        self.api.archives[1] = original
        self.api.rows[0]["size_in_bytes"] += 1
        with self.assertRaisesRegex(ValueError, "digest differs"):
            self.collect()

    def test_duplicate_or_oversized_inventory_refuses_before_download(self):
        self.api.rows.append(copy.deepcopy(self.api.rows[0]))
        with self.assertRaisesRegex(ValueError, "Duplicate"):
            self.collect()
        self.api.rows.pop()
        self.api.rows[0]["size_in_bytes"] = promotion.MAX_EVIDENCE_BYTES
        with self.assertRaisesRegex(ValueError, "2 GiB"):
            self.collect()
        self.assertFalse(self.output.exists())

    def test_required_security_evidence_includes_reports_and_attestation_bundles(self):
        names = promotion.required_evidence("42", "7", "2")
        for name in ("codeql-csharp-42-2", "codeql-javascript-typescript-42-2", "commit-secret-scan-42-2",
                     "release-attestation-desktop-installer-win-x64-42-2",
                     "release-attestation-desktop-installer-win-arm64-42-2",
                     "release-attestation-consumer-setup-win-x64-42-2",
                     "production-certification-7-2", "production-recovery-drill-42-2"):
            self.assertIn(name, names)


if __name__ == "__main__":
    unittest.main()
