#!/usr/bin/env python3
"""Render deterministic source module diagrams."""

from __future__ import annotations

from pathlib import Path

from common import build_arg_parser, load_data, repo_root, write_text_if_changed


def main() -> int:
    parser = build_arg_parser("Render source module Mermaid diagram.")
    args = parser.parse_args()
    root = repo_root(args.root)
    modules = load_data(root / "docs" / "source" / "data" / "source-modules.yml").get("modules", [])
    lines = [
        "flowchart LR",
        '  Contracts["Contracts"]',
        '  Core["Core"]',
        '  Domain["Domain"]',
        '  Application["Application"]',
        '  UiServices["UI Services"]',
        '  UiShared["UI Shared"]',
        '  Dashboard["Browser Dashboard"]',
        '  Wpf["WPF Retained"]',
        "  Contracts --> Core",
        "  Contracts --> Domain",
        "  Core --> Application",
        "  Application --> UiServices",
        "  UiShared --> Dashboard",
        "  UiShared --> Wpf",
        "  UiServices --> Dashboard",
        "  UiServices --> Wpf",
        "",
        "%% Registered modules",
    ]
    for module in sorted(modules, key=lambda item: item.get("id", "")):
        lines.append(f"%% {module.get('id')}: {module.get('path')}")
    changed = write_text_if_changed(root / "docs" / "architecture" / "diagrams" / "meridian-source-layer-map.mmd", "\n".join(lines))
    if args.summary:
        print(f"source diagrams rendered: {1 if changed else 0} file(s) changed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
