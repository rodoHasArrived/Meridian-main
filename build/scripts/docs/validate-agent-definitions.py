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

This validator closes that gap. It checks four things per file:

1. frontmatter is present and well formed;
2. `name` matches the filename, so an agent is addressable by the name it is
   filed under;
3. `description` is present, since that is what the host routes on;
4. every entry in `tools` / `disallowedTools` is either a known built-in or a
   valid MCP pattern.

On MCP: the host accepts `mcp__<server>` and `mcp__<server>__<tool>` in these
fields, so those forms pass. A bare `mcp` does not resolve and is rejected -
that was one of the four invalid tokens. Repository-level agent files generally
should not name a concrete MCP server, because which servers exist is a
property of the host session rather than of this repository.
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
AGENTS_ROOT = REPO_ROOT / ".claude" / "agents"

# Built-in tool names a subagent may be granted. Sourced from the Claude Code
# subagent documentation; extend deliberately rather than to silence a failure.
KNOWN_TOOLS = frozenset(
    {
        "Agent",
        "Bash",
        "BashOutput",
        "Edit",
        "ExitPlanMode",
        "Glob",
        "Grep",
        "KillShell",
        "NotebookEdit",
        "Read",
        "SlashCommand",
        "Skill",
        "Task",
        "TodoWrite",
        "WebFetch",
        "WebSearch",
        "Write",
    }
)

# `mcp__server` or `mcp__server__tool`; a trailing `*` is accepted as a wildcard.
MCP_PATTERN = re.compile(r"^mcp__[A-Za-z0-9_.-]+(?:__(?:[A-Za-z0-9_.-]+|\*))?$|^mcp__\*$")
NAME_PATTERN = re.compile(r"^[a-z0-9]+(?:-[a-z0-9]+)*$")
TOOL_FIELDS = ("tools", "disallowedTools")


def split_frontmatter(text: str) -> str:
    if not text.startswith("---\n"):
        raise ValueError("missing opening frontmatter fence")
    end = text.find("\n---\n", 4)
    if end == -1:
        raise ValueError("missing closing frontmatter fence")
    return text[4:end]


def scalar_field(frontmatter: str, field: str) -> str | None:
    match = re.search(rf"^{re.escape(field)}:\s*(.*)$", frontmatter, re.MULTILINE)
    if not match:
        return None
    return match.group(1).strip()


def parse_tool_list(raw: str) -> tuple[list[str], str | None]:
    """Split a tools field into entries, reporting a syntax problem if present.

    The host expects a comma-separated string (`tools: Read, Glob, Grep`).
    A YAML/JSON sequence is the exact mistake this validator exists to catch,
    so it is reported rather than quietly accepted.
    """
    value = raw.strip()
    if not value:
        return [], "is empty; omit the field entirely to inherit the default tool pool"
    if value.startswith("["):
        return [], (
            "is a YAML sequence; the host expects a comma-separated string, "
            "for example `tools: Read, Glob, Grep`"
        )
    return [entry.strip() for entry in value.split(",") if entry.strip()], None


def validate_agent(path: Path) -> list[str]:
    errors: list[str] = []
    try:
        frontmatter = split_frontmatter(path.read_text(encoding="utf-8"))
    except ValueError as exc:
        return [f"{path.name}: {exc}"]

    name = scalar_field(frontmatter, "name")
    if not name:
        errors.append(f"{path.name}: missing `name`")
    else:
        if not NAME_PATTERN.match(name):
            errors.append(f"{path.name}: `name` '{name}' is not kebab-case")
        if name != path.stem:
            errors.append(f"{path.name}: `name` '{name}' does not match the filename")

    if not scalar_field(frontmatter, "description"):
        errors.append(f"{path.name}: missing `description`; the host routes on it")

    for field in TOOL_FIELDS:
        raw = scalar_field(frontmatter, field)
        if raw is None:
            continue
        entries, problem = parse_tool_list(raw)
        if problem:
            errors.append(f"{path.name}: `{field}` {problem}")
            continue
        for entry in entries:
            if entry in KNOWN_TOOLS or MCP_PATTERN.match(entry):
                continue
            hint = ""
            lowered = entry.lower()
            for known in sorted(KNOWN_TOOLS):
                if known.lower() == lowered:
                    hint = f"; did you mean `{known}`?"
                    break
            else:
                if lowered == "search":
                    hint = "; use `Glob, Grep`"
                elif lowered == "mcp":
                    hint = "; use an `mcp__<server>` pattern, or omit it"
            errors.append(
                f"{path.name}: `{field}` entry '{entry}' is not a known tool{hint}"
            )

    return errors


def main() -> int:
    if not AGENTS_ROOT.is_dir():
        print(f"no agent directory at {AGENTS_ROOT.relative_to(REPO_ROOT)}")
        return 0

    agents = sorted(AGENTS_ROOT.glob("*.md"))
    if not agents:
        print("no agent definitions found")
        return 0

    errors: list[str] = []
    for path in agents:
        errors.extend(validate_agent(path))

    for error in errors:
        print(f"error: {error}")

    print(f"agent definition validation: {len(agents)} file(s), {len(errors)} error(s)")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
