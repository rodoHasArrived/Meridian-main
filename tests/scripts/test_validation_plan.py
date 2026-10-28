"""Focused validation recommendations preserve coverage and never execute checks."""
import argparse
from contextlib import redirect_stderr, redirect_stdout
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

REPO = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("validation_plan", REPO / "build/python/cli/validation_plan.py")
plan = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(plan)


class ValidationPlanTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.write(plan.MATRIX, "# Task-to-Proof Matrix\n")
        self.write(plan.REGISTRY, "modules: []\n")
        self.write("Directory.Build.props", "<Project/>\n")
        self.write("tests/Directory.Build.props", "<Project/>\n")

    def write(self, path, text):
        destination = self.root / path
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_text(text)
        return destination

    def project(self, name, references=()):
        path = name + "/" + Path(name).name + ".csproj"
        flags = "<PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup>" if name.startswith("tests/") else ""
        self.write(path, "<Project>" + flags + "<ItemGroup>" + "".join(
            '<ProjectReference Include="' + ref + '"/>' for ref in references) + "</ItemGroup></Project>")
        return path

    def recommend(self, *paths):
        return plan.recommend_validation(self.root, list(paths))

    def browser(self):
        self.write(plan.BROWSER + "/package.json", json.dumps({"scripts": {
            "test": "vitest", "lint": "eslint src", "typecheck:strict": "tsc", "build": "vite build"}}))

    def test_transitive_graph_cycle_and_test_only_change(self):
        self.project("src/Core", ["../Middle/Middle.csproj"])
        self.project("src/Middle", ["../Core/Core.csproj"])
        tests = self.project("tests/Core.Tests", ["../../src/Middle/Middle.csproj"])
        self.write("src/Core/Thing.cs", "// source")
        data = self.recommend("src/Core/Thing.cs")
        check = next(c for c in data["checks"] if c["id"] == "dotnet-Core.Tests")
        self.assertEqual(check["argv"], ["python", "build/python/cli/buildctl.py", "test", "--project", tests, "--queue"])
        direct = self.recommend("tests/Core.Tests/ThingTests.cs")
        self.assertEqual(len([c for c in direct["checks"] if c["id"].startswith("dotnet-")]), 1)

    def test_shared_contracts_force_both_consumers_without_refs(self):
        self.project("src/Meridian.Contracts")
        self.project("tests/Meridian.Wpf.Tests")
        self.project("tests/Meridian.Ui.Tests")
        self.project("tests/Meridian.Tests")
        self.browser()
        data = self.recommend("src/Meridian.Contracts/NewDto.cs")
        self.assertEqual(data["affectedConsumers"], ["browser", "wpf"])
        commands = [c["argv"] for c in data["checks"]]
        self.assertIn(["npm", "--prefix", plan.BROWSER, "run", "test"], commands)
        self.assertTrue(any("tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj" in argv for argv in commands))
        self.assertTrue(any("Windows" in warning for warning in data["coverageWarnings"]))
        wpf = next(argv for argv in commands if "tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj" in argv)
        self.assertIn("--full-wpf-build", wpf)
        self.assertEqual(data["requiredFinalChecks"][0]["argv"], ["bash", "scripts/ci.sh"])

    def test_browser_exact_target_then_general_fallback(self):
        self.browser()
        source = plan.BROWSER + "/src/view.ts"
        test = plan.BROWSER + "/src/view.test.ts"
        self.write(source, "export {}")
        self.write(test, "test('view', () => {})")
        data = self.recommend(source)
        check = next(c for c in data["checks"] if c["id"] == "browser-test")
        self.assertEqual(check["argv"][-2:], ["--", "src/view.test.ts"])
        data = self.recommend(source, plan.BROWSER + "/src/new.ts")
        check = next(c for c in data["checks"] if c["id"] == "browser-test")
        self.assertEqual(check["argv"], ["npm", "--prefix", plan.BROWSER, "run", "test"])

    def test_exact_python_mapping_and_test_only(self):
        self.write("build/scripts/ai/example-tool.py", "pass")
        self.write("tests/scripts/test_example_tool.py", "pass")
        data = self.recommend("build/scripts/ai/example-tool.py", "tests/scripts/test_example_tool.py")
        checks = [c for c in data["checks"] if c["id"] == "python-tests-scripts-test_example_tool"]
        self.assertEqual(len(checks), 1)
        self.assertEqual(checks[0]["argv"], ["python", "-m", "unittest", "discover", "-s", "tests/scripts", "-p", "test_example_tool.py"])
        self.assertEqual(len(checks[0]["paths"]), 2)

    def test_dynamic_missing_malformed_and_unknown_coverage_warn(self):
        self.project("src/Core", ["$(CoreRoot)/Dynamic.csproj", "../Gone/Gone.csproj"])
        self.write("src/Broken/Broken.csproj", "<Project")
        data = self.recommend("src/Core/New.cs", "config/unmapped.json")
        warnings = "\n".join(data["coverageWarnings"])
        for message in ("Unresolved ProjectReference", "Missing referenced project", "Cannot parse", "No focused mapping"):
            self.assertIn(message, warnings)
        self.assertIn("absent", warnings)

    def test_paths_reject_traversal_absolute_and_symlink_escape(self):
        outside = self.root.parent / (self.root.name + "-outside")
        outside.mkdir()
        self.addCleanup(outside.rmdir)
        (self.root / "link").symlink_to(outside, target_is_directory=True)
        for name in ("../bad.py", "/tmp/bad", "C:\\bad.py", "link/new.py", "-flag"):
            with self.subTest(name=name), self.assertRaises(ValueError):
                self.recommend(name)
        self.assertEqual(plan.normalize_path(self.root, "tests\\new.py"), "tests/new.py")

    def test_deterministic_source_hashes_and_no_execution(self):
        self.project("src/Core")
        self.project("tests/Core.Tests", ["../../src/Core/Core.csproj"])
        with patch.object(plan.subprocess, "run", side_effect=AssertionError("must not execute")):
            first = self.recommend("src/Core/new.cs")
            second = self.recommend("src/Core/new.cs", "src/Core/new.cs")
        self.assertEqual(first, second)
        before = next(s["sha256"] for s in first["sources"] if s["path"] == plan.MATRIX)
        self.write(plan.MATRIX, "# Changed proof rules\n")
        after = next(s["sha256"] for s in self.recommend("src/Core/new.cs")["sources"] if s["path"] == plan.MATRIX)
        self.assertNotEqual(before, after)

    def test_git_collects_staged_unstaged_untracked_and_deleted(self):
        def git(*args):
            subprocess.run(["git", *args], cwd=self.root, check=True, capture_output=True)
        git("init")
        git("config", "user.name", "Fixture")
        git("config", "user.email", "fixture@example.invalid")
        for name in ("staged.py", "unstaged.py", "deleted.py"):
            self.write(name, "before\n")
        git("add", ".")
        git("commit", "-m", "fixture")
        self.write("staged.py", "after\n")
        git("add", "staged.py")
        self.write("unstaged.py", "after\n")
        (self.root / "deleted.py").unlink()
        self.write("untracked.py", "new\n")
        self.assertEqual(plan.collect_changed_paths(self.root), ["deleted.py", "staged.py", "unstaged.py", "untracked.py"])

    def test_cli_output_is_optional_and_never_overwrites_input(self):
        source = self.write("docs/change.md", "unchanged\n")
        args = argparse.Namespace(root=self.root, changed_file=["docs/change.md"], from_git=False,
                                  json=True, output=None)
        stdout = io.StringIO()
        with redirect_stdout(stdout):
            self.assertEqual(plan.run_cli(args), 0)
        self.assertEqual(json.loads(stdout.getvalue())["changedPaths"], ["docs/change.md"])
        self.assertEqual(source.read_text(), "unchanged\n")
        args.output = source
        with redirect_stderr(io.StringIO()):
            self.assertEqual(plan.run_cli(args), 1)
        self.assertEqual(source.read_text(), "unchanged\n")

    def test_msbuild_and_sdk_changes_select_broad_tests_and_track_command_runner(self):
        self.project("src/Core")
        first = self.project("tests/Core.Tests", ["../../src/Core/Core.csproj"])
        second = self.project("tests/Other.Tests")
        self.write("build/python/cli/buildctl.py", "# command runner\n")
        for changed in ("Directory.Packages.props", "custom.targets", "global.json"):
            with self.subTest(changed=changed):
                data = self.recommend(changed)
                commands = [c["argv"] for c in data["checks"]]
                self.assertTrue(any(first in argv for argv in commands))
                self.assertTrue(any(second in argv for argv in commands))
                self.assertTrue(any("evaluated coverage is unknown" in warning for warning in data["coverageWarnings"]))
                self.assertIn("build/python/cli/buildctl.py", [s["path"] for s in data["sources"]])

    def test_same_named_python_tests_have_unique_check_ids(self):
        self.write("build/scripts/ai/example.py", "pass")
        self.write("tests/scripts/test_example.py", "pass")
        self.write("build/scripts/ai/tests/test_example.py", "pass")
        data = self.recommend("build/scripts/ai/example.py")
        checks = [c for c in data["checks"] if c["id"].startswith("python-")]
        self.assertEqual(len(checks), 2)
        self.assertEqual(len({c["id"] for c in checks}), 2)

    def test_staged_only_whitespace_is_checked(self):
        def git(*args):
            return subprocess.run(["git", *args], cwd=self.root, check=True, capture_output=True)
        git("init")
        git("config", "user.name", "Fixture")
        git("config", "user.email", "fixture@example.invalid")
        self.write("docs/tracked.md", "before\n")
        git("add", ".")
        git("commit", "-m", "fixture")
        self.write("docs/tracked.md", "after  \n")
        git("add", "docs/tracked.md")
        data = self.recommend("docs/tracked.md")
        unstaged = next(c for c in data["checks"] if c["id"] == "diff-check")
        staged = next(c for c in data["checks"] if c["id"] == "diff-check-staged")
        self.assertEqual(subprocess.run(unstaged["argv"], cwd=self.root, capture_output=True).returncode, 0)
        self.assertNotEqual(subprocess.run(staged["argv"], cwd=self.root, capture_output=True).returncode, 0)

    def test_helper_executables_are_not_classified_as_test_projects(self):
        self.project("src/Core")
        helper = "tests/ProcessTestHelper/ProcessTestHelper.csproj"
        self.write(helper, '<Project><PropertyGroup><IsTestProject>false</IsTestProject></PropertyGroup>'
                           '<ItemGroup><ProjectReference Include="../../src/Core/Core.csproj"/>'
                           '<PackageReference Include="Microsoft.NET.Test.Sdk"/></ItemGroup></Project>')
        test_project = "tests/BySdk/BySdk.csproj"
        self.write(test_project, '<Project><ItemGroup><ProjectReference Include="../../src/Core/Core.csproj"/>'
                                 '<PackageReference Include="Microsoft.NET.Test.Sdk"/></ItemGroup></Project>')
        self.write("tests/Dynamic/Dynamic.csproj", '<Project><PropertyGroup>'
                   '<IsTestProject>$(TestMode)</IsTestProject></PropertyGroup></Project>')
        data = self.recommend("src/Core/File.cs")
        commands = [c["argv"] for c in data["checks"]]
        self.assertFalse(any(helper in argv for argv in commands))
        self.assertTrue(any(test_project in argv for argv in commands))
        self.assertTrue(any("dynamic IsTestProject" in warning for warning in data["coverageWarnings"]))

    def test_output_cannot_overwrite_changed_source_via_alias(self):
        real = self.write("docs/real.md", "unchanged\n")
        (self.root / "docs/alias.md").symlink_to(real)
        args = argparse.Namespace(root=self.root, changed_file=["docs/alias.md"], from_git=False,
                                  json=True, output=real)
        with redirect_stderr(io.StringIO()):
            self.assertEqual(plan.run_cli(args), 1)
        self.assertEqual(real.read_text(), "unchanged\n")

    def test_buildctl_parser_recommendation_does_not_require_optional_yaml(self):
        result = subprocess.run(["python", str(REPO / "build/python/cli/buildctl.py"), "recommend-validation",
                                 "--root", str(self.root), "--changed-file", "docs/new.md", "--json"],
                                capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(json.loads(result.stdout)["changedPaths"], ["docs/new.md"])


if __name__ == "__main__":
    unittest.main()
