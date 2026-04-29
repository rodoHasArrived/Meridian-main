#!/usr/bin/env python3
"""Generate a shared-contract review packet for the DK interop cadence."""

from __future__ import annotations

import argparse
import json
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

SCRIPT_DIR = Path(__file__).resolve().parent
if str(SCRIPT_DIR) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIR))

import check_contract_compatibility_gate as compatibility_gate

PACKET_CONTRACT_NAME = "shared-contract-review-packet"
PACKET_SCHEMA_VERSION = 1


def classify_surface(path: str) -> str:
    if path == "src/Meridian.Contracts/Api/UiApiRoutes.cs":
        return "shared-ui-routes"
    if path == "src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs":
        return "workstation-endpoints"
    if path.startswith("src/Meridian.Contracts/Workstation/"):
        return "workstation-dtos"
    if path.startswith("src/Meridian.Strategies/Services/"):
        return "strategy-services"
    if path.startswith("src/Meridian.Ledger/"):
        return "ledger-contracts"
    return "unknown"


def build_review_packet(
    *,
    base_ref: str,
    head_ref: str,
    changed_files: list[str],
    patch: str,
    matrix_doc_path: str,
    matrix_updated: bool,
    matrix_migration_note_added: bool,
    pr_migration_notes_present: bool | None,
    generated_at_utc: str | None = None,
) -> dict[str, Any]:
    tracked_surfaces = [
        {"path": path, "scope": classify_surface(path)}
        for path in changed_files
        if compatibility_gate.is_tracked(path)
    ]
    is_breaking = compatibility_gate.patch_has_breaking_removal(patch) if tracked_surfaces else False
    requires_migration_notes = is_breaking
    pr_body_checked = pr_migration_notes_present is not None

    findings: list[dict[str, str]] = []
    if not tracked_surfaces:
        change_category = "none"
        findings.append(
            {
                "severity": "info",
                "message": "No tracked shared contract surfaces changed in this diff.",
            }
        )
    elif is_breaking:
        change_category = "potential-breaking"
        if not matrix_updated:
            findings.append(
                {
                    "severity": "blocking",
                    "message": f"Update {matrix_doc_path} for the potential breaking contract change.",
                }
            )
        if not matrix_migration_note_added:
            findings.append(
                {
                    "severity": "blocking",
                    "message": f"Add a dated migration note under Migration Notes in {matrix_doc_path}.",
                }
            )
        if pr_body_checked and not pr_migration_notes_present:
            findings.append(
                {
                    "severity": "blocking",
                    "message": "Add migration notes to the PR body and check the contract migration-notes item.",
                }
            )
    else:
        change_category = "additive-or-compatible"
        findings.append(
            {
                "severity": "review",
                "message": "Tracked contract surfaces changed without a breaking-removal heuristic match; reviewer should confirm the change is additive.",
            }
        )

    if requires_migration_notes and not pr_body_checked:
        findings.append(
            {
                "severity": "blocking",
                "message": "PR body was not supplied; provide it so migration notes can be verified.",
            }
        )

    ready_for_cadence_review = not any(finding["severity"] == "blocking" for finding in findings)

    generated_at_utc = generated_at_utc or datetime.now(timezone.utc).replace(microsecond=0).isoformat()

    return {
        "contractName": PACKET_CONTRACT_NAME,
        "schemaVersion": PACKET_SCHEMA_VERSION,
        "generatedAtUtc": generated_at_utc,
        "diff": {
            "baseRef": base_ref,
            "headRef": head_ref,
        },
        "summary": {
            "trackedSurfaceCount": len(tracked_surfaces),
            "contractChangeCategory": change_category,
            "requiresMigrationNotes": requires_migration_notes,
            "matrixUpdated": matrix_updated,
            "matrixMigrationNoteAdded": matrix_migration_note_added,
            "prMigrationNotesChecked": pr_body_checked,
            "prMigrationNotesPresent": pr_migration_notes_present,
            "readyForCadenceReview": ready_for_cadence_review,
        },
        "trackedSurfaces": tracked_surfaces,
        "findings": findings,
        "reviewChecklist": [
            "Confirm changed DTO, route, service, or ledger surfaces remain additive or have a documented migration path.",
            "Run the contract compatibility gate for the same base/head range.",
            "Run focused serialization, endpoint, strategy-service, or ledger snapshot tests for touched surfaces.",
            "Record the packet path and owner decision in the weekly interop review.",
        ],
    }


def render_markdown(packet: dict[str, Any]) -> str:
    summary = packet["summary"]
    lines = [
        "# Shared Contract Review Packet",
        "",
        f"- Generated: {packet['generatedAtUtc']}",
        f"- Diff: {packet['diff']['baseRef']}...{packet['diff']['headRef']}",
        f"- Change category: {summary['contractChangeCategory']}",
        f"- Tracked surfaces: {summary['trackedSurfaceCount']}",
        f"- Requires migration notes: {str(summary['requiresMigrationNotes']).lower()}",
        f"- Ready for cadence review: {str(summary['readyForCadenceReview']).lower()}",
        "",
        "## Tracked Surfaces",
        "",
    ]

    if packet["trackedSurfaces"]:
        lines.extend(
            f"- `{surface['path']}` ({surface['scope']})"
            for surface in packet["trackedSurfaces"]
        )
    else:
        lines.append("- None")

    lines.extend(["", "## Findings", ""])
    lines.extend(
        f"- {finding['severity']}: {finding['message']}"
        for finding in packet["findings"]
    )

    lines.extend(["", "## Review Checklist", ""])
    lines.extend(f"- [ ] {item}" for item in packet["reviewChecklist"])
    lines.append("")
    return "\n".join(lines)


def write_text(path: str | None, content: str) -> None:
    if not path:
        return
    output_path = Path(path)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(content, encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description="Generate a DK shared-contract review packet.")
    parser.add_argument("--base", required=True, help="Base git ref for comparison.")
    parser.add_argument("--head", required=True, help="Head git ref for comparison.")
    parser.add_argument(
        "--matrix-doc",
        default="docs/status/contract-compatibility-matrix.md",
        help="Compatibility matrix documentation path.",
    )
    parser.add_argument("--pr-body-file", default=None, help="Optional pull request body text file.")
    parser.add_argument("--output", default=None, help="Optional JSON packet output path.")
    parser.add_argument("--markdown-output", default=None, help="Optional Markdown packet output path.")
    args = parser.parse_args()

    diff_range = f"{args.base}...{args.head}"
    changed_files_raw = compatibility_gate.run_git(["diff", "--name-only", diff_range])
    changed_files = [line.strip() for line in changed_files_raw.splitlines() if line.strip()]
    tracked_changed_files = [path for path in changed_files if compatibility_gate.is_tracked(path)]

    patch = ""
    if tracked_changed_files:
        patch = compatibility_gate.run_git(["diff", "--unified=0", diff_range, "--", *tracked_changed_files])

    pr_body = compatibility_gate.load_pr_body(args.pr_body_file)
    pr_notes_present = (
        compatibility_gate.migration_note_in_pr_body(pr_body)
        if args.pr_body_file
        else None
    )

    packet = build_review_packet(
        base_ref=args.base,
        head_ref=args.head,
        changed_files=changed_files,
        patch=patch,
        matrix_doc_path=args.matrix_doc,
        matrix_updated=args.matrix_doc in changed_files,
        matrix_migration_note_added=compatibility_gate.migration_note_in_matrix(diff_range, args.matrix_doc),
        pr_migration_notes_present=pr_notes_present,
    )

    json_content = json.dumps(packet, indent=2) + "\n"
    if args.output:
        write_text(args.output, json_content)
    else:
        print(json_content, end="")

    if args.markdown_output:
        write_text(args.markdown_output, render_markdown(packet))

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
