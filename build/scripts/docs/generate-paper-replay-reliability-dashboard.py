#!/usr/bin/env python3
"""Generate the paper replay reliability dashboard.

Usage:
    python3 generate-paper-replay-reliability-dashboard.py \
      --output docs/status/paper-replay-reliability-dashboard.md \
      --json-output docs/status/paper-replay-reliability-dashboard.json
    python3 generate-paper-replay-reliability-dashboard.py --summary
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
    "docs/plans/paper-trading-cockpit-reliability-sprint.md",
    "docs/status/FEATURE_INVENTORY.md",
    "src/Meridian.Execution/Services/PaperSessionPersistenceService.cs",
    "src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs",
    "tests/Meridian.Tests/Ui/WorkstationEndpointsTests.cs",
]

CHECKS = [
    {
        "id": "replay-plan",
        "category": "Acceptance Contract",
        "label": "Reliability sprint records replay evidence and stale-readiness semantics",
        "paths": ["docs/plans/paper-trading-cockpit-reliability-sprint.md"],
        "terms": ["replay verification", "paper-replay-stale", "fill, order, or ledger-entry counts"],
        "weight": 3,
        "remediation": "Refresh the reliability sprint plan with current replay verification semantics.",
    },
    {
        "id": "readiness-contract",
        "category": "Acceptance Contract",
        "label": "Shared trading readiness endpoint remains the replay acceptance lane",
        "paths": [
            "docs/plans/paper-trading-cockpit-reliability-sprint.md",
            "src/Meridian.Ui.Shared/Services/TradingOperatorReadinessService.cs",
        ],
        "terms": ["/api/workstation/trading/readiness", "AcceptanceGates", "replay"],
        "weight": 3,
        "remediation": "Route replay posture through the shared trading readiness service and document it.",
    },
    {
        "id": "execution-replay-route",
        "category": "API Surface",
        "label": "Execution endpoints expose replay verification for sessions",
        "paths": ["src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs"],
        "terms": ["replay", "VerifyReplay", "sessionId"],
        "weight": 2,
        "remediation": "Restore the execution replay verification endpoint or update the dashboard checks.",
    },
    {
        "id": "durable-session-service",
        "category": "Durability",
        "label": "Paper session persistence can verify replay from durable fills, orders, and ledger state",
        "paths": ["src/Meridian.Execution/Services/PaperSessionPersistenceService.cs"],
        "terms": ["VerifyReplayAsync", "OrderCount", "LedgerEntryCount"],
        "weight": 3,
        "remediation": "Keep replay verification grounded in durable order and ledger evidence.",
    },
    {
        "id": "feature-inventory",
        "category": "Status",
        "label": "Feature inventory describes paper cockpit replay and stale-coverage posture",
        "paths": ["docs/status/FEATURE_INVENTORY.md"],
        "terms": ["Paper-trading cockpit", "replay-audit metadata", "stale-coverage detection"],
        "weight": 2,
        "remediation": "Update the feature inventory when replay reliability semantics change.",
    },
    {
        "id": "endpoint-tests",
        "category": "Validation",
        "label": "Workstation endpoint tests cover replay readiness and operator inbox routing",
        "paths": ["tests/Meridian.Tests/Ui/WorkstationEndpointsTests.cs"],
        "terms": ["Replay", "OperatorInbox", "TradingReadiness"],
        "weight": 2,
        "remediation": "Add or restore endpoint tests for replay readiness and queue projection.",
    },
]


def build_dashboard(root: Path) -> dict:
    return build_text_signal_dashboard(
        root=root,
        dashboard="paper-replay-reliability",
        title="Paper Replay Reliability Dashboard",
        description=(
            "Tracks whether paper-session replay verification, stale-readiness detection, "
            "and shared trading readiness evidence remain wired through docs, services, and tests."
        ),
        checks=CHECKS,
    )


def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Generate the paper replay reliability dashboard.")
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
