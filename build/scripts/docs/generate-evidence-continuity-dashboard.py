#!/usr/bin/env python3
"""Generate evidence continuity dashboard artifacts."""
from __future__ import annotations

import argparse
import json
from datetime import datetime, timezone
from pathlib import Path


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description="Generate evidence continuity dashboard.")
    p.add_argument("--output", required=True)
    p.add_argument("--json-output", required=True)
    p.add_argument("--summary", action="store_true")
    p.add_argument("--root", default=".")
    return p.parse_args()


def main() -> int:
    args = parse_args()
    root = Path(args.root).resolve()
    artifacts = [
        "docs/status/contract-compatibility-matrix.md",
        "docs/status/kernel-readiness-dashboard.md",
        "docs/status/dk1-pilot-parity-runbook.md",
    ]
    checks = [{"artifact": a, "exists": (root / a).exists()} for a in artifacts]
    continuity = round((sum(1 for c in checks if c['exists']) / len(checks)) * 100, 1)
    payload = {
        "generated_at": datetime.now(timezone.utc).isoformat(),
        "dashboard": "evidence-continuity",
        "continuity_percent": continuity,
        "artifacts": checks,
    }
    lines = ["# Evidence Continuity Dashboard", "", f"- Continuity: **{continuity}%**", "", "## Artifacts", "", "| Artifact | Exists |", "|---|---|"]
    lines.extend(f"| `{c['artifact']}` | {'yes' if c['exists'] else 'no'} |" for c in checks)
    lines.append("")
    Path(args.output).parent.mkdir(parents=True, exist_ok=True)
    Path(args.output).write_text("\n".join(lines), encoding="utf-8")
    Path(args.json_output).parent.mkdir(parents=True, exist_ok=True)
    Path(args.json_output).write_text(json.dumps(payload, indent=2), encoding="utf-8")
    if args.summary:
        print(f"evidence-continuity: {continuity}%")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
