#!/usr/bin/env python3
"""Generate the evidence continuity dashboard.

Usage:
    python3 generate-evidence-continuity-dashboard.py \
      --output docs/status/evidence-continuity-dashboard.md \
      --json-output docs/status/evidence-continuity-dashboard.json
    python3 generate-evidence-continuity-dashboard.py --summary
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path
from typing import Optional, Sequence

from dashboard_rendering import (
    build_text_signal_dashboard,
    load_canonical_json,
    render_text_signal_dashboard_markdown,
    text_signal_dashboard_summary,
    write_canonical_json,
)


DATA_SOURCES = [
    "docs/status/dk1-pilot-parity-runbook.md",
    "docs/status/kernel-readiness-dashboard.md",
    "docs/status/contract-compatibility-matrix.md",
    "scripts/dev/generate-dk1-pilot-parity-packet.ps1",
    "scripts/generate_contract_review_packet.py",
]

CHECKS = [
    {
        "id": "dk1-run-evidence",
        "category": "DK1 Evidence",
        "label": "DK1 runbook requires fresh date-stamped evidence artifacts",
        "paths": ["docs/status/dk1-pilot-parity-runbook.md"],
        "terms": ["generated run evidence", "run date", "packetReview"],
        "weight": 3,
        "remediation": "Refresh the DK1 runbook so current evidence cannot be confused with stale artifacts.",
    },
    {
        "id": "kernel-evidence-link",
        "category": "DK1 Evidence",
        "label": "Kernel dashboard links the active DK1 packet and sign-off evidence",
        "paths": ["docs/status/kernel-readiness-dashboard.md"],
        "terms": [
            "artifacts/provider-validation/_automation/2026-04-27",
            "dk1-pilot-parity-packet.json",
            "dk1-operator-signoff.json",
        ],
        "weight": 2,
        "remediation": "Update the readiness dashboard when the current DK1 evidence packet changes.",
    },
    {
        "id": "packet-generator",
        "category": "Automation",
        "label": "DK1 packet generator and sign-off preparer are present",
        "paths": [
            "scripts/dev/generate-dk1-pilot-parity-packet.ps1",
            "scripts/dev/prepare-dk1-operator-signoff.ps1",
        ],
        "weight": 2,
        "remediation": "Restore the packet-generation scripts or remove stale workflow claims.",
    },
    {
        "id": "packet-tests",
        "category": "Automation",
        "label": "DK1 evidence packet tests guard packet identity and sign-off validation",
        "paths": [
            "tests/scripts/test_generate_dk1_pilot_parity_packet.py",
            "tests/scripts/test_prepare_dk1_operator_signoff.py",
        ],
        "terms": ["packet", "signoff"],
        "weight": 2,
        "remediation": "Add focused tests for packet identity, packet binding, and sign-off validation.",
    },
    {
        "id": "contract-packet",
        "category": "Shared Contracts",
        "label": "Shared-contract review packet has a repeatable generator and owner-decision trail",
        "paths": [
            "docs/status/contract-compatibility-matrix.md",
            "scripts/generate_contract_review_packet.py",
        ],
        "terms": ["contract-review-packet", "readyForCadenceReview", "Owner decision"],
        "weight": 3,
        "remediation": "Keep contract packets and owner decisions attached to shared-interop cadence reviews.",
    },
    {
        "id": "contract-packet-tests",
        "category": "Shared Contracts",
        "label": "Contract packet and compatibility gate scripts have regression tests",
        "paths": [
            "tests/scripts/test_generate_contract_review_packet.py",
            "tests/scripts/test_check_contract_compatibility_gate.py",
        ],
        "weight": 2,
        "remediation": "Restore Python tests for the contract packet and compatibility gate scripts.",
    },
]


def build_dashboard(root: Path) -> dict:
    return build_text_signal_dashboard(
        root=root,
        dashboard="evidence-continuity",
        title="Evidence Continuity Dashboard",
        description=(
            "Tracks whether DK1 and shared-contract evidence remain packet-bound, "
            "date-scoped, test-covered, and connected to current readiness dashboards."
        ),
        checks=CHECKS,
    )


def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Generate the evidence continuity dashboard.")
    parser.add_argument("--output", "-o", type=Path, help="Markdown output path.")
    parser.add_argument("--json-output", "-j", type=Path, help="JSON output path.")
    parser.add_argument("--root", "-r", type=Path, default=Path("."), help="Repository root.")
    parser.add_argument("--summary", "-s", action="store_true", help="Print a CLI summary.")
    return parser


def main(argv: Optional[Sequence[str]] = None) -> int:
    args = _build_parser().parse_args(argv)
    root = args.root.resolve()
    if not root.is_dir():
        print(f"Error: root directory does not exist: {root}", file=sys.stderr)
        return 1
    if args.output and not args.json_output:
        print(
            "Error: --output requires --json-output so markdown is rendered from canonical JSON.",
            file=sys.stderr,
        )
        return 1

    payload = build_dashboard(root)

    if args.json_output:
        write_canonical_json(payload, args.json_output)
        print(f"JSON dashboard written to {args.json_output}")

    if args.output:
        canonical = load_canonical_json(args.json_output)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(
            render_text_signal_dashboard_markdown(canonical, data_sources=DATA_SOURCES),
            encoding="utf-8",
        )
        print(f"Markdown dashboard written to {args.output}")

    if args.summary or (not args.output and not args.json_output):
        print(text_signal_dashboard_summary(payload))

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
