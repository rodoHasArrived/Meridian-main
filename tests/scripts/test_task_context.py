"""Behavior checks for bounded task packets and source freshness."""
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

SCRIPTS = Path(__file__).resolve().parents[2] / "build/scripts/ai"
sys.path.insert(0, str(SCRIPTS))
from task_context import build_packet, check_packet, main, markdown
from unittest.mock import patch


class TaskContextTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.addCleanup(self.temp.cleanup)
        self.write("AGENTS.md", "# Instructions\nPreserve audit evidence.\n")
        self.write("src/Meridian.Contracts/Example.cs", "public record Example(int Value);\n")
        self.write("src/Meridian.Contracts/README.md", "# Contracts\nShared transport contracts.\n")
        self.write("src/Meridian.Ui/dashboard/README.md", "# Browser consumer\n")
        self.write("src/Meridian.Wpf/README.md", "# WPF consumer\n")
        self.write("docs/source/data/source-modules.yml", """modules:
  - id: SRC-CONTRACTS
    path: src/Meridian.Contracts
    owner_lane: Contract Compatibility
    readme: src/Meridian.Contracts/README.md
    validation: ['dotnet test tests/Contracts.Tests']
""")

    def write(self, name, text):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")
        return path

    def packet(self, **kwargs):
        return build_packet(self.root, "Update shared contract Example", ["src/Meridian.Contracts/Example.cs"], **kwargs)

    def test_deterministic_and_owners_and_both_consumers(self):
        packet = self.packet()
        self.assertEqual(packet, self.packet())
        check_packet(self.root, packet)
        self.assertEqual(packet["moduleOwners"][0]["owner_lane"], "Contract Compatibility")
        self.assertIn("dotnet test tests/Contracts.Tests", packet["validation"])
        self.assertEqual(packet["scope"]["affectedConsumers"], ["src/Meridian.Ui/dashboard", "src/Meridian.Wpf"])
        self.assertIn("AGENTS.md", packet["instructions"])
        self.assertIn("never override", markdown(packet))

    def test_hash_detects_uncommitted_source_changes(self):
        packet = self.packet()
        self.write("src/Meridian.Contracts/Example.cs", "public record Example(long Value);\n")
        with self.assertRaisesRegex(ValueError, "stale"):
            check_packet(self.root, packet)

    def test_changes_in_instructions_or_new_instruction_invalidate(self):
        packet = self.packet()
        self.write("src/AGENTS.md", "Follow source-specific rules.\n")
        with self.assertRaisesRegex(ValueError, "stale"):
            check_packet(self.root, packet)
        packet = self.packet()
        self.write("AGENTS.md", "New instruction.\n")
        with self.assertRaisesRegex(ValueError, "stale"):
            check_packet(self.root, packet)

    def test_budget_bounds_actual_excerpts(self):
        self.write("src/Meridian.Contracts/README.md", "# Contract\n" + "A" * 2000)
        packet = self.packet(max_files=1, max_chars=256)
        self.assertEqual(len(packet["context"]), 1)
        self.assertEqual(packet["context"][0]["path"], "src/Meridian.Contracts/Example.cs")
        self.assertTrue(any(item["reason"].startswith("packet") for item in packet["omitted"]))
        self.assertLessEqual(sum(len(item["excerpt"]) for item in packet["context"]), 256)

    def test_tooling_guide_selected_before_large_optional_context(self):
        self.write("build/scripts/ai/example.py", "# AI guidance tool\n" + "X" * 8000)
        self.write("README.md", "# Large unrelated root guidance\n" + "R" * 10000)
        self.write("docs/ai/tooling/README.md", "# Tooling\nUse focused validation.\n")
        packet = build_packet(self.root, "Update AI guidance generator", ["build/scripts/ai/example.py"], max_files=2)
        self.assertEqual([item["path"] for item in packet["context"]], ["build/scripts/ai/example.py", "docs/ai/tooling/README.md"])
        self.assertLessEqual(max(len(item["excerpt"]) for item in packet["context"]), 3000)

    def test_host_guidance_scope_avoids_unrelated_product_sources(self):
        self.write("docs/ai/tooling/README.md", "# AI tooling\nUse focused proof.\n")
        self.write("docs/ai/generated/repo-navigation.json", json.dumps({
            "subsystems": [{"id": "ui", "keywords": ["guidance"],
                            "keyContracts": ["src/Meridian.Ui/dashboard/README.md"]}],
        }))
        targets = [".claude/skills/_shared/project-context.md", ".codex/skills/example.md",
                   ".agents/skills/example.md", ".github/agents/example.md", "AGENTS.md",
                   "CLAUDE.md", ".github/copilot-instructions.md"]
        for target in targets:
            with self.subTest(target=target):
                self.write(target, "# Host guidance\n## Useful Commands\nRelevant command.\n")
                packet = build_packet(self.root, "Update guidance under Useful Commands", [target])
                paths = [item["path"] for item in packet["context"]]
                self.assertEqual(paths[0], target)
                self.assertIn("docs/ai/tooling/README.md", paths)
                self.assertFalse(any(path.startswith("src/") for path in paths))
                self.assertTrue(any(item["reason"] == "outside explicit AI-tooling scope" for item in packet["omitted"]))

    def test_route_validation_contains_commands_not_script_ids(self):
        # Reuse the real maintained router rules, so symbolic script IDs cannot pass as commands.
        rules = SCRIPTS.parents[2] / "docs/ai/codex/prompt-route-rules.json"
        self.write("docs/ai/codex/prompt-route-rules.json", rules.read_text(encoding="utf-8"))
        packet = self.packet()
        route = packet["route"]["promptRoute"]
        self.assertTrue(route["validation_floor"])
        self.assertTrue(all(command in packet["validation"] for command in route["validation_floor"]))
        self.assertNotIn("prompt-route-linter", packet["validation"])
        self.assertIn("prompt-route-linter", route["validation_scripts"])

    def test_explicit_named_markdown_heading_beats_generic_keyword_hits(self):
        target = "docs/ai/shared-guidance.md"
        self.write(target, """# Shared Guidance
## Platform Snapshot
Update shared guidance and commands for the platform.
## Commands
Other unrelated commands.
## Useful Commands
The exact requested useful command section.
### Validation
Keep the relevant nested command proof.
## Source Documentation Mesh
Unrelated shared guidance update.
""")
        for task in ("Update shared guidance under ## Useful Commands",
                     "Update shared guidance under Useful Commands"):
            with self.subTest(task=task):
                packet = build_packet(self.root, task, [target])
                excerpt = packet["context"][0]
                self.assertEqual(excerpt["path"], target)
                self.assertEqual(excerpt["lineStart"], 6)
                self.assertTrue(excerpt["excerpt"].startswith("## Useful Commands\n"))
                self.assertIn("Keep the relevant nested command proof.", excerpt["excerpt"])
                self.assertNotIn("Platform Snapshot", excerpt["excerpt"])
                self.assertNotIn("Source Documentation Mesh", excerpt["excerpt"])

    def brief(self, **updates):
        return {
            "desiredBehavior": "Expose useful guidance at startup.",
            "acceptanceExample": "A developer opens a packet and sees the exact proof command.",
            "ownedPaths": ["src/Meridian.Contracts/Example.cs", "src/Meridian.Contracts/Future.cs"],
            "exclusions": ["Do not change persistence or WPF behavior."],
            "validationCommands": ["python -m unittest tests.test_guidance"],
            **updates,
        }

    def test_brief_details_are_rendered_and_owned_future_paths_planned(self):
        brief = self.brief()
        self.write("brief.json", json.dumps(brief))
        packet = build_packet(self.root, brief_path="brief.json")
        self.assertEqual(packet["objective"], brief["desiredBehavior"])
        self.assertEqual(packet["scope"]["targets"], ["src/Meridian.Contracts/Example.cs"])
        self.assertEqual(packet["scope"]["ownedPaths"], brief["ownedPaths"])
        self.assertIn("src/Meridian.Contracts/Future.cs", packet["validationPlan"]["changedPaths"])
        self.assertIn(brief["validationCommands"][0], packet["validation"])
        rendered = markdown(packet)
        self.assertIn(brief["acceptanceExample"], rendered)
        self.assertIn(brief["exclusions"][0], rendered)
        self.assertIn("Required final checks remain", rendered)
        self.assertTrue(any(source["path"] == "brief.json" for source in packet["sources"]))
        check_packet(self.root, packet)

    def test_brief_source_and_embedded_details_are_checked_against_current_file(self):
        brief = self.brief()
        self.write("brief.json", json.dumps(brief))
        packet = build_packet(self.root, brief_path="brief.json")
        altered = json.loads(json.dumps(packet))
        altered["brief"]["acceptanceExample"] = "Changed embedded assertion"
        with self.assertRaisesRegex(ValueError, "stale"):
            check_packet(self.root, altered)
        self.write("brief.json", json.dumps({**brief, "acceptanceExample": "Changed source assertion"}))
        with self.assertRaisesRegex(ValueError, "stale"):
            check_packet(self.root, packet)

    def test_planned_only_brief_requires_existing_anchor(self):
        brief = self.brief(ownedPaths=["src/Meridian.Contracts/Future.cs"])
        self.write("brief.json", json.dumps(brief))
        with self.assertRaisesRegex(ValueError, "--target-file"):
            build_packet(self.root, brief_path="brief.json")
        packet = build_packet(self.root, targets=["src/Meridian.Contracts/README.md"], brief_path="brief.json")
        self.assertEqual(packet["scope"]["targets"], ["src/Meridian.Contracts/README.md"])
        self.assertIn("src/Meridian.Contracts/Future.cs", packet["scope"]["ownedPaths"])

    def test_owned_directory_includes_its_own_readme_and_instructions(self):
        self.write("docs/README.md", "# Unrelated context anchor\n")
        self.write("src/NewModule/README.md", "# Module ownership and validation\n")
        for filename in ("AGENTS.md", "AGENTS.override.md"):
            self.write(f"src/NewModule/{filename}", "Module-specific instructions.\n")
        packet = build_packet(self.root, targets=["docs/README.md"],
                              brief=self.brief(ownedPaths=["src/NewModule"]))
        selected = [item["path"] for item in packet["context"]]
        self.assertIn("src/NewModule/README.md", selected)
        self.assertIn("src/NewModule/AGENTS.md", packet["instructions"])
        self.assertIn("src/NewModule/AGENTS.override.md", packet["instructions"])
        check_packet(self.root, packet)
        for filename in ("README.md", "AGENTS.md", "AGENTS.override.md"):
            with self.subTest(filename=filename):
                path = self.root / "src/NewModule" / filename
                original = path.read_text(encoding="utf-8")
                path.write_text(original + "Changed module authority.\n", encoding="utf-8")
                with self.assertRaisesRegex(ValueError, "stale"):
                    check_packet(self.root, packet)
                path.write_text(original, encoding="utf-8")

    def test_new_instruction_in_owned_directory_invalidates_packet(self):
        self.write("docs/README.md", "# Unrelated context anchor\n")
        (self.root / "src/NewModule").mkdir()
        packet = build_packet(self.root, targets=["docs/README.md"],
                              brief=self.brief(ownedPaths=["src/NewModule"]))
        self.write("src/NewModule/AGENTS.md", "New module-specific instructions.\n")
        with self.assertRaisesRegex(ValueError, "stale"):
            check_packet(self.root, packet)

    def test_planner_sources_and_buildctl_changes_invalidate(self):
        for name in ("build/python/cli/validation_plan.py", "build/python/cli/buildctl.py",
                     "docs/ai/codex/quickstart.md"):
            self.write(name, "# Existing planner input\n")
        for name in ("build/python/cli/validation_plan.py", "build/python/cli/buildctl.py",
                     "docs/ai/codex/quickstart.md"):
            with self.subTest(name=name):
                packet = self.packet()
                self.assertTrue(any(source["path"] == name for source in packet["sources"]))
                self.write(name, "# Changed planner input\n")
                with self.assertRaisesRegex(ValueError, "stale"):
                    check_packet(self.root, packet)
                self.write(name, "# Existing planner input\n")

    def test_cli_brief_file_alternative_and_source_collision(self):
        self.write("brief.json", json.dumps(self.brief()))
        command = [sys.executable, str(SCRIPTS / "task-context.py"), "--root", str(self.root)]
        result = subprocess.run(command + ["--brief-file", "brief.json", "--output", "artifacts/brief-packet.json"], capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        result = subprocess.run(command + ["--check", "artifacts/brief-packet.json"], capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        result = subprocess.run(command + ["--brief-file", "brief.json", "--task", "Ambiguous"], capture_output=True, text=True)
        self.assertNotEqual(result.returncode, 0)
        before = (self.root / "brief.json").read_bytes()
        result = subprocess.run(command + ["--brief-file", "brief.json", "--output", "brief.json"], capture_output=True, text=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(before, (self.root / "brief.json").read_bytes())

    def test_missing_optional_context_is_explicit(self):
        packet = self.packet()
        self.assertIn("docs/ai/generated/repo-navigation.json", packet["missingSources"])
        self.assertTrue(packet["warnings"])

    def test_missing_target_and_traversal_and_absolute_rejected(self):
        for name in ("missing.py", "../outside", "/etc/passwd", "C:\\Windows\\system.ini"):
            with self.subTest(name=name), self.assertRaises(ValueError):
                build_packet(self.root, "Update", [name])

    def test_symlink_source_and_output_escapes_rejected(self):
        with tempfile.TemporaryDirectory() as outside:
            external = Path(outside) / "secret.txt"
            external.write_text("private", encoding="utf-8")
            (self.root / "escape").symlink_to(outside, target_is_directory=True)
            with self.assertRaisesRegex(ValueError, "escapes"):
                build_packet(self.root, "Update", ["escape/secret.txt"])
            argv = ["packet", "--root", str(self.root), "--task", "Update", "--target-file", "AGENTS.md", "--output", "escape/out.json"]
            with patch.object(sys, "argv", argv):
                self.assertEqual(main(), 1)
            self.assertFalse((Path(outside) / "out.json").exists())

    def test_output_cannot_overwrite_input_or_other_output(self):
        original = (self.root / "AGENTS.md").read_bytes()
        base = ["packet", "--root", str(self.root), "--task", "Update", "--target-file", "AGENTS.md"]
        for args in (["--output", "AGENTS.md"], ["--output", "out.json", "--markdown-output", "out.json"]):
            with patch.object(sys, "argv", base + args):
                self.assertEqual(main(), 1)
        self.assertEqual(original, (self.root / "AGENTS.md").read_bytes())

    def test_cli_generation_and_freshness_check(self):
        command = [sys.executable, str(SCRIPTS / "task-context.py"), "--root", str(self.root)]
        result = subprocess.run(command + ["--task", "Update contract", "--target-file", "src/Meridian.Contracts/Example.cs", "--output", "artifacts/task.json", "--markdown-output", "artifacts/task.md"], capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        first = (self.root / "artifacts/task.json").read_bytes()
        result = subprocess.run(command + ["--check", "artifacts/task.json"], capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.write("src/Meridian.Contracts/Example.cs", "changed")
        result = subprocess.run(command + ["--check", "artifacts/task.json"], capture_output=True, text=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(first, (self.root / "artifacts/task.json").read_bytes())

    def test_revision_change_invalidates_even_unchanged_sources(self):
        def git(*args):
            result = subprocess.run(["git", "-C", str(self.root), *args], capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stderr)
        git("init", "--quiet")
        git("-c", "user.name=Test", "-c", "user.email=test@example.invalid", "commit", "--quiet", "--allow-empty", "-m", "initial")
        packet = self.packet()
        self.assertIsNotNone(packet["repository"]["revision"])
        git("-c", "user.name=Test", "-c", "user.email=test@example.invalid", "commit", "--quiet", "--allow-empty", "-m", "next")
        with self.assertRaisesRegex(ValueError, "stale"):
            check_packet(self.root, packet)

    def test_malformed_registry_fails_with_actionable_error(self):
        self.write("docs/source/data/source-modules.yml", "modules: [42]\n")
        with self.assertRaisesRegex(ValueError, "list of mappings"):
            self.packet()

    def test_routing_metadata_changes_invalidate(self):
        self.write("docs/ai/generated/repo-navigation.json", json.dumps({"subsystems": [], "taskRoutes": []}))
        packet = self.packet()
        self.write("docs/ai/generated/repo-navigation.json", json.dumps({"subsystems": [], "taskRoutes": [], "version": "new"}))
        with self.assertRaisesRegex(ValueError, "stale"):
            check_packet(self.root, packet)


if __name__ == "__main__":
    unittest.main()
