#!/usr/bin/env python3
"""Fail when milestone IDs have conflicting status labels across status docs."""

from __future__ import annotations

import re
import sys
from pathlib import Path

FILES = [
    Path("docs/status/PROGRAM_STATE.md"),
    Path("docs/status/ROADMAP.md"),
    Path("docs/status/IMPROVEMENTS.md"),
    Path("docs/status/production-status.md"),
    Path("docs/status/FULL_IMPLEMENTATION_TODO_2026_03_20.md"),
    Path("docs/status/ROADMAP_COMBINED.md"),
]

BLOCK_RE = re.compile(
    r"<!--\s*program-state:begin\s*-->(?P<body>.*?)<!--\s*program-state:end\s*-->",
    re.DOTALL | re.IGNORECASE,
)
ROW_RE = re.compile(
    r"^\|\s*(?P<milestone>W\d+)\s*\|\s*[^|]*\|\s*(?P<status>[^|]+?)\s*\|",
    re.IGNORECASE,
)
VALID_STATUSES = {"done", "in progress", "planned", "blocked"}


def parse_file(path: Path):
    text = path.read_text(encoding="utf-8")
    block = BLOCK_RE.search(text)
    if not block:
        raise ValueError(f"missing program-state block in {path}")

    results = {}
    for raw_line in block.group("body").splitlines():
        line = raw_line.strip()
        if not line.startswith("|"):
            continue
        m = ROW_RE.match(line)
        if not m:
            continue
        milestone = m.group("milestone").upper()
        status = " ".join(m.group("status").split())
        norm = status.lower()
        if norm not in VALID_STATUSES:
            raise ValueError(
                f"invalid status '{status}' for {milestone} in {path}; "
                f"expected one of {sorted(VALID_STATUSES)}"
            )
        if milestone in results:
            raise ValueError(
                f"duplicate milestone '{milestone}' in program-state block of {path}"
            )
        results[milestone] = status
    if not results:
        raise ValueError(f"no milestone rows parsed from {path}")
    return results


def main() -> int:
    all_statuses: dict[str, list[tuple[Path, str]]] = {}
    errors = []
    for file in FILES:
        try:
            parsed = parse_file(file)
            for milestone, status in parsed.items():
                all_statuses.setdefault(milestone, []).append((file, status))
        except ValueError as e:
            errors.append(str(e))

    for milestone, entries in sorted(all_statuses.items()):
        normalized = {status.lower() for _, status in entries}
        if len(normalized) > 1:
            details = ", ".join(f"{path}:{status}" for path, status in entries)
            errors.append(f"{milestone} has conflicting statuses -> {details}")

    if errors:
        print("Program state consistency check failed:\n", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1

    print("Program state consistency check passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
