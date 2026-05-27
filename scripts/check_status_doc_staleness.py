#!/usr/bin/env python3
"""Fail if any status doc marked <!-- auto-sync:tests --> has a stale Last Updated date.

A doc is considered stale when its "Last Updated:" date is older than MAX_AGE_DAYS.
This is wired into CI after the test run so that docs which claim to reflect test
evidence are forced to stay current.
"""

from __future__ import annotations

import argparse
import re
import sys
from datetime import date, datetime, timezone
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]

AUTO_SYNC_MARKER = "<!-- auto-sync:tests -->"
LAST_UPDATED_RE = re.compile(r"\*\*Last Updated:\*\*\s*(\d{4}-\d{2}-\d{2})")

DEFAULT_MAX_AGE_DAYS = 14

# Status docs that carry the auto-sync marker are checked. Any doc without the
# marker is silently skipped so this script stays opt-in.
STATUS_DOCS = [
    "docs/status/production-status.md",
    "docs/status/PROGRAM_STATE.md",
]


def check_doc(path: Path, max_age_days: int, today: date) -> list[str]:
    errors: list[str] = []
    text = path.read_text(encoding="utf-8")

    if AUTO_SYNC_MARKER not in text:
        return []  # not opted-in

    match = LAST_UPDATED_RE.search(text)
    if not match:
        errors.append(
            f"{path}: carries {AUTO_SYNC_MARKER!r} but has no 'Last Updated:' date."
        )
        return errors

    try:
        doc_date = datetime.strptime(match.group(1), "%Y-%m-%d").date()
    except ValueError:
        errors.append(f"{path}: could not parse date '{match.group(1)}'.")
        return errors

    age = (today - doc_date).days
    if age > max_age_days:
        errors.append(
            f"{path}: Last Updated date {doc_date} is {age} days old"
            f" (limit: {max_age_days}). Update the date after tests pass."
        )

    return errors


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--max-age-days",
        type=int,
        default=DEFAULT_MAX_AGE_DAYS,
        help=f"Maximum allowed age in days for a synced status doc (default: {DEFAULT_MAX_AGE_DAYS}).",
    )
    parser.add_argument(
        "--docs",
        nargs="*",
        default=STATUS_DOCS,
        help="Status doc paths to check (relative to repo root).",
    )
    args = parser.parse_args()

    today = datetime.now(tz=timezone.utc).date()
    errors: list[str] = []

    for rel in args.docs:
        path = REPO_ROOT / rel
        if not path.exists():
            # Missing docs are checked by other validators; skip silently here.
            continue
        errors.extend(check_doc(path, args.max_age_days, today))

    if errors:
        print("Status doc staleness check failed:", file=sys.stderr)
        for err in errors:
            print(f"  - {err}", file=sys.stderr)
        return 1

    print("Status doc staleness check passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
