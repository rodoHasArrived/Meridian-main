from __future__ import annotations

import argparse
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch


MODULE_PATH = Path(__file__).resolve().parents[2] / "build/python/cli/build_profiles.py"
spec = importlib.util.spec_from_file_location("build_profiles_under_test", MODULE_PATH)
assert spec is not None and spec.loader is not None
profiles = importlib.util.module_from_spec(spec)
spec.loader.exec_module(profiles)


class BuildProfileTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.project = self.root / "src/Example/Example.csproj"
        self.project.parent.mkdir(parents=True)
        self.project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>')
        (self.root / "Directory.Build.props").write_text("<Project />")

    def args(self, **updates) -> argparse.Namespace:
        defaults = dict(profile="worktree", project="src/Example/Example.csproj", framework=None,
                        configuration="Debug", runtime=None, full_wpf_build=False,
                        property=[], skip_restore=False, no_build=False)
        defaults.update(updates)
        return argparse.Namespace(**defaults)

    def prepare(self, sdk="10.0.100", **updates) -> dict:
        return profiles.prepare_profile(self.root, self.args(**updates), sdk)

    def build_successfully(self, profile: dict) -> None:
        for kind in ("bin", "obj"):
            (self.root / "artifacts" / kind / profile["isolationKey"]).mkdir(parents=True, exist_ok=True)
        profiles.mark_profile_built(profile)

    def test_unchanged_and_single_source_edit_reuse_profile(self) -> None:
        initial = self.prepare()
        self.build_successfully(initial)
        self.assertEqual(initial, self.prepare())
        (self.project.parent / "Example.cs").write_text("public class Example { public int Value => 1; }")
        self.assertEqual(initial, self.prepare())
        (self.project.parent / "Example.cs").write_text("public class Example { public int Value => 2; }")
        self.assertEqual(initial, self.prepare(no_build=True))

    def test_session_names_and_worktrees_have_distinct_keys(self) -> None:
        worktree = self.prepare()
        session = self.prepare(profile="session:feature-one")
        self.assertNotEqual(worktree["isolationKey"], session["isolationKey"])
        other = self.root / "other"
        (other / "src/Example").mkdir(parents=True)
        (other / "src/Example/Example.csproj").write_bytes(self.project.read_bytes())
        second = profiles.prepare_profile(other, self.args(profile="session:feature-one"), "10.0.100")
        self.assertNotEqual(session["isolationKey"], second["isolationKey"])

    def test_compatibility_matrix_rejects_mismatches_without_overwrite(self) -> None:
        initial = self.prepare()
        original = Path(initial["manifestPath"]).read_bytes()
        changes = [({"sdk": "10.0.101"}, "sdkVersion"), ({"framework": "net9.0"}, "framework"),
                   ({"configuration": "Release"}, "configuration"), ({"runtime": "linux-x64"}, "runtime"),
                   ({"full_wpf_build": True}, "fullWpfBuild"), ({"property": ["UseAppHost=false"]}, "properties")]
        for change, field in changes:
            with self.subTest(field=field), self.assertRaisesRegex(ValueError, field):
                self.prepare(**change)
            self.assertEqual(original, Path(initial["manifestPath"]).read_bytes())

    def test_project_and_build_definition_changes_invalidate_inferred_framework(self) -> None:
        initial = self.prepare()
        for path in (self.project, self.root / "Directory.Build.props", self.root / "global.json"):
            before = path.read_bytes() if path.exists() else None
            with self.subTest(path=path):
                path.write_text("changed definition")
                with self.assertRaisesRegex(ValueError, "buildDefinitions"):
                    self.prepare()
                if before is None:
                    path.unlink()
                else:
                    path.write_bytes(before)
        alternate = self.project.with_name("Other.csproj")
        alternate.write_bytes(self.project.read_bytes())
        with self.assertRaisesRegex(ValueError, "project"):
            self.prepare(project="src/Example/Other.csproj")
        self.assertFalse(initial["built"])

    def test_generated_build_definitions_do_not_invalidate_profile(self) -> None:
        initial = self.prepare()
        for relative in ("artifacts/obj/generated.props", "src/Example/obj/generated.props",
                         "src/Example/bin-old/generated.targets", "src/Example/Example_abcd_wpftmp.csproj",
                         "src/Example/node_modules/dependency.props", ".ai/run/generated.props"):
            path = self.root / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("generated")
        self.assertEqual(initial, self.prepare())

    def test_solution_profile_detects_benchmark_project_changes(self) -> None:
        (self.root / "Example.sln").write_text("benchmark solution")
        benchmark = self.root / "benchmarks/Example/Example.csproj"
        benchmark.parent.mkdir(parents=True)
        benchmark.write_bytes(self.project.read_bytes())
        self.prepare(project="Example.sln")
        benchmark.write_text('<Project><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>')
        with self.assertRaisesRegex(ValueError, "buildDefinitions"):
            self.prepare(project="Example.sln")

    def test_normalized_properties_allow_reordering_case_and_last_wins(self) -> None:
        initial = self.prepare(property=["UseAppHost=true", "/p:UseAppHost=false", "-p:DebugType=portable"])
        self.assertEqual(initial, self.prepare(property=["/P:DEBUGTYPE=portable", "useapphost=false"]))

    def test_profile_properties_reject_managed_and_compound_assignments(self) -> None:
        rejected = ["Configuration=Release", "TargetFramework=net9.0", "RuntimeIdentifier=linux-x64",
                    "MeridianBuildIsolationKey=other", "BaseOutputPath=/tmp/out", "OutDir=/tmp/out",
                    "BaseIntermediateOutputPath=/tmp/obj", "MSBuildProjectExtensionsPath=/tmp/obj",
                    "IntermediateOutputPath=/tmp/obj", "UseArtifactsOutput=true", "EnableFullWpfBuild=true",
                    "Foo=one;Configuration=Release", "Foo=one,OutDir=/tmp", "Foo", "-property:Foo=bar"]
        for assignment in rejected:
            with self.subTest(assignment=assignment), self.assertRaises(ValueError):
                self.prepare(property=[assignment])
        self.assertFalse((self.root / ".ai").exists())

    def test_skip_options_require_successful_prior_build(self) -> None:
        for option in ("skip_restore", "no_build"):
            with self.subTest(option=option), self.assertRaisesRegex(ValueError, "successful restore and build"):
                self.prepare(**{option: True})
        profile = self.prepare()
        with self.assertRaisesRegex(ValueError, "unbuilt profile"):
            self.prepare(no_build=True)
        self.build_successfully(profile)
        self.assertTrue(self.prepare(skip_restore=True)["built"])
        self.assertTrue(self.prepare(no_build=True)["built"])
        profiles.mark_profile_build_started(profile)
        with self.assertRaisesRegex(ValueError, "unbuilt profile"):
            self.prepare(no_build=True)
        self.assertFalse(self.prepare()["built"])

    def test_profile_properties_cannot_override_test_no_build(self) -> None:
        for assignment in ("VSTestNoBuild=false", "/p:vstestnobuild=false", "-P:VSTESTNOBUILD=false"):
            with self.subTest(assignment=assignment), self.assertRaisesRegex(ValueError, "vstestnobuild.*managed"):
                self.prepare(property=[assignment])
        self.assertFalse((self.root / ".ai").exists())

    def test_skip_options_reject_missing_output_after_success(self) -> None:
        profile = self.prepare()
        profiles.mark_profile_built(profile)
        with self.assertRaisesRegex(ValueError, "output is missing"):
            self.prepare(no_build=True)

    def test_corrupt_unknown_schema_or_identity_manifest_fails_safe(self) -> None:
        profile = self.prepare()
        path = Path(profile["manifestPath"])
        invalid = ["{", "[]", json.dumps({**profile, "schemaVersion": 999}),
                   json.dumps({**profile, "built": "true"}), json.dumps({**profile, "profileName": "different"})]
        for content in invalid:
            with self.subTest(content=content):
                path.write_text(content)
                with self.assertRaises(ValueError):
                    self.prepare()
                self.assertEqual(content, path.read_text())

    def test_existing_outputs_without_manifest_are_not_adopted(self) -> None:
        profile = self.prepare()
        self.build_successfully(profile)
        Path(profile["manifestPath"]).unlink()
        with self.assertRaisesRegex(ValueError, "without a manifest"):
            self.prepare()

    def test_missing_nullable_compatibility_field_is_not_accepted(self) -> None:
        profile = self.prepare()
        del profile["compatibility"]["framework"]
        Path(profile["manifestPath"]).write_text(json.dumps(profile))
        with self.assertRaisesRegex(ValueError, "framework"):
            self.prepare()

    def test_output_symlink_cannot_alias_another_profile(self) -> None:
        profile = self.prepare()
        other_output = self.root / "other-output"
        other_output.mkdir()
        root = self.root / "artifacts/bin"
        root.mkdir(parents=True)
        try:
            (root / profile["isolationKey"]).symlink_to(other_output, target_is_directory=True)
        except OSError as exc:
            self.skipTest(f"Directory symlinks unavailable: {exc}")
        with self.assertRaisesRegex(ValueError, "symlink"):
            self.prepare()

    def test_atomic_update_failure_preserves_previous_manifest(self) -> None:
        profile = self.prepare()
        original = Path(profile["manifestPath"]).read_bytes()
        with patch.object(profiles.os, "replace", side_effect=OSError("interrupted")):
            with self.assertRaises(OSError):
                profiles.mark_profile_built(profile)
        self.assertEqual(original, Path(profile["manifestPath"]).read_bytes())
        self.assertEqual([Path(profile["manifestPath"])], list(Path(profile["manifestPath"]).parent.iterdir()))

    def test_rejects_invalid_profile_sdk_and_external_project(self) -> None:
        for name in (None, "session:", "session:../bad", "session:name/child", "other"):
            with self.subTest(name=name), self.assertRaises(ValueError):
                self.prepare(profile=name)
        with self.assertRaisesRegex(ValueError, "SDK"):
            self.prepare(sdk="")
        with self.assertRaisesRegex(ValueError, "existing file inside"):
            self.prepare(project="../Other.csproj")


if __name__ == "__main__":
    unittest.main()
