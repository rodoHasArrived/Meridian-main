#!/usr/bin/env python3
"""Generate paper replay reliability dashboard artifacts."""
from __future__ import annotations

import argparse
import json
from datetime import datetime, timezone
from pathlib import Path


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Generate paper replay reliability dashboard.")
    parser.add_argument("--output", required=True)
    parser.add_argument("--json-output", required=True)
    parser.add_argument("--summary", action="store_true")
    parser.add_argument("--root", default=".")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    root = Path(args.root).resolve()
    signals = [
        "docs/plans/paper-trading-cockpit-reliability-sprint.md",
        "docs/status/FEATURE_INVENTORY.md",
    ]
    checks = [{"artifact": rel, "status": "present" if (root / rel).exists() else "missing"} for rel in signals]
    present = sum(1 for c in checks if c["status"] == "present")
    total = len(checks)
    reliability = round((present / total) * 100, 1) if total else 0.0
    payload = {
        "generated_at": datetime.now(timezone.utc).isoformat(),
        "dashboard": "paper-replay-reliability",
        "reliability_percent": reliability,
        "signals": checks,
    }
    md = [
        "# Paper Replay Reliability Dashboard",
        "",
        f"- Generated: {payload['generated_at']}",
        f"- Reliability posture: **{reliability}%** ({present}/{total} signals present)",
        "",
        "## Evidence Signals",
        "",
        "| Signal | Status |",
        "|---|---|",
        *[f"| `{c['artifact']}` | {c['status']} |" for c in checks],
        "",
    ]
    Path(args.output).parent.mkdir(parents=True, exist_ok=True)
    Path(args.output).write_text("\n".join(md), encoding="utf-8")
    Path(args.json_output).parent.mkdir(parents=True, exist_ok=True)
    Path(args.json_output).write_text(json.dumps(payload, indent=2), encoding="utf-8")
    if args.summary:
        print(f"paper-replay-reliability: {reliability}%")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
