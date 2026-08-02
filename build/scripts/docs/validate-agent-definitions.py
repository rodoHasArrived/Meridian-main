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

# Frontmatter keys the host understands — the whole documented surface, not just the
# three this repository currently uses. An unknown key is an error rather than something
# to ignore, because the two permission fields fail *open* when misspelled: omitting
# `tools` inherits the default tool pool, so a `tool:` typo silently turns a deliberately
# read-only agent into one with edit and command access, and no other check here would
# notice. That check only earns its place if the allowlist is complete, since this gate
# now runs on every agent change — a field the host supports but this set omits would
# block legitimate work. Add new host fields here as they ship.
KNOWN_FIELDS = frozenset(
    {
        "name",
        "description",
        "tools",
        "disallowedTools",
        "model",
        "color",
        "permissionMode",
        "skills",
        "hooks",
        "memory",
        "background",
        "isolation",
    }
)

# Value domains, for the fields whose domain is small and well established.
#
# Deliberately partial. Restricting a field to a set that turns out to be
# incomplete blocks legitimate work, which is exactly how the first version of
# KNOWN_FIELDS failed - so only fields whose accepted values can be stated with
# confidence get a value check here. The rest are type-checked instead, which
# catches the shape errors (a mapping where a string belongs, a string where a
# boolean belongs) without inventing a vocabulary.
PERMISSION_MODES = frozenset({"default", "acceptEdits", "bypassPermissions", "plan"})

# Expected Python type per field once the frontmatter has been resolved. `bool`
# is checked before `int` because Python treats bool as a subclass of int.
FIELD_TYPES: dict[str, tuple[type, ...]] = {
    "name": (str,),
    "description": (str,),
    "tools": (str,),
    "disallowedTools": (str,),
    "model": (str,),
    "color": (str,),
    "permissionMode": (str,),
    "memory": (str,),
    "isolation": (str,),
    "background": (bool,),
    "skills": (str, list),
    "hooks": (dict,),
}


KEY_PATTERN = re.compile(r"^(?P<key>[A-Za-z_][A-Za-z0-9_-]*):(?:[ \t]+(?P<value>.*))?$")
BLOCK_SCALAR_PATTERN = re.compile(r"^[|>][+-]?\d*$")


def split_frontmatter(text: str) -> str:
    if not text.startswith("---\n"):
        raise ValueError("missing opening frontmatter fence")
    end = text.find("\n---\n", 4)
    if end == -1:
        raise ValueError("missing closing frontmatter fence")
    return text[4:end]


# Plain-scalar resolution, matching PyYAML (which implements YAML 1.1, so `yes`/`no`/
# `on`/`off` are booleans). Quoted scalars are always strings and skip this entirely.
_YAML_NULL = frozenset({"", "~", "null", "Null", "NULL"})
_YAML_TRUE = frozenset({"true", "True", "TRUE", "yes", "Yes", "YES", "on", "On", "ON"})
_YAML_FALSE = frozenset({"false", "False", "FALSE", "no", "No", "NO", "off", "Off", "OFF"})


def _resolve_scalar(value: str) -> object:
    """Resolve a plain scalar to its YAML type rather than leaving everything a string.

    Without this, `description: null` becomes the string `"null"` and passes the
    non-empty check, while the host's parser resolves it to null and leaves the agent
    with no routing description — a definition that validates clean and does not work.
    Number and boolean forms matter for the same reason: `tools: 123` must be reported
    as a non-string, not silently treated as a one-entry tool list.

    Exotic YAML 1.1 numeric spellings (hex, octal, underscores, sexagesimal) are left as
    strings; none appears in agent frontmatter, and the differential test against PyYAML
    would surface it if one ever did.
    """
    if value in _YAML_NULL:
        return None
    if value in _YAML_TRUE:
        return True
    if value in _YAML_FALSE:
        return False
    try:
        return int(value)
    except ValueError:
        pass
    try:
        return float(value)
    except ValueError:
        return value


def _strip_quotes(value: str) -> object:
    """Unquote a scalar, or resolve a plain one, rejecting an unterminated quote.

    A real YAML parser fails the whole document on `description: "unterminated`.
    Returning it as a plain string instead would pass a definition the host cannot
    load at all, which is the failure mode this validator exists to prevent.
    """
    if not value:
        return None
    if value[0] not in "\"'":
        return _resolve_scalar(value)
    quote = value[0]
    if len(value) >= 2 and value[-1] == quote:
        return value[1:-1]
    raise ValueError(
        f"frontmatter is not valid YAML: unterminated {quote} quoted scalar"
    )


def _parse_flow_sequence(value: str) -> list[object]:
    inner = value[1:-1].strip() if value.endswith("]") else value[1:].strip()
    if not inner:
        return []
    return [_strip_quotes(item.strip()) for item in inner.split(",") if item.strip()]


def _prepare_lines(raw: str) -> list[tuple[int, str, int]]:
    """Strip blanks and comments, returning (indent, content, line-number) triples."""
    prepared: list[tuple[int, str, int]] = []
    for number, line in enumerate(raw.split("\n"), start=1):
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        indent = len(line) - len(line.lstrip(" \t"))
        prepared.append((indent, line.strip(), number))
    return prepared


def _gather_block_scalar(
    lines: list[tuple[int, str, int]], index: int, indent: int, separator: str
) -> tuple[str, int]:
    body: list[str] = []
    while index < len(lines) and lines[index][0] > indent:
        body.append(lines[index][1])
        index += 1
    return separator.join(part for part in body if part), index


def _parse_sequence(
    lines: list[tuple[int, str, int]], index: int, indent: int
) -> tuple[list[object], int]:
    items: list[object] = []
    while index < len(lines) and lines[index][0] == indent and lines[index][1].startswith("- "):
        item = lines[index][1][2:].strip()
        index += 1
        if not item and index < len(lines) and lines[index][0] > indent:
            nested, index = _parse_mapping(lines, index, lines[index][0])
            items.append(nested)
        else:
            items.append(_strip_quotes(item))
    return items, index


def _parse_mapping(
    lines: list[tuple[int, str, int]], index: int, indent: int
) -> tuple[dict[str, object], int]:
    mapping: dict[str, object] = {}
    while index < len(lines) and lines[index][0] == indent:
        _, content, number = lines[index]
        if content.startswith("- "):
            raise ValueError("frontmatter is a sequence, expected a mapping")

        match = KEY_PATTERN.match(content)
        if not match:
            raise ValueError(
                f"frontmatter is not valid YAML: line {number} is not a `key: value` entry"
            )
        key = match.group("key")
        if key in mapping:
            raise ValueError(
                f"duplicate key '{key}' in frontmatter; the host resolves the last "
                "occurrence, so a repeated key silently changes the effective value"
            )
        value = (match.group("value") or "").strip()
        index += 1

        if BLOCK_SCALAR_PATTERN.match(value):
            mapping[key], index = _gather_block_scalar(
                lines, index, indent, "\n" if value[0] == "|" else " "
            )
            continue
        if value:
            # A key with an inline value cannot also own an indented block.
            if index < len(lines) and lines[index][0] > indent:
                raise ValueError(
                    "frontmatter is not valid YAML: unexpected indented content on "
                    f"line {lines[index][2]}"
                )
            mapping[key] = (
                _parse_flow_sequence(value)
                if value.startswith("[")
                else _strip_quotes(value)
            )
            continue

        # A bare key owns whatever is indented beneath it: a sequence, a nested
        # mapping such as a `hooks:` block, or nothing at all.
        if index < len(lines) and lines[index][0] > indent:
            child_indent = lines[index][0]
            if lines[index][1].startswith("- "):
                mapping[key], index = _parse_sequence(lines, index, child_indent)
            else:
                mapping[key], index = _parse_mapping(lines, index, child_indent)
        else:
            mapping[key] = None

    if index < len(lines) and lines[index][0] > indent:
        raise ValueError(
            "frontmatter is not valid YAML: unexpected indented content on "
            f"line {lines[index][2]}"
        )
    return mapping, index


def parse_frontmatter(text: str) -> dict[str, object]:
    """Return the frontmatter mapping, raising ValueError on anything the host would reject.

    PyYAML is an optional dependency in this repository - `common.py` guards its
    import and the hosted docs lanes do not install it - so this parses the
    frontmatter subset directly rather than importing it. One code path runs in
    every environment, which is what a gate needs; the test suite cross-checks
    this parser against PyYAML wherever PyYAML is actually available.

    The subset covers plain scalars, folded and literal block scalars, flow
    sequences, block sequences, and nested mappings - the last of these because
    `hooks:` is a documented frontmatter field whose value is a mapping, so a
    parser that only accepted scalars and sequences would reject valid
    configuration. Anything else is reported rather than skipped, and a repeated
    key is an error rather than a silent last-wins overwrite.
    """
    lines = _prepare_lines(split_frontmatter(text))
    if not lines:
        raise ValueError("frontmatter is empty")
    if lines[0][0] != 0:
        raise ValueError(
            "frontmatter is not valid YAML: unexpected indented content on "
            f"line {lines[0][2]}"
        )

    mapping, index = _parse_mapping(lines, 0, 0)
    if index != len(lines):
        raise ValueError(
            f"frontmatter is not valid YAML: line {lines[index][2]} is not a `key: value` entry"
        )
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
        entries = split_top_level(text)
    except ValueError as exc:
        return [], str(exc)
    if not entries:
        # Non-empty but punctuation-only, such as `tools: ","`. The emptiness check
        # above passes and the split yields nothing, so without this the validator
        # would approve the very empty-grant state it exists to prevent.
        return [], (
            "contains no tool entries; omit the field entirely to inherit the "
            "default tool pool"
        )
    return entries, None


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


def _matches_type(value: object, expected: tuple[type, ...]) -> bool:
    # bool is a subclass of int in Python, so a boolean would otherwise satisfy an
    # int expectation and vice versa.
    if isinstance(value, bool):
        return bool in expected
    return isinstance(value, expected) and not (int in expected and isinstance(value, bool))


def _render_types(expected: tuple[type, ...]) -> str:
    names = {
        str: "a string",
        bool: "a boolean",
        list: "a sequence",
        dict: "a mapping",
        int: "an integer",
    }
    return " or ".join(names.get(item, item.__name__) for item in expected)


def _is_near_miss(candidate: str, known: str) -> bool:
    """True when `candidate` looks like a typo of `known` — case, or one edit away."""
    a, b = candidate.lower(), known.lower()
    if a == b:
        return True
    if abs(len(a) - len(b)) > 1:
        return False
    if len(a) == len(b):
        return sum(x != y for x, y in zip(a, b)) == 1
    shorter, longer = (a, b) if len(a) < len(b) else (b, a)
    for index in range(len(longer)):
        if longer[:index] + longer[index + 1 :] == shorter:
            return True
    return False


def validate_agent(path: Path) -> list[str]:
    errors: list[str] = []
    try:
        frontmatter = parse_frontmatter(path.read_text(encoding="utf-8"))
    except ValueError as exc:
        return [f"{path.name}: {exc}"]

    for key, value in frontmatter.items():
        if key in KNOWN_FIELDS:
            expected = FIELD_TYPES.get(key)
            # A recognised key is not the same as a usable value: `background: maybe`
            # or a `hooks` block written as a scalar is something the host cannot
            # interpret as intended, and only the shape check catches it.
            if expected and value is not None and not _matches_type(value, expected):
                errors.append(
                    f"{path.name}: `{key}` is a {type(value).__name__}, expected "
                    f"{_render_types(expected)}"
                )
            elif key == "permissionMode" and isinstance(value, str) and value not in PERMISSION_MODES:
                errors.append(
                    f"{path.name}: `permissionMode` '{value}' is not a known mode; "
                    f"expected one of {', '.join(sorted(PERMISSION_MODES))}"
                )
            continue
        hint = ""
        for known in sorted(TOOL_FIELDS):
            if _is_near_miss(key, known):
                consequence = (
                    "an omitted `tools` inherits the default tool pool, so this "
                    "misspelling widens the grant instead of narrowing it"
                    if known == ALLOW_LIST_FIELD
                    else "an omitted `disallowedTools` denies nothing, so this "
                    "misspelling silently drops the restriction"
                )
                hint = f"; did you mean `{known}`? {consequence}"
                break
        errors.append(f"{path.name}: unknown frontmatter key `{key}`{hint}")

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
        builtin_count = 0
        for entry in entries:
            head, scope_problem = entry_head(entry)
            if scope_problem:
                errors.append(f"{path.name}: `{field}` entry '{entry}' {scope_problem}")
                continue
            if head in KNOWN_TOOLS:
                builtin_count += 1
                continue
            if MCP_PATTERN.match(head):
                continue
            if head == MCP_ALL_SERVERS and field != ALLOW_LIST_FIELD:
                continue
            errors.append(
                f"{path.name}: `{field}` entry '{head}' is not a known tool"
                f"{describe_unknown(field, head)}"
            )

        # Which MCP servers exist is a property of the host session, not of this
        # repository - the same reason this file's guidance discourages naming one.
        # An allow-list made only of MCP patterns therefore resolves to nothing on
        # any session without that server, which is the empty grant that made the
        # whole agent layer inert. A deny-list has no such failure mode.
        if field == ALLOW_LIST_FIELD and entries and builtin_count == 0:
            errors.append(
                f"{path.name}: `{field}` names only MCP entries; an MCP server is a "
                "property of the host session, so this grants nothing on a session "
                "without it - include at least one built-in tool"
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
