#!/usr/bin/env python3
"""Render deterministic source registry documentation and README generated blocks."""

from __future__ import annotations

from pathlib import Path
import json

from common import (
    build_arg_parser,
    generated_header,
    load_data,
    markdown_table,
    replace_marked_block,
    repo_path,
    repo_root,
    write_manifest,
    write_text_if_changed,
)

STALE_DOCS_REPORT = Path("docs/source/generated/stale-docs.json")


def source_inputs(root: Path) -> list[Path]:
    return sorted((root / "docs" / "source" / "data").glob("*.yml"))


def module_maps(root: Path) -> tuple[dict, dict, dict]:
    source_dir = root / "docs" / "source" / "data"
    roadmap_dir = root / "docs" / "roadmap" / "data"
    modules = load_data(source_dir / "source-modules.yml")
    todos = load_data(source_dir / "source-todos.yml")
    roadmap = load_data(roadmap_dir / "roadmap-items.yml")
    return modules, todos, roadmap


def render_module_index(root: Path, modules: dict, inputs: list[Path]) -> str:
    rows = []
    for module in sorted(modules.get("modules", []), key=lambda item: item.get("id", "")):
        rows.append(
            [
                f"`{module.get('id')}`",
                module.get("name"),
                f"`{module.get('path')}`",
                module.get("layer"),
                module.get("status"),
                module.get("owner_lane"),
                f"`{module.get('readme')}`",
            ]
        )
    schema_versions = [f"{modules.get('schema', {}).get('id')}@{modules.get('schema', {}).get('version')}"]
    return (
        generated_header("build/scripts/docs/render-source-docs.py", schema_versions, [repo_path(path, root) for path in inputs])
        + "\n# Source Module Index\n\n"
        + markdown_table(["ID", "Name", "Path", "Layer", "Status", "Owner lane", "README"], rows)
        + "\n"
    )


def render_traceability(root: Path, modules: dict, roadmap: dict, inputs: list[Path]) -> str:
    item_titles = {item.get("id"): item.get("title") for item in roadmap.get("items", [])}
    rows = []
    for module in sorted(modules.get("modules", []), key=lambda item: item.get("id", "")):
        for roadmap_item in module.get("roadmap_items", []) or []:
            rows.append([f"`{module.get('id')}`", module.get("name"), f"`{roadmap_item}`", item_titles.get(roadmap_item, "-")])
    schema_versions = [f"{modules.get('schema', {}).get('id')}@{modules.get('schema', {}).get('version')}"]
    return (
        generated_header("build/scripts/docs/render-source-docs.py", schema_versions, [repo_path(path, root) for path in inputs])
        + "\n# Source Roadmap Traceability\n\n"
        + markdown_table(["Module", "Name", "Roadmap item", "Roadmap title"], rows)
        + "\n"
    )


def render_todo_checklist(root: Path, todos: dict, inputs: list[Path]) -> str:
    rows = []
    for todo in sorted(todos.get("todos", []), key=lambda item: item.get("id", "")):
        rows.append([f"`{todo.get('id')}`", todo.get("title"), f"`{todo.get('module_id')}`", todo.get("status"), todo.get("priority"), todo.get("owner_lane")])
    schema_versions = [f"{todos.get('schema', {}).get('id')}@{todos.get('schema', {}).get('version')}"]
    return (
        generated_header("build/scripts/docs/render-source-docs.py", schema_versions, [repo_path(path, root) for path in inputs])
        + "\n# Source TODO Checklist\n\n"
        + markdown_table(["ID", "Title", "Module", "Status", "Priority", "Owner lane"], rows)
        + "\n"
    )


def readme_blocks(module: dict, todos: dict, roadmap: dict) -> tuple[str, str]:
    item_titles = {item.get("id"): item.get("title") for item in roadmap.get("items", [])}
    roadmap_rows = [[f"`{item}`", item_titles.get(item, "-")] for item in module.get("roadmap_items", []) or []]
    todo_rows = [
        [f"`{todo.get('id')}`", todo.get("title"), todo.get("status"), todo.get("priority")]
        for todo in todos.get("todos", [])
        if todo.get("module_id") == module.get("id")
    ]
    trace = markdown_table(["Roadmap item", "Title"], roadmap_rows) if roadmap_rows else "- No roadmap items registered."
    checklist = markdown_table(["TODO", "Title", "Status", "Priority"], todo_rows) if todo_rows else "- No registry-backed TODOs are open for this module."
    return trace, checklist


def load_stale_module_ids(root: Path) -> set[str]:
    path = root / STALE_DOCS_REPORT
    if not path.exists():
        return set()
    report = json.loads(path.read_text(encoding="utf-8"))
    return {entry["module_id"] for entry in report.get("stale_modules", [])}


def update_readmes(root: Path, modules: dict, todos: dict, roadmap: dict, module_filter: set[str] | None = None) -> int:
    changed = 0
    for module in modules.get("modules", []):
        if module_filter is not None and module.get("id") not in module_filter:
            continue
        readme = root / module.get("readme", "")
        if not readme.exists():
            continue
        markdown = readme.read_text(encoding="utf-8")
        trace, checklist = readme_blocks(module, todos, roadmap)
        trace_begin = f"<!-- source-roadmap-traceability:begin module={module.get('id')} -->"
        todo_begin = f"<!-- source-todos:begin module={module.get('id')} -->"
        markdown, trace_replaced = replace_marked_block(markdown, trace_begin, "<!-- source-roadmap-traceability:end -->", trace)
        markdown, todo_replaced = replace_marked_block(markdown, todo_begin, "<!-- source-todos:end -->", checklist)
        if trace_replaced and todo_replaced and write_text_if_changed(readme, markdown):
            changed += 1
    return changed


def main() -> int:
    parser = build_arg_parser("Render deterministic source docs.")
    parser.add_argument("--stale-only", action="store_true", help="Only update README generated blocks for modules listed in stale-docs.json.")
    args = parser.parse_args()
    root = repo_root(args.root)
    modules, todos, roadmap = module_maps(root)
    inputs = source_inputs(root)
    output_dir = root / "docs" / "source" / "generated"
    outputs = [
        output_dir / "source-module-index.md",
        output_dir / "source-roadmap-traceability.md",
        output_dir / "source-todo-checklist.md",
    ]
    changed = [
        write_text_if_changed(outputs[0], render_module_index(root, modules, inputs)),
        write_text_if_changed(outputs[1], render_traceability(root, modules, roadmap, inputs)),
        write_text_if_changed(outputs[2], render_todo_checklist(root, todos, inputs)),
    ]
    module_filter = load_stale_module_ids(root) if args.stale_only else None
    readme_count = update_readmes(root, modules, todos, roadmap, module_filter)
    manifest_changed = write_manifest(output_dir / "MANIFEST.json", "build/scripts/docs/render-source-docs.py", inputs, outputs, root)
    if args.summary:
        mode = " stale modules" if args.stale_only else ""
        print(f"source docs rendered{mode}: {sum(1 for item in changed if item) + readme_count + int(manifest_changed)} file(s) changed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
