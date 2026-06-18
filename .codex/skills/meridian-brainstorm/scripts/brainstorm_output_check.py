#!/usr/bin/env python3
"""Validate Meridian brainstorm output shape for receipt-first triage."""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

SAMPLES = {
    "receipt-table": """**Skill Selection**
- Skill: `meridian-brainstorm`
- Mode: `quick wins`
- Reason: operator trust ideation
- Required Opening: compact triage table first

## Ideas at a Glance
| # | Idea | Effort | Audience | Impact | Depends On |
| --- | --- | --- | --- | --- | --- |
| 1 | Exception close cockpit | M | Financial operations | High | Shared explorers |

## Idea 1
Tie retained evidence, reconciliation status, and close blockers into one operator queue.

## Synthesis
Start with the shared explorer-backed queue before larger workflow automation.
""",
}

RECEIPT_RE = re.compile(
    r"^\*\*Skill Selection\*\*\s*\n"
    r"- Skill:\s*`?meridian-brainstorm`?\s*\n"
    r"- Mode:\s*`?([^`\n]+)`?\s*\n"
    r"- Reason:\s*([^\n]+)\n"
    r"- Required Opening:\s*([^\n]+)",
    re.MULTILINE,
)
TABLE_HEADER = "| # | Idea | Effort | Audience | Impact | Depends On |"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--file", type=Path, help="Markdown response to inspect.")
    source.add_argument("--text", help="Markdown response text to inspect.")
    source.add_argument("--sample", choices=sorted(SAMPLES), help="Built-in deterministic sample.")
    parser.add_argument("--summary", action="store_true", help="Print a compact human summary.")
    parser.add_argument("--json", action="store_true", help="Emit machine-readable JSON.")
    return parser.parse_args()


def load_text(args: argparse.Namespace) -> str:
    if args.file is not None:
        return args.file.read_text(encoding="utf-8")
    if args.text is not None:
        return args.text
    return SAMPLES[args.sample]


def validate(text: str) -> dict[str, object]:
    failures: list[str] = []
    receipt_matches = list(RECEIPT_RE.finditer(text))
    table_index = text.find(TABLE_HEADER)
    ideas_heading_index = text.find("## Ideas at a Glance")

    if not receipt_matches:
        failures.append("missing meridian-brainstorm Skill Selection receipt")
    if ideas_heading_index < 0:
        failures.append("missing Ideas at a Glance heading")
    if table_index < 0:
        failures.append("missing compact idea triage table header")

    mode = ""
    if receipt_matches:
        first = receipt_matches[0]
        mode = first.group(1).strip()
        required_shape = first.group(3).strip().lower()
        if first.start() > 0 and text[: first.start()].strip():
            failures.append("receipt is not the first substantive output")
        if not mode or "<" in mode or mode.lower() in {"n/a", "unknown"}:
            failures.append("receipt mode is not concrete")
        if "triage table" not in required_shape and "summary table" not in required_shape:
            failures.append("receipt does not name the compact table opening shape")
        if table_index >= 0 and first.end() > table_index:
            failures.append("compact table appears before receipt")

    status = "pass" if not failures else "fail"
    return {
        "status": status,
        "failures": failures,
        "summary": {
            "skill": "meridian-brainstorm",
            "mode": mode,
            "receipt_count": len(receipt_matches),
            "table_count": 1 if table_index >= 0 else 0,
            "ideas_heading_count": 1 if ideas_heading_index >= 0 else 0,
        },
    }


def main() -> int:
    args = parse_args()
    payload = validate(load_text(args))

    if args.json:
        print(json.dumps(payload, indent=2))
    else:
        print(
            "brainstorm_output_check "
            f"status={payload['status']} "
            f"receipts={payload['summary']['receipt_count']} "
            f"tables={payload['summary']['table_count']}"
        )
        if args.summary:
            for failure in payload["failures"]:
                print(f"- {failure}")

    return 0 if payload["status"] == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main())
