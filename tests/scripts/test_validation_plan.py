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
        self.assertEqual(check["argv"], ["python", "build/python/cli/buildctl.py", "test", "--project", tests, "--queue",
                                         "--profile", plan.iteration_profile(tests, plan.profile_build_definitions(self.root))])
        direct = self.recommend("tests/Core.Tests/ThingTests.cs")
        self.assertEqual(len([c for c in direct["checks"] if c["id"].startswith("dotnet-")]), 1)

    def test_warm_recommendations_are_stable_and_separate_same_named_projects(self):
        projects = [self.project("tests/First/Example.Tests"), self.project("tests/Second/Example.Tests")]
        commands = [check["argv"] for check in self.recommend(*projects)["checks"] if check["id"].startswith("dotnet-")]
        profiles = [command[command.index("--profile") + 1] for command in commands]
        self.assertEqual(len(set(profiles)), 2)
        for project in projects:
            profile = plan.iteration_profile(project, {})
            self.assertEqual(profile, plan.iteration_profile(project, {}))
            self.assertLessEqual(len(profile.removeprefix("session:")), 64)
        self.assertTrue(all("--no-build" not in command for command in commands))

    def test_source_edits_reuse_profile_and_build_metadata_edits_rotate_it(self):
        project = self.project("tests/Example.Tests")
        source = "tests/Example.Tests/ThingTests.cs"
        self.write(source, "// before")

        def session():
            data = self.recommend(source)
            command = next(check["argv"] for check in data["checks"] if check["id"].startswith("dotnet-"))
            return command[command.index("--profile") + 1], data

        before, _ = session()
        self.write(source, "// after")
        self.assertEqual(session()[0], before)
        for path, contents in ((project, "<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>"),
                               ("Directory.Build.props", "<Project><!-- changed --></Project>"),
                               ("Meridian.sln", "new solution metadata"),
                               ("global.json", '{"sdk":{"version":"10.0.100"}}')):
            with self.subTest(path=path):
                self.write(path, contents)
                current, data = session()
                self.assertNotEqual(current, before)
                self.assertIn(path, [item["path"] for item in data["sources"]])
                before = current

    def test_profile_definition_scan_is_lazy_once_and_prunes_ignored_trees(self):
        projects = [self.project("tests/First.Tests"), self.project("tests/Second.Tests")]
        self.write("artifacts/ignored.csproj", "not XML")
        self.write("node_modules/ignored.props", "not XML")
        self.write("tests/First.Tests/obj/ignored.targets", "not XML")
        with patch.object(plan, "profile_build_definitions", wraps=plan.profile_build_definitions) as scan:
            data = self.recommend(*projects)
        self.assertEqual(scan.call_count, 1)
        self.assertFalse(any("ignored" in item["path"] for item in data["sources"]))
        with patch.object(plan, "profile_build_definitions", side_effect=AssertionError("unneeded definition scan")):
            self.recommend("docs/example.md")
            plan.recommend_validation(self.root, projects, fresh=True)

    def test_fresh_recommendations_retain_final_gate_and_require_builds(self):
        project = self.project("tests/Example.Tests")
        data = plan.recommend_validation(self.root, [project], fresh=True)
        command = next(check["argv"] for check in data["checks"] if check["id"].startswith("dotnet-"))
        self.assertEqual(data["iterationMode"], "fresh")
        self.assertIn("--fresh", command)
        self.assertNotIn("--profile", command)
        self.assertNotIn("--no-build", command)
        self.assertEqual(data["requiredFinalChecks"][0]["argv"], ["bash", "scripts/ci.sh"])
        self.assertFalse(data["requiredFinalChecks"][0]["required"])
        self.assertTrue(all(c["required"] for c in data["requiredFinalChecks"][1:]))

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

    def test_git_base_includes_committed_rename_and_dirty_paths(self):
        def git(*args):
            return subprocess.check_output(["git", *args], cwd=self.root, stderr=subprocess.PIPE).decode().strip()
        git("init")
        git("config", "user.name", "Fixture")
        git("config", "user.email", "fixture@example.invalid")
        self.write("docs/original name.md", "same content\n")
        git("add", ".")
        git("commit", "-m", "baseline")
        git("branch", "base")
        git("mv", "docs/original name.md", "new name.cs")
        git("commit", "-m", "move between subsystems")
        self.write("local script.py", "pass\n")
        self.assertEqual(plan.collect_changed_paths(self.root, "base"),
                         ["docs/original name.md", "local script.py", "new name.cs"])
        # Default remains local-only, so an explicit base has an observable purpose.
        self.assertEqual(plan.collect_changed_paths(self.root), ["local script.py"])

    def test_git_staged_rename_preserves_both_paths_and_unusual_names(self):
        def git(*args):
            subprocess.run(["git", *args], cwd=self.root, check=True, capture_output=True)
        git("init")
        git("config", "user.name", "Fixture")
        git("config", "user.email", "fixture@example.invalid")
        self.write("docs/old.md", "same content\n")
        git("add", ".")
        git("commit", "-m", "baseline")
        git("mv", "docs/old.md", "new.cs")
        self.write("unusual\nfile $(touch OWNED).py", "pass\n")
        self.assertEqual(plan.collect_changed_paths(self.root),
                         ["docs/old.md", "new.cs", "unusual\nfile $(touch OWNED).py"])
        self.assertFalse((self.root / "OWNED").exists())

    def test_invalid_base_fails_closed(self):
        subprocess.run(["git", "init"], cwd=self.root, check=True, capture_output=True)
        with self.assertRaisesRegex(ValueError, "cannot resolve base"):
            plan.collect_changed_paths(self.root, "--invalid-option")

    def test_node_launcher_selects_existing_focused_tests_without_execution(self):
        self.write("scripts/dev/web-dev.mjs", "export {}")
        self.write("scripts/dev/web-dev.test.mjs", "// fixture")
        self.write("scripts/dev/web-doctor.test.mjs", "// fixture")
        with patch.object(plan.subprocess, "run", side_effect=AssertionError("must not execute")):
            data = self.recommend("scripts/dev/web-dev.mjs", "scripts/dev/web-dev.test.mjs", "scripts/dev/web-doctor.test.mjs")
        checks = [c for c in data["checks"] if c["id"].startswith("node-")]
        self.assertEqual(len(checks), 2)
        dev = next(c for c in checks if c["argv"][-1] == "scripts/dev/web-dev.test.mjs")
        self.assertEqual(dev["argv"], ["node", "--test", "scripts/dev/web-dev.test.mjs"])
        self.assertEqual(len(dev["paths"]), 2)
        self.assertIn("scripts/dev/web-doctor.test.mjs", [s["path"] for s in data["sources"]])

    def test_missing_node_test_is_visible_without_invented_command(self):
        data = self.recommend("scripts/dev/unmapped.mjs")
        self.assertFalse(any(c["id"].startswith("node-") for c in data["checks"]))
        self.assertTrue(any("No exact Node test filename" in warning for warning in data["coverageWarnings"]))

    def test_docs_and_workflow_lanes_are_explicit_but_final_gate_retained(self):
        data = self.recommend("docs/engineering/README.md", ".github/workflows/ci.yml")
        commands = [c["argv"] for c in data["checks"]]
        self.assertIn(["bash", "scripts/ci.sh", "--lane", "verify-docs"], commands)
        self.assertIn(["bash", "scripts/ci.sh", "--lane", "verify-workflows"], commands)
        self.assertEqual(data["requiredFinalChecks"][0]["argv"], ["bash", "scripts/ci.sh"])

    def test_local_ci_optional_for_routine_and_required_for_ci_tooling(self):
        for changed in (["docs/a.md"], ["src/Meridian.Ui/dashboard/src/a.ts"]):
            data = self.recommend(*changed)
            local = data["requiredFinalChecks"][0]
            self.assertEqual(local["id"], "local-ci")
            self.assertFalse(local["required"])
            self.assertIn("Optional", local["reason"])
        for changed in ("scripts/ci.sh", ".github/workflows/ci.yml", "build/ci/lane-manifest.json",
                        "build/python/cli/validation_plan.py"):
            data = self.recommend(changed)
            self.assertTrue(data["requiredFinalChecks"][0]["required"], changed)
            self.assertIn("Required", data["requiredFinalChecks"][0]["reason"])
        data = self.recommend("docs/a.md")
        self.assertEqual(data["schemaVersion"], 1)
        self.assertIsInstance(data["requiredFinalChecks"], list)
        hosted = [c["name"] for c in data["requiredFinalChecks"] if c.get("kind") == "github"]
        self.assertEqual(hosted, ["Meridian CI / quality-gate", "Meridian CI / integration-gate"])

    def test_text_output_labels_final_checks_by_tier(self):
        def render(path):
            out = io.StringIO()
            args = argparse.Namespace(root=self.root, changed_file=[path], from_git=False, base=None,
                                      json=False, output=None, fresh=False)
            with redirect_stdout(out):
                self.assertEqual(plan.run_cli(args), 0)
            return out.getvalue()
        docs = render("docs/a.md")
        self.assertIn("Final checks (by risk tier): bash scripts/ci.sh (optional); Meridian CI / quality-gate", docs)
        self.assertNotIn("Required final checks", docs)
        self.assertIn("Final checks (by risk tier): bash scripts/ci.sh; Meridian CI", render("scripts/ci.sh"))

    def test_cli_base_argument_includes_branch_scope_and_never_runs_checks(self):
        def git(*args):
            subprocess.run(["git", *args], cwd=self.root, check=True, capture_output=True)
        git("init")
        git("config", "user.name", "Fixture")
        git("config", "user.email", "fixture@example.invalid")
        git("add", ".")
        git("commit", "-m", "baseline")
        git("branch", "base")
        self.write("committed.txt", "new\n")
        git("add", ".")
        git("commit", "-m", "change")
        before = subprocess.check_output(["git", "status", "--porcelain=v1", "-z"], cwd=self.root)
        result = subprocess.run(["python", str(REPO / "build/python/cli/buildctl.py"), "recommend-validation",
                                 "--root", str(self.root), "--base", "base", "--json"], capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(json.loads(result.stdout)["changedPaths"], ["committed.txt"])
        self.assertEqual(subprocess.check_output(["git", "status", "--porcelain=v1", "-z"], cwd=self.root), before)
        self.assertFalse((self.root / "artifacts").exists())

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

    def test_ai_docs_and_python_skip_unrelated_dotnet_graph(self):
        self.write("src/Broken/Broken.csproj", "<broken XML")
        self.write("build/scripts/ai/example.py", "pass")
        self.write("tests/scripts/test_example.py", "pass")
        for changed in ("docs/ai/tooling/throughput-pilot.md", "build/scripts/ai/example.py", "tests/scripts/test_example.py"):
            with self.subTest(changed=changed), patch.object(plan.ET, "fromstring", side_effect=AssertionError("XML must not be parsed")), \
                    patch.object(plan.os, "walk", side_effect=AssertionError("project trees must not be walked")):
                data = self.recommend(changed)
                self.assertFalse(any("Broken" in warning for warning in data["coverageWarnings"]))
                source_paths = {source["path"] for source in data["sources"]}
                self.assertNotIn(plan.REGISTRY, source_paths)
                self.assertNotIn("src/Broken/Broken.csproj", source_paths)
                self.assertNotIn("Directory.Build.props", source_paths)
                self.assertIn(plan.MATRIX, source_paths)

    def test_runtime_json_and_planned_asset_keep_dotnet_owner_coverage(self):
        self.project("src/Core")
        tests = self.project("tests/Core.Tests", ["../../src/Core/Core.csproj"])
        self.write("src/Core/Resources/runtime.json", "{}")
        for changed in ("src/Core/Resources/runtime.json", "src/Core/Resources/planned.json"):
            with self.subTest(changed=changed):
                data = self.recommend(changed)
                self.assertTrue(any(tests in check["argv"] for check in data["checks"]))
                self.assertIn("src/Core/Core.csproj", [source["path"] for source in data["sources"]])
                self.assertIn(plan.REGISTRY, [source["path"] for source in data["sources"]])

    def test_project_walk_prunes_previously_ignored_output_trees(self):
        self.project("src/Core")
        self.project("tests/Core.Tests", ["../../src/Core/Core.csproj"])
        for ignored in ("node_modules", "obj", "bin", "artifacts"):
            self.write(f"src/Core/{ignored}/Broken/Broken.csproj", "<invalid XML")
        data = self.recommend("src/Core/File.cs")
        self.assertFalse(any("Broken" in warning for warning in data["coverageWarnings"]))
        self.assertFalse(any("Broken" in source["path"] for source in data["sources"]))
        visited = []
        real_walk = plan.os.walk
        def observe_walk(*args, **kwargs):
            for directory, children, files in real_walk(*args, **kwargs):
                visited.append(Path(directory).relative_to(self.root).as_posix())
                yield directory, children, files
        with patch.object(plan.os, "walk", side_effect=observe_walk):
            self.recommend("src/Core/File.cs")
        self.assertFalse(any(set(Path(path).parts) & plan.IGNORED_PARTS for path in visited))

    def test_nuget_metadata_keeps_broad_dotnet_validation(self):
        self.project("src/Core")
        tests = self.project("tests/Core.Tests", ["../../src/Core/Core.csproj"])
        self.write("NuGet.Config", "<configuration/>")
        data = self.recommend("NuGet.Config")
        self.assertTrue(any(tests in check["argv"] for check in data["checks"]))
        self.assertTrue(any("evaluated coverage is unknown" in warning for warning in data["coverageWarnings"]))

    def test_buildctl_parser_recommendation_does_not_require_optional_yaml(self):
        result = subprocess.run(["python", str(REPO / "build/python/cli/buildctl.py"), "recommend-validation",
                                 "--root", str(self.root), "--changed-file", "docs/new.md", "--json"],
                                capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(json.loads(result.stdout)["changedPaths"], ["docs/new.md"])


if __name__ == "__main__":
    unittest.main()
