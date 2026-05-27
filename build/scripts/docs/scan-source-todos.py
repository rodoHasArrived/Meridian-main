#!/usr/bin/env python3
"""Validate source TODO comments against the source TODO registry."""

from __future__ import annotations

import os
import re
from pathlib import Path

from common import EXCLUDE_DIRS, Finding, build_arg_parser, emit_findings, load_data, repo_path, repo_root

TODO_RE = re.compile(r"(?://|#|<!--|/\*)\s*(TODO|FIXME|HACK)\(([^)]+)\):")


def iter_text_files(root: Path, module_paths: list[Path]):
    suffixes = {".cs", ".fs", ".ts", ".tsx", ".js", ".jsx", ".py", ".md", ".ps1", ".mjs"}
    for module_path in module_paths:
        if module_path.is_file():
            candidates = [module_path]
            for path in candidates:
                if path.suffix.lower() in suffixes:
                    yield path
            continue
        else:
            for current, dirs, files in os.walk(module_path):
                dirs[:] = [directory for directory in dirs if directory not in EXCLUDE_DIRS]
                current_path = Path(current)
                for file_name in files:
                    path = current_path / file_name
                    if path.suffix.lower() in suffixes:
                        yield path


def validate(root: Path) -> list[Finding]:
    findings: list[Finding] = []
    source_dir = root / "docs" / "source" / "data"
    todos_path = source_dir / "source-todos.yml"
    modules_path = source_dir / "source-modules.yml"
    roadmap_path = root / "docs" / "roadmap" / "data" / "roadmap-items.yml"

    todos_data = load_data(todos_path) if todos_path.exists() else {}
    modules_data = load_data(modules_path) if modules_path.exists() else {}
    roadmap_data = load_data(roadmap_path) if roadmap_path.exists() else {}

    todos = todos_data.get("todos", []) if isinstance(todos_data, dict) else []
    modules = modules_data.get("modules", []) if isinstance(modules_data, dict) else []
    roadmap_items = roadmap_data.get("items", []) if isinstance(roadmap_data, dict) else []
    todo_ids = {todo.get("id") for todo in todos if isinstance(todo, dict)}
    module_ids = {module.get("id") for module in modules if isinstance(module, dict)}
    roadmap_ids = {item.get("id") for item in roadmap_items if isinstance(item, dict)}
    module_paths = [root / module.get("path", "") for module in modules if isinstance(module, dict) and module.get("path")]

    for todo in todos:
        todo_id = todo.get("id", "<missing>")
        if not todo.get("module_id") or todo.get("module_id") not in module_ids:
            findings.append(Finding("error", repo_path(todos_path, root), f"{todo_id} references unknown module {todo.get('module_id')}"))
        if todo.get("roadmap_item") and todo.get("roadmap_item") not in roadmap_ids:
            findings.append(Finding("error", repo_path(todos_path, root), f"{todo_id} references unknown roadmap item {todo.get('roadmap_item')}"))
        if todo.get("priority") == "high" and not todo.get("owner_lane"):
            findings.append(Finding("error", repo_path(todos_path, root), f"{todo_id} is high priority without owner_lane"))
        if todo.get("status") == "closed":
            for source_path in todo.get("source_paths", []) or []:
                candidate = root / source_path
                if candidate.exists() and todo_id in candidate.read_text(encoding="utf-8", errors="ignore"):
                    findings.append(Finding("error", repo_path(candidate, root), f"closed TODO {todo_id} still appears in source"))

    seen_inline: set[str] = set()
    for path in iter_text_files(root, module_paths):
        text = path.read_text(encoding="utf-8", errors="ignore")
        for line_number, line in enumerate(text.splitlines(), 1):
            if "TODO" not in line and "FIXME" not in line and "HACK" not in line:
                continue
            stripped = line.lstrip()
            if path.suffix.lower() == ".md" and not stripped.startswith("<!--"):
                continue
            match = TODO_RE.search(line)
            if not match:
                if re.search(r"(?://|#|<!--|/\*)\s*(TODO|FIXME|HACK)\b", line):
                    findings.append(Finding("error", f"{repo_path(path, root)}:{line_number}", "TODO/FIXME/HACK comment is missing registry or roadmap ID"))
                continue
            reference = match.group(2)
            seen_inline.add(reference)
            if reference not in todo_ids and reference not in roadmap_ids:
                findings.append(Finding("error", f"{repo_path(path, root)}:{line_number}", f"unknown TODO reference {reference}"))

    return findings


def main() -> int:
    parser = build_arg_parser("Validate source TODO registry alignment.")
    args = parser.parse_args()
    return emit_findings(validate(repo_root(args.root)), args.summary, "source TODO validation")


if __name__ == "__main__":
    raise SystemExit(main())
