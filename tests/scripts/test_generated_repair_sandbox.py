"""The producer must not see the Actions host or export links into it."""
from __future__ import annotations

import base64
import hashlib
import importlib.util
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("sandbox", ROOT / "build/scripts/ci/generated_repair_sandbox.py")
sandbox = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(sandbox)


class SandboxTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(ignore_cleanup_errors=True)
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.raw = self.root / "raw"
        self.raw.mkdir()
        self.output = self.root / "safe"

    def test_container_has_only_disposable_mounts_and_explicit_environment(self):
        with mock.patch.dict(os.environ, {"GITHUB_TOKEN": "secret", "ACTIONS_RUNTIME_TOKEN": "secret"}):
            command = sandbox.container_command(self.root / "work", self.raw, "repair-test", "1" * 40, "2" * 40)
        text = " ".join(command)
        self.assertNotIn("secret", text)
        self.assertNotIn("GITHUB_", text)
        self.assertNotIn("ACTIONS_", text)
        self.assertNotIn("docker.sock", text)
        self.assertIn("--read-only", command)
        self.assertIn("--cap-drop=ALL", command)
        self.assertIn("--security-opt=no-new-privileges", command)
        self.assertIn("--user", command)
        self.assertEqual(command.count("--mount"), 2)

    def test_pinned_inputs_are_validated_before_git(self):
        with mock.patch.object(sandbox.subprocess, "run") as run, self.assertRaises(ValueError):
            sandbox.run(self.root, self.output, "repair-test", "--evil", "2" * 40)
        run.assert_not_called()
        with mock.patch.object(sandbox.subprocess, "run") as run, self.assertRaises(ValueError):
            sandbox.clone_repository(self.root, self.root / "clone", "--evil", "2" * 40)
        run.assert_not_called()

    def make_lfs_repo(self):
        repo = self.root / "source"
        repo.mkdir()
        def git(*args):
            return subprocess.check_output(["git", "-c", "core.hooksPath=/dev/null", *args],
                                           cwd=repo, stderr=subprocess.PIPE)
        git("init", "--initial-branch=main")
        git("config", "user.name", "LFS isolation test")
        git("config", "user.email", "test@example.invalid")
        git("config", "commit.gpgsign", "false")
        git("lfs", "install", "--local", "--skip-repo")
        (repo / ".gitattributes").write_text("*.png filter=lfs diff=lfs merge=lfs -text\n")
        payload = base64.b64decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a2ioAAAAASUVORK5CYII=")
        (repo / "image.png").write_bytes(payload)
        git("add", ".")
        git("commit", "-m", "PNG stored with LFS")
        remote = self.root / "remote.git"
        subprocess.run(["git", "init", "--bare", "--initial-branch=main", str(remote)],
                       check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        git("remote", "add", "origin", str(remote))
        git("push", "origin", "main")
        git("lfs", "push", "--all", "origin", "main")
        return repo, git("rev-parse", "HEAD").decode().strip(), payload

    def test_local_clone_hydrates_lfs_without_sharing_git_or_lfs_objects(self):
        repo, sha, payload = self.make_lfs_repo()
        clone = self.root / "clone"
        sandbox.clone_repository(repo, clone, sha, sha)
        self.assertEqual((clone / "image.png").read_bytes(), payload)
        self.assertFalse((clone / ".git/objects/info/alternates").exists())
        for path in (clone / ".git/objects").rglob("*"):
            if path.is_file():
                self.assertFalse(path.is_symlink())
                self.assertEqual(path.stat().st_nlink, 1, path)
        oid = hashlib.sha256(payload).hexdigest()
        relative = Path(".git/lfs/objects") / oid[:2] / oid[2:4] / oid
        source_object, cloned_object = repo / relative, clone / relative
        self.assertEqual(cloned_object.read_bytes(), payload)
        self.assertEqual(cloned_object.stat().st_nlink, 1)
        self.assertFalse(os.path.samefile(source_object, cloned_object))
        # Candidate code can corrupt its own cache without modifying the host.
        cloned_object.write_bytes(b"candidate mutation")
        self.assertEqual(source_object.read_bytes(), payload)

    def test_missing_parent_lfs_payload_fails_before_untrusted_execution(self):
        repo, sha, payload = self.make_lfs_repo()
        oid = hashlib.sha256(payload).hexdigest()
        relative = Path("lfs/objects") / oid[:2] / oid[2:4] / oid
        (repo / ".git" / relative).unlink()
        with self.assertRaises(subprocess.CalledProcessError):
            sandbox.clone_repository(repo, self.root / "clone", sha, sha)

    def test_candidate_only_lfs_image_requires_manual_hydration_before_generation(self):
        repo, base, payload = self.make_lfs_repo()
        def git(*args):
            return subprocess.check_output(["git", "-c", "core.hooksPath=/dev/null", *args],
                                           cwd=repo, stderr=subprocess.PIPE)
        git("switch", "-c", "feature")
        new_payload = payload + b"new requested image"
        (repo / "requested.png").write_bytes(new_payload)
        git("add", "requested.png")
        git("commit", "-m", "publish requested image")
        head = git("rev-parse", "HEAD").decode().strip()
        git("push", "origin", "feature")
        git("lfs", "push", "--all", "origin", "feature")
        git("switch", "main")
        oid = hashlib.sha256(new_payload).hexdigest()
        relative = Path(".git/lfs/objects") / oid[:2] / oid[2:4] / oid
        remote_object = self.root / "remote.git/lfs/objects" / oid[:2] / oid[2:4] / oid
        self.assertEqual(remote_object.read_bytes(), new_payload)
        # actions/checkout lfs:true initially hydrates the trusted revision only.
        (repo / relative).unlink()
        clone = self.root / "clone"
        sandbox.clone_repository(repo, clone, head, base)
        self.assertFalse((repo / "requested.png").exists())
        self.assertFalse((clone / "requested.png").exists())
        self.assertFalse((clone / relative).exists())
        isolated_env = dict(os.environ, GIT_CONFIG_GLOBAL=os.devnull, GIT_CONFIG_NOSYSTEM="1")
        # Mirror the local filter bootstrap while host/system filters are hidden.
        subprocess.run(["git", "-C", str(clone), "lfs", "install", "--local", "--skip-repo"],
                       env=isolated_env, check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        spec = importlib.util.spec_from_file_location("candidate_lfs_repair", ROOT / "build/scripts/repair-generated.py")
        repair = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(repair)
        generate = mock.Mock()
        with mock.patch.dict(os.environ, isolated_env, clear=True):
            report = repair.repair(clone, head, base, self.output, generation_runner=generate)
        self.assertEqual(report["status"], "failed")
        self.assertIn("smudge", report["error"])
        generate.assert_not_called()
        self.assertFalse((repo / relative).exists())
        self.assertFalse((self.output / "repair.patch").exists())

    def test_only_bounded_regular_exports_are_copied(self):
        (self.raw / "repair.patch").write_text("patch")
        (self.raw / "logs").mkdir()
        (self.raw / "logs/step.log").write_text("details")
        sandbox.collect_exports(self.raw, self.output)
        self.assertEqual((self.output / "repair.patch").read_text(), "patch")
        self.assertEqual((self.output / "logs/step.log").read_text(), "details")

    def test_host_secret_symlink_is_not_followed(self):
        secret = self.root / "host-secret"
        secret.write_text("do not upload")
        (self.raw / "report.json").symlink_to(secret)
        with self.assertRaises(ValueError):
            sandbox.collect_exports(self.raw, self.output)
        self.assertFalse(self.output.exists())

    def test_symlinked_log_directory_is_not_followed(self):
        (self.raw / "logs").symlink_to(self.root, target_is_directory=True)
        with self.assertRaises(ValueError):
            sandbox.collect_exports(self.raw, self.output)
        self.assertFalse(self.output.exists())

    def test_special_files_and_hardlinks_are_rejected(self):
        os.mkfifo(self.raw / "report.json")
        with self.assertRaises(ValueError):
            sandbox.collect_exports(self.raw, self.output)
        (self.raw / "report.json").unlink()
        external = self.root / "regular"
        external.write_text("private")
        os.link(external, self.raw / "report.json")
        with self.assertRaises(ValueError):
            sandbox.collect_exports(self.raw, self.output)

    def test_oversized_or_unexpected_files_fail_before_upload(self):
        (self.raw / "repair.patch").write_text("123")
        with mock.patch.object(sandbox, "MAX_EXPORT", 2), self.assertRaises(ValueError):
            sandbox.collect_exports(self.raw, self.output)
        (self.raw / "executable.py").write_text("evil")
        with self.assertRaises(ValueError):
            sandbox.collect_exports(self.raw, self.output)


if __name__ == "__main__":
    unittest.main()
