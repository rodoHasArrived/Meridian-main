#!/usr/bin/env python3
"""Create missing source READMEs from the source module registry."""

from __future__ import annotations

import fnmatch
import json
import os
from pathlib import Path

from common import build_arg_parser, load_data, repo_root, write_text_if_changed

DEFAULT_IGNORE_PATTERNS = [
    "**/.git/**",
    "**/.vs/**",
    "**/artifacts/**",
    "**/bin/**",
    "**/coverage/**",
    "**/dist/**",
    "**/node_modules/**",
    "**/obj/**",
    "**/TestResults/**",
    "src/Meridian.Ui/wwwroot/workstation/**",
]
STALE_DOCS_REPORT = Path("docs/source/generated/stale-docs.json")

SOURCE_SUFFIXES = {
    ".cs",
    ".csproj",
    ".fs",
    ".fsproj",
    ".ts",
    ".tsx",
    ".js",
    ".jsx",
    ".mjs",
    ".css",
    ".scss",
    ".xaml",
    ".json",
    ".md",
}


def repo_relative(path: Path, root: Path) -> str:
    return path.resolve().relative_to(root.resolve()).as_posix()


def load_ignore_patterns(root: Path) -> list[str]:
    path = root / "docs" / "source" / "data" / "source-readme-ignore.yml"
    if not path.exists():
        return DEFAULT_IGNORE_PATTERNS
    data = load_data(path)
    patterns = data.get("patterns", []) if isinstance(data, dict) else []
    return list(dict.fromkeys(DEFAULT_IGNORE_PATTERNS + [str(pattern) for pattern in patterns]))


def load_tree_roots(root: Path) -> list[str]:
    path = root / "docs" / "source" / "data" / "source-readme-ignore.yml"
    if not path.exists():
        return []
    data = load_data(path)
    roots = data.get("tree_roots", []) if isinstance(data, dict) else []
    return [str(item).rstrip("/") for item in roots]


def load_stale_module_ids(root: Path) -> set[str]:
    path = root / STALE_DOCS_REPORT
    if not path.exists():
        return set()
    report = json.loads(path.read_text(encoding="utf-8"))
    return {entry["module_id"] for entry in report.get("stale_modules", [])}


def is_ignored(path: Path, root: Path, patterns: list[str]) -> bool:
    rel = repo_relative(path, root)
    rel_dir = rel + "/" if path.is_dir() else rel
    return any(fnmatch.fnmatch(rel, pattern) or fnmatch.fnmatch(rel_dir, pattern) for pattern in patterns)


def is_tree_relevant(path: Path, root: Path, tree_roots: list[str]) -> bool:
    if not tree_roots:
        return True
    rel = repo_relative(path, root).rstrip("/")
    return any(rel == item or rel.startswith(item + "/") or item.startswith(rel + "/") for item in tree_roots)


def render_readme(module: dict) -> str:
    module_id = module["id"]
    path = module["path"]
    validation = "\n".join(f"{command}" for command in module.get("validation", []))
    diagrams = ", ".join(f"`{diagram}`" for diagram in module.get("diagrams", [])) or "No diagrams registered."
    return f"""---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: {module_id}
path: {path}
status: {module.get("status", "active")}
owner_lane: {module.get("owner_lane", "Core Team")}
last_reviewed: {module.get("last_reviewed", "2026-05-20")}
---

# {path}

## Purpose

{module.get("purpose", "Describe the module purpose.")}

## Layer responsibility

This module belongs to the {module.get("layer", "unspecified")} layer. Keep changes within that ownership boundary and update the registry if the boundary changes.

## Key folders and files

- `{path}` - registered source module root.

## Important workflows

Use this README to understand the module before editing source files. Update the registry when validation, roadmap links, diagrams, or ownership changes.

## Diagrams

{diagrams}

## Roadmap traceability

<!-- source-roadmap-traceability:begin module={module_id} -->
Generated content.
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module={module_id} -->
Generated content.
<!-- source-todos:end -->

## Validation

```bash
{validation or "python3 build/scripts/docs/validate-source-readmes.py --summary"}
```

## Optional conditional sections

Add only the sections that apply to this module:

- `### Plans and roadmap`
- `### End-user value`
- `### Benchmarks and performance`
- `### Operational evidence`
- `### Security and credentials`
- `### API and contract notes`
- `### Migration and archive notes`

## Change rules

Preserve the module boundary declared in `docs/source/data/source-modules.yml` and update the nearest docs when behavior or workflow semantics change.

## Related docs

- `docs/source/README.md`
- `docs/source/generated/source-module-index.md`
- `docs/architecture/module-map.md`
"""


def render_folder_readme(module: dict, folder: Path, root: Path) -> str:
    module_id = module["id"]
    rel = repo_relative(folder, root)
    parent_rel = repo_relative(folder.parent, root)
    return f"""# {rel}

## Purpose

This folder belongs to `{module_id}` and supports the `{module.get("name", module_id)}` module.

## Parent module

- Module: `{module_id}`
- Module README: `{module.get("readme")}`
- Parent folder: `{parent_rel}`

## Local responsibility

Document the folder-specific purpose, key files, and validation notes here when this folder becomes important enough for AI or maintainers to edit directly.

## Change rules

Keep this README aligned with the parent module README and `docs/source/data/source-modules.yml`. If this folder should not receive generated documentation, add a pattern to `docs/source/data/source-readme-ignore.yml`.
"""


def folder_has_source_files(folder: Path) -> bool:
    for child in folder.iterdir():
        if child.is_file() and child.suffix.lower() in SOURCE_SUFFIXES and child.name != "README.md":
            return True
    return False


def discover_tree_folders(root: Path, modules: list[dict], max_depth: int, patterns: list[str], tree_roots: list[str]) -> list[tuple[dict, Path]]:
    discovered: list[tuple[dict, Path]] = []
    for module in modules:
        module_root = root / module["path"]
        if not module_root.exists():
            continue
        for current, dirs, _files in os.walk(module_root):
            current_path = Path(current)
            if is_ignored(current_path, root, patterns) or not is_tree_relevant(current_path, root, tree_roots):
                dirs[:] = []
                continue
            depth = len(current_path.relative_to(module_root).parts)
            if depth == 0 or depth > max_depth:
                if depth >= max_depth:
                    dirs[:] = []
                continue
            dirs[:] = [
                directory
                for directory in dirs
                if not is_ignored(current_path / directory, root, patterns)
                and is_tree_relevant(current_path / directory, root, tree_roots)
            ]
            if folder_has_source_files(current_path):
                discovered.append((module, current_path))
    return sorted(discovered, key=lambda item: repo_relative(item[1], root))


def sync(root: Path, create_missing: bool, tree: bool = False, max_depth: int = 2, stale_only: bool = False) -> tuple[int, list[str]]:
    data = load_data(root / "docs" / "source" / "data" / "source-modules.yml")
    modules = data.get("modules", [])
    stale_ids = load_stale_module_ids(root) if stale_only else set()
    if stale_only:
        modules = [module for module in modules if module.get("id") in stale_ids]
    changed = 0
    skipped: list[str] = []
    for module in modules:
        readme = root / module["readme"]
        if readme.exists():
            skipped.append(module["id"])
            continue
        if create_missing:
            if write_text_if_changed(readme, render_readme(module)):
                changed += 1
        else:
            skipped.append(module["id"])
    if tree:
        patterns = load_ignore_patterns(root)
        tree_roots = load_tree_roots(root)
        for module, folder in discover_tree_folders(root, modules, max_depth, patterns, tree_roots):
            readme = folder / "README.md"
            rel = repo_relative(readme, root)
            if readme.exists() or is_ignored(readme, root, patterns):
                skipped.append(rel)
                continue
            if create_missing:
                if write_text_if_changed(readme, render_folder_readme(module, folder, root)):
                    changed += 1
            else:
                skipped.append(rel)
    return changed, skipped


def main() -> int:
    parser = build_arg_parser("Create missing source READMEs from docs/source/data/source-modules.yml.")
    parser.add_argument("--create-missing", action="store_true", help="Write README files for registered modules that do not have one.")
    parser.add_argument("--tree", action="store_true", help="Also discover nested source folders under registered modules.")
    parser.add_argument("--max-depth", type=int, default=2, help="Maximum nested folder depth for --tree discovery.")
    parser.add_argument("--stale-only", action="store_true", help="Only sync modules listed in docs/source/generated/stale-docs.json.")
    args = parser.parse_args()
    changed, skipped = sync(repo_root(args.root), args.create_missing, tree=args.tree, max_depth=args.max_depth, stale_only=args.stale_only)
    if args.summary:
        mode = "created" if args.create_missing else "dry-run"
        tree_mode = ", tree" if args.tree else ""
        stale_mode = ", stale-only" if args.stale_only else ""
        print(f"source README sync ({mode}{tree_mode}{stale_mode}): {changed} file(s) changed, {len(skipped)} existing/skipped")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
