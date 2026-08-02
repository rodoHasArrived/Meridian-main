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

    def test_empty_frontmatter_is_rejected(self) -> None:
        with self.assertRaises(ValueError) as caught:
            module.parse_frontmatter("---\n\n---\n")

        self.assertIn("frontmatter is empty", str(caught.exception))

    def test_unexpected_indented_content_is_rejected(self) -> None:
        with self.assertRaises(ValueError) as caught:
            module.parse_frontmatter("---\nname: x\n  stray: value\n---\n")

        self.assertIn("unexpected indented content", str(caught.exception))

    def test_folded_block_is_joined_into_one_scalar(self) -> None:
        parsed = module.parse_frontmatter(
            "---\ndescription: >\n  first line\n  second line\nname: x\n---\n"
        )

        self.assertEqual("first line second line", parsed["description"])
        self.assertEqual("x", parsed["name"])

    def test_block_sequence_parses_as_a_list(self) -> None:
        parsed = module.parse_frontmatter("---\ntools:\n  - Read\n  - Glob\n---\n")

        self.assertEqual(["Read", "Glob"], parsed["tools"])

    def test_bare_key_with_no_items_is_an_empty_value_not_an_empty_list(self) -> None:
        parsed = module.parse_frontmatter("---\nname: x\ndescription:\n---\n")

        self.assertIsNone(parsed["description"])


class PyYamlDifferentialTests(unittest.TestCase):
    """Cross-check the hand-rolled parser against PyYAML wherever PyYAML exists.

    PyYAML is optional in this repository — `common.py` guards its import and the
    hosted docs lanes do not install it — so the validator parses the frontmatter
    subset itself and one code path runs everywhere. These tests keep that parser
    honest against the real implementation on machines that do have PyYAML.
    """

    def setUp(self) -> None:
        try:
            import yaml  # noqa: PLC0415 - optional dependency, probed deliberately
        except ImportError:  # pragma: no cover - exercised only on lanes without PyYAML
            self.skipTest("PyYAML is not installed in this environment")
        self.yaml = yaml

    def test_tracked_definitions_parse_the_same_as_pyyaml(self) -> None:
        for path in sorted(module.AGENTS_ROOT.rglob("*.md")):
            text = path.read_text(encoding="utf-8")
            ours = module.parse_frontmatter(text)
            theirs = self.yaml.safe_load(module.split_frontmatter(text))

            self.assertEqual(set(theirs), set(ours), path.name)
            for key, value in theirs.items():
                if isinstance(value, str):
                    self.assertEqual(value.strip(), str(ours[key]).strip(), f"{path.name}:{key}")
                else:
                    self.assertEqual(value, ours[key], f"{path.name}:{key}")

    def test_scalar_types_resolve_the_same_as_pyyaml(self) -> None:
        # Comparing only the tracked tree missed `description: null` resolving to the
        # string "null" here and to None in PyYAML, so the fixtures are explicit.
        for frontmatter in (
            "description: null\n",
            "description: ~\n",
            "description:\n",
            "description: Null\n",
            "tools: 123\n",
            "tools: 1.5\n",
            "tools: true\n",
            "tools: yes\n",
            "tools: off\n",
            'description: "null"\n',
            "description: 'null'\n",
            "tools: Read, Glob\n",
            "description: >\n  folded body\n",
            "tools:\n  - Read\n  - Glob\n",
        ):
            with self.subTest(frontmatter=frontmatter):
                ours = module.parse_frontmatter(f"---\n{frontmatter}---\n")
                theirs = self.yaml.safe_load(frontmatter)

                self.assertEqual(set(theirs), set(ours))
                for key, value in theirs.items():
                    self.assertEqual(type(value), type(ours[key]), key)
                    if isinstance(value, str):
                        self.assertEqual(value.strip(), ours[key].strip(), key)
                    else:
                        self.assertEqual(value, ours[key], key)

    def test_pyyaml_also_rejects_what_the_parser_rejects(self) -> None:
        for frontmatter in (
            "name: x\n: invalid yaml\n",
            "name: x\n  stray: value\n",
        ):
            with self.subTest(frontmatter=frontmatter):
                with self.assertRaises(ValueError):
                    module.parse_frontmatter(f"---\n{frontmatter}---\n")
                with self.assertRaises(self.yaml.YAMLError):
                    self.yaml.safe_load(frontmatter)


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

    def test_punctuation_only_tool_list_is_rejected(self) -> None:
        # Non-empty, so the emptiness check passes, but it splits to nothing - the same
        # effective empty grant this validator exists to prevent.
        path = write_agent(
            self.directory,
            "sample-agent",
            'name: sample-agent\ndescription: Does a thing.\ntools: ","',
        )

        errors = " | ".join(module.validate_agent(path))

        self.assertIn("contains no tool entries", errors)

    def test_misspelled_tools_key_is_rejected_because_it_fails_open(self) -> None:
        # Omitting `tools` inherits the default pool, so a typo here quietly turns a
        # read-only agent into one with edit and command access.
        path = write_agent(
            self.directory,
            "sample-agent",
            "name: sample-agent\ndescription: Does a thing.\ntool: Read, Glob",
        )

        errors = " | ".join(module.validate_agent(path))

        self.assertIn("unknown frontmatter key `tool`", errors)
        self.assertIn("did you mean `tools`?", errors)
        self.assertIn("widens the grant", errors)

    def test_misspelled_disallowed_tools_key_names_its_own_consequence(self) -> None:
        path = write_agent(
            self.directory,
            "sample-agent",
            "name: sample-agent\ndescription: Does a thing.\ndisalowedTools: Bash",
        )

        errors = " | ".join(module.validate_agent(path))

        self.assertIn("did you mean `disallowedTools`?", errors)
        self.assertIn("drops the restriction", errors)

    def test_unrelated_unknown_key_is_rejected_without_a_misleading_hint(self) -> None:
        path = write_agent(
            self.directory,
            "sample-agent",
            "name: sample-agent\ndescription: Does a thing.\ntools: Read\nwhatever: 1",
        )

        errors = " | ".join(module.validate_agent(path))

        self.assertIn("unknown frontmatter key `whatever`", errors)
        self.assertNotIn("did you mean", errors)

    def test_every_supported_host_field_is_accepted(self) -> None:
        # The allowlist has to cover the host's whole documented surface, not just the
        # fields this repository happens to use: this gate now runs on every agent
        # change, so an omitted-but-supported field would block legitimate work.
        path = write_agent(
            self.directory,
            "sample-agent",
            "name: sample-agent\ndescription: Does a thing.\ntools: Read\n"
            "disallowedTools: Bash\nmodel: opus\ncolor: blue\npermissionMode: plan\n"
            "skills: some-skill\nhooks: some-hook\nmemory: project\n"
            "background: true\nisolation: worktree",
        )

        self.assertEqual([], module.validate_agent(path))

    def test_null_description_is_rejected_rather_than_read_as_the_string_null(self) -> None:
        # The host's parser resolves this to null and the agent loses its routing
        # description; a parser that kept "null" as text would validate it clean.
        for literal in ("null", "~", "Null"):
            with self.subTest(literal=literal):
                path = write_agent(
                    self.directory,
                    "sample-agent",
                    f"name: sample-agent\ndescription: {literal}\ntools: Read",
                )

                errors = " | ".join(module.validate_agent(path))

                self.assertIn("`description` is empty", errors)

    def test_quoted_null_stays_a_real_string(self) -> None:
        path = write_agent(
            self.directory,
            "sample-agent",
            'name: sample-agent\ndescription: "null"\ntools: Read',
        )

        self.assertEqual([], module.validate_agent(path))

    def test_non_string_tools_scalar_is_reported_by_type(self) -> None:
        for literal, rendered in (("123", "int"), ("true", "bool"), ("1.5", "float")):
            with self.subTest(literal=literal):
                path = write_agent(
                    self.directory,
                    "sample-agent",
                    f"name: sample-agent\ndescription: Does a thing.\ntools: {literal}",
                )

                errors = " | ".join(module.validate_agent(path))

                self.assertIn(f"is a {rendered}", errors)

    def test_unterminated_quoted_scalar_is_rejected(self) -> None:
        # A real YAML parser fails the whole document; returning it as a plain string
        # would pass a definition the host cannot load at all.
        path = write_agent(
            self.directory,
            "sample-agent",
            'name: sample-agent\ndescription: "unterminated\ntools: Read',
        )

        errors = " | ".join(module.validate_agent(path))

        self.assertIn("unterminated", errors)

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
