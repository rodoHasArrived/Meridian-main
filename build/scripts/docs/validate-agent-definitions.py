#!/usr/bin/env python3
"""Validate Claude agent definitions in `.claude/agents/` against host tool vocabulary.

Every agent file under `.claude/agents/` carried
`tools: ["read", "search", "edit", "mcp"]` until 2026-08. None of those four
resolve to a Claude Code tool. The result is not a reduced grant but an empty
one: when nothing in `tools` resolves, the host refuses to launch the subagent
and the Agent tool returns an unresolved-entry error, so the entire agent layer
was inert. Nothing caught it - `validate-skill-packages.py` walks only
`.claude/skills/`, and `check-ai-inventory.py` enumerates filenames without
opening them.

This validator closes that gap. It checks per file:

1. frontmatter is present, fenced, and parses as YAML with no duplicate keys;
2. `name` matches the filename, so an agent is addressable by the name it is
   filed under;
3. `description` is present and non-empty, since that is what the host routes on;
4. `tools` / `disallowedTools` are comma-separated strings rather than
   sequences, and every entry resolves to a known built-in or a valid MCP
   pattern.

Duplicate keys and YAML well-formedness matter because the host loads the
frontmatter with a real parser. A file whose `tools` key appears twice would
pass a first-textual-match check while the host resolves the *last* value, and
a folded `description: >` with no body reads as empty to the host while a
regex sees the literal `>` as content. Both put an agent back in the inert
state this validator exists to prevent, so the frontmatter is parsed once,
strictly, and the resulting field types are checked.

On scoped entries: Claude Code accepts a parenthesised scope after a tool name
(`Bash(git:*)` is used throughout this repository's own
`.claude/settings.local.json`, and the agent form is `Agent(worker, researcher)`).
Commas inside those parentheses are part of one entry, so entries are split at
the top level only and the head name before `(` is what gets validated.

On MCP: the host accepts `mcp__<server>` and `mcp__<server>__<tool>` in these
fields, so those forms pass. A bare `mcp` does not resolve and is rejected -
that was one of the four invalid tokens. The all-server wildcard `mcp__*` is
accepted only in `disallowedTools`: as a deny-list entry that matches nothing
it is harmless, but as an allow-list entry that matches nothing it produces
exactly the empty grant described above. Repository-level agent files generally
should not name a concrete MCP server, because which servers exist is a
property of the host session rather than of this repository.
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
AGENTS_ROOT = REPO_ROOT / ".claude" / "agents"

# Built-in tool names a subagent may be granted, kept in sync with the tool
# surface Claude Code exposes to subagents. There is no machine-readable host
# schema to source this from, so the set is maintained by hand and pinned by
# `tests/scripts/test_validate_agent_definitions.py` - a name added or removed
# here fails that test until the change is made deliberately in both places.
# Extend it when the host gains a tool, never to silence a failing definition.
KNOWN_TOOLS = frozenset(
    {
        # File and search
        "Edit",
        "Glob",
        "Grep",
        "NotebookEdit",
        "Read",
        "Write",
        # Command execution
        "Bash",
        "BashOutput",
        "KillShell",
        "PowerShell",
        # Delegation and orchestration
        "Agent",
        "SendMessage",
        "Task",
        "TaskCreate",
        "TaskGet",
        "TaskList",
        "TaskOutput",
        "TaskStop",
        "TaskUpdate",
        "TodoWrite",
        "Workflow",
        # Planning and session control
        "AskUserQuestion",
        "EnterPlanMode",
        "ExitPlanMode",
        "EnterWorktree",
        "ExitWorktree",
        "Monitor",
        # Discovery
        "ListMcpResourcesTool",
        "ReadMcpResourceTool",
        "Skill",
        "SlashCommand",
        "ToolSearch",
        # Web and output surfaces
        "Artifact",
        "WebFetch",
        "WebSearch",
    }
)

# `mcp__server`, `mcp__server__tool`, or `mcp__server__*`. The all-server
# `mcp__*` is handled separately because it is only valid in `disallowedTools`.
MCP_PATTERN = re.compile(r"^mcp__[A-Za-z0-9_.-]+(?:__(?:[A-Za-z0-9_.-]+|\*))?$")
MCP_ALL_SERVERS = "mcp__*"
NAME_PATTERN = re.compile(r"^[a-z0-9]+(?:-[a-z0-9]+)*$")
TOOL_FIELDS = ("tools", "disallowedTools")
ALLOW_LIST_FIELD = "tools"


KEY_PATTERN = re.compile(r"^(?P<key>[A-Za-z_][A-Za-z0-9_-]*):(?:[ \t]+(?P<value>.*))?$")
BLOCK_SCALAR_PATTERN = re.compile(r"^[|>][+-]?\d*$")


def split_frontmatter(text: str) -> str:
    if not text.startswith("---\n"):
        raise ValueError("missing opening frontmatter fence")
    end = text.find("\n---\n", 4)
    if end == -1:
        raise ValueError("missing closing frontmatter fence")
    return text[4:end]


def _strip_quotes(value: str) -> str:
    if len(value) >= 2 and value[0] == value[-1] and value[0] in "\"'":
        return value[1:-1]
    return value


def _parse_flow_sequence(value: str) -> list[str]:
    inner = value[1:-1].strip() if value.endswith("]") else value[1:].strip()
    if not inner:
        return []
    return [_strip_quotes(item.strip()) for item in inner.split(",") if item.strip()]


def parse_frontmatter(text: str) -> dict[str, object]:
    """Return the frontmatter mapping, raising ValueError on anything the host would reject.

    PyYAML is an optional dependency in this repository - `common.py` guards its
    import and the hosted docs lanes do not install it - so this parses the
    frontmatter subset directly rather than importing it. One code path runs in
    every environment, which is what a gate needs; the test suite cross-checks
    this parser against PyYAML wherever PyYAML is actually available.

    The subset is what agent frontmatter uses: plain scalars, folded and literal
    block scalars, flow sequences, and block sequences. Anything else at column
    zero is reported as invalid rather than skipped, and a repeated key is an
    error rather than a silent last-wins overwrite.
    """
    raw = split_frontmatter(text)
    mapping: dict[str, object] = {}
    block_lines: list[str] | None = None
    block_separator = " "
    sequence_items: list[str] | None = None
    open_key: str | None = None

    def close_open_key() -> None:
        nonlocal block_lines, sequence_items, open_key
        if open_key is not None:
            if block_lines is not None:
                mapping[open_key] = block_separator.join(
                    line for line in block_lines if line
                )
            elif sequence_items is not None:
                # A bare key with no items is an empty value, not an empty sequence -
                # `description:` must read as absent the way the host reads it.
                mapping[open_key] = sequence_items if sequence_items else None
        block_lines = None
        sequence_items = None
        open_key = None

    for number, line in enumerate(raw.split("\n"), start=1):
        if not line.strip() or line.lstrip().startswith("#"):
            continue

        if line[0] in " \t":
            stripped = line.strip()
            if block_lines is not None:
                block_lines.append(stripped)
                continue
            if sequence_items is not None and stripped.startswith("- "):
                sequence_items.append(_strip_quotes(stripped[2:].strip()))
                continue
            raise ValueError(
                f"frontmatter is not valid YAML: unexpected indented content on line {number}"
            )

        if line.startswith("- "):
            raise ValueError("frontmatter is a sequence, expected a mapping")

        match = KEY_PATTERN.match(line)
        if not match:
            raise ValueError(
                f"frontmatter is not valid YAML: line {number} is not a `key: value` entry"
            )

        close_open_key()
        key = match.group("key")
        if key in mapping:
            raise ValueError(
                f"duplicate key '{key}' in frontmatter; the host resolves the last "
                "occurrence, so a repeated key silently changes the effective value"
            )

        value = (match.group("value") or "").strip()
        if not value:
            # A bare key opens either a block sequence or an empty value, and which one
            # is only knowable from the next line.
            open_key = key
            sequence_items = []
            continue
        if BLOCK_SCALAR_PATTERN.match(value):
            open_key = key
            block_lines = []
            block_separator = "\n" if value[0] == "|" else " "
            continue
        mapping[key] = (
            _parse_flow_sequence(value) if value.startswith("[") else _strip_quotes(value)
        )

    close_open_key()

    if not mapping:
        raise ValueError("frontmatter is empty")
    return mapping


def split_top_level(value: str) -> list[str]:
    """Split on commas outside parentheses, so `Agent(worker, researcher)` stays one entry."""
    entries: list[str] = []
    current: list[str] = []
    depth = 0
    for char in value:
        if char == "(":
            depth += 1
        elif char == ")":
            if depth == 0:
                raise ValueError("unbalanced ')' in the entry list")
            depth -= 1
        if char == "," and depth == 0:
            entries.append("".join(current))
            current = []
        else:
            current.append(char)
    if depth:
        raise ValueError("unbalanced '(' in the entry list")
    entries.append("".join(current))
    return [entry.strip() for entry in entries if entry.strip()]


def parse_tool_list(value: object) -> tuple[list[str], str | None]:
    """Split a tools field into entries, reporting a syntax problem if present.

    The host expects a comma-separated string (`tools: Read, Glob, Grep`).
    A YAML/JSON sequence is the exact mistake this validator exists to catch,
    so it is reported rather than quietly accepted.
    """
    if isinstance(value, list):
        return [], (
            "is a YAML sequence; the host expects a comma-separated string, "
            "for example `tools: Read, Glob, Grep`"
        )
    if not isinstance(value, str):
        return [], f"is a {type(value).__name__}, expected a comma-separated string"
    text = value.strip()
    if not text:
        return [], "is empty; omit the field entirely to inherit the default tool pool"
    try:
        return split_top_level(text), None
    except ValueError as exc:
        return [], str(exc)


def entry_head(entry: str) -> tuple[str, str | None]:
    """Strip a parenthesised scope, returning the tool name and any syntax problem."""
    if "(" not in entry:
        return entry, None
    if not entry.endswith(")"):
        return entry, "has an unterminated scope; expected `Tool(scope)`"
    return entry[: entry.index("(")].strip(), None


def describe_unknown(field: str, entry: str) -> str:
    lowered = entry.lower()
    for known in sorted(KNOWN_TOOLS):
        if known.lower() == lowered:
            return f"; did you mean `{known}`?"
    if lowered == "search":
        return "; use `Glob, Grep`"
    if lowered == "mcp":
        return "; use an `mcp__<server>` pattern, or omit it"
    if entry == MCP_ALL_SERVERS and field == ALLOW_LIST_FIELD:
        return (
            "; the all-server wildcard grants nothing when no MCP server is "
            "connected, which is an empty tool grant - it is only valid in "
            "`disallowedTools`"
        )
    return ""


def required_string(
    frontmatter: dict[str, object],
    field: str,
    path: Path,
    errors: list[str],
    note: str = "",
) -> str | None:
    """Return the stripped value, or None having recorded exactly why it is unusable.

    A folded scalar with no body (`description: >`) parses to an empty string and
    a bare key parses to None; the host treats both as absent, so both are errors
    here rather than content.
    """
    if field not in frontmatter:
        errors.append(f"{path.name}: missing `{field}`{note}")
        return None
    value = frontmatter[field]
    if value is None:
        errors.append(f"{path.name}: `{field}` is empty{note}")
        return None
    if not isinstance(value, str):
        errors.append(
            f"{path.name}: `{field}` is a {type(value).__name__}, expected a string"
        )
        return None
    stripped = value.strip()
    if not stripped:
        errors.append(f"{path.name}: `{field}` is empty{note}")
        return None
    return stripped


def validate_agent(path: Path) -> list[str]:
    errors: list[str] = []
    try:
        frontmatter = parse_frontmatter(path.read_text(encoding="utf-8"))
    except ValueError as exc:
        return [f"{path.name}: {exc}"]

    name = required_string(frontmatter, "name", path, errors)
    if name:
        if not NAME_PATTERN.match(name):
            errors.append(f"{path.name}: `name` '{name}' is not kebab-case")
        if name != path.stem:
            errors.append(f"{path.name}: `name` '{name}' does not match the filename")

    required_string(
        frontmatter, "description", path, errors, note="; the host routes on it"
    )

    for field in TOOL_FIELDS:
        if field not in frontmatter:
            continue
        entries, problem = parse_tool_list(frontmatter[field])
        if problem:
            errors.append(f"{path.name}: `{field}` {problem}")
            continue
        for entry in entries:
            head, scope_problem = entry_head(entry)
            if scope_problem:
                errors.append(f"{path.name}: `{field}` entry '{entry}' {scope_problem}")
                continue
            if head in KNOWN_TOOLS or MCP_PATTERN.match(head):
                continue
            if head == MCP_ALL_SERVERS and field != ALLOW_LIST_FIELD:
                continue
            errors.append(
                f"{path.name}: `{field}` entry '{head}' is not a known tool"
                f"{describe_unknown(field, head)}"
            )

    return errors


def display_path(path: Path) -> str:
    try:
        return str(path.relative_to(REPO_ROOT))
    except ValueError:
        return str(path)


def main() -> int:
    # Fail closed: a missing or empty directory means none of the repository's
    # catalogued Claude agents can exist, which is a worse state than an invalid
    # declaration, not a reason to report success.
    if not AGENTS_ROOT.is_dir():
        print(f"error: no agent directory at {display_path(AGENTS_ROOT)}")
        return 1

    agents = sorted(AGENTS_ROOT.rglob("*.md"))
    if not agents:
        print(f"error: no agent definitions under {display_path(AGENTS_ROOT)}")
        return 1

    errors: list[str] = []
    for path in agents:
        errors.extend(validate_agent(path))

    for error in errors:
        print(f"error: {error}")

    print(f"agent definition validation: {len(agents)} file(s), {len(errors)} error(s)")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
