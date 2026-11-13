"""The producer must not see the Actions host or export links into it."""
from __future__ import annotations

import importlib.util
import hashlib
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

    def test_local_clone_retains_lfs_pointers_without_host_objects_or_filters(self):
        """Exercise real Git/LFS; only the Docker invocation is replaced."""
        repo = self.root / "host-repo"
        repo.mkdir()
        environment = dict(os.environ, GIT_CONFIG_GLOBAL=str(self.root / "host-gitconfig"),
                           GIT_CONFIG_NOSYSTEM="1", GIT_TERMINAL_PROMPT="0",
                           GITHUB_TOKEN="synthetic-host-token", ACTIONS_RUNTIME_TOKEN="synthetic-runtime-token")
        environment.pop("GIT_LFS_SKIP_SMUDGE", None)
        real_run = subprocess.run

        def git(*arguments, env=environment, check=True):
            return real_run(["git", *arguments], env=env, capture_output=True, text=True, check=check)

        # Match the runner's installed global LFS filters without changing real configuration.
        for key, value in (("filter.lfs.process", "git-lfs filter-process"),
                           ("filter.lfs.clean", "git-lfs clean -- %f"),
                           ("filter.lfs.smudge", "git-lfs smudge -- %f"),
                           ("filter.lfs.required", "true")):
            git("config", "--global", key, value)
        git("init", "-b", "main", str(repo))
        git("-C", str(repo), "config", "user.name", "LFS regression")
        git("-C", str(repo), "config", "user.email", "lfs-regression@example.invalid")
        git("-C", str(repo), "config", "meridian.hostOnly", "synthetic-host-setting")
        payload = b"synthetic retained diagram without local LFS object\n"
        oid = hashlib.sha256(payload).hexdigest()
        pointer = f"version https://git-lfs.github.com/spec/v1\noid sha256:{oid}\nsize {len(payload)}\n"
        relative = Path("docs/diagrams/analytics/backtesting-engine.png")
        (repo / ".gitattributes").write_text("docs/diagrams/**/*.png filter=lfs diff=lfs merge=lfs -text\n")
        (repo / relative).parent.mkdir(parents=True)
        (repo / relative).write_text(pointer)
        git("-C", str(repo), "add", ".")
        git("-C", str(repo), "commit", "-m", "Retain synthetic LFS pointer")
        revision = git("-C", str(repo), "rev-parse", "HEAD").stdout.strip()
        self.assertFalse((repo / ".git/lfs/objects" / oid[:2] / oid[2:4] / oid).exists())
        host_cache = repo / ".git/lfs/objects/aa/bb/host-only-cache"
        host_cache.parent.mkdir(parents=True, exist_ok=True)
        host_cache.write_text("synthetic host cache must not be mounted or copied")

        # The old command must reproduce the hosted missing-object failure, not pass accidentally.
        control = git("-c", "core.hooksPath=/dev/null", "clone", "--no-hardlinks", str(repo),
                      str(self.root / "failing-clone"), check=False)
        self.assertEqual(control.returncode, 128)
        self.assertIn("remote missing object", control.stderr)
        self.assertIn("smudge filter lfs failed", control.stderr)
        docker_calls = []

        def run_without_docker(command, *args, **kwargs):
            if command[0] != "docker":
                if "clone" in command:
                    self.assertEqual(kwargs["env"]["GIT_LFS_SKIP_SMUDGE"], "1")
                    self.assertIn("--no-hardlinks", command)
                    self.assertIn("core.hooksPath=/dev/null", command)
                return real_run(command, *args, **kwargs)
            docker_calls.append(command)
            self.assertEqual(command.count("--mount"), 2)
            self.assertNotIn("GIT_LFS_SKIP_SMUDGE", " ".join(command))
            self.assertNotIn("synthetic-host-token", " ".join(command))
            work_mount = next(value for value in command if value.startswith("type=bind,source=") and value.endswith(",target=/work"))
            work = Path(work_mount.removeprefix("type=bind,source=").removesuffix(",target=/work"))
            clone = work / "repo"
            self.assertEqual((clone / relative).read_text(), pointer)
            self.assertFalse((clone / ".git/lfs/objects/aa/bb/host-only-cache").exists())
            self.assertFalse((clone / ".git/objects/info/alternates").exists())
            self.assertNotIn("synthetic-host-setting", (clone / ".git/config").read_text())
            for object_file in (clone / ".git/objects").rglob("*"):
                if object_file.is_file():
                    self.assertEqual(object_file.stat().st_nlink, 1)

            # Use the container's existing configuration policy for real worktree and staging commands.
            # No host LFS filters or skip-smudge environment are passed into that policy.
            container_env = {"PATH": environment.get("PATH", os.defpath), "GIT_CONFIG_GLOBAL": os.devnull,
                             "GIT_CONFIG_NOSYSTEM": "1", "GIT_TERMINAL_PROMPT": "0"}
            checkout = work / "checkout"
            git("-c", "core.hooksPath=/dev/null", "-C", str(clone), "worktree", "add", "--detach",
                str(checkout), revision, env=container_env)
            self.assertEqual((checkout / relative).read_text(), pointer)
            self.assertEqual(git("-C", str(checkout), "status", "--porcelain", env=container_env).stdout, "")
            git("-C", str(checkout), "add", "-A", env=container_env)
            self.assertEqual(git("-C", str(checkout), "write-tree", env=container_env).stdout.strip(),
                             git("-C", str(repo), "rev-parse", "HEAD^{tree}").stdout.strip())
            return subprocess.CompletedProcess(command, 0)

        with mock.patch.dict(os.environ, environment, clear=True), mock.patch.object(sandbox.subprocess, "run", side_effect=run_without_docker):
            self.assertEqual(sandbox.run(repo, self.output, "repair-test", revision, revision), 0)
            self.assertNotIn("GIT_LFS_SKIP_SMUDGE", os.environ)
        self.assertEqual(len(docker_calls), 1)
        self.assertTrue((self.output / "container.log").is_file())
        self.assertEqual((repo / relative).read_text(), pointer)

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
