#!/usr/bin/env python3
"""Generate API contract coverage dashboard artifacts."""
from __future__ import annotations

import argparse
import json
from datetime import datetime, timezone
from pathlib import Path


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description="Generate API contract coverage dashboard.")
    p.add_argument("--output", required=True)
    p.add_argument("--json-output", required=True)
    p.add_argument("--summary", action="store_true")
    p.add_argument("--root", default=".")
    return p.parse_args()


def main() -> int:
    args = parse_args()
    root = Path(args.root).resolve()
    artifacts = [
        "docs/status/api-docs-report.md",
        "docs/status/contract-compatibility-matrix.md",
    ]
    checks = [{"artifact": a, "covered": (root / a).exists()} for a in artifacts]
    coverage = round((sum(1 for c in checks if c['covered']) / len(checks)) * 100, 1)
    payload = {
        "generated_at": datetime.now(timezone.utc).isoformat(),
        "dashboard": "api-contract-coverage",
        "coverage_percent": coverage,
        "checks": checks,
    }
    lines = ["# API Contract Coverage Dashboard", "", f"- Coverage: **{coverage}%**", "", "| Artifact | Covered |", "|---|---|"]
    lines.extend(f"| `{c['artifact']}` | {'yes' if c['covered'] else 'no'} |" for c in checks)
    lines.append("")
    Path(args.output).parent.mkdir(parents=True, exist_ok=True)
    Path(args.output).write_text("\n".join(lines), encoding="utf-8")
    Path(args.json_output).parent.mkdir(parents=True, exist_ok=True)
    Path(args.json_output).write_text(json.dumps(payload, indent=2), encoding="utf-8")
    if args.summary:
        print(f"api-contract-coverage: {coverage}%")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
