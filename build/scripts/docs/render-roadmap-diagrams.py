#!/usr/bin/env python3
"""Render deterministic roadmap Mermaid diagrams."""

from __future__ import annotations

import re

from common import build_arg_parser, load_data, repo_root, write_text_if_changed


# Roadmap identifiers are `W<wave><suffix>-<AREA>-<NNN>`, e.g. W1-DATA-001, W5X-CONNECT-001,
# W5X-STMT-ONBOARD-001 (two-token area), W9-ASSET-010, W10-MARK-001.
_WAVE_PATTERN = re.compile(r"^W(\d+)([A-Z]*)$")

# Sorts unparsable tokens deterministically last instead of letting them collide with wave 1.
_UNPARSABLE = 1_000_000


def diagram_sort_key(identifier: str) -> tuple[int, str, int, str, str]:
    """Order roadmap identifiers by wave, then by sequence within the wave.

    Sorting on the raw identifier string is lexicographic, which places `W10-` between `W1-` and
    `W2-` and orders items within a wave alphabetically by area. Both are wrong: the first renders
    a planned wave in the middle of delivered work, and the second discards the rank that the
    numeric suffix encodes (see `DEC-PRIORITY-SLATE-001`, which states rank is carried in each W9
    item's numeric suffix).

    Waves sort numerically with their alphabetic suffix as a tiebreak, so `W5` precedes `W5X` and
    `W9` precedes `W10`. Within a wave, items sort by their trailing sequence number first so a
    rank-encoding wave renders in rank order, then by area for stability.
    """
    parts = identifier.split("-")
    wave_token = parts[0] if parts else ""
    wave_match = _WAVE_PATTERN.match(wave_token)
    wave_number = int(wave_match.group(1)) if wave_match else _UNPARSABLE
    wave_suffix = wave_match.group(2) if wave_match else wave_token

    try:
        sequence = int(parts[-1])
    except (IndexError, ValueError):
        sequence = _UNPARSABLE

    area = "-".join(parts[1:-1])
    return (wave_number, wave_suffix, sequence, area, identifier)


def main() -> int:
    parser = build_arg_parser("Render roadmap Mermaid diagram.")
    args = parser.parse_args()
    root = repo_root(args.root)
    items = load_data(root / "docs" / "roadmap" / "data" / "roadmap-items.yml").get("items", [])
    lines = ["flowchart LR"]
    previous = None
    for item in sorted(items, key=lambda entry: diagram_sort_key(entry.get("id", "") or "")):
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
