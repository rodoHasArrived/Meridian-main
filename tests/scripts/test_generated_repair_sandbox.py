"""The producer must not see the Actions host or export links into it."""
from __future__ import annotations

import importlib.util
import os
from pathlib import Path
import shutil
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

    def test_pointer_only_checkout_is_cloned_without_fetching_lfs_payloads(self):
        self.assertIsNotNone(shutil.which("git-lfs"), "Git LFS is required for this clone regression")
        repo = self.root / "repo"
        repo.mkdir()
        pointer = ("version https://git-lfs.github.com/spec/v1\n"
                   f"oid sha256:{'1' * 64}\nsize 42\n")
        clean_env = dict(os.environ, GIT_CONFIG_GLOBAL=os.devnull, GIT_CONFIG_NOSYSTEM="1",
                         GIT_TERMINAL_PROMPT="0", GIT_CONFIG_COUNT="0")
        clean_env.pop("GIT_LFS_SKIP_SMUDGE", None)
        clean_env.pop("GIT_LFS_SKIP_DOWNLOAD_ERRORS", None)
        git = ["git", "-C", str(repo), "-c", "core.hooksPath=/dev/null"]
        for args in (("init",), ("config", "user.name", "Synthetic clone test"),
                     ("config", "user.email", "synthetic@example.invalid")):
            subprocess.run([*git, *args], env=clean_env, check=True, capture_output=True)
        (repo / ".gitattributes").write_text("*.png filter=lfs diff=lfs merge=lfs -text\n")
        (repo / "evidence.png").write_text(pointer)
        subprocess.run([*git, "add", "."], env=clean_env, check=True, capture_output=True)
        subprocess.run([*git, "commit", "-m", "Pointer-only synthetic checkout"],
                       env=clean_env, check=True, capture_output=True)
        head = subprocess.check_output([*git, "rev-parse", "HEAD"], env=clean_env, text=True).strip()
        lfs_env = dict(clean_env, GIT_CONFIG_COUNT="2", GIT_CONFIG_KEY_0="filter.lfs.process",
                       GIT_CONFIG_VALUE_0="git-lfs filter-process", GIT_CONFIG_KEY_1="filter.lfs.required",
                       GIT_CONFIG_VALUE_1="true")
        failed = subprocess.run(["git", "clone", "--no-hardlinks", str(repo), str(self.root / "unprepared")],
                                env=lfs_env, text=True, capture_output=True)
        self.assertNotEqual(failed.returncode, 0)
        self.assertIn("remote missing object", failed.stderr)

        real_run = subprocess.run

        def run_with_fake_container(command, **kwargs):
            if command[0] != "docker":
                return real_run(command, **kwargs)
            mounts = [command[index + 1] for index, value in enumerate(command) if value == "--mount"]
            work = Path(next(value for value in mounts if "target=/work" in value).split("source=", 1)[1].split(",", 1)[0])
            raw = Path(next(value for value in mounts if "target=/output" in value).split("source=", 1)[1].split(",", 1)[0])
            self.assertEqual((work / "repo" / "evidence.png").read_text(), pointer)
            (raw / "report.json").write_text('{"status":"noop"}')
            return subprocess.CompletedProcess(command, 0)

        with mock.patch.dict(os.environ, lfs_env, clear=True), \
                mock.patch.object(sandbox.subprocess, "run", side_effect=run_with_fake_container):
            self.assertEqual(sandbox.run(repo, self.output, "repair-test", head, head), 0)
        self.assertEqual((self.output / "report.json").read_text(), '{"status":"noop"}')
        self.assertEqual((repo / "evidence.png").read_text(), pointer)

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
