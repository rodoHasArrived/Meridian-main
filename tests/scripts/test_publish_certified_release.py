import copy
import hashlib
import importlib.util
import json
import io
import os
from pathlib import Path
import tempfile
import shutil
import unittest
from unittest.mock import patch
import zipfile

from tests.scripts.test_release_audit_evidence import EvidenceApi, promotion

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("publisher", ROOT / "build/scripts/ci/publish-certified-release.py")
publisher = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(publisher)


class FakeGitHub:
    def __init__(self):
        self.tag = "abc"
        self.current = None
        self.remote = {}
        self.uploaded = []
        self.created = 0
        self.published = 0
        self.attestations = []
        self.interrupt_after = None
        self.corrupt_download = None
        self.reject_attestation = False
        self.after_upload = None

    def tag_commit(self, tag):
        return self.tag

    def release(self, tag):
        return copy.deepcopy(self.current)

    def create(self, tag, commit):
        self.created += 1
        self.current = dict(id=42, tag_name=tag, target_commitish=commit, draft=True, prerelease="-" in tag)
        return self.release(tag)

    def assets(self, release_id):
        return [dict(id=i, name=name, size=len(data), state="uploaded")
                for i, (name, data) in enumerate(sorted(self.remote.items()), 1)]

    def upload(self, tag, path):
        if self.interrupt_after == len(self.uploaded):
            raise RuntimeError("simulated interrupted upload")
        if path.name in self.remote:
            raise ValueError("overwrite forbidden")
        self.remote[path.name] = path.read_bytes()
        self.uploaded.append(path.name)
        if self.after_upload:
            self.after_upload()

    def download(self, row, path):
        data = self.remote[row["name"]]
        path.write_bytes(b"corruption" if row["name"] == self.corrupt_download else data)

    def verify_attestation(self, path, commit):
        if self.reject_attestation:
            raise ValueError("wrong attestation signer or source commit")
        self.attestations.append((path.name, commit))

    def publish(self, release_id):
        self.published += 1
        self.current["draft"] = False
        return self.release(self.current["tag_name"])


class CertifiedPublicationTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        (self.root / "Meridian-Setup.exe").write_bytes(b"certified executable")
        needs = {name: {"result": "success"} for name in publisher.GATES}
        upstream = promotion.collect_upstream_evidence(
            self.root / publisher.BUNDLE_FILE, repository="test/repo", commit="abc",
            run_id="42", number="7", attempt="2", needs=needs, api=EvidenceApi())
        self.gate = dict(
            schemaVersion=2, repository="test/repo", commitSha="abc", runId="42", runAttempt="2",
            validations=needs, upstreamEvidence=upstream,
            assets={p.name: publisher.digest(p) for p in self.root.iterdir()},
        )
        self.write_gate()
        self.api = FakeGitHub()

    def write_gate(self):
        (self.root / publisher.GATE_FILE).write_text(json.dumps(self.gate))

    def run_publish(self, **kwargs):
        options = dict(repository="test/repo", commit="abc", event="push", ref="refs/tags/v1.2.3")
        return publisher.publish(self.root, self.api, **(options | kwargs))

    def test_verifies_downloaded_bytes_and_attestations_then_publishes(self):
        self.assertEqual(self.run_publish(), "published")
        self.assertEqual(self.api.created, 1)
        self.assertEqual(self.api.published, 1)
        self.assertEqual(set(self.api.remote), {p.name for p in self.root.iterdir()})
        self.assertEqual(self.api.attestations.count(("Meridian-Setup.exe", "abc")), 2)

    def test_interrupted_upload_resumes_without_overwrite(self):
        self.api.interrupt_after = 1
        with self.assertRaisesRegex(RuntimeError, "interrupted"):
            self.run_publish()
        self.assertTrue(self.api.current["draft"])
        self.assertEqual(self.api.published, 0)
        before = dict(self.api.remote)
        self.api.interrupt_after = None
        self.assertEqual(self.run_publish(), "published")
        self.assertEqual(self.api.created, 1)
        self.assertEqual(len(self.api.uploaded), len(set(self.api.uploaded)))
        self.assertTrue(all(self.api.remote[name] == data for name, data in before.items()))

    def test_repeated_request_verifies_published_bytes_without_writing(self):
        self.run_publish()
        uploaded = list(self.api.uploaded)
        self.assertEqual(self.run_publish(), "already-published")
        self.assertEqual(self.api.uploaded, uploaded)
        self.assertEqual(self.api.published, 1)

    def test_existing_corrupt_asset_refuses_before_any_upload(self):
        self.api.create("v1.2.3", "abc")
        self.api.remote["Meridian-Setup.exe"] = b"other release"
        with self.assertRaisesRegex(ValueError, "conflicts"):
            self.run_publish()
        self.assertFalse(self.api.uploaded)
        self.assertEqual(self.api.published, 0)

    def test_corrupt_download_blocks_visibility(self):
        self.api.corrupt_download = "Meridian-Setup.exe"
        with self.assertRaisesRegex(ValueError, "conflicts"):
            self.run_publish()
        self.assertTrue(self.api.current["draft"])
        self.assertEqual(self.api.published, 0)

    def test_unexpected_remote_asset_is_never_deleted_or_ignored(self):
        self.api.create("v1.2.3", "abc")
        self.api.remote["unreviewed.txt"] = b"unexpected"
        with self.assertRaisesRegex(ValueError, "unexpected"):
            self.run_publish()
        self.assertIn("unreviewed.txt", self.api.remote)
        self.assertFalse(self.api.uploaded)

    def test_missing_published_asset_cannot_be_filled_in_place(self):
        self.run_publish()
        self.api.remote.pop(publisher.BUNDLE_FILE)
        uploaded = list(self.api.uploaded)
        with self.assertRaisesRegex(ValueError, "missing"):
            self.run_publish()
        self.assertEqual(self.api.uploaded, uploaded)

    def test_dropped_upload_cannot_publish(self):
        self.api.upload = lambda tag, path: None
        with self.assertRaisesRegex(ValueError, "missing"):
            self.run_publish()
        self.assertEqual(self.api.published, 0)

    def test_wrong_or_moving_tag_blocks_publication(self):
        self.api.tag = "different"
        with self.assertRaisesRegex(ValueError, "source commit"):
            self.run_publish()
        self.assertEqual(self.api.created, 0)
        self.api.tag = "abc"
        self.api.after_upload = lambda: setattr(self.api, "tag", "moved")
        with self.assertRaisesRegex(ValueError, "moved"):
            self.run_publish()
        self.assertEqual(self.api.published, 0)

    def test_draft_for_wrong_commit_or_channel_is_refused(self):
        for changes in ({"target_commitish": "main"}, {"prerelease": True}, {"tag_name": "v9.0.0"}):
            with self.subTest(changes=changes):
                self.api.create("v1.2.3", "abc")
                self.api.current.update(changes)
                with self.assertRaisesRegex(ValueError, "identity"):
                    self.run_publish()
        self.assertFalse(self.api.uploaded)

    def test_rehearsal_and_nonproduction_events_cannot_create_release(self):
        for event, ref in (("workflow_dispatch", "refs/tags/v1.2.3"), ("schedule", "refs/heads/main"),
                           ("push", "refs/heads/main"), ("push", "refs/tags/eval-v1.2.3")):
            with self.subTest(event=event, ref=ref), self.assertRaisesRegex(ValueError, "tag push"):
                self.run_publish(event=event, ref=ref)
        self.assertEqual(self.api.created, 0)

    def test_changed_local_inventory_or_missing_bundle_refuses_before_write(self):
        (self.root / "unexpected.txt").write_text("extra")
        with self.assertRaisesRegex(ValueError, "inventory"):
            self.run_publish()
        (self.root / "unexpected.txt").unlink()
        (self.root / "Meridian-Setup.exe").write_text("changed")
        with self.assertRaisesRegex(ValueError, "digest changed"):
            self.run_publish()
        self.assertEqual(self.api.created, 0)

    def test_failed_gate_wrong_provenance_or_attestation_refuses_before_write(self):
        original = copy.deepcopy(self.gate)
        for changes in ({"repository": "other/repo"}, {"commitSha": "other"}, {"schemaVersion": 1},
                        {"validations": {}}, {"runAttempt": "missing"}):
            self.gate = original | changes
            self.write_gate()
            with self.assertRaisesRegex(ValueError, "provenance"):
                self.run_publish()
        self.gate = original
        self.write_gate()
        self.api.reject_attestation = True
        with self.assertRaisesRegex(ValueError, "attestation"):
            self.run_publish()
        self.assertEqual(self.api.created, 0)

    def test_rebuilt_attempt_cannot_replace_original_certified_release(self):
        self.run_publish()
        # A newly generated, valid gate changes the evidence bytes even if the
        # package happens to be reproducible. It must never overwrite a release.
        self.gate["runUrl"] = "new evidence"
        self.write_gate()
        with self.assertRaisesRegex(ValueError, "conflicts"):
            self.run_publish()
        self.assertEqual(self.api.published, 1)

    def test_empty_incomplete_asset_has_explicit_manual_recovery_without_writes(self):
        self.api.create("v1.2.3", "abc")
        self.api.assets = lambda release_id: [
            dict(id=99, name="Meridian-Setup.exe", size=0, state="starter")]
        with self.assertRaisesRegex(ValueError, "99.*Meridian-Setup.exe.*release owner"):
            self.run_publish()
        self.assertFalse(self.api.uploaded)
        self.assertEqual(self.api.published, 0)

    def archived_release(self, mutate=None):
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, "w") as archive:
            for path in sorted(self.root.iterdir()):
                archive.writestr(path.name, path.read_bytes())
            if mutate:
                mutate(archive)
        return stream.getvalue()

    def resume_api(self, archive):
        class Api:
            def api(inner, endpoint):
                if "attempts" in endpoint:
                    return dict(id=42, run_attempt=2, head_sha="abc", path=publisher.WORKFLOW, event="push")
                return dict(total_count=1, artifacts=[
                    dict(id=7, name="validated-release-42-2", expired=False,
                         digest="sha256:" + hashlib.sha256(archive).hexdigest(),
                         size_in_bytes=len(archive), workflow_run=dict(id=42, head_sha="abc"))])

            def download_artifact(inner, artifact, path):
                path.write_bytes(archive)
        return Api()

    def resume(self, archive, **kwargs):
        destination = self.root.parent / (self.root.name + "-restored")
        self.addCleanup(shutil.rmtree, destination, True)
        options = dict(repository="test/repo", commit="abc", event="push", ref="refs/tags/v1.2.3",
                       run_id="42", attempt="3")
        resumed = publisher.prepare_resume(destination, self.resume_api(archive), **(options | kwargs))
        return resumed, destination

    def test_tag_job_rerun_restores_original_attempt_and_resumes_original_draft(self):
        self.api.interrupt_after = 1
        with self.assertRaises(RuntimeError):
            self.run_publish()
        restored, path = self.resume(self.archived_release())
        self.assertTrue(restored)
        self.assertEqual(json.loads((path / publisher.GATE_FILE).read_text())["runAttempt"], "2")
        self.assertEqual({p.name: p.read_bytes() for p in path.iterdir()},
                         {p.name: p.read_bytes() for p in self.root.iterdir()})
        self.api.interrupt_after = None
        self.assertEqual(publisher.publish(path, self.api, repository="test/repo", commit="abc",
                                          event="push", ref="refs/tags/v1.2.3"), "published")

    def test_resume_rejects_malformed_traversal_symlink_and_bomb_archives(self):
        def link(archive):
            entry = zipfile.ZipInfo("link")
            entry.create_system = 3
            entry.external_attr = (0o120777 << 16)
            archive.writestr(entry, "../outside")
        for mutate in (lambda archive: archive.writestr("../outside", "bad"), link):
            with self.subTest(mutate=mutate), self.assertRaisesRegex(ValueError, "unsafe"):
                self.resume(self.archived_release(mutate))
        with self.assertRaises(zipfile.BadZipFile):
            self.resume(b"not a zip")
        with patch.object(publisher, "MAX_RELEASE_BYTES", 1):
            with self.assertRaisesRegex(ValueError, "oversized"):
                self.resume(self.archived_release())

    def test_resume_rejects_wrong_source_and_other_run(self):
        with self.assertRaisesRegex(ValueError, "provenance"):
            self.resume(self.archived_release(), commit="different")
        self.gate["runId"] = "other-run"
        self.write_gate()
        with self.assertRaises(ValueError):
            self.resume(self.archived_release())

    def test_resume_cannot_skip_missing_original_upstream_evidence(self):
        self.gate["upstreamEvidence"] = []
        self.write_gate()
        with self.assertRaisesRegex(ValueError, "upstream provenance"):
            self.resume(self.archived_release())

    def test_retained_archive_corruption_is_rejected_even_if_outer_digest_is_updated(self):
        bundle = self.root / publisher.BUNDLE_FILE
        with zipfile.ZipFile(bundle) as archive:
            entries = {name: archive.read(name) for name in archive.namelist()}
        first = next(name for name in entries if name.startswith("artifacts/"))
        entries[first] = b"changed upstream report"
        with zipfile.ZipFile(bundle, "w") as archive:
            for name, data in entries.items():
                archive.writestr(name, data)
        self.gate["assets"][publisher.BUNDLE_FILE] = publisher.digest(bundle)
        self.write_gate()
        with self.assertRaisesRegex(ValueError, "archive bytes"):
            self.run_publish()
        self.assertEqual(self.api.created, 0)

    def test_readiness_copy_records_publication_attempt_without_mutating_certified_bytes(self):
        original = (self.root / publisher.GATE_FILE).read_bytes()
        output = self.root.parent / f"{self.root.name}-readiness.json"
        self.addCleanup(output.unlink, missing_ok=True)
        env = dict(GITHUB_REPOSITORY="test/repo", GITHUB_SHA="abc", GITHUB_RUN_ID="42",
                   GITHUB_RUN_ATTEMPT="3", GITHUB_EVENT_NAME="push", GITHUB_REF="refs/tags/v1.2.3")
        with patch.dict(os.environ, env), patch("sys.argv", [
                "publisher", "--root", str(self.root), "--readiness-output", str(output)]):
            publisher.main()
        receipt = json.loads(output.read_text())
        self.assertEqual(receipt["runAttempt"], "2")
        self.assertEqual(receipt["publicationRunAttempt"], "3")
        self.assertEqual(receipt["certifiedGateSha256"], hashlib.sha256(original).hexdigest())
        self.assertEqual((self.root / publisher.GATE_FILE).read_bytes(), original)

    def test_actions_download_rechecks_archive_digest_and_size(self):
        content = self.archived_release()
        api = publisher.GitHub("test/repo")
        artifact = dict(id=7, size_in_bytes=len(content),
                        digest="sha256:" + hashlib.sha256(content).hexdigest())
        target = self.root / "download.zip"
        def download(command, **kwargs):
            self.assertEqual(command[-1], "repos/test/repo/actions/artifacts/7/zip")
            kwargs["stdout"].write(content)
        with patch.object(publisher.subprocess, "run", side_effect=download):
            api.download_artifact(artifact, target)
            for mutation in (dict(digest="sha256:" + "0" * 64), dict(size_in_bytes=len(content) + 1)):
                with self.subTest(mutation=mutation), self.assertRaisesRegex(ValueError, "digest/size"):
                    api.download_artifact(artifact | mutation, target)


if __name__ == "__main__":
    unittest.main()
