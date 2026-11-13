#!/usr/bin/env python3
"""Render the sample configuration as a deterministic, source-linked reference.

The sample is JSON with comments. This reference records its explicit values;
it deliberately makes no claim about missing-key defaults or runtime validation.
"""

from __future__ import annotations

import argparse
from dataclasses import dataclass
import json
from pathlib import Path
import re
import sys

from common import ROOT, write_text_if_changed


SAMPLE_PATH = Path("config/appsettings.sample.json")
OUTPUT_PATH = Path("docs/generated/configuration-schema.md")
_DECODER = json.JSONDecoder()
_SENSITIVE_SEGMENT = re.compile(
    r"(?:password|secret|token|apikey|credential|connectionstring|privatekey)", re.I
)


@dataclass(frozen=True)
class Entry:
    path: str
    value: object
    line: int
    note: str


def strip_comments(source: str) -> str:
    """Blank JSONC comments while preserving string contents and source offsets."""
    result = list(source)
    cursor = 0
    while cursor < len(source):
        if source[cursor] == '"':
            _, consumed = _DECODER.raw_decode(source, cursor)
            cursor = consumed
        elif source.startswith("//", cursor):
            end = source.find("\n", cursor)
            end = len(source) if end < 0 else end
            result[cursor:end] = " " * (end - cursor)
            cursor = end
        elif source.startswith("/*", cursor):
            close = source.find("*/", cursor + 2)
            if close < 0:
                raise ValueError("Unterminated JSON block comment")
            end = close + 2
            result[cursor:end] = ["\n" if ch == "\n" else " " for ch in source[cursor:end]]
            cursor = end
        else:
            cursor += 1
    return "".join(result)


def _unique_object(pairs: list[tuple[str, object]]) -> dict[str, object]:
    result: dict[str, object] = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"Duplicate configuration key: {key}")
        result[key] = value
    return result


def _preceding_note(lines: list[str], line: int) -> str:
    comments = []
    cursor = line - 2
    while cursor >= 0 and lines[cursor].lstrip().startswith("//"):
        comment = lines[cursor].lstrip()[2:].strip()
        if not comment or re.fullmatch(r"[-=]+", comment):
            break
        comments.append(comment)
        cursor -= 1
    return " ".join(reversed(comments))


def sample_entries(source: str) -> list[Entry]:
    """Read every explicit key, including keys inside array objects, in source order."""
    cleaned = strip_comments(source)
    data = json.loads(cleaned, object_pairs_hook=_unique_object)
    if not isinstance(data, dict) or not data:
        raise ValueError("Sample configuration must be a nonempty JSON object")
    lines = source.splitlines()
    entries: list[Entry] = []

    def skip_space(cursor: int) -> int:
        while cursor < len(cleaned) and cleaned[cursor].isspace():
            cursor += 1
        return cursor

    def walk(cursor: int, value: object, path: str) -> int:
        cursor = skip_space(cursor)
        if isinstance(value, dict):
            cursor = skip_space(cursor + 1)
            for key, child in value.items():
                parsed_key, end = _DECODER.raw_decode(cleaned, cursor)
                if parsed_key != key:
                    raise ValueError(f"Source key mismatch for {path}")
                line = cleaned.count("\n", 0, cursor) + 1
                child_path = f"{path}.{key}" if path else key
                entries.append(Entry(child_path, child, line, _preceding_note(lines, line)))
                cursor = skip_space(end)
                if cleaned[cursor] != ":":
                    raise ValueError(f"Missing ':' after {child_path}")
                cursor = skip_space(walk(cursor + 1, child, child_path))
                if cleaned[cursor] == ",":
                    cursor = skip_space(cursor + 1)
            return cursor + 1
        if isinstance(value, list):
            cursor = skip_space(cursor + 1)
            for index, child in enumerate(value):
                cursor = skip_space(walk(cursor, child, f"{path}[{index}]"))
                if cleaned[cursor] == ",":
                    cursor = skip_space(cursor + 1)
            return cursor + 1
        _, end = _DECODER.raw_decode(cleaned, cursor)
        return end

    walk(0, data, "")
    return entries


def json_type(value: object) -> str:
    if value is None:
        return "null"
    if isinstance(value, bool):
        return "boolean"
    if isinstance(value, int):
        return "integer"
    if isinstance(value, float):
        return "number"
    if isinstance(value, dict):
        return "object"
    if isinstance(value, list):
        return "array"
    return "string"


def sample_value(entry: Entry) -> str:
    if _SENSITIVE_SEGMENT.search(entry.path):
        return "[redacted]"
    if isinstance(entry.value, dict):
        return f"object ({len(entry.value)} keys)"
    if isinstance(entry.value, list) and any(isinstance(item, (dict, list)) for item in entry.value):
        return f"array ({len(entry.value)} items; fields below)"
    return json.dumps(entry.value, ensure_ascii=False)


def _cell(value: str) -> str:
    return value.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;").replace("|", "&#124;").replace("`", "&#96;").replace("\n", " ")


def render(source: str) -> str:
    entries = sample_entries(source)
    lines = [
        "<!-- Generated by build/scripts/docs/generate-configuration-reference.py. Auto-generated. Do not edit directly. -->",
        "",
        "# Configuration Schema",
        "",
        "Source: [`config/appsettings.sample.json`](../../config/appsettings.sample.json).",
        "",
        "This reference lists the keys and explicit example values in the sample configuration.",
        "The JSON type column describes each sample value; `null` does not establish the allowed runtime type.",
        "These values are **sample values, not missing-key runtime defaults**. Comments are copied from",
        "the sample and describe that example; they do not certify provider entitlements or live behavior.",
        "The [machine schema](../../config/appsettings.schema.json),",
        "[Appsettings Schema Reference](../reference/appsettings-schema.md), and",
        "[Environment Variables](../reference/environment-variables.md) describe validation and supported overrides.",
        "",
        "Object rows introduce the child keys below them. Array-object fields retain their sample index",
        "(for example `DataSources.Sources[0].Id`); the index identifies an example, not a fixed runtime slot.",
        "Sensitive-key values are redacted. Only the tracked sample is read; local configuration and environment values are never read.",
        "",
        f"Explicit sample keys: **{len(entries)}**. Regenerate with:",
        "",
        "```bash",
        "python3 build/scripts/docs/generate-configuration-reference.py",
        "python3 build/scripts/docs/generate-configuration-reference.py --check",
        "```",
        "",
    ]
    for entry in entries:
        if "." not in entry.path and "[" not in entry.path:
            lines.extend([
                f"## `{entry.path}`", "",
                "| Key | Sample JSON type | Sample value | Source comment | Source |",
                "| --- | --- | --- | --- | --- |",
            ])
        note = entry.note or "—"
        # Never echo a sensitive comment: it could contain a sample credential.
        if _SENSITIVE_SEGMENT.search(entry.path):
            note = "Sensitive setting; provide credentials through the documented secret mechanism."
        lines.append(
            f"| `{_cell(entry.path)}` | {json_type(entry.value)} | `{_cell(sample_value(entry))}` "
            f"| {_cell(note)} | [line {entry.line}](../../config/appsettings.sample.json#L{entry.line}) |"
        )
    # A blank line before each heading is needed after a preceding table.
    return "\n".join(lines).replace("\n## `", "\n\n## `").rstrip() + "\n"


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=ROOT, help="Repository root")
    parser.add_argument("--check", action="store_true", help="Fail if the committed reference is stale; do not write")
    args = parser.parse_args(argv)
    root = args.root.resolve()
    try:
        rendered = render((root / SAMPLE_PATH).read_text(encoding="utf-8"))
        output = root / OUTPUT_PATH
        if args.check:
            if not output.is_file() or output.read_text(encoding="utf-8") != rendered:
                print(f"Stale configuration reference: {OUTPUT_PATH}", file=sys.stderr)
                return 1
            print(f"Configuration reference is current: {OUTPUT_PATH}")
        else:
            write_text_if_changed(output, rendered)
            print(f"Configuration reference written: {OUTPUT_PATH}")
    except (OSError, ValueError) as exc:
        print(f"Configuration reference failed: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
