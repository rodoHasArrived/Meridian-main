#!/usr/bin/env python3
"""Generate machine-readable and markdown summaries from docs/status/PROGRAM_STATE.md."""

from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
DEFAULT_REPO_ROOT = SCRIPT_DIR.parent
PROGRAM_STATE_DOC = "docs/status/PROGRAM_STATE.md"
DEFAULT_JSON_OUT = "docs/status/program-state-summary.json"
DEFAULT_MD_OUT = "docs/status/program-state-summary.md"

BLOCK_RE = re.compile(
    r"<!--\s*program-state:begin\s*-->(?P<body>.*?)<!--\s*program-state:end\s*-->",
    re.DOTALL | re.IGNORECASE,
)


REQUIRED_HEADERS = [
    "Wave",
    "Owner",
    "Primary Owner",
    "Backup Owner",
    "Escalation SLA",
    "Dependency Owners",
    "Status",
    "Target Date",
    "Evidence Link",
]


def _parse_markdown_row(line: str) -> list[str] | None:
    stripped = line.strip()
    if not stripped.startswith("|"):
        return None
    return [part.strip() for part in stripped.strip("|").split("|")]


def parse_program_state_table(path: Path) -> list[dict[str, str]]:
    text = path.read_text(encoding="utf-8")
    match = BLOCK_RE.search(text)
    if not match:
        raise ValueError(f"missing program-state block in {path}")

    lines = [line.strip() for line in match.group("body").splitlines() if line.strip()]
    if len(lines) < 3:
        raise ValueError(f"program-state block is incomplete in {path}")

    header = _parse_markdown_row(lines[0])
    if header != REQUIRED_HEADERS:
        raise ValueError(
            f"unexpected headers in {path}; expected {REQUIRED_HEADERS}, found {header}"
        )

    rows: list[dict[str, str]] = []
    for raw_line in lines[2:]:
        values = _parse_markdown_row(raw_line)
        if values is None or len(values) != len(header):
            continue

        row = dict(zip(header, values))
        if not re.fullmatch(r"W\d+", row["Wave"], flags=re.IGNORECASE):
            continue

        rows.append(row)

    if not rows:
        raise ValueError(f"no program-state rows parsed from {path}")

    return rows


def render_markdown(rows: list[dict[str, str]]) -> str:
    lines = [
        "# Program State Summary (Generated)",
        "",
        "This file is generated from `docs/status/PROGRAM_STATE.md`.",
        "",
        "",
        "| Wave | Status | Primary Owner | Backup Owner | Escalation SLA | Dependency Owners |",
        "| --- | --- | --- | --- | --- | --- |",
    ]

    for row in rows:
        lines.append(
            "| {Wave} | {Status} | {Primary Owner} | {Backup Owner} | {Escalation SLA} | {Dependency Owners} |".format(
                **row
            )
        )

    lines.extend(
        [
            "",
            "## Escalation Routing",
            "",
            "Blocked or at-risk workflows should escalate to the wave `Primary Owner` first,",
            "then to the `Backup Owner` according to the published `Escalation SLA`.",
        ]
    )
    return "\n".join(lines) + "\n"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=DEFAULT_REPO_ROOT)
    parser.add_argument("--json-out", default=DEFAULT_JSON_OUT)
    parser.add_argument("--markdown-out", default=DEFAULT_MD_OUT)
    parser.add_argument(
        "--check",
        action="store_true",
        help="Fail if outputs are out of date instead of writing them.",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    repo_root = args.repo_root.resolve()

    rows = parse_program_state_table(repo_root / PROGRAM_STATE_DOC)
    json_payload = {
        "source": PROGRAM_STATE_DOC,
        "waves": rows,
    }
    markdown_payload = render_markdown(rows)

    json_out = repo_root / args.json_out
    md_out = repo_root / args.markdown_out

    expected_json = json.dumps(json_payload, indent=2) + "\n"

    if args.check:
        issues = []
        if not json_out.exists() or json_out.read_text(encoding="utf-8") != expected_json:
            issues.append(str(json_out))
        if not md_out.exists() or md_out.read_text(encoding="utf-8") != markdown_payload:
            issues.append(str(md_out))
        if issues:
            print("Program-state summary is out of date:")
            for issue in issues:
                print(f"- {issue}")
            return 1
        print("Program-state summary is up to date.")
        return 0

    json_out.write_text(expected_json, encoding="utf-8")
    md_out.write_text(markdown_payload, encoding="utf-8")
    print(f"Wrote {json_out}")
    print(f"Wrote {md_out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
