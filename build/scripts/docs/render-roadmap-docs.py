#!/usr/bin/env python3
"""Render deterministic roadmap registry documentation."""

from __future__ import annotations

from pathlib import Path

from common import (
    build_arg_parser,
    generated_header,
    load_data,
    markdown_table,
    repo_path,
    repo_root,
    roadmap_item_sort_key,
    write_manifest,
    write_text_if_changed,
)


def load_roadmap(root: Path) -> tuple[dict, dict, list[Path]]:
    data_dir = root / "docs" / "roadmap" / "data"
    program = load_data(data_dir / "program-state.yml")
    items = load_data(data_dir / "roadmap-items.yml")
    inputs = sorted(data_dir.glob("*.yml"))
    return program, items, inputs


def render_summary(root: Path, program: dict, roadmap: dict, inputs: list[Path]) -> str:
    items = sorted(roadmap.get("items", []), key=roadmap_item_sort_key)
    schema_versions = [
        f"{program.get('schema', {}).get('id')}@{program.get('schema', {}).get('version')}",
        f"{roadmap.get('schema', {}).get('id')}@{roadmap.get('schema', {}).get('version')}",
    ]
    rows = [
        [
            item.get("id"),
            item.get("title"),
            item.get("status"),
            item.get("health"),
            item.get("priority"),
            item.get("owner_lane"),
        ]
        for item in items
    ]
    return (
        generated_header("build/scripts/docs/render-roadmap-docs.py", schema_versions, [repo_path(path, root) for path in inputs])
        + "\n# Roadmap Summary\n\n"
        + f"Snapshot date: {program.get('program', {}).get('snapshot_date', '-')}\n\n"
        + markdown_table(["ID", "Title", "Status", "Health", "Priority", "Owner lane"], rows)
        + "\n"
    )


def render_register(root: Path, program: dict, roadmap: dict, inputs: list[Path]) -> str:
    items = sorted(roadmap.get("items", []), key=roadmap_item_sort_key)
    schema_versions = [f"{roadmap.get('schema', {}).get('id')}@{roadmap.get('schema', {}).get('version')}"]
    parts = [
        generated_header("build/scripts/docs/render-roadmap-docs.py", schema_versions, [repo_path(path, root) for path in inputs]),
        "\n# Roadmap Register\n",
        f"\nSnapshot date: {program.get('program', {}).get('snapshot_date', '-')}\n",
    ]
    for item in items:
        parts.append(f"\n## {item.get('id')} - {item.get('title')}\n")
        parts.append(
            markdown_table(
                ["Field", "Value"],
                [
                    ["Wave", item.get("wave")],
                    ["Status", item.get("status")],
                    ["Health", item.get("health")],
                    ["Priority", item.get("priority")],
                    ["Owner lane", item.get("owner_lane")],
                    ["Evidence posture", item.get("evidence_posture")],
                    ["Last reviewed", item.get("last_reviewed")],
                ],
            )
        )
        parts.append("\n\n### Current Summary\n\n")
        parts.append(str(item.get("current_summary", "-")))
        parts.append("\n\n### Exit Criteria\n\n")
        for criterion in item.get("exit_criteria", []) or []:
            parts.append(f"- {criterion}\n")
        parts.append("\n### Source Modules\n\n")
        for module in item.get("source_modules", []) or []:
            parts.append(f"- `{module}`\n")
    return "".join(parts)


def main() -> int:
    parser = build_arg_parser("Render deterministic roadmap docs.")
    args = parser.parse_args()
    root = repo_root(args.root)
    program, roadmap, inputs = load_roadmap(root)
    output_dir = root / "docs" / "roadmap" / "generated"
    status_dir = root / "docs" / "status"
    outputs = [
        output_dir / "ROADMAP_SUMMARY.md",
        output_dir / "roadmap-register.md",
        status_dir / "ROADMAP_SUMMARY.md",
    ]
    changed = [
        write_text_if_changed(outputs[0], render_summary(root, program, roadmap, inputs)),
        write_text_if_changed(outputs[1], render_register(root, program, roadmap, inputs)),
        write_text_if_changed(outputs[2], render_summary(root, program, roadmap, inputs)),
    ]
    manifest_changed = write_manifest(output_dir / "MANIFEST.json", "build/scripts/docs/render-roadmap-docs.py", inputs, outputs, root)
    if args.summary:
        print(f"roadmap docs rendered: {sum(1 for item in changed if item) + int(manifest_changed)} file(s) changed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
