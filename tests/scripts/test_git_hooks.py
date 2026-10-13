from __future__ import annotations

import hashlib
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
HOOK_FILES = (
    ".githooks/pre-commit",
    "scripts/dev/install-git-hooks.sh",
    "build/scripts/hooks/install-hooks.sh",
    "build/scripts/hooks/pre-commit",
)


@unittest.skipUnless(
    os.name == "posix" and shutil.which("bash") and shutil.which("git"),
    "Git hook integration tests require Git and Bash on a POSIX host",
)
class GitHooksTests(unittest.TestCase):
    """Run the real hooks against disposable Git indexes, without a .NET SDK."""

    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory(prefix="meridian hook tests ")
        self.addCleanup(self.temporary.cleanup)
        self.base = Path(self.temporary.name)
        self.repo = self.base / "ordinary checkout"
        self.repo.mkdir()
        self.bin = self.base / "bin"
        self.bin.mkdir()
        # An isolated PATH proves the no-SDK cases even on .NET-enabled hosts.
        for command in (
            "bash", "git", "dirname", "mkdir", "mktemp", "rm", "chmod",
            "cp", "cat", "sed", "sort", "find", "awk", "tr", "uname", "make",
        ):
            executable = shutil.which(command)
            if executable:
                (self.bin / command).symlink_to(executable)
        self.snapshots = self.base / "temporary snapshots"
        self.snapshots.mkdir()
        self.log = self.base / "dotnet.jsonl"
        self.global_config = self.base / "global.gitconfig"
        self.global_config.write_text("", encoding="utf-8")
        self.env = os.environ.copy()
        for key in tuple(self.env):
            if key in {
                "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_COMMON_DIR",
                "GIT_PREFIX", "GIT_CONFIG", "GIT_CONFIG_PARAMETERS",
            } or key.startswith("GIT_CONFIG_"):
                self.env.pop(key)
        self.env.update({
            "PATH": str(self.bin),
            "TMPDIR": str(self.snapshots),
            "GIT_CONFIG_GLOBAL": str(self.global_config),
            "GIT_CONFIG_NOSYSTEM": "1",
            "GIT_AUTHOR_NAME": "Hook Tests",
            "GIT_AUTHOR_EMAIL": "hook-tests@example.invalid",
            "GIT_COMMITTER_NAME": "Hook Tests",
            "GIT_COMMITTER_EMAIL": "hook-tests@example.invalid",
            "FAKE_DOTNET_LOG": str(self.log),
        })
        for relative in HOOK_FILES:
            destination = self.repo / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(REPO_ROOT / relative, destination)
        shutil.copy2(REPO_ROOT / "Makefile", self.repo / "Makefile")
        shutil.copytree(REPO_ROOT / "make", self.repo / "make")
        self._write("README.md", "Initial documentation\n")
        self._write("src/Example.cs", "// GOOD initial example\n")
        self._write("src/Unchanged.cs", "// GOOD unchanged source\n")
        self._write("src/Rename.cs", "// GOOD source to rename\n")
        self._write("src/Delete.cs", "// GOOD source to delete\n")
        self._write(".editorconfig", "root = true\n[*]\nindent_size = 4\n")
        self._write("src/.editorconfig", "[*.cs]\nindent_size = 2\n")
        self._write("config/.globalconfig", "is_global = true\n")
        self._write("global.json", '{"sdk":{"version":"10.0.100"}}\n')
        self._git("init", "--template=")
        self._git("config", "commit.gpgsign", "false")
        self._git("config", "core.autocrlf", "false")
        self._git("add", ".")
        self._git("commit", "-m", "Initial fixture")
        self._install_fake_dotnet()

    def _install_fake_dotnet(self) -> None:
        script = f"""#!{sys.executable}
import json
import os
import sys
from pathlib import Path

root = Path.cwd()
arguments = sys.argv[1:]
files = {{path.relative_to(root).as_posix(): path.read_bytes().decode('utf-8')
         for path in root.rglob('*') if path.is_file()}}
with open(os.environ['FAKE_DOTNET_LOG'], 'a', encoding='utf-8') as log:
    log.write(json.dumps({{'cwd': str(root), 'args': arguments, 'files': files}}) + '\\n')
includes = [path.removeprefix('./') for path in arguments[arguments.index('--include') + 1:]] if '--include' in arguments else []
failed = any('BAD_FORMAT' in files.get(path, '') for path in includes)
if os.environ.get('FAKE_DOTNET_MODIFY_SNAPSHOT') == '1':
    for path in includes:
        (root / path).write_text('// formatter changed temporary content\\n', encoding='utf-8')
sys.exit(1 if failed else 0)
"""
        executable = self.bin / "dotnet"
        executable.write_text(script, encoding="utf-8")
        executable.chmod(0o755)

    def _write(self, relative: str, content: str, repo: Path | None = None) -> None:
        path = (repo or self.repo) / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(content.encode("utf-8"))

    def _run(
        self, arguments: list[str], *, cwd: Path | None = None,
        env: dict[str, str] | None = None, check: bool = True,
    ) -> subprocess.CompletedProcess[str]:
        result = subprocess.run(
            arguments, cwd=cwd or self.repo, env=env or self.env,
            capture_output=True, text=True, timeout=30,
        )
        if check:
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        return result

    def _git(
        self, *arguments: str, repo: Path | None = None,
        env: dict[str, str] | None = None,
    ) -> str:
        return self._run(["git", *arguments], cwd=repo, env=env).stdout.strip()

    def _state(self, repo: Path, env: dict[str, str]) -> tuple[str, str, dict]:
        # write-tree can update Git's cache-tree extension; capture the index after it.
        tree = self._git("write-tree", repo=repo, env=env)
        index = Path(self._git("rev-parse", "--git-path", "index", repo=repo, env=env))
        if not index.is_absolute():
            index = repo / index
        files = {}
        for directory, subdirectories, names in os.walk(repo):
            if ".git" in subdirectories:
                subdirectories.remove(".git")
            for name in names:
                path = Path(directory) / name
                files[path.relative_to(repo).as_posix()] = (
                    hashlib.sha256(path.read_bytes()).hexdigest(),
                    path.stat().st_mode & 0o777,
                )
        return hashlib.sha256(index.read_bytes()).hexdigest(), tree, files

    def _hook(
        self, *, expected: int = 0, legacy: bool = False,
        repo: Path | None = None, env: dict[str, str] | None = None,
        cwd: Path | None = None,
    ) -> subprocess.CompletedProcess[str]:
        repository, environment = repo or self.repo, env or self.env
        script = "build/scripts/hooks/pre-commit" if legacy else ".githooks/pre-commit"
        before = self._state(repository, environment)
        result = self._run(
            ["bash", str(repository / script)], cwd=cwd or repository,
            env=environment, check=False,
        )
        self.assertEqual(result.returncode, expected, result.stdout + result.stderr)
        self.assertEqual(before, self._state(repository, environment), "Hook changed index or checkout")
        self.assertEqual(list(self.snapshots.iterdir()), [], "Hook leaked a temporary snapshot")
        return result

    def _calls(self) -> list[dict]:
        if not self.log.exists():
            return []
        return [json.loads(line) for line in self.log.read_text(encoding="utf-8").splitlines()]

    def _assert_format_call(self, included: set[str]) -> dict:
        calls = self._calls()
        self.assertEqual(len(calls), 1)
        call = calls[0]
        arguments = call["args"]
        self.assertEqual(
            arguments[:8],
            ["format", "whitespace", ".", "--folder", "--verify-no-changes",
             "--verbosity", "minimal", "--include"],
        )
        self.assertEqual(set(path.removeprefix("./") for path in arguments[8:]), included)
        self.assertEqual(len(arguments[8:]), len(included), "Duplicate include arguments")
        for path in arguments[8:]:
            self.assertFalse(path.startswith("-"), "Include path can be parsed as a formatter flag")
        snapshot = Path(call["cwd"])
        self.assertIn(self.snapshots, snapshot.parents)
        self.assertFalse(snapshot.exists(), "Formatter snapshot was not removed")
        return call

    def test_documentation_and_non_dotnet_sources_skip_without_sdk(self) -> None:
        (self.bin / "dotnet").unlink()
        self._write("README.md", "Updated documentation\n")
        self._write("src/Example.cs", "// BAD_FORMAT unstaged source\n")
        self._write("notes.csproj", "<Project />\n")
        self._write("script.fs", "let x=1\n")
        self._write(".editorconfig", "root = true\n")
        self._git("add", "README.md", "notes.csproj", "script.fs", ".editorconfig")
        self._hook()
        self.assertEqual(self._calls(), [])

    def test_csharp_and_visual_basic_changes_require_sdk(self) -> None:
        (self.bin / "dotnet").unlink()
        for suffix in ("cs", "vb"):
            with self.subTest(suffix=suffix):
                path = f"New.{suffix}"
                self._write(path, "// GOOD source\n")
                self._git("add", "--", path)
                result = self._hook(expected=1)
                self.assertIn("dotnet", result.stdout + result.stderr)
                self._git("reset", "HEAD", "--", path)
                (self.repo / path).unlink()
        self.assertEqual(self._calls(), [])

    def test_staged_bad_content_fails_even_when_checkout_is_formatted(self) -> None:
        self._write("src/Example.cs", "// BAD_FORMAT staged\n")
        self._git("add", "src/Example.cs")
        self._write("src/Example.cs", "// GOOD unstaged correction\n")
        self._hook(expected=1)
        call = self._assert_format_call({"src/Example.cs"})
        self.assertEqual(call["files"]["src/Example.cs"], "// BAD_FORMAT staged\n")

    def test_staged_good_content_passes_and_partial_staging_is_untouched(self) -> None:
        self._write("src/Example.cs", "// GOOD staged raw bytes\r\n")
        self._git("add", "src/Example.cs")
        self._write("src/Example.cs", "// BAD_FORMAT unstaged edit\n")
        environment = dict(self.env, FAKE_DOTNET_MODIFY_SNAPSHOT="1")
        self._hook(env=environment)
        call = self._assert_format_call({"src/Example.cs"})
        self.assertEqual(call["files"]["src/Example.cs"], "// GOOD staged raw bytes\r\n")

    def test_snapshot_uses_index_versions_of_formatting_configuration(self) -> None:
        expected = {
            "src/Example.cs": "// GOOD staged\n",
            ".editorconfig": "root = true\n[*]\nindent_size = 8\n",
            "src/.editorconfig": "[*.cs]\nindent_size = 2\n",
            "config/.globalconfig": "is_global = true\nindent_size = 8\n",
            "global.json": '{"sdk":{"version":"10.0.200"}}\n',
            "new rules/.editorconfig": "[*]\nindent_style = tab\n",
        }
        for path, content in expected.items():
            self._write(path, content)
        self._git("add", "src/Example.cs", ".editorconfig", "config/.globalconfig",
                  "global.json", "new rules/.editorconfig")
        for path in expected:
            if path != "src/Example.cs":
                self._write(path, "UNSTAGED configuration\n")
        self._write("untracked/.editorconfig", "UNTRACKED configuration\n")
        self._hook()
        call = self._assert_format_call({"src/Example.cs"})
        self.assertEqual(call["files"], expected)

    def test_added_modified_and_renamed_paths_are_null_delimited(self) -> None:
        renamed = "src/Renamed\nsource.cs"
        self._git("mv", "src/Rename.cs", renamed)
        added = {
            "src/space in name.cs": "// GOOD added C#\n",
            "src/new\nfile.vb": "' GOOD added Visual Basic\n",
            "directory\n/Example.cs": "// GOOD newline directory\n",
            "-leading.cs": "// GOOD leading dash\n",
        }
        for path, content in added.items():
            self._write(path, content)
            self._git("add", "--", path)
        self._write("src/Example.cs", "// GOOD modified\n")
        self._git("add", "src/Example.cs")
        self._git("rm", "src/Delete.cs")
        nested = self.repo / "src"
        self._hook(cwd=nested)
        included = {renamed, "src/Example.cs", *added}
        call = self._assert_format_call(included)
        for path in included:
            self.assertEqual(call["files"][path], (self.repo / path).read_bytes().decode("utf-8"))
        self.assertNotIn("src/Delete.cs", call["files"])
        self.assertNotIn("src/Rename.cs", call["files"])
        self.assertNotIn("src/Unchanged.cs", call["files"])

    def test_deleted_sources_skip_without_sdk(self) -> None:
        (self.bin / "dotnet").unlink()
        self._git("rm", "src/Delete.cs")
        self._git("mv", "src/Rename.cs", "src/Rename.txt")
        self._hook()
        self.assertEqual(self._calls(), [])

    def test_alternate_index_is_checked_and_both_indexes_are_preserved(self) -> None:
        default_index = self.repo / ".git" / "index"
        alternate_index = self.base / "alternate index"
        shutil.copy2(default_index, alternate_index)
        environment = dict(self.env, GIT_INDEX_FILE=str(alternate_index))
        self._write("src/Example.cs", "// GOOD staged in alternate index\n")
        self._git("add", "src/Example.cs", env=environment)
        self._write("src/Example.cs", "// BAD_FORMAT unstaged\n")
        default_before = default_index.read_bytes()
        self._hook(env=environment)
        self.assertEqual(default_index.read_bytes(), default_before)
        call = self._assert_format_call({"src/Example.cs"})
        self.assertEqual(call["files"]["src/Example.cs"], "// GOOD staged in alternate index\n")

    def _assert_installer_in_ordinary_and_linked_checkouts(self, method: str) -> None:
        linked = self.base / "linked worktree"
        self._git("worktree", "add", "--detach", str(linked), "HEAD")
        self.global_config.write_text("[core]\n\thooksPath = global-hooks\n", encoding="utf-8")
        global_before = self.global_config.read_bytes()
        for repo in (self.repo, linked):
            with self.subTest(checkout=repo.name, installer=method):
                # Installation must restore executable permission for actual Git invocation.
                (repo / ".githooks" / "pre-commit").chmod(0o644)
                if method == "make":
                    self._run(["make", "install-hooks"], cwd=repo)
                else:
                    self._run(["bash", str(repo / method)], cwd=self.base)
                self.assertEqual(self._git("config", "--local", "--get", "core.hooksPath", repo=repo), ".githooks")
                self.assertEqual(self.global_config.read_bytes(), global_before)
                self.assertTrue(os.access(repo / ".githooks" / "pre-commit", os.X_OK))
                self.assertFalse((repo / ".git" / "hooks" / "pre-commit").exists())
                self._write("src/Example.cs", "// GOOD installed hook\n", repo=repo)
                self._git("add", "src/Example.cs", repo=repo)
                self._write("src/Example.cs", "// BAD_FORMAT unstaged after install\n", repo=repo)
                self.log.unlink(missing_ok=True)
                checkout_before = (repo / "src/Example.cs").read_bytes()
                self._git("commit", "-m", "Verify installed hook", repo=repo)
                self.assertEqual((repo / "src/Example.cs").read_bytes(), checkout_before)
                self.assertEqual(self._git("show", "HEAD:src/Example.cs", repo=repo), "// GOOD installed hook")
                call = self._assert_format_call({"src/Example.cs"})
                self.assertEqual(call["files"]["src/Example.cs"], "// GOOD installed hook\n")
                self.assertEqual(list(self.snapshots.iterdir()), [])
                (self.bin / "dotnet").unlink()
                try:
                    self.log.unlink()
                    self._write("README.md", "Installed documentation-only commit\n", repo=repo)
                    self._git("add", "README.md", repo=repo)
                    self._git("commit", "-m", "Documentation without SDK", repo=repo)
                    self.assertEqual(self._calls(), [])
                    self.assertEqual((repo / "src/Example.cs").read_bytes(), checkout_before)
                    self.assertEqual(list(self.snapshots.iterdir()), [])
                finally:
                    self._install_fake_dotnet()

    def test_canonical_installer_runs_hook_in_ordinary_and_linked_checkouts(self) -> None:
        self._assert_installer_in_ordinary_and_linked_checkouts("scripts/dev/install-git-hooks.sh")

    def test_legacy_installer_runs_hook_in_ordinary_and_linked_checkouts(self) -> None:
        self._assert_installer_in_ordinary_and_linked_checkouts("build/scripts/hooks/install-hooks.sh")

    @unittest.skipUnless(shutil.which("make"), "GNU Make is required for its installer target")
    def test_make_target_runs_hook_in_ordinary_and_linked_checkouts(self) -> None:
        self._assert_installer_in_ordinary_and_linked_checkouts("make")

    def test_legacy_pre_commit_checks_staged_content_and_preserves_checkout(self) -> None:
        self._write("src/Example.cs", "// BAD_FORMAT staged via legacy hook\n")
        self._git("add", "src/Example.cs")
        self._write("src/Example.cs", "// GOOD unstaged\n")
        self._hook(expected=1, legacy=True, cwd=self.repo / "src")
        call = self._assert_format_call({"src/Example.cs"})
        self.assertEqual(call["files"]["src/Example.cs"], "// BAD_FORMAT staged via legacy hook\n")

    def test_legacy_pre_commit_skips_documentation_without_sdk(self) -> None:
        (self.bin / "dotnet").unlink()
        self._write("README.md", "Documentation through legacy hook\n")
        self._git("add", "README.md")
        self._hook(legacy=True)
        self.assertEqual(self._calls(), [])


if __name__ == "__main__":
    unittest.main()
