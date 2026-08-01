#!/usr/bin/env python3
"""Shared helpers for deterministic Meridian documentation automation."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable, Sequence

ROOT = Path(__file__).resolve().parents[3]
GENERATOR_CONTRACT = "meridian.generated-docs.v1"
EMPTY = "-"
EXCLUDE_DIRS = {
    ".git",
    ".vs",
    ".vscode",
    "__pycache__",
    "artifacts",
    "bin",
    "dist",
    "node_modules",
    "obj",
    "TestResults",
}


@dataclass(frozen=True)
class Finding:
    severity: str
    path: str
    message: str


def build_arg_parser(description: str) -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=description)
    parser.add_argument("--root", default=str(ROOT), help="Repository root.")
    parser.add_argument("--summary", action="store_true", help="Print compact summary output.")
    return parser


def repo_root(value: str | Path | None = None) -> Path:
    return Path(value).resolve() if value else ROOT


def repo_path(path: str | Path, root: Path | None = None) -> str:
    base = root or ROOT
    resolved = Path(path)
    if not resolved.is_absolute():
        resolved = base / resolved
    try:
        return resolved.resolve().relative_to(base.resolve()).as_posix()
    except ValueError:
        return resolved.as_posix()


def normalize_path(value: str | Path) -> str:
    return str(value).replace("\\", "/")


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def sha256_manifest_file(path: Path) -> str:
    content = path.read_bytes()
    try:
        text = content.decode("utf-8")
    except UnicodeDecodeError:
        return hashlib.sha256(content).hexdigest()

    normalized = text.replace("\r\n", "\n").replace("\r", "\n")
    return hashlib.sha256(normalized.encode("utf-8")).hexdigest()


def sha256_text(text: str) -> str:
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def write_text_if_changed(path: Path, text: str) -> bool:
    normalized = text.replace("\r\n", "\n").replace("\r", "\n")
    if not normalized.endswith("\n"):
        normalized += "\n"
    existing = path.read_text(encoding="utf-8") if path.exists() else None
    if existing == normalized:
        return False
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="\n") as handle:
        handle.write(normalized)
    return True


def _strip_comment(line: str) -> str:
    in_single = False
    in_double = False
    for index, char in enumerate(line):
        if char == "'" and not in_double:
            in_single = not in_single
        elif char == '"' and not in_single:
            in_double = not in_double
        elif char == "#" and not in_single and not in_double:
            return line[:index]
    return line


def _scalar(value: str) -> Any:
    value = value.strip()
    if value in {"", "null", "Null", "NULL", "~"}:
        return None
    if value in {"true", "True", "TRUE"}:
        return True
    if value in {"false", "False", "FALSE"}:
        return False
    if (value.startswith('"') and value.endswith('"')) or (value.startswith("'") and value.endswith("'")):
        return value[1:-1]
    if value.startswith("[") and value.endswith("]"):
        inner = value[1:-1].strip()
        if not inner:
            return []
        return [_scalar(part.strip()) for part in inner.split(",")]
    if re.fullmatch(r"-?\d+", value):
        try:
            return int(value)
        except ValueError:
            return value
    return value


def _parse_yaml_subset(text: str) -> Any:
    prepared: list[tuple[int, str]] = []
    for raw in text.splitlines():
        line = _strip_comment(raw).rstrip()
        if not line.strip():
            continue
        prepared.append((len(line) - len(line.lstrip(" ")), line.lstrip(" ")))

    def parse_block(index: int, indent: int) -> tuple[Any, int]:
        if index >= len(prepared):
            return {}, index
        is_list = prepared[index][0] == indent and prepared[index][1].startswith("- ")
        if is_list:
            items: list[Any] = []
            while index < len(prepared):
                current_indent, content = prepared[index]
                if current_indent != indent or not content.startswith("- "):
                    break
                item_text = content[2:].strip()
                index += 1
                if not item_text:
                    value, index = parse_block(index, indent + 2)
                    items.append(value)
                    continue
                if ":" in item_text:
                    key, value_text = item_text.split(":", 1)
                    item: dict[str, Any] = {}
                    if value_text.strip():
                        item[key.strip()] = _scalar(value_text.strip())
                    else:
                        value, index = parse_block(index, indent + 2)
                        item[key.strip()] = value
                    while index < len(prepared) and prepared[index][0] == indent + 2 and not prepared[index][1].startswith("- "):
                        nested_key, nested_value_text = prepared[index][1].split(":", 1)
                        index += 1
                        if nested_value_text.strip():
                            item[nested_key.strip()] = _scalar(nested_value_text.strip())
                        else:
                            nested, index = parse_block(index, indent + 4)
                            item[nested_key.strip()] = nested
                    items.append(item)
                else:
                    items.append(_scalar(item_text))
            return items, index

        mapping: dict[str, Any] = {}
        while index < len(prepared):
            current_indent, content = prepared[index]
            if current_indent < indent or content.startswith("- "):
                break
            if current_indent > indent:
                index += 1
                continue
            key, value_text = content.split(":", 1)
            index += 1
            if value_text.strip():
                mapping[key.strip()] = _scalar(value_text.strip())
            else:
                value, index = parse_block(index, indent + 2)
                mapping[key.strip()] = value
        return mapping, index

    parsed, _ = parse_block(0, prepared[0][0] if prepared else 0)
    return parsed


def load_data(path: Path) -> Any:
    text = path.read_text(encoding="utf-8")
    if path.suffix.lower() == ".json":
        return json.loads(text)
    try:
        import yaml  # type: ignore

        return yaml.safe_load(text) or {}
    except Exception:
        return _parse_yaml_subset(text)


def front_matter(markdown: str) -> dict[str, str]:
    if not markdown.startswith("---\n"):
        return {}
    end = markdown.find("\n---", 4)
    if end == -1:
        return {}
    result: dict[str, str] = {}
    for line in markdown[4:end].splitlines():
        if ":" not in line:
            continue
        key, value = line.split(":", 1)
        result[key.strip()] = str(_scalar(value.strip()))
    return result


# Roadmap identifiers are `W<wave><suffix>-<AREA>-<NNN>`, e.g. W1-DATA-001, W5X-CONNECT-001,
# W5X-STMT-ONBOARD-001 (two-token area), W9-ASSET-010, W10-MARK-001.
_WAVE_PATTERN = re.compile(r"^W(\d+)([A-Z]*)$")

# Sorts unparsable tokens deterministically last instead of letting them collide with wave 1.
_UNPARSABLE_SEQUENCE = 1_000_000


def roadmap_item_sort_key(item: dict) -> tuple[int, str, int, str, str]:
    """Order roadmap items by wave, then by rank within the wave.

    Every generated roadmap view shares this ordering so the diagram, the summary, and the register
    cannot disagree about delivery sequence.

    Sorting on the raw identifier string is lexicographic, which places `W10-` between `W1-` and
    `W2-` and orders items within a wave alphabetically by area. Both are wrong: the first renders a
    planned wave in the middle of delivered work, and the second discards the rank that the numeric
    suffix encodes (see `DEC-PRIORITY-SLATE-001`, which states rank is carried in each W9 item's
    numeric suffix).

    Waves sort numerically with their alphabetic suffix as a tiebreak, so `W5` precedes `W5X` and
    `W9` precedes `W10`.

    Within a wave, an explicit `sequence` wins when the item declares a usable one. Otherwise rank
    falls back to the identifier's trailing number, which is only meaningful for waves that encode
    rank there. A wave that numbers per area instead - every row `-001` - has no recoverable rank
    without the explicit field. Area and identifier remain as stability tiebreaks.
    """
    identifier = item.get("id", "") or ""
    parts = identifier.split("-")
    wave_token = parts[0] if parts else ""
    wave_match = _WAVE_PATTERN.match(wave_token)
    wave_number = int(wave_match.group(1)) if wave_match else _UNPARSABLE_SEQUENCE
    wave_suffix = wave_match.group(2) if wave_match else wave_token

    declared = item.get("sequence")
    if is_usable_sequence(declared):
        sequence = declared
    else:
        try:
            sequence = int(parts[-1])
        except (IndexError, ValueError):
            sequence = _UNPARSABLE_SEQUENCE

    area = "-".join(parts[1:-1])
    return (wave_number, wave_suffix, sequence, area, identifier)


def is_usable_sequence(value: Any) -> bool:
    """Whether a declared `sequence` is a rank the renderers may sort on.

    `bool` is an `int` subclass in Python, so `sequence: true` would otherwise sort as rank 1. The
    schema also bounds the field at 1; a nonpositive value is rejected here rather than silently
    ordering ahead of every legitimate rank. Validation reports these; the renderers fall back.
    """
    return isinstance(value, int) and not isinstance(value, bool) and value >= 1


def markdown_table(headers: Sequence[str], rows: Iterable[Sequence[Any]]) -> str:
    output = [
        "| " + " | ".join(headers) + " |",
        "| " + " | ".join("---" for _ in headers) + " |",
    ]
    for row in rows:
        output.append("| " + " | ".join(str(cell if cell not in {None, ""} else EMPTY) for cell in row) + " |")
    return "\n".join(output)


def generated_header(
    generator: str,
    schema_versions: Sequence[str],
    inputs: Sequence[str],
    generator_version: str = "1.0.0",
) -> str:
    """Emit the standard generated-doc header.

    `generator_version` is per-generator: a generator that changes its output semantics bumps its
    own version so an audit can tell which ordering or shape contract produced a committed artifact,
    without disturbing every other generated doc.
    """
    schema_lines = "\n".join(f"  - {item}" for item in schema_versions)
    input_lines = "\n".join(f"  - {normalize_path(item)}" for item in inputs)
    return (
        "<!--\n"
        "generated: true\n"
        f"generator: {normalize_path(generator)}\n"
        f"generator_version: {generator_version}\n"
        f"render_contract: {GENERATOR_CONTRACT}\n"
        "schema_versions:\n"
        f"{schema_lines}\n"
        "inputs:\n"
        f"{input_lines}\n"
        "do_not_edit: true\n"
        "-->\n"
    )


def write_manifest(
    path: Path,
    generator: str,
    inputs: Sequence[Path],
    outputs: Sequence[Path],
    root: Path,
    generator_version: str = "1.0.0",
) -> bool:
    def sort_key(item: Path) -> str:
        return repo_path(item, root)

    payload = {
        "schema": {"id": "meridian.generated-manifest", "version": "1.0.0"},
        "generator": {"name": normalize_path(generator), "version": generator_version},
        "inputs": [{"path": repo_path(item, root), "sha256": sha256_manifest_file(item)} for item in sorted(inputs, key=sort_key)],
        "outputs": [{"path": repo_path(item, root), "sha256": sha256_manifest_file(item)} for item in sorted(outputs, key=sort_key) if item.exists()],
    }
    return write_text_if_changed(path, json.dumps(payload, indent=2, sort_keys=True))


def replace_marked_block(markdown: str, begin: str, end: str, replacement: str) -> tuple[str, bool]:
    pattern = re.compile(re.escape(begin) + r".*?" + re.escape(end), re.DOTALL)
    new_block = f"{begin}\n{replacement.rstrip()}\n{end}"
    updated, count = pattern.subn(new_block, markdown)
    return updated, count > 0


def emit_findings(findings: Sequence[Finding], summary: bool, ok_message: str) -> int:
    errors = [finding for finding in findings if finding.severity == "error"]
    warnings = [finding for finding in findings if finding.severity == "warning"]
    if summary:
        print(f"{ok_message}: {len(errors)} error(s), {len(warnings)} warning(s)")
    for finding in findings:
        stream = sys.stderr if finding.severity == "error" else sys.stdout
        print(f"{finding.severity.upper()}: {finding.path}: {finding.message}", file=stream)
    return 1 if errors else 0
