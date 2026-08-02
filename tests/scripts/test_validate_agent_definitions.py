import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
MODULE_PATH = ROOT / "build" / "scripts" / "docs" / "validate-agent-definitions.py"

spec = importlib.util.spec_from_file_location("validate_agent_definitions", MODULE_PATH)
assert spec is not None and spec.loader is not None
module = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = module
spec.loader.exec_module(module)


def write_agent(directory: Path, stem: str, frontmatter: str, body: str = "\n# Agent\n") -> Path:
    path = directory / f"{stem}.md"
    path.write_text(f"---\n{frontmatter}\n---\n{body}", encoding="utf-8")
    return path


class TrackedAgentDefinitionsTests(unittest.TestCase):
    """The repository's own definitions must stay valid, since the host silently
    refuses to launch a subagent whose declared tools do not resolve."""

    def test_tracked_agent_definitions_are_valid(self) -> None:
        errors: list[str] = []
        for path in sorted(module.AGENTS_ROOT.rglob("*.md")):
            errors.extend(module.validate_agent(path))

        self.assertEqual([], errors)

    def test_agents_whose_workflow_runs_commands_are_granted_bash(self) -> None:
        # Edit and Write cannot run a test suite or move a file, so an agent whose
        # own workflow mandates one of those needs Bash or it can only report
        # unvalidated work.
        for stem in (
            "meridian-test-writer",
            "meridian-provider-builder",
            "meridian-browser-workstation",
            "meridian-cleanup",
            "meridian-archive-organizer",
            "meridian-docs",
            "meridian-implementation-assurance",
        ):
            path = module.AGENTS_ROOT / f"{stem}.md"
            frontmatter = module.parse_frontmatter(path.read_text(encoding="utf-8"))
            entries, problem = module.parse_tool_list(frontmatter["tools"])

            self.assertIsNone(problem, stem)
            self.assertIn("Bash", entries, stem)

    def test_findings_only_agents_are_not_granted_write_access(self) -> None:
        for stem in (
            "meridian-code-review",
            "meridian-repo-navigation",
            "meridian-simulated-user-panel",
        ):
            path = module.AGENTS_ROOT / f"{stem}.md"
            frontmatter = module.parse_frontmatter(path.read_text(encoding="utf-8"))
            entries, problem = module.parse_tool_list(frontmatter["tools"])

            self.assertIsNone(problem, stem)
            self.assertNotIn("Edit", entries, stem)
            self.assertNotIn("Write", entries, stem)
            self.assertNotIn("Bash", entries, stem)


class KnownToolsTests(unittest.TestCase):
    def test_known_tools_is_pinned(self) -> None:
        # No machine-readable host schema exists to source this from, so the set is
        # pinned here: adding or removing a name fails this test until the change is
        # made deliberately in both places.
        self.assertEqual(
            {
                "Agent",
                "Artifact",
                "AskUserQuestion",
                "Bash",
                "BashOutput",
                "Edit",
                "EnterPlanMode",
                "EnterWorktree",
                "ExitPlanMode",
                "ExitWorktree",
                "Glob",
                "Grep",
                "KillShell",
                "ListMcpResourcesTool",
                "Monitor",
                "NotebookEdit",
                "PowerShell",
                "Read",
                "ReadMcpResourceTool",
                "SendMessage",
                "Skill",
                "SlashCommand",
                "Task",
                "TaskCreate",
                "TaskGet",
                "TaskList",
                "TaskOutput",
                "TaskStop",
                "TaskUpdate",
                "TodoWrite",
                "ToolSearch",
                "WebFetch",
                "WebSearch",
                "Workflow",
                "Write",
            },
            set(module.KNOWN_TOOLS),
        )


class ToolListParsingTests(unittest.TestCase):
    def test_comma_separated_string_splits_into_entries(self) -> None:
        entries, problem = module.parse_tool_list("Read, Glob, Grep")

        self.assertIsNone(problem)
        self.assertEqual(["Read", "Glob", "Grep"], entries)

    def test_yaml_sequence_is_rejected_with_the_string_form_hint(self) -> None:
        entries, problem = module.parse_tool_list(["read", "search", "edit", "mcp"])

        self.assertEqual([], entries)
        assert problem is not None
        self.assertIn("YAML sequence", problem)
        self.assertIn("Read, Glob, Grep", problem)

    def test_empty_value_is_rejected_rather_than_read_as_no_grant(self) -> None:
        _, problem = module.parse_tool_list("   ")

        assert problem is not None
        self.assertIn("omit the field entirely", problem)

    def test_non_string_scalar_is_rejected(self) -> None:
        _, problem = module.parse_tool_list(7)

        assert problem is not None
        self.assertIn("expected a comma-separated string", problem)

    def test_scoped_entry_keeps_its_inner_commas(self) -> None:
        entries, problem = module.parse_tool_list("Agent(worker, researcher), Read")

        self.assertIsNone(problem)
        self.assertEqual(["Agent(worker, researcher)", "Read"], entries)

    def test_unbalanced_parenthesis_is_reported(self) -> None:
        _, problem = module.parse_tool_list("Bash(git:*, Read")

        assert problem is not None
        self.assertIn("unbalanced", problem)

    def test_entry_head_strips_a_scope(self) -> None:
        self.assertEqual(("Bash", None), module.entry_head("Bash(git:*)"))
        self.assertEqual(("Read", None), module.entry_head("Read"))

    def test_entry_head_reports_an_unterminated_scope(self) -> None:
        _, problem = module.entry_head("Bash(git:*")

        assert problem is not None
        self.assertIn("unterminated scope", problem)


class FrontmatterParsingTests(unittest.TestCase):
    def test_missing_opening_fence_is_rejected(self) -> None:
        with self.assertRaises(ValueError) as caught:
            module.parse_frontmatter("name: x\n")

        self.assertIn("missing opening frontmatter fence", str(caught.exception))

    def test_missing_closing_fence_is_rejected(self) -> None:
        with self.assertRaises(ValueError) as caught:
            module.parse_frontmatter("---\nname: x\n")

        self.assertIn("missing closing frontmatter fence", str(caught.exception))

    def test_fenced_but_invalid_yaml_is_rejected(self) -> None:
        # Fences alone are not well-formedness: the host loads this with a real
        # parser and would fail to load the agent at all.
        with self.assertRaises(ValueError) as caught:
            module.parse_frontmatter("---\nname: x\n: invalid yaml\n---\n")

        self.assertIn("not valid YAML", str(caught.exception))

    def test_duplicate_key_is_rejected(self) -> None:
        # A first-textual-match check would validate the first value while the host
        # resolves the last, so the two can disagree without anything failing.
        with self.assertRaises(ValueError) as caught:
            module.parse_frontmatter(
                "---\nname: x\ntools: Read\ntools: [\"read\"]\n---\n"
            )

        self.assertIn("duplicate key", str(caught.exception))

    def test_non_mapping_frontmatter_is_rejected(self) -> None:
        with self.assertRaises(ValueError) as caught:
            module.parse_frontmatter("---\n- one\n- two\n---\n")

        self.assertIn("expected a mapping", str(caught.exception))


class ValidateAgentTests(unittest.TestCase):
    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.directory = Path(self._tmp.name)

    def test_valid_definition_produces_no_errors(self) -> None:
        path = write_agent(
            self.directory,
            "sample-agent",
            "name: sample-agent\ndescription: Does a thing.\ntools: Read, Glob, Grep",
        )

        self.assertEqual([], module.validate_agent(path))

    def test_lowercase_pseudo_names_are_rejected_with_hints(self) -> None:
        path = write_agent(
            self.directory,
            "sample-agent",
            "name: sample-agent\ndescription: Does a thing.\ntools: read, search, edit, mcp",
        )

        errors = " | ".join(module.validate_agent(path))

        self.assertIn("did you mean `Read`?", errors)
        self.assertIn("use `Glob, Grep`", errors)
        self.assertIn("did you mean `Edit`?", errors)
        self.assertIn("use an `mcp__<server>` pattern", errors)

    def test_name_must_match_the_filename(self) -> None:
        path = write_agent(
            self.directory,
            "sample-agent",
            "name: other-agent\ndescription: Does a thing.",
        )

        errors = " | ".join(module.validate_agent(path))

        self.assertIn("does not match the filename", errors)

    def test_name_must_be_kebab_case(self) -> None:
        path = write_agent(
            self.directory,
            "Sample_Agent",
            "name: Sample_Agent\ndescription: Does a thing.",
        )

        errors = " | ".join(module.validate_agent(path))

        self.assertIn("is not kebab-case", errors)

    def test_empty_folded_description_is_rejected(self) -> None:
        # `description: >` with no body parses to an empty string; a regex over the
        # raw text would see the literal `>` and call the field present.
        path = write_agent(
            self.directory,
            "sample-agent",
            "name: sample-agent\ndescription: >\ntools: Read",
        )

        errors = " | ".join(module.validate_agent(path))

        self.assertIn("`description` is empty", errors)

    def test_missing_description_is_rejected(self) -> None:
        path = write_agent(self.directory, "sample-agent", "name: sample-agent\ntools: Read")

        errors = " | ".join(module.validate_agent(path))

        self.assertIn("missing `description`", errors)

    def test_scoped_entries_validate_their_head_name(self) -> None:
        path = write_agent(
            self.directory,
            "sample-agent",
            "name: sample-agent\ndescription: Does a thing.\n"
            "tools: Agent(worker, researcher), Bash(git:*)",
        )

        self.assertEqual([], module.validate_agent(path))

    def test_scoped_entry_with_an_unknown_head_is_rejected(self) -> None:
        path = write_agent(
            self.directory,
            "sample-agent",
            "name: sample-agent\ndescription: Does a thing.\ntools: shell(git:*)",
        )

        errors = " | ".join(module.validate_agent(path))

        self.assertIn("'shell' is not a known tool", errors)

    def test_server_scoped_mcp_patterns_pass_in_both_fields(self) -> None:
        path = write_agent(
            self.directory,
            "sample-agent",
            "name: sample-agent\ndescription: Does a thing.\n"
            "tools: mcp__github, mcp__github__get_me, mcp__github__*\n"
            "disallowedTools: mcp__github__delete_file",
        )

        self.assertEqual([], module.validate_agent(path))

    def test_all_server_wildcard_is_rejected_in_tools(self) -> None:
        # As an allow-list entry it resolves to nothing when no server is connected,
        # which is the empty grant this validator exists to catch.
        path = write_agent(
            self.directory,
            "sample-agent",
            "name: sample-agent\ndescription: Does a thing.\ntools: mcp__*",
        )

        errors = " | ".join(module.validate_agent(path))

        self.assertIn("only valid in `disallowedTools`", errors)

    def test_all_server_wildcard_is_accepted_in_disallowed_tools(self) -> None:
        path = write_agent(
            self.directory,
            "sample-agent",
            "name: sample-agent\ndescription: Does a thing.\ndisallowedTools: mcp__*",
        )

        self.assertEqual([], module.validate_agent(path))

    def test_yaml_sequence_tools_are_rejected(self) -> None:
        path = write_agent(
            self.directory,
            "sample-agent",
            'name: sample-agent\ndescription: Does a thing.\ntools: ["Read", "Glob"]',
        )

        errors = " | ".join(module.validate_agent(path))

        self.assertIn("is a YAML sequence", errors)

    def test_omitted_tools_field_is_allowed(self) -> None:
        path = write_agent(
            self.directory, "sample-agent", "name: sample-agent\ndescription: Does a thing."
        )

        self.assertEqual([], module.validate_agent(path))


class DiscoveryTests(unittest.TestCase):
    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.directory = Path(self._tmp.name)
        self._original_root = module.AGENTS_ROOT
        self.addCleanup(setattr, module, "AGENTS_ROOT", self._original_root)

    def test_missing_directory_fails_closed(self) -> None:
        module.AGENTS_ROOT = self.directory / "absent"

        self.assertEqual(1, module.main())

    def test_empty_directory_fails_closed(self) -> None:
        module.AGENTS_ROOT = self.directory

        self.assertEqual(1, module.main())

    def test_nested_definitions_are_discovered(self) -> None:
        # The host discovers agent files recursively, so a definition filed in a
        # subfolder must not escape validation.
        nested = self.directory / "lanes"
        nested.mkdir()
        write_agent(nested, "nested-agent", "name: nested-agent\ndescription: x\ntools: bogus")
        module.AGENTS_ROOT = self.directory

        self.assertEqual(1, module.main())


if __name__ == "__main__":
    unittest.main()
