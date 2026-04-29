#!/usr/bin/env python3
"""Generate the governance readiness dashboard.

Usage:
    python3 generate-governance-readiness-dashboard.py \
      --output docs/status/governance-readiness-dashboard.md \
      --json-output docs/status/governance-readiness-dashboard.json
    python3 generate-governance-readiness-dashboard.py --summary
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
    "docs/status/kernel-readiness-dashboard.md",
    "docs/status/contract-compatibility-matrix.md",
    "docs/status/FEATURE_INVENTORY.md",
    "src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs",
    "tests/Meridian.Tests/Ui/WorkstationEndpointsTests.cs",
]

CHECKS = [
    {
        "id": "kernel-governance",
        "category": "Readiness Board",
        "label": "Kernel dashboard tracks reconciliation and governance DK2 readiness",
        "paths": ["docs/status/kernel-readiness-dashboard.md"],
        "terms": ["Reconciliation + governance", "Governance/Fund Ops owner", "Operator Sign-off"],
        "weight": 3,
        "remediation": "Refresh the kernel dashboard governance row before claiming DK2 readiness.",
    },
    {
        "id": "contract-governance",
        "category": "Shared Contracts",
        "label": "Contract compatibility matrix requires review packets and owner decisions",
        "paths": ["docs/status/contract-compatibility-matrix.md"],
        "terms": ["Contract review packet", "Owner decision", "migration notes"],
        "weight": 3,
        "remediation": "Record contract-review packet evidence and owner decisions in the matrix.",
    },
    {
        "id": "reconciliation-contract",
        "category": "Governance Operations",
        "label": "Feature inventory describes reconciliation calibration and sign-off posture",
        "paths": ["docs/status/FEATURE_INVENTORY.md"],
        "terms": [
            "calibration-summary",
            "tolerance-profile posture",
            "required sign-off role",
        ],
        "weight": 2,
        "remediation": "Update the feature inventory with the current reconciliation governance scope.",
    },
    {
        "id": "governance-endpoints",
        "category": "Governance Operations",
        "label": "Workstation endpoints expose governance break queue and calibration routes",
        "paths": ["src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs"],
        "terms": ["break-queue", "calibration-summary", "sign-off"],
        "weight": 3,
        "remediation": "Keep governance readiness tied to workstation endpoints and documented route evidence.",
    },
    {
        "id": "governance-tests",
        "category": "Validation",
        "label": "Endpoint tests cover governance break queue and calibration readiness",
        "paths": ["tests/Meridian.Tests/Ui/WorkstationEndpointsTests.cs"],
        "terms": ["BreakQueue", "ReconciliationCalibrationSummary", "Signoff"],
        "weight": 2,
        "remediation": "Add focused endpoint tests for governance break queue and calibration readiness.",
    },
    {
        "id": "status-docs",
        "category": "Status",
        "label": "Provider and contract status dashboards remain present for governance reviews",
        "paths": [
            "docs/status/provider-validation-matrix.md",
            "docs/status/contract-compatibility-matrix.md",
            "docs/status/kernel-readiness-dashboard.md",
        ],
        "weight": 2,
        "remediation": "Restore the status docs used for governance readiness reviews.",
    },
]


def build_dashboard(root: Path) -> dict:
    return build_text_signal_dashboard(
        root=root,
        dashboard="governance-readiness",
        title="Governance Readiness Dashboard",
        description=(
            "Tracks whether DK2 governance, reconciliation, and shared-contract controls "
            "have current status evidence, route support, and validation coverage."
        ),
        checks=CHECKS,
    )


def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Generate the governance readiness dashboard.")
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
