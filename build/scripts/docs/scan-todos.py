#!/usr/bin/env python3
"""Scan repository for explicit TODO/FIXME/HACK/NOTE annotations."""
from __future__ import annotations

import argparse
import json
import os
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
SKIP_DIRS = {
    ".git",
    "node_modules",
    "bin",
    "obj",
    ".vs",
    ".idea",
    ".vscode",
    "packages",
    "__pycache__",
    ".pytest_cache",
    "artifacts",
    "dist",
    "output",
    "publish",
}
# Files/dirs whose content is meta (they process or report TODOs rather than contain them).
# Scanning them produces thousands of self-referential false positives.
SKIP_PATH_PREFIXES = (
    "docs/status/TODO",                              # the output file itself
    "docs/status/todo-scan-results.json",            # machine-readable TODO scan output
    "docs/status/todo-issue-creation-summary.json",  # machine-readable issue creation output
    "build/scripts/docs/",                           # scripts that process TODO scan results
    "docs/_site/",                                   # generated docfx site
    "docs/generated/",                               # auto-generated documentation
    "docs/examples/provider-template/",              # template examples (not work items)
    "archive/",                                      # archived/historical documentation
    ".github/workflows/documentation.yml",           # workflow script with many metadata TODOs
    "src/Meridian.Infrastructure/Adapters/Templates/",  # scaffold template with intentional developer instructions (not work items)
    "docs/development/documentation-automation.md",  # meta-doc describing the TODO scanning system itself
    ".claude/worktrees/",                            # local worktree copies duplicate repository content
)
TAG_PATTERN = re.compile(r"\b(TODO|FIXME|HACK|NOTE)\b\s*:", re.IGNORECASE)
ISSUE_PATTERN = re.compile(r"(?:#\d+|issues?/\d+)")
PRIORITY_BY_TAG = {
    "FIXME": "high",
    "HACK": "high",
    "TODO": "normal",
    "NOTE": "low",
}


@dataclass
class TodoItem:
    file: str
    line: int
    tag: str
    text: str
    has_issue: bool
    issue_refs: list[str]
    priority: str
    context: str

    def to_json_dict(self) -> dict[str, object]:
        payload = asdict(self)
        payload["type"] = self.tag
        return payload


def is_excluded(rel_path: str) -> bool:
    return any(rel_path.startswith(prefix) for prefix in SKIP_PATH_PREFIXES)


def should_descend(rel_path: str) -> bool:
    if not rel_path:
        return True
    return not is_excluded(f"{rel_path}/")


def iter_files(root: Path) -> Iterable[Path]:
    for dirpath, dirnames, filenames in os.walk(root):
        current = Path(dirpath)
        rel_dir = "" if current == root else current.relative_to(root).as_posix()

        dirnames[:] = [
            name
            for name in dirnames
            if name not in SKIP_DIRS and should_descend(f"{rel_dir}/{name}".strip("/"))
        ]

        for filename in filenames:
            path = current / filename
            if path.suffix.lower() not in TEXT_EXTENSIONS:
                continue
            rel_path = f"{rel_dir}/{filename}".strip("/")
            if is_excluded(rel_path):
                continue
            yield path


def has_annotation_prefix(prefix: str) -> bool:
    normalized = prefix.strip()
    if not normalized:
        return True

    if normalized in {"-", "*", ">", "|", ";"}:
        return True

    if normalized.endswith(("//", "#", "<!--", "/*", "|")):
        return True

    if re.fullmatch(r"\d+\.", normalized):
        return True

    return bool(re.fullmatch(r"[-*]\s+\[[ xX]\]", normalized))


def build_context(lines: list[str], line_number: int, radius: int = 1) -> str:
    start = max(1, line_number - radius)
    end = min(len(lines), line_number + radius)
    return "\n".join(f"{idx}: {lines[idx - 1].rstrip()}" for idx in range(start, end + 1))


def scan_file(path: Path, root: Path, include_notes: bool) -> list[TodoItem]:
    items: list[TodoItem] = []
    try:
        lines = path.read_text(encoding="utf-8").splitlines()
    except (UnicodeDecodeError, OSError):
        return items

    for idx, line in enumerate(lines, start=1):
        match = TAG_PATTERN.search(line)
        if not match:
            continue

        if not has_annotation_prefix(line[:match.start()]):
            continue

        tag = match.group(1).upper()
        if tag == "NOTE" and not include_notes:
            continue

        text = line.strip()
        issue_refs = ISSUE_PATTERN.findall(text)
        items.append(
            TodoItem(
                file=str(path.relative_to(root)).replace("\\", "/"),
                line=idx,
                tag=tag,
                text=text,
                has_issue=bool(issue_refs),
                issue_refs=issue_refs,
                priority=PRIORITY_BY_TAG.get(tag, "normal"),
                context=build_context(lines, idx),
            )
        )
    return items


def write_markdown(path: Path, todos: list[TodoItem]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    lines = [
        "# TODO / FIXME / HACK / NOTE Scan",
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
    parser = argparse.ArgumentParser(description="Scan repository TODO/FIXME/HACK/NOTE annotations")
    parser.add_argument("--root", type=Path, default=ROOT, help="Repository root to scan")
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--format", choices=["markdown"], default="markdown")
    parser.add_argument("--include-notes", type=parse_bool, default=True)
    parser.add_argument("--json-output", type=Path, default=None)
    args = parser.parse_args()

    root = args.root.resolve()

    todos: list[TodoItem] = []
    for file in iter_files(root):
        todos.extend(scan_file(file, root, include_notes=args.include_notes))

    todos.sort(key=lambda t: (t.file, t.line, t.tag))

    if args.format == "markdown":
        write_markdown(args.output, todos)

    if args.json_output:
        args.json_output.parent.mkdir(parents=True, exist_ok=True)
        payload = {
            "total_count": len(todos),
            "todos": [item.to_json_dict() for item in todos],
        }
        args.json_output.write_text(json.dumps(payload, indent=2), encoding="utf-8")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
