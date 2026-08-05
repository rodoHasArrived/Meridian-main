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
            # AGENTS.md:131 makes `git status --short` a start gate for any agent that
            # edits, so every write-capable agent needs command access too.
            "meridian-blueprint",
            "meridian-brainstorm",
            "meridian-roadmap-strategist",
        ):
            path = module.AGENTS_ROOT / f"{stem}.md"
            frontmatter = module.parse_frontmatter(path.read_text(encoding="utf-8"))
            entries, problem = module.parse_tool_list(frontmatter["tools"])

            self.assertIsNone(problem, stem)
            self.assertIn("Bash", entries, stem)

    def test_findings_only_agents_cannot_write(self) -> None:
        # Two shapes reach the same posture, and both are legitimate. An allow-list
        # that omits the writers is fail-closed against tools the host adds later; a
        # deny-list naming them keeps session-provided MCP reachable, which
        # repo-navigation's skill requires. Assert the outcome, not the mechanism.
        writers = ("Edit", "Write", "Bash", "NotebookEdit")
        for stem in (
            "meridian-code-review",
            "meridian-repo-navigation",
            "meridian-simulated-user-panel",
        ):
            path = module.AGENTS_ROOT / f"{stem}.md"
            frontmatter = module.parse_frontmatter(path.read_text(encoding="utf-8"))

            if "tools" in frontmatter:
                entries, problem = module.parse_tool_list(frontmatter["tools"])
                self.assertIsNone(problem, stem)
                heads = [module.entry_head(entry)[0] for entry in entries]
                for writer in writers:
                    self.assertNotIn(writer, heads, f"{stem} allow-lists {writer}")
            else:
                denied, problem = module.parse_tool_list(frontmatter["disallowedTools"])
                self.assertIsNone(problem, stem)
                heads = [module.entry_head(entry)[0] for entry in denied]
                for writer in writers:
                    self.assertIn(writer, heads, f"{stem} inherits {writer} undenied")

    def test_repo_navigation_keeps_session_mcp_reachable(self) -> None:
        # Its skill directs it to prefer mdc://repo-navigation/* and find-subsystem
        # when the session provides them; an allow-list would suppress all of them,
        # and they cannot be allow-listed because MCP servers are a session property.
        path = module.AGENTS_ROOT / "meridian-repo-navigation.md"
        frontmatter = module.parse_frontmatter(path.read_text(encoding="utf-8"))

        self.assertNotIn("tools", frontmatter)
        self.assertIn("disallowedTools", frontmatter)

    def test_code_review_holds_no_command_tool_at_all(self) -> None:
        # A scoped git grant was considered and rejected: Claude Code scoping is a
        # command prefix match, so `Bash(git diff:*)` also admits
        # `git diff --output=<file>`, which writes. With no Bash of any shape, the
        # "findings only - no edits" posture is a property of the tool set rather than
        # of an instruction, which is why this asserts absence rather than scoping.
        path = module.AGENTS_ROOT / "meridian-code-review.md"
        frontmatter = module.parse_frontmatter(path.read_text(encoding="utf-8"))
        entries, problem = module.parse_tool_list(frontmatter["tools"])

        self.assertIsNone(problem)
        self.assertEqual(["Read", "Glob", "Grep"], entries)

    def test_simulated_user_panel_can_launch_personas_but_not_write(self) -> None:
        # Its contract offers independent persona voices on explicit request; without
        # Agent a requested panel would collapse to one voice while still calling
        # itself a panel.
        path = module.AGENTS_ROOT / "meridian-simulated-user-panel.md"
        frontmatter = module.parse_frontmatter(path.read_text(encoding="utf-8"))
        entries, problem = module.parse_tool_list(frontmatter["tools"])

        self.assertIsNone(problem)
        self.assertIn("Agent", entries)
        for forbidden in ("Edit", "Write", "Bash"):
            self.assertNotIn(forbidden, entries)


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

        self.assertIn("not valid YAML", str(caught.exception))

    def test_folded_block_is_joined_into_one_scalar(self) -> None:
        parsed = module.parse_frontmatter(
            "---\ndescription: >\n  first line\n  second line\nname: x\n---\n"
        )

        self.assertEqual("first line second line", parsed["description"].strip())
        self.assertEqual("x", parsed["name"])

    def test_block_sequence_parses_as_a_list(self) -> None:
        parsed = module.parse_frontmatter("---\ntools:\n  - Read\n  - Glob\n---\n")

        self.assertEqual(["Read", "Glob"], parsed["tools"])

    def test_bare_key_with_no_items_is_an_empty_value_not_an_empty_list(self) -> None:
        parsed = module.parse_frontmatter("---\nname: x\ndescription:\n---\n")

        self.assertIsNone(parsed["description"])


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
            # A built-in alongside them, since an MCP-only allow-list is its own error.
            "tools: Read, mcp__github, mcp__github__get_me, mcp__github__*\n"
            "disallowedTools: mcp__github__delete_file",
        )

        self.assertEqual([], module.validate_agent(path))

    def test_partial_tool_wildcard_is_accepted(self) -> None:
        # `mcp__github__get_*` is documented as matching that server's `get_` tools, and
        # the first pattern rejected it: the tool character class excluded `*`, so only a
        # bare `*` matched. A valid declaration was failing the gate.
        path = write_agent(
            self.directory,
            "sample-agent",
            "name: sample-agent\ndescription: Does a thing.\n"
            "tools: Read, mcp__github__get_*",
        )

        self.assertEqual([], module.validate_agent(path))

    def test_bare_server_entry_stays_valid(self) -> None:
        # Reviewed and deliberately kept: `mcp__puppeteer` "matches any tool provided by
        # the puppeteer server", so it resolves. This validator exists to catch grants
        # that resolve to nothing, and tightening this would reject working definitions
        # for no safety gain - the same over-restriction that made KNOWN_FIELDS fail.
        self.assertTrue(module.MCP_PATTERN.match("mcp__puppeteer"))

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
            "skills: some-skill\nmemory: project\n"
            "background: true\nisolation: worktree\n"
            "hooks:\n  PreToolUse:\n    - echo before",
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

    def test_nested_hook_mapping_is_accepted(self) -> None:
        # `hooks` is a supported field whose value is a mapping. Allowlisting the key
        # while the parser only accepted scalars and sequences would have made the
        # gate reject valid hook configuration.
        path = write_agent(
            self.directory,
            "sample-agent",
            "name: sample-agent\ndescription: Does a thing.\ntools: Read\n"
            "hooks:\n  PreToolUse:\n    - echo before\n  PostToolUse:\n    - echo after",
        )

        self.assertEqual([], module.validate_agent(path))

    def test_hooks_written_as_a_scalar_is_rejected(self) -> None:
        path = write_agent(
            self.directory,
            "sample-agent",
            "name: sample-agent\ndescription: Does a thing.\ntools: Read\nhooks: nope",
        )

        errors = " | ".join(module.validate_agent(path))

        self.assertIn("`hooks` is a str, expected a mapping", errors)

    def test_unknown_permission_mode_is_rejected(self) -> None:
        path = write_agent(
            self.directory,
            "sample-agent",
            "name: sample-agent\ndescription: Does a thing.\ntools: Read\npermissionMode: plna",
        )

        errors = " | ".join(module.validate_agent(path))

        self.assertIn("is not a known mode", errors)

    def test_known_permission_modes_are_accepted(self) -> None:
        for mode in sorted(module.PERMISSION_MODES):
            with self.subTest(mode=mode):
                path = write_agent(
                    self.directory,
                    "sample-agent",
                    f"name: sample-agent\ndescription: Does a thing.\ntools: Read\npermissionMode: {mode}",
                )

                self.assertEqual([], module.validate_agent(path))

    def test_permission_modes_cover_every_documented_mode(self) -> None:
        # Pinned literally rather than derived from PERMISSION_MODES, because a test that
        # iterates the constant passes no matter what the constant omits. This set has now
        # been short twice - `dontAsk` first, then `auto` and `manual` - and each omission
        # rejected a valid definition. Source: the permissionMode row of the frontmatter
        # table at https://code.claude.com/docs/en/sub-agents.
        self.assertEqual(
            {
                "default",
                "acceptEdits",
                "auto",
                "bypassPermissions",
                "dontAsk",
                "plan",
                "manual",
            },
            set(module.PERMISSION_MODES),
        )

    def test_known_fields_cover_every_documented_frontmatter_field(self) -> None:
        # Same reasoning as the mode set above: this allowlist rejects anything it omits,
        # so it has to be pinned against the documented surface rather than grown one
        # review finding at a time. It was short by four - maxTurns, mcpServers, effort,
        # and initialPrompt - and review caught only two of them.
        self.assertEqual(
            {
                "name",
                "description",
                "tools",
                "disallowedTools",
                "model",
                "permissionMode",
                "maxTurns",
                "skills",
                "mcpServers",
                "hooks",
                "memory",
                "background",
                "effort",
                "isolation",
                "color",
                "initialPrompt",
            },
            set(module.KNOWN_FIELDS),
        )

    def test_newly_recognised_fields_validate_their_shape(self) -> None:
        for frontmatter, expect_error in (
            ("maxTurns: 12", False),
            ("maxTurns: true", True),  # bool is a subclass of int; must not satisfy it
            ("maxTurns: many", True),
            ("mcpServers:\n  - slack", False),
            ("mcpServers:\n  slack:\n    command: slack-mcp", False),
            ("mcpServers: slack", True),  # a bare scalar is neither list nor mapping
            ("effort: high", False),
            ("initialPrompt: Start by reading the register.", False),
        ):
            with self.subTest(frontmatter=frontmatter):
                path = write_agent(
                    self.directory,
                    "sample-agent",
                    "name: sample-agent\ndescription: Does a thing.\ntools: Read\n"
                    + frontmatter,
                )

                errors = module.validate_agent(path)

                self.assertEqual(expect_error, bool(errors), errors)

    def test_background_must_be_a_boolean(self) -> None:
        path = write_agent(
            self.directory,
            "sample-agent",
            "name: sample-agent\ndescription: Does a thing.\ntools: Read\nbackground: maybe",
        )

        errors = " | ".join(module.validate_agent(path))

        self.assertIn("`background` is a str, expected a boolean", errors)

    def test_mcp_only_tool_list_is_rejected(self) -> None:
        # Which MCP servers exist is a host-session property, so an allow-list of only
        # MCP patterns resolves to nothing on a session without that server - the same
        # empty grant that made the agent layer inert.
        path = write_agent(
            self.directory,
            "sample-agent",
            "name: sample-agent\ndescription: Does a thing.\ntools: mcp__github__*",
        )

        errors = " | ".join(module.validate_agent(path))

        self.assertIn("names only MCP entries", errors)

    def test_mcp_alongside_a_builtin_is_accepted(self) -> None:
        path = write_agent(
            self.directory,
            "sample-agent",
            "name: sample-agent\ndescription: Does a thing.\ntools: Read, mcp__github__*",
        )

        self.assertEqual([], module.validate_agent(path))

    def test_unterminated_flow_sequence_is_rejected(self) -> None:
        # Dropping the bracket and returning the items anyway would pass a definition
        # a real parser fails outright.
        path = write_agent(
            self.directory,
            "sample-agent",
            "name: sample-agent\ndescription: Does a thing.\ntools: Read\nskills: [foo",
        )

        errors = " | ".join(module.validate_agent(path))

        self.assertIn("not valid YAML", errors)

    def test_sequence_of_mappings_is_parsed(self) -> None:
        # The ordinary shape of a hook entry: `- matcher: Bash` with sibling keys
        # aligned beneath it. Treating every `- ` item as a scalar rejected valid
        # configuration for a field the allowlist had just accepted.
        path = write_agent(
            self.directory,
            "sample-agent",
            "name: sample-agent\ndescription: Does a thing.\ntools: Read\n"
            "hooks:\n  PreToolUse:\n    - matcher: Bash\n      hooks:\n"
            "        - type: command\n          command: echo hi",
        )

        self.assertEqual([], module.validate_agent(path))

        parsed = module.parse_frontmatter(path.read_text(encoding="utf-8"))
        self.assertEqual(
            {"PreToolUse": [{"matcher": "Bash", "hooks": [{"type": "command", "command": "echo hi"}]}]},
            parsed["hooks"],
        )

    def test_deny_list_cancelling_every_allowed_entry_is_rejected(self) -> None:
        # Each field was valid on its own; the empty grant only appears when the two
        # are compared, which is a route neither field-level check could see.
        path = write_agent(
            self.directory,
            "sample-agent",
            "name: sample-agent\ndescription: Does a thing.\ntools: Read\ndisallowedTools: Read",
        )

        errors = " | ".join(module.validate_agent(path))

        self.assertIn("cancels every entry in `tools`", errors)

    def test_deny_list_cancelling_some_entries_is_accepted(self) -> None:
        path = write_agent(
            self.directory,
            "sample-agent",
            "name: sample-agent\ndescription: Does a thing.\ntools: Read, Glob\n"
            "disallowedTools: Glob",
        )

        self.assertEqual([], module.validate_agent(path))

    def test_unscoped_deny_cancels_a_scoped_grant(self) -> None:
        # An unscoped deny covers every scope of that tool.
        path = write_agent(
            self.directory,
            "sample-agent",
            "name: sample-agent\ndescription: Does a thing.\n"
            "tools: Bash(git diff:*)\ndisallowedTools: Bash",
        )

        errors = " | ".join(module.validate_agent(path))

        self.assertIn("cancels every entry in `tools`", errors)

    def test_narrower_deny_does_not_cancel_a_broader_grant(self) -> None:
        # The least-privilege shape: broad read access with a narrow mutation denial.
        # Collapsing both sides to the head `Bash` reported the whole grant cancelled
        # and made this inexpressible.
        path = write_agent(
            self.directory,
            "sample-agent",
            "name: sample-agent\ndescription: Does a thing.\n"
            "tools: Bash(git:*)\ndisallowedTools: Bash(git push:*)",
        )

        self.assertEqual([], module.validate_agent(path))

    def test_broader_deny_cancels_a_narrower_grant(self) -> None:
        # The reverse direction, and the one the first implementation got wrong:
        # `"git push:*".startswith("git:")` is False, so a raw string comparison
        # reported this effectively empty grant as surviving.
        path = write_agent(
            self.directory,
            "sample-agent",
            "name: sample-agent\ndescription: Does a thing.\n"
            "tools: Bash(git push:*)\ndisallowedTools: Bash(git:*)",
        )

        errors = module.validate_agent(path)

        self.assertTrue(any("cancels every entry" in error for error in errors), errors)

    def test_deny_cancels_across_both_wildcard_spellings(self) -> None:
        # `command:*` and `command *` mean the same thing and both appear in this
        # repository's settings, so a deny in one spelling must cancel a grant in the
        # other or the check is trivially evaded by choosing a different form.
        path = write_agent(
            self.directory,
            "sample-agent",
            "name: sample-agent\ndescription: Does a thing.\n"
            "tools: Bash(git push:*)\ndisallowedTools: Bash(git *)",
        )

        errors = module.validate_agent(path)

        self.assertTrue(any("cancels every entry" in error for error in errors), errors)

    def test_word_boundary_keeps_a_similarly_named_command_alive(self) -> None:
        # Per the permission reference, a space before `*` enforces a word boundary:
        # `Bash(ls *)` matches `ls -la` but not `lsof`. A naive prefix fix would swallow
        # `gitfoo` into `git:*` and report a live grant as cancelled.
        path = write_agent(
            self.directory,
            "sample-agent",
            "name: sample-agent\ndescription: Does a thing.\n"
            "tools: Bash(gitfoo:*)\ndisallowedTools: Bash(git:*)",
        )

        self.assertEqual([], module.validate_agent(path))

    def test_identical_scopes_cancel(self) -> None:
        path = write_agent(
            self.directory,
            "sample-agent",
            "name: sample-agent\ndescription: Does a thing.\n"
            "tools: Bash(git:*)\ndisallowedTools: Bash(git:*)",
        )

        errors = " | ".join(module.validate_agent(path))

        self.assertIn("cancels every entry in `tools`", errors)

    def test_invalid_escape_in_a_quoted_scalar_is_rejected(self) -> None:
        # PyYAML fails the document on an unknown escape, so the definition the host
        # cannot load is the definition this gate refuses.
        path = write_agent(
            self.directory,
            "sample-agent",
            'name: sample-agent\ndescription: "bad\\q"\ntools: Read',
        )

        errors = " | ".join(module.validate_agent(path))

        self.assertIn("not valid YAML", errors)

    def test_escapes_are_decoded_the_way_the_host_resolves_them(self) -> None:
        # A checked-but-undecoded escape made `name` mismatch its filename and turned an
        # escaped comma into one unknown tool instead of two grants.
        path = write_agent(
            self.directory,
            "sample-agent",
            'name: "sample\\u002dagent"\ndescription: x\ntools: "Read\\u002c Glob"',
        )

        self.assertEqual([], module.validate_agent(path))

        frontmatter = module.parse_frontmatter(path.read_text(encoding="utf-8"))
        self.assertEqual("sample-agent", frontmatter["name"])
        self.assertEqual((["Read", "Glob"], None), module.parse_tool_list(frontmatter["tools"]))

    def test_inline_comments_do_not_reach_the_value(self) -> None:
        # `tools: Read # read-only` was read as a tool literally named "Read # read-only".
        path = write_agent(
            self.directory,
            "sample-agent",
            "name: sample-agent\ndescription: Does a thing.\ntools: Read, Glob # read-only",
        )

        self.assertEqual([], module.validate_agent(path))

    def test_deny_list_leaving_only_mcp_entries_is_rejected(self) -> None:
        # Each earlier rule passes on its own: a built-in was declared, and not every
        # entry was cancelled. Only what *survives* the deny list shows the empty grant.
        path = write_agent(
            self.directory,
            "sample-agent",
            "name: sample-agent\ndescription: Does a thing.\n"
            "tools: Read, mcp__github__*\ndisallowedTools: Read",
        )

        errors = " | ".join(module.validate_agent(path))

        self.assertIn("removes every built-in", errors)

    def test_deny_list_leaving_a_builtin_beside_mcp_is_accepted(self) -> None:
        path = write_agent(
            self.directory,
            "sample-agent",
            "name: sample-agent\ndescription: Does a thing.\n"
            "tools: Read, Glob, mcp__github__*\ndisallowedTools: Glob",
        )

        self.assertEqual([], module.validate_agent(path))

    def test_bare_dash_sequence_indicator_is_parsed(self) -> None:
        # The alternate block form: `-` alone, with the entry's mapping indented beneath.
        path = write_agent(
            self.directory,
            "sample-agent",
            "name: sample-agent\ndescription: Does a thing.\ntools: Read\n"
            "hooks:\n  PreToolUse:\n    -\n      matcher: Bash\n      hooks:\n"
            "        - type: command",
        )

        self.assertEqual([], module.validate_agent(path))

        parsed = module.parse_frontmatter(path.read_text(encoding="utf-8"))
        self.assertEqual(
            {"PreToolUse": [{"matcher": "Bash", "hooks": [{"type": "command"}]}]},
            parsed["hooks"],
        )

    def test_mcp_only_disallowed_tools_is_accepted(self) -> None:
        # A deny-list that matches nothing is harmless; only an allow-list fails open.
        path = write_agent(
            self.directory,
            "sample-agent",
            "name: sample-agent\ndescription: Does a thing.\ntools: Read\ndisallowedTools: mcp__*",
        )

        self.assertEqual([], module.validate_agent(path))

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

    def test_duplicate_name_across_directories_fails(self) -> None:
        # Each file is valid on its own; the collision only exists across the tree,
        # and leaves neither agent addressable unambiguously.
        nested = self.directory / "lanes"
        nested.mkdir()
        body = "name: shared-agent\ndescription: Does a thing.\ntools: Read"
        write_agent(self.directory, "shared-agent", body)
        write_agent(nested, "shared-agent", body)
        module.AGENTS_ROOT = self.directory

        self.assertEqual([], module.validate_agent(self.directory / "shared-agent.md"))
        self.assertEqual([], module.validate_agent(nested / "shared-agent.md"))
        self.assertEqual(1, module.main())

    def test_distinct_names_across_directories_pass(self) -> None:
        nested = self.directory / "lanes"
        nested.mkdir()
        write_agent(self.directory, "first-agent", "name: first-agent\ndescription: x\ntools: Read")
        write_agent(nested, "second-agent", "name: second-agent\ndescription: x\ntools: Read")
        module.AGENTS_ROOT = self.directory

        self.assertEqual(0, module.main())


if __name__ == "__main__":
    unittest.main()
