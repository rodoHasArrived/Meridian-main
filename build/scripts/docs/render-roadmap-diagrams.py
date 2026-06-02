#!/usr/bin/env python3
"""Render deterministic roadmap Mermaid diagrams."""

from __future__ import annotations

from common import build_arg_parser, load_data, repo_root, write_text_if_changed


def main() -> int:
    parser = build_arg_parser("Render roadmap Mermaid diagram.")
    args = parser.parse_args()
    root = repo_root(args.root)
    items = load_data(root / "docs" / "roadmap" / "data" / "roadmap-items.yml").get("items", [])
    lines = ["flowchart LR"]
    previous = None
    for item in sorted(items, key=lambda entry: entry.get("id", "")):
        node = item.get("id", "").replace("-", "_")
        label = f"{item.get('id')}\\n{item.get('wave')} - {item.get('status')} / {item.get('health')}"
        lines.append(f'  {node}["{label}"]')
        if previous:
            lines.append(f"  {previous} --> {node}")
        previous = node
    changed = write_text_if_changed(root / "docs" / "architecture" / "diagrams" / "meridian-development-roadmap.mmd", "\n".join(lines))
    if args.summary:
        print(f"roadmap diagrams rendered: {1 if changed else 0} file(s) changed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
