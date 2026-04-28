#!/usr/bin/env python3
"""Generate governance readiness dashboard artifacts."""
from __future__ import annotations

import argparse
import json
from datetime import datetime, timezone
from pathlib import Path


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description="Generate governance readiness dashboard.")
    p.add_argument("--output", required=True)
    p.add_argument("--json-output", required=True)
    p.add_argument("--summary", action="store_true")
    p.add_argument("--root", default=".")
    return p.parse_args()


def main() -> int:
    args = parse_args()
    root = Path(args.root).resolve()
    policies = [
        "docs/status/contract-compatibility-matrix.md",
        "docs/status/provider-validation-matrix.md",
    ]
    checks = [{"item": p, "status": "ready" if (root / p).exists() else "gap"} for p in policies]
    ready = sum(1 for c in checks if c["status"] == "ready")
    score = round((ready / len(checks)) * 100, 1)
    payload = {"generated_at": datetime.now(timezone.utc).isoformat(), "dashboard": "governance-readiness", "score": score, "checks": checks}
    md = ["# Governance Readiness Dashboard", "", f"- Governance score: **{score}%**", "", "| Control | Status |", "|---|---|", *[f"| `{c['item']}` | {c['status']} |" for c in checks], ""]
    Path(args.output).parent.mkdir(parents=True, exist_ok=True)
    Path(args.output).write_text("\n".join(md), encoding="utf-8")
    Path(args.json_output).parent.mkdir(parents=True, exist_ok=True)
    Path(args.json_output).write_text(json.dumps(payload, indent=2), encoding="utf-8")
    if args.summary:
        print(f"governance-readiness: {score}%")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
