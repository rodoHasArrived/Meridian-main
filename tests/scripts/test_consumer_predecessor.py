from __future__ import annotations

import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[2]


class ConsumerPredecessorTests(unittest.TestCase):
    def setUp(self):
        spec = importlib.util.spec_from_file_location(
            "consumer_predecessor", ROOT / "build/scripts/ci/resolve-consumer-predecessor.py")
        self.resolver = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.resolver)
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.args = self.resolver.parse_args([
            "--repository", "example/Meridian", "--current-tag", "v3.0.0",
            "--current-commit", "current", "--output-dir", str(self.root / "prior"),
            "--evidence", str(self.root / "predecessor.json"),
        ])
        self.environment = {"GITHUB_SHA": "current", "GITHUB_RUN_ID": "42", "GITHUB_RUN_ATTEMPT": "2"}
        self.package_bytes = b"exact prior signed EXE"
        self.package_digest = hashlib.sha256(self.package_bytes).hexdigest()
        self.checksum_bytes = f"{self.package_digest}  Meridian-Setup.exe\n".encode()
        self.releases = []
        self.asset_pages = {}
        self.commits = {}
        self.asset_content = {}
        self.queries = []

    def release(self, tag="v2.0.0", *, consumer=True, draft=False, prerelease=False,
                commit=None, checksum=True, name=None):
        identifier = len(self.releases) + 1
        assets = []
        if consumer:
            assets.append(dict(id=identifier * 10, name="Meridian-Setup.exe", size=len(self.package_bytes)))
            self.asset_content[identifier * 10] = self.package_bytes
            if checksum:
                assets.append(dict(id=identifier * 10 + 1, name=self.resolver.CHECKSUM_NAME,
                                   size=len(self.checksum_bytes)))
                self.asset_content[identifier * 10 + 1] = self.checksum_bytes
        else:
            assets.append(dict(id=identifier * 10, name="Meridian.Desktop-x64.msix", size=100))
        release = dict(id=identifier, tag_name=tag, draft=draft, prerelease=prerelease,
                       name=name or tag, published_at="2026-09-01T00:00:00Z", assets=assets)
        self.releases.append(release)
        self.asset_pages[identifier] = [assets, []]
        self.commits[tag] = commit or "commit-" + tag
        return release

    def gh_json(self, endpoint):
        self.queries.append(endpoint)
        if "/commits/" in endpoint:
            return {"sha": self.commits[endpoint.rsplit("/", 1)[1]]}
        page = int(endpoint.rsplit("page=", 1)[1])
        if "/assets?" in endpoint:
            identifier = int(endpoint.split("/releases/")[1].split("/")[0])
            pages = self.asset_pages[identifier]
        else:
            pages = [self.releases, []]
        return pages[page - 1] if page <= len(pages) else []

    def download(self, repository, asset, destination):
        self.assertEqual(repository, "example/Meridian")
        destination.write_bytes(self.asset_content[asset["id"]])

    def resolve(self):
        with patch.object(self.resolver, "gh_json", side_effect=self.gh_json), \
                patch.object(self.resolver, "download_asset", side_effect=self.download):
            return self.resolver.resolve(self.args, self.environment)

    def test_msix_history_records_explicit_consumer_first_release(self):
        self.release(consumer=False)
        evidence = self.resolve()
        self.assertEqual(evidence["schemaVersion"], 1)
        self.assertEqual(evidence["project"], "consumer-setup")
        self.assertEqual(evidence["runtime"], "win-x64")
        self.assertEqual((evidence["sourceCommit"], evidence["workflowRunId"], evidence["workflowRunAttempt"]),
                         ("current", "42", "2"))
        self.assertTrue(evidence["firstRelease"])
        self.assertIsNone(evidence["priorReleaseTag"])
        self.assertIsNone(evidence["priorPackage"])
        self.assertEqual(evidence["eligibleReleaseCount"], 1)
        self.assertEqual(evidence["consumerReleaseCount"], 0)
        self.assertIn("none published Meridian-Setup.exe", evidence["reason"])

    def test_empty_history_records_explicit_first_release(self):
        evidence = self.resolve()
        self.assertTrue(evidence["firstRelease"])
        self.assertEqual(evidence["eligibleReleaseCount"], 0)
        self.assertIn("No published production predecessor", evidence["reason"])

    def test_latest_consumer_is_selected_independently_of_latest_msix(self):
        self.release("v2.5.0", consumer=False)
        self.release("v1.0.0")
        self.release("v2.0.0")
        evidence = self.resolve()
        self.assertFalse(evidence["firstRelease"])
        self.assertEqual(evidence["priorReleaseTag"], "v2.0.0")
        self.assertEqual(evidence["eligibleReleaseCount"], 3)
        self.assertEqual(evidence["consumerReleaseCount"], 2)
        self.assertEqual(evidence["priorPackage"]["sha256"], self.package_digest)
        self.assertEqual(Path(evidence["priorPackage"]["path"]).read_bytes(), self.package_bytes)

    def test_production_rc_is_eligible_but_evaluation_and_draft_are_not(self):
        self.release("v2.0.0-rc.1", prerelease=True)
        self.release("eval-v2.5.0", prerelease=True)
        self.release("v2.6.0", draft=True)
        evidence = self.resolve()
        self.assertEqual(evidence["priorReleaseTag"], "v2.0.0-rc.1")
        self.assertEqual(evidence["eligibleReleaseCount"], 1)

    def test_current_and_newer_releases_cannot_be_predecessors(self):
        self.release("v3.0.0")
        self.release("v4.0.0")
        self.release("v2.0.0")
        evidence = self.resolve()
        self.assertEqual(evidence["priorReleaseTag"], "v2.0.0")
        self.assertEqual(evidence["eligibleReleaseCount"], 1)

    def test_older_tag_at_current_commit_is_still_an_available_predecessor(self):
        self.release("v2.0.0", commit="current")
        evidence = self.resolve()
        self.assertFalse(evidence["firstRelease"])
        self.assertEqual(evidence["priorReleaseTag"], "v2.0.0")

    def test_mutable_names_cannot_hide_a_v_tagged_consumer_predecessor(self):
        release = self.release("v2.0.0", name="Meridian self-signed evaluation")
        release["assets"].append(dict(id=100, name="desktop-evaluation-win-x64-SHA256SUMS", size=100))
        evidence = self.resolve()
        self.assertFalse(evidence["firstRelease"])
        self.assertEqual(evidence["priorReleaseTag"], "v2.0.0")

    def test_override_requires_a_genuine_published_consumer_predecessor(self):
        self.release("v1.0.0")
        self.release("v2.0.0")
        self.args.prior_release_tag = "v2.0.0"
        self.assertEqual(self.resolve()["priorReleaseTag"], "v2.0.0")
        for tag in ("v3.0.0", "v4.0.0", "eval-v1.0.0", "v0.5.0"):
            with self.subTest(tag=tag):
                self.args.prior_release_tag = tag
                with self.assertRaises(ValueError):
                    self.resolve()

        self.args.prior_release_tag = "v1.0.0"
        with self.assertRaisesRegex(ValueError, "latest eligible consumer release"):
            self.resolve()

    def test_override_cannot_force_first_release_or_select_msix_only_or_draft(self):
        for attributes in (dict(consumer=False), dict(draft=True)):
            with self.subTest(attributes=attributes):
                self.releases.clear()
                self.release("v2.0.0", **attributes)
                self.args.prior_release_tag = "v2.0.0"
                with self.assertRaises(ValueError):
                    self.resolve()

    def test_query_failure_cannot_be_reported_as_first_release(self):
        failure = subprocess.CalledProcessError(1, ["gh", "api"])
        with patch.object(self.resolver, "gh_json", side_effect=failure):
            with self.assertRaises(subprocess.CalledProcessError):
                self.resolver.resolve(self.args, self.environment)

    def test_asset_query_failure_cannot_be_reported_as_first_release(self):
        self.release(consumer=False)
        original = self.gh_json

        def query(endpoint):
            if "/assets?" in endpoint:
                raise subprocess.CalledProcessError(1, ["gh", "api"])
            return original(endpoint)

        with patch.object(self.resolver, "gh_json", side_effect=query):
            with self.assertRaises(subprocess.CalledProcessError):
                self.resolver.resolve(self.args, self.environment)

    def test_release_and_asset_pagination_are_complete(self):
        older = self.release("v1.0.0", consumer=False)
        later = self.release("v2.0.0")
        self.asset_pages[later["id"]] = [[later["assets"][0]], [later["assets"][1]], []]
        original = self.gh_json

        def query(endpoint):
            if "/releases?" in endpoint:
                self.queries.append(endpoint)
                page = int(endpoint.rsplit("page=", 1)[1])
                return {1: [older], 2: [later], 3: []}[page]
            return original(endpoint)

        with patch.object(self.resolver, "gh_json", side_effect=query), \
                patch.object(self.resolver, "download_asset", side_effect=self.download):
            evidence = self.resolver.resolve(self.args, self.environment)
        self.assertEqual(evidence["priorReleaseTag"], "v2.0.0")
        self.assertTrue(any("releases?per_page=100&page=3" in query for query in self.queries))
        self.assertTrue(any("/assets?per_page=100&page=3" in query for query in self.queries))

    def test_missing_checksum_and_duplicate_exe_are_fatal(self):
        release = self.release(checksum=False)
        with self.assertRaisesRegex(ValueError, "exactly one.*SHA256SUMS"):
            self.resolve()
        release["assets"].append(dict(release["assets"][0]))
        with self.assertRaisesRegex(ValueError, "exactly one Meridian-Setup.exe"):
            self.resolve()

    def test_corrupt_or_ambiguous_checksum_is_fatal(self):
        release = self.release()
        checksum_id = release["assets"][1]["id"]
        for content in (b"no checksum", f"{'0' * 64}  Meridian-Setup.exe\n".encode(),
                        self.checksum_bytes * 2):
            with self.subTest(content=content):
                self.asset_content[checksum_id] = content
                with self.assertRaises(ValueError):
                    self.resolve()
                self.assertFalse((self.root / "prior/Meridian-Setup.exe").exists())

    def test_unbound_evidence_is_rejected(self):
        for field in ("GITHUB_RUN_ID", "GITHUB_RUN_ATTEMPT"):
            with self.subTest(field=field):
                environment = dict(self.environment, **{field: ""})
                with self.assertRaisesRegex(ValueError, "required"):
                    self.resolver.resolve(self.args, environment)
        with self.assertRaisesRegex(ValueError, "differs from GITHUB_SHA"):
            self.resolver.resolve(self.args, dict(self.environment, GITHUB_SHA="wrong"))

    def test_failed_retry_removes_stale_success_evidence_and_package(self):
        self.release()
        evidence = self.resolve()
        path = Path(self.args.evidence)
        path.write_text(json.dumps(evidence))
        with patch.dict(self.resolver.os.environ, self.environment), \
                patch.object(self.resolver, "gh_json", side_effect=ValueError("incomplete response")):
            result = self.resolver.main([
                "--repository", self.args.repository, "--current-tag", self.args.current_tag,
                "--current-commit", self.args.current_commit, "--output-dir", self.args.output_dir,
                "--evidence", self.args.evidence,
            ])
        self.assertEqual(result, 1)
        self.assertFalse(path.exists())
        self.assertFalse((self.root / "prior/Meridian-Setup.exe").exists())

    def test_cli_writes_bound_evidence_and_github_outputs(self):
        self.release()
        github_output = self.root / "github-output"
        with patch.dict(self.resolver.os.environ, dict(self.environment, GITHUB_OUTPUT=str(github_output))), \
                patch.object(self.resolver, "gh_json", side_effect=self.gh_json), \
                patch.object(self.resolver, "download_asset", side_effect=self.download):
            result = self.resolver.main([
                "--repository", self.args.repository, "--current-tag", self.args.current_tag,
                "--current-commit", self.args.current_commit, "--output-dir", self.args.output_dir,
                "--evidence", self.args.evidence,
            ])
        self.assertEqual(result, 0)
        evidence = json.loads(Path(self.args.evidence).read_text())
        self.assertEqual(evidence["workflowRunAttempt"], "2")
        output = github_output.read_text()
        self.assertIn("first_release=false\n", output)
        self.assertIn("prior_tag=v2.0.0\n", output)
        self.assertIn(f"prior_path={evidence['priorPackage']['path']}\n", output)

    def test_incomplete_actual_download_is_rejected(self):
        destination = self.root / "download.exe"
        asset = dict(id=1, name="Meridian-Setup.exe", size=100)

        def run(command, stdout, check):
            self.assertIn("Accept: application/octet-stream", command)
            self.assertTrue(check)
            stdout.write(b"truncated")

        with patch.object(self.resolver.subprocess, "run", side_effect=run):
            with self.assertRaisesRegex(ValueError, "Incomplete download"):
                self.resolver.download_asset("example/Meridian", asset, destination)

    def test_malformed_pages_are_fatal(self):
        for page in ({"message": "rate limit"}, [None]):
            with self.subTest(page=page), patch.object(self.resolver, "gh_json", return_value=page):
                with self.assertRaisesRegex(ValueError, "malformed"):
                    self.resolver.resolve(self.args, self.environment)


if __name__ == "__main__":
    unittest.main()
