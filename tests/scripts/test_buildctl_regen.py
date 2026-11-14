from __future__ import annotations

import importlib.util
import io
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from contextlib import redirect_stderr
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("buildctl_regen_test", ROOT / "build/python/cli/buildctl.py")
buildctl = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(buildctl)


class RegenCommandTests(unittest.TestCase):
    def run_regen(self, arguments, codes):
        args = buildctl.build_parser().parse_args(["regen", *arguments])
        clean = subprocess.CompletedProcess([], 0, stdout="", stderr="")
        with patch.object(buildctl, "_run", return_value=clean), \
                patch.object(buildctl, "_run_passthrough", side_effect=codes) as run:
            result = buildctl.cmd_regen(args)
        return result, [call.args[0] for call in run.call_args_list]

    def test_failed_generation_never_reaches_freshness_check(self):
        result, commands = self.run_regen([], [1])
        self.assertEqual(result, 1)
        self.assertEqual(len(commands), 1)

    def test_check_reports_reviewed_drift_after_successful_generation(self):
        result, commands = self.run_regen([], [0, 1])
        self.assertEqual(result, 1)
        self.assertEqual(commands[-1][-1], "build/scripts/ci/check-generated-output.py")

    def test_fix_retains_generated_changes_for_review(self):
        result, commands = self.run_regen(["--fix", "--docs-only"], [0])
        self.assertEqual(result, 0)
        self.assertEqual(len(commands), 1)
        self.assertIn("--docs-only", commands[0])

    def test_conflicts_refuse_generation_before_writing(self):
        args = buildctl.build_parser().parse_args(["regen", "--fix"])
        conflict = subprocess.CompletedProcess([], 0, stdout="100644 object 1\tREADME.md\0", stderr="")
        with patch.object(buildctl, "_run", return_value=conflict), \
                patch.object(buildctl, "_run_passthrough") as run, redirect_stderr(io.StringIO()):
            self.assertEqual(buildctl.cmd_regen(args), 2)
        run.assert_not_called()


@unittest.skipUnless(os.name == "posix" and shutil.which("git") and shutil.which("bash"), "Git/Bash hook integration")
class MergeRegenerationTests(unittest.TestCase):
    def test_concurrent_review_snapshots_conflict_instead_of_text_merging(self):
        for snapshot in ("docs/generated/workflow-command-reference.md", "docs/source/generated/source-hash-manifest.json"):
            with self.subTest(snapshot=snapshot), tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as temporary:
                root = Path(temporary)
                environment = dict(os.environ, GIT_CONFIG_GLOBAL=os.devnull, GIT_CONFIG_NOSYSTEM="1")

                def git(*args, check=True):
                    return subprocess.run(["git", *args], cwd=root, env=environment, capture_output=True, check=check)

                git("init", "--template=")
                git("config", "user.name", "Regen test")
                git("config", "user.email", "regen@example.invalid")
                shutil.copy2(ROOT / ".gitattributes", root / ".gitattributes")
                target = root / snapshot
                target.parent.mkdir(parents=True)
                lines = [f"line {number}\n" for number in range(20)]
                target.write_text("".join(lines))
                git("add", ".")
                git("commit", "-m", "accepted snapshot")
                git("branch", "incoming")
                lines[0] = "ours\n"
                target.write_text("".join(lines))
                git("commit", "-am", "ours")
                git("checkout", "incoming")
                lines[0] = "line 0\n"
                lines[-1] = "theirs\n"
                target.write_text("".join(lines))
                git("commit", "-am", "theirs")
                git("checkout", "-")
                result = git("merge", "--no-commit", "incoming", check=False)
                self.assertEqual(result.returncode, 1)
                self.assertEqual(git("diff", "--name-only", "--diff-filter=U").stdout.decode().strip(), snapshot)

    def test_fast_forward_merge_regenerates_without_staging_or_replacing_user_prose(self):
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as temporary:
            root = Path(temporary)
            environment = dict(os.environ, GIT_CONFIG_GLOBAL=os.devnull, GIT_CONFIG_NOSYSTEM="1")

            def git(*args):
                return subprocess.check_output(["git", *args], cwd=root, env=environment, stderr=subprocess.STDOUT)

            git("init", "--template=")
            git("config", "user.name", "Regen test")
            git("config", "user.email", "regen@example.invalid")
            hook = root / ".githooks/post-merge"
            hook.parent.mkdir()
            shutil.copy2(ROOT / ".githooks/post-merge", hook)
            hook.chmod(0o755)
            cli = root / "build/python/cli/buildctl.py"
            cli.parent.mkdir(parents=True)
            cli.write_text(
                "from pathlib import Path\nimport sys\n"
                "assert sys.argv[1:] == ['regen', '--fix']\n"
                "Path('machine-output.txt').write_text(Path('source.txt').read_text())\n"
                "Path('reviewed.md').write_text('generated from ' + Path('source.txt').read_text())\n"
            )
            (root / "source.txt").write_text("initial\n")
            (root / "reviewed.md").write_text("generated from initial\n")
            (root / "human.md").write_text("human prose\n")
            (root / ".gitignore").write_text("machine-output.txt\n")
            git("add", ".")
            git("commit", "-m", "fixture")
            git("branch", "incoming")
            git("checkout", "incoming")
            (root / "source.txt").write_text("merged source\n")
            git("add", "source.txt")
            git("commit", "-m", "source update")
            incoming = git("rev-parse", "HEAD")
            git("checkout", "-")
            git("config", "core.hooksPath", ".githooks")
            (root / "human.md").write_text("local human edit\n")
            git("merge", "--ff-only", "incoming")
            self.assertEqual(git("rev-parse", "HEAD"), incoming)
            self.assertEqual((root / "machine-output.txt").read_text(), "merged source\n")
            self.assertEqual((root / "reviewed.md").read_text(), "generated from merged source\n")
            self.assertEqual((root / "human.md").read_text(), "local human edit\n")
            self.assertEqual(git("diff", "--cached"), b"")
            self.assertEqual(set(git("diff", "--name-only").decode().splitlines()), {"human.md", "reviewed.md"})

    def test_failed_hook_reports_completed_merge_and_recovery_command(self):
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as temporary:
            root = Path(temporary)
            subprocess.run(["git", "init", "--template="], cwd=root, check=True, capture_output=True)
            cli = root / "build/python/cli/buildctl.py"
            cli.parent.mkdir(parents=True)
            cli.write_text("raise SystemExit(7)\n")
            result = subprocess.run(["bash", str(ROOT / ".githooks/post-merge")], cwd=root, text=True, capture_output=True)
            self.assertEqual(result.returncode, 7)
            self.assertIn("merge has already completed", result.stderr)
            self.assertIn("regen --fix", result.stderr)


if __name__ == "__main__":
    unittest.main()
