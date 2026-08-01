#!/usr/bin/env python3
"""Generate machine-readable and Markdown summaries from roadmap registry data."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any

SCRIPT_DIR = Path(__file__).resolve().parent
DEFAULT_REPO_ROOT = SCRIPT_DIR.parent
DOCS_COMMON_DIR = DEFAULT_REPO_ROOT / "build" / "scripts" / "docs"
if str(DOCS_COMMON_DIR) not in sys.path:
    sys.path.insert(0, str(DOCS_COMMON_DIR))

from common import load_data, markdown_table, roadmap_item_sort_key  # noqa: E402

PROGRAM_STATE_DATA = "docs/roadmap/data/program-state.yml"
ROADMAP_ITEMS_DATA = "docs/roadmap/data/roadmap-items.yml"
DEFAULT_JSON_OUT = "docs/status/program-state-summary.json"
DEFAULT_MD_OUT = "docs/status/program-state-summary.md"
# v3 orders rows by wave then rank via the shared roadmap sort key. Changing the ordering of a
# generated output is a semantic change, which docs/roadmap/schema-versioning.md classifies as
# major; this generator carries no separate renderer version, so the summary schema version is
# the one consumers have to distinguish v2 lexicographic ordering from v3 wave/rank ordering.
SUMMARY_SCHEMA_VERSION = "program-state-summary/v3"


def _as_list(value: Any) -> list[Any]:
    if isinstance(value, list):
        return value
    if value in (None, ""):
        return []
    return [value]


def _join(values: Any) -> str:
    return "; ".join(str(value) for value in _as_list(values) if str(value).strip())


def _evidence_links(item: dict[str, Any]) -> str:
    links: list[str] = []
    for evidence in _as_list(item.get("evidence")):
        if isinstance(evidence, dict) and evidence.get("path"):
            links.append(str(evidence["path"]))
        elif evidence:
            links.append(str(evidence))
    return "; ".join(links)


def load_program_state(root: Path) -> tuple[dict[str, Any], list[dict[str, str]]]:
    program_data = load_data(root / PROGRAM_STATE_DATA)
    roadmap_data = load_data(root / ROADMAP_ITEMS_DATA)

    if not isinstance(program_data, dict):
        raise ValueError(f"{PROGRAM_STATE_DATA} must contain a mapping")
    if not isinstance(roadmap_data, dict):
        raise ValueError(f"{ROADMAP_ITEMS_DATA} must contain a mapping")

    items = roadmap_data.get("items")
    if not isinstance(items, list) or not items:
        raise ValueError(f"{ROADMAP_ITEMS_DATA} must contain roadmap items")

    rows: list[dict[str, str]] = []
    # Shares `roadmap_item_sort_key` with the roadmap renderers so this compatibility view cannot
    # disagree with ROADMAP_SUMMARY.md about delivery order. Sorting on the raw identifier placed
    # W10 between W1 and W2 and ignored the `sequence` rank those rows now declare, so the two
    # generated status documents contradicted each other. Both the Markdown and JSON outputs are
    # built from these rows, so ordering them here fixes both.
    for item in sorted(items, key=roadmap_item_sort_key):
        if not isinstance(item, dict):
            continue
        rows.append(
            {
                "ID": str(item.get("id", "")),
                "Wave": str(item.get("wave", "")),
                "Title": str(item.get("title", "")),
                "Workspaces": _join(item.get("workspace")),
                "Status": str(item.get("status", "")),
                "Health": str(item.get("health", "")),
                "Priority": str(item.get("priority", "")),
                "Owner Lane": str(item.get("owner_lane", "")),
                "Evidence Posture": str(item.get("evidence_posture", "")),
                "Evidence": _evidence_links(item),
                "Last Reviewed": str(item.get("last_reviewed", "")),
            }
        )

    if not rows:
        raise ValueError(f"no roadmap rows parsed from {ROADMAP_ITEMS_DATA}")

    program = program_data.get("program", {})
    if not isinstance(program, dict):
        raise ValueError(f"{PROGRAM_STATE_DATA} must contain a program mapping")

    return program, rows


def render_markdown(program: dict[str, Any], rows: list[dict[str, str]]) -> str:
    table_rows = [
        [
            row["ID"],
            row["Wave"],
            row["Title"],
            row["Workspaces"],
            row["Status"],
            row["Health"],
            row["Priority"],
            row["Owner Lane"],
            row["Evidence Posture"],
            row["Last Reviewed"],
        ]
        for row in rows
    ]
    return (
        "# Program State Summary (Generated)\n\n"
        "This file is generated from `docs/roadmap/data/program-state.yml` and "
        "`docs/roadmap/data/roadmap-items.yml`.\n\n"
        f"Snapshot date: {program.get('snapshot_date', '-')}\n\n"
        + markdown_table(
            [
                "ID",
                "Wave",
                "Title",
                "Workspaces",
                "Status",
                "Health",
                "Priority",
                "Owner Lane",
                "Evidence Posture",
                "Last Reviewed",
            ],
            table_rows,
        )
        + "\n\n## Source Contract\n\n"
        "Durable roadmap truth belongs in `docs/roadmap/data/*.yml`; this status summary is a generated compatibility view.\n"
    )


def render_json(program: dict[str, Any], rows: list[dict[str, str]]) -> str:
    payload = {
        "schemaVersion": SUMMARY_SCHEMA_VERSION,
        "programSource": PROGRAM_STATE_DATA,
        "roadmapSource": ROADMAP_ITEMS_DATA,
        "snapshotDate": str(program.get("snapshot_date", "")),
        "program": {
            "id": str(program.get("id", "")),
            "title": str(program.get("title", "")),
            "owner": str(program.get("owner", "")),
            "status": str(program.get("status", "")),
            "currentFocus": [str(item) for item in _as_list(program.get("current_focus"))],
            "activeUiLane": str(program.get("active_ui_lane", "")),
            "retainedSupportLane": str(program.get("retained_support_lane", "")),
            "mobileLane": str(program.get("mobile_lane", "")),
        },
        "items": rows,
    }
    return json.dumps(payload, indent=2) + "\n"


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

    program, rows = load_program_state(repo_root)
    expected_json = render_json(program, rows)
    expected_markdown = render_markdown(program, rows)

    json_out = repo_root / args.json_out
    md_out = repo_root / args.markdown_out

    if args.check:
        issues = []
        if not json_out.exists() or json_out.read_text(encoding="utf-8") != expected_json:
            issues.append(str(json_out))
        if not md_out.exists() or md_out.read_text(encoding="utf-8") != expected_markdown:
            issues.append(str(md_out))
        if issues:
            print("Program-state summary is out of date:")
            for issue in issues:
                print(f"- {issue}")
            return 1
        print("Program-state summary is up to date.")
        return 0

    json_out.write_text(expected_json, encoding="utf-8")
    md_out.write_text(expected_markdown, encoding="utf-8")
    print(f"Wrote {json_out}")
    print(f"Wrote {md_out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
