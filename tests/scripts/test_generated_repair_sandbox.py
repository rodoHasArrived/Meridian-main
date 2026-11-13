"""The producer must not see the Actions host or export links into it."""
from __future__ import annotations

import importlib.util
import os
from pathlib import Path
import subprocess
import sys
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

    def test_clone_retains_unavailable_lfs_pointer_and_pinned_commits_without_hooks(self):
        repo = self.root / "repo"
        repo.mkdir()
        config = self.root / "gitconfig"
        config.write_text('[filter "lfs"]\nprocess = git-lfs filter-process\nrequired = true\n')
        environment = {**os.environ, "GIT_CONFIG_GLOBAL": str(config),
                       "GIT_CONFIG_NOSYSTEM": "1", "GIT_LFS_SKIP_SMUDGE": "0"}

        def git(*args, directory=repo):
            return subprocess.check_output(["git", "-c", "core.hooksPath=/dev/null", *args],
                                           cwd=directory, env=environment, stderr=subprocess.PIPE)

        git("init", "-q")
        git("config", "user.name", "Fixture")
        git("config", "user.email", "fixture@example.invalid")
        (repo / ".gitattributes").write_text("*.png filter=lfs diff=lfs merge=lfs -text\n")
        pointer = ("version https://git-lfs.github.com/spec/v1\noid sha256:" + "1" * 64 + "\nsize 4\n").encode()
        (repo / "diagram.png").write_bytes(pointer)
        git("-c", "filter.lfs.process=", "-c", "filter.lfs.required=false", "add", ".")
        git("commit", "-qm", "Track an unavailable LFS object")
        base = git("rev-parse", "HEAD").decode().strip()
        (repo / "note.txt").write_text("pinned head\n")
        git("add", "note.txt")
        git("commit", "-qm", "Advance the pinned head")
        head = git("rev-parse", "HEAD").decode().strip()
        hooks = self.root / "hooks"
        hooks.mkdir()
        hook_marker = self.root / "hook-ran"
        (hooks / "post-checkout").write_text(f'#!/bin/sh\ntouch "{hook_marker}"\n')
        (hooks / "post-checkout").chmod(0o755)
        git("config", "--file", str(config), "core.hooksPath", str(hooks))

        def inspect_clone(work, output, image, requested_head, requested_base):
            clone = work / "repo"
            self.assertEqual((requested_head, requested_base), (head, base))
            self.assertEqual((clone / "diagram.png").read_bytes(), pointer)
            for revision in (head, base):
                self.assertEqual(git("rev-parse", "--verify", f"{revision}^{{commit}}", directory=clone).decode().strip(), revision)
            self.assertFalse(hook_marker.exists())
            original = repo / ".git" / "objects" / head[:2] / head[2:]
            copied = clone / ".git" / "objects" / head[:2] / head[2:]
            self.assertNotEqual((original.stat().st_dev, original.stat().st_ino),
                                (copied.stat().st_dev, copied.stat().st_ino))
            return [sys.executable, "-c", "pass"]

        with mock.patch.dict(os.environ, environment), mock.patch.object(sandbox, "container_command", side_effect=inspect_clone):
            self.assertEqual(sandbox.run(repo, self.output, "repair-test", head, base), 0)
            self.assertEqual(os.environ["GIT_LFS_SKIP_SMUDGE"], "0")
        self.assertFalse(hook_marker.exists())

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
