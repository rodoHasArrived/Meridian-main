#!/usr/bin/env python3
"""Generate the DK1 pilot readiness dashboard.

Usage:
    python3 generate-pilot-readiness-dashboard.py \
      --output docs/status/pilot-readiness-dashboard.md \
      --json-output docs/status/pilot-readiness-dashboard.json
    python3 generate-pilot-readiness-dashboard.py --summary
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
    "docs/status/provider-validation-matrix.md",
    "docs/status/dk1-pilot-parity-runbook.md",
    "docs/status/kernel-readiness-dashboard.md",
    "scripts/dev/*dk1*",
    "tests/scripts/test_*dk1*",
]

CHECKS = [
    {
        "id": "provider-matrix",
        "category": "Provider Evidence",
        "label": "Pilot provider matrix covers Alpaca, Robinhood, Yahoo, and Wave 1 status",
        "paths": ["docs/status/provider-validation-matrix.md"],
        "terms": ["Alpaca", "Robinhood", "Yahoo", "Wave 1"],
        "weight": 2,
        "remediation": "Refresh the provider validation matrix before claiming DK1 pilot readiness.",
    },
    {
        "id": "parity-runbook",
        "category": "Provider Evidence",
        "label": "DK1 parity runbook names generated packet and run-date artifact requirements",
        "paths": ["docs/status/dk1-pilot-parity-runbook.md"],
        "terms": [
            "dk1-pilot-parity-packet",
            "operator sign-off",
            "artifacts/provider-validation/_automation",
        ],
        "weight": 2,
        "remediation": "Document the packet and sign-off binding workflow in the DK1 runbook.",
    },
    {
        "id": "kernel-signoff",
        "category": "Operator Sign-off",
        "label": "Kernel dashboard records signed packet-bound DK1 operator sign-off",
        "paths": ["docs/status/kernel-readiness-dashboard.md"],
        "terms": [
            "operatorSignoff.status=signed",
            "operatorSignoff.validForDk1Exit=true",
            "ready-for-operator-review",
        ],
        "weight": 3,
        "remediation": "Update the kernel dashboard with the current signed, packet-bound DK1 evidence.",
    },
    {
        "id": "automation-scripts",
        "category": "Automation",
        "label": "Provider validation, packet generation, and sign-off scripts are present",
        "paths": [
            "scripts/dev/run-wave1-provider-validation.ps1",
            "scripts/dev/generate-dk1-pilot-parity-packet.ps1",
            "scripts/dev/prepare-dk1-operator-signoff.ps1",
        ],
        "weight": 2,
        "remediation": "Restore the DK1 provider-validation automation scripts or update the runbook.",
    },
    {
        "id": "packet-tests",
        "category": "Automation",
        "label": "DK1 packet and sign-off scripts have focused regression tests",
        "paths": [
            "tests/scripts/test_generate_dk1_pilot_parity_packet.py",
            "tests/scripts/test_prepare_dk1_operator_signoff.py",
        ],
        "weight": 2,
        "remediation": "Add focused Python tests for the DK1 packet and sign-off generators.",
    },
    {
        "id": "readiness-handoff",
        "category": "Trading Readiness",
        "label": "Pilot posture is consumed by the shared trading readiness lane",
        "paths": [
            "docs/plans/paper-trading-cockpit-reliability-sprint.md",
            "src/Meridian.Ui.Shared/Services/Dk1TrustGateReadinessService.cs",
        ],
        "terms": [
            "/api/workstation/trading/readiness",
            "ProviderTrustGate",
            "operator sign-off",
        ],
        "weight": 2,
        "remediation": "Keep the DK1 trust-gate handoff wired into the shared trading readiness contract.",
    },
]


def build_dashboard(root: Path) -> dict:
    return build_text_signal_dashboard(
        root=root,
        dashboard="pilot-readiness",
        title="Pilot Readiness Dashboard",
        description=(
            "Tracks whether DK1 pilot evidence, packet-bound operator sign-off, "
            "and the trading readiness handoff remain present and synchronized."
        ),
        checks=CHECKS,
    )


def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Generate the DK1 pilot readiness dashboard.")
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
