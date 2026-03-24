#!/usr/bin/env python3
"""Scan repository for TODO/FIXME (and optional NOTE) comments."""
from __future__ import annotations

import argparse
import json
import re
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Iterable

ROOT = Path(__file__).resolve().parents[3]
DEFAULT_OUTPUT = ROOT / "docs/status/TODO.md"

TEXT_EXTENSIONS = {
    ".cs", ".fs", ".fsx", ".xaml", ".csproj", ".props", ".targets", ".md", ".yml", ".yaml", ".json", ".py",
    ".ts", ".tsx", ".js", ".jsx", ".sh", ".sql", ".xml", ".ps1", ".cmd", ".bat", ".txt", ".toml", ".ini",
}
SKIP_DIRS = {".git", "node_modules", "bin", "obj", ".vs", ".idea", ".vscode", "packages"}
TAG_PATTERN = re.compile(r"\b(TODO|FIXME|NOTE)\b", re.IGNORECASE)
ISSUE_PATTERN = re.compile(r"(?:#\d+|issues?/\d+)")


@dataclass
class TodoItem:
    file: str
    line: int
    tag: str
    text: str
    has_issue: bool


def iter_files(root: Path) -> Iterable[Path]:
    for path in root.rglob("*"):
        if not path.is_file():
            continue
        if any(part in SKIP_DIRS for part in path.parts):
            continue
        if path.suffix.lower() not in TEXT_EXTENSIONS:
            continue
        yield path


def scan_file(path: Path, include_notes: bool) -> list[TodoItem]:
    items: list[TodoItem] = []
    try:
        lines = path.read_text(encoding="utf-8").splitlines()
    except (UnicodeDecodeError, OSError):
        return items

    for idx, line in enumerate(lines, start=1):
        match = TAG_PATTERN.search(line)
        if not match:
            continue
        tag = match.group(1).upper()
        if tag == "NOTE" and not include_notes:
            continue
        text = line.strip()
        items.append(
            TodoItem(
                file=str(path.relative_to(ROOT)).replace("\\", "/"),
                line=idx,
                tag=tag,
                text=text,
                has_issue=bool(ISSUE_PATTERN.search(text)),
            )
        )
    return items


def write_markdown(path: Path, todos: list[TodoItem]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    lines = [
        "# TODO / FIXME / NOTE Scan",
        "",
        f"Total items: **{len(todos)}**",
        "",
        "| File | Line | Tag | Linked Issue | Text |",
        "|---|---:|---|:---:|---|",
    ]
    for item in todos:
        safe_text = item.text.replace("|", "\\|")
        linked = "✅" if item.has_issue else "❌"
        lines.append(f"| `{item.file}` | {item.line} | `{item.tag}` | {linked} | {safe_text} |")

    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def parse_bool(value: str) -> bool:
    return value.strip().lower() in {"1", "true", "yes", "y", "on"}


def main() -> int:
    parser = argparse.ArgumentParser(description="Scan repository TODO/FIXME/NOTE comments")
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--format", choices=["markdown"], default="markdown")
    parser.add_argument("--include-notes", type=parse_bool, default=True)
    parser.add_argument("--json-output", type=Path, default=None)
    args = parser.parse_args()

    todos: list[TodoItem] = []
    for file in iter_files(ROOT):
        todos.extend(scan_file(file, include_notes=args.include_notes))

    todos.sort(key=lambda t: (t.file, t.line, t.tag))

    if args.format == "markdown":
        write_markdown(args.output, todos)

    if args.json_output:
        args.json_output.parent.mkdir(parents=True, exist_ok=True)
        payload = {
            "total_count": len(todos),
            "todos": [asdict(item) for item in todos],
        }
        args.json_output.write_text(json.dumps(payload, indent=2), encoding="utf-8")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
