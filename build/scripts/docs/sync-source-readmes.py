#!/usr/bin/env python3
"""Create missing source READMEs from the source module registry."""

from __future__ import annotations

from pathlib import Path

from common import build_arg_parser, load_data, repo_root, write_text_if_changed


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

## Change rules

Preserve the module boundary declared in `docs/source/data/source-modules.yml` and update the nearest docs when behavior or workflow semantics change.

## Related docs

- `docs/source/README.md`
- `docs/source/generated/source-module-index.md`
- `docs/architecture/module-map.md`
"""


def sync(root: Path, create_missing: bool) -> tuple[int, list[str]]:
    data = load_data(root / "docs" / "source" / "data" / "source-modules.yml")
    changed = 0
    skipped: list[str] = []
    for module in data.get("modules", []):
        readme = root / module["readme"]
        if readme.exists():
            skipped.append(module["id"])
            continue
        if create_missing:
            if write_text_if_changed(readme, render_readme(module)):
                changed += 1
        else:
            skipped.append(module["id"])
    return changed, skipped


def main() -> int:
    parser = build_arg_parser("Create missing source READMEs from docs/source/data/source-modules.yml.")
    parser.add_argument("--create-missing", action="store_true", help="Write README files for registered modules that do not have one.")
    args = parser.parse_args()
    changed, skipped = sync(repo_root(args.root), args.create_missing)
    if args.summary:
        mode = "created" if args.create_missing else "dry-run"
        print(f"source README sync ({mode}): {changed} file(s) changed, {len(skipped)} existing/skipped")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
