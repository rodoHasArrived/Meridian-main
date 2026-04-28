#!/usr/bin/env python3
"""Fail when canonical program-state rows drift or miss ownership metadata."""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
DEFAULT_REPO_ROOT = SCRIPT_DIR.parent
STATUS_DOC_PATHS = [
    "docs/status/PROGRAM_STATE.md",
    "docs/status/ROADMAP.md",
    "docs/status/IMPROVEMENTS.md",
    "docs/status/production-status.md",
    "docs/status/FULL_IMPLEMENTATION_TODO_2026_03_20.md",
    "docs/status/ROADMAP_COMBINED.md",
]

BLOCK_RE = re.compile(
    r"<!--\s*program-state:begin\s*-->(?P<body>.*?)<!--\s*program-state:end\s*-->",
    re.DOTALL | re.IGNORECASE,
)
VALID_STATUSES = {"done", "in progress", "planned", "blocked"}
REQUIRED_COLUMNS = {
    "Wave",
    "Owner",
    "Primary Owner",
    "Backup Owner",
    "Escalation SLA",
    "Dependency Owners",
    "Status",
    "Target Date",
    "Evidence Link",
}
VALID_SLA_RE = re.compile(
    r"(?i)^\s*\d+\s*(?:h|hr|hrs|hour|hours|business day|business days|day|days)(?:\s*/\s*\d+\s*(?:h|hr|hrs|hour|hours|business day|business days|day|days))?\s*$"
)


def _parse_markdown_row(line: str) -> list[str] | None:
    stripped = line.strip()
    if not stripped.startswith("|"):
        return None
    parts = [part.strip() for part in stripped.strip("|").split("|")]
    if not parts:
        return None
    return parts


def parse_file(path: Path):
    text = path.read_text(encoding="utf-8")
    block = BLOCK_RE.search(text)
    if not block:
        raise ValueError(f"missing program-state block in {path}")

    lines = [line.strip() for line in block.group("body").splitlines() if line.strip()]
    if len(lines) < 3:
        raise ValueError(f"program-state block is incomplete in {path}")

    header = _parse_markdown_row(lines[0])
    divider = _parse_markdown_row(lines[1])
    if header is None or divider is None:
        raise ValueError(f"program-state block has invalid table header in {path}")

    if set(header) != REQUIRED_COLUMNS:
        missing = sorted(REQUIRED_COLUMNS - set(header))
        extra = sorted(set(header) - REQUIRED_COLUMNS)
        details = []
        if missing:
            details.append(f"missing columns: {missing}")
        if extra:
            details.append(f"unexpected columns: {extra}")
        raise ValueError(f"program-state table columns invalid in {path}; {'; '.join(details)}")

    column_index = {name: idx for idx, name in enumerate(header)}

    results: dict[str, dict[str, str]] = {}
    for raw_line in lines[2:]:
        row = _parse_markdown_row(raw_line)
        if row is None:
            continue
        if len(row) != len(header):
            raise ValueError(f"invalid row column count in {path}: {raw_line}")

        milestone = row[column_index["Wave"]].upper()
        if not re.fullmatch(r"W\d+", milestone):
            continue

        status = " ".join(row[column_index["Status"]].split())
        norm_status = status.lower()
        if norm_status not in VALID_STATUSES:
            raise ValueError(
                f"invalid status '{status}' for {milestone} in {path}; "
                f"expected one of {sorted(VALID_STATUSES)}"
            )

        primary_owner = row[column_index["Primary Owner"]].strip()
        backup_owner = row[column_index["Backup Owner"]].strip()
        escalation_sla = row[column_index["Escalation SLA"]].strip()
        dependency_owners = row[column_index["Dependency Owners"]].strip()

        if not primary_owner or primary_owner.lower() in {"tbd", "n/a", "none"}:
            raise ValueError(f"missing/invalid primary owner for {milestone} in {path}")
        if not backup_owner or backup_owner.lower() in {"tbd", "n/a", "none"}:
            raise ValueError(f"missing/invalid backup owner for {milestone} in {path}")
        if not VALID_SLA_RE.match(escalation_sla):
            raise ValueError(
                f"invalid escalation SLA '{escalation_sla}' for {milestone} in {path}; "
                "expected values like '4 hours' or '4 hours / 1 business day'"
            )
        if (
            not dependency_owners
            or dependency_owners.lower() in {"tbd", "n/a", "none"}
            or ";" not in dependency_owners
        ):
            raise ValueError(
                f"missing/invalid dependency owners for {milestone} in {path}; "
                "use a ';' separated owner list"
            )

        if milestone in results:
            raise ValueError(
                f"duplicate milestone '{milestone}' in program-state block of {path}"
            )

        results[milestone] = {
            "Owner": row[column_index["Owner"]].strip(),
            "Primary Owner": primary_owner,
            "Backup Owner": backup_owner,
            "Escalation SLA": escalation_sla,
            "Dependency Owners": dependency_owners,
            "Status": status,
            "Target Date": row[column_index["Target Date"]].strip(),
            "Evidence Link": row[column_index["Evidence Link"]].strip(),
        }

    if not results:
        raise ValueError(f"no milestone rows parsed from {path}")
    return results


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Fail when milestone IDs have conflicting state/owner metadata across status docs."
        )
    )
    parser.add_argument(
        "--repo-root",
        type=Path,
        default=DEFAULT_REPO_ROOT,
        help="Repository root directory. Defaults to the parent of scripts/.",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    repo_root = args.repo_root.resolve()
    files = [repo_root / rel_path for rel_path in STATUS_DOC_PATHS]

    all_rows: dict[str, list[tuple[Path, dict[str, str]]]] = {}
    errors = []
    for file in files:
        try:
            parsed = parse_file(file)
            for milestone, row in parsed.items():
                all_rows.setdefault(milestone, []).append((file, row))
        except ValueError as e:
            errors.append(str(e))

    comparable_fields = [
        "Owner",
        "Primary Owner",
        "Backup Owner",
        "Escalation SLA",
        "Dependency Owners",
        "Status",
        "Target Date",
        "Evidence Link",
    ]

    for milestone, entries in sorted(all_rows.items()):
        baseline_path, baseline_row = entries[0]
        for file_path, row in entries[1:]:
            for field in comparable_fields:
                if row[field] != baseline_row[field]:
                    errors.append(
                        f"{milestone} has conflicting {field} -> "
                        f"{baseline_path}:{baseline_row[field]} | {file_path}:{row[field]}"
                    )

    if errors:
        print("Program state consistency check failed:\n", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1

    print("Program state consistency check passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
