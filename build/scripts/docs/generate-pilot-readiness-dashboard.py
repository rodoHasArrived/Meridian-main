#!/usr/bin/env python3
"""Generate a pilot-readiness dashboard in Markdown and JSON."""
from __future__ import annotations

import argparse
import json
from datetime import datetime, timezone
from pathlib import Path


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Generate pilot readiness dashboard.")
    parser.add_argument("--output", required=True, help="Markdown output path.")
    parser.add_argument("--json-output", required=True, help="JSON output path.")
    parser.add_argument("--summary", action="store_true", help="Print summary to stdout.")
    parser.add_argument("--root", default=".", help="Repository root.")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    root = Path(args.root).resolve()

    required_docs = [
        "docs/status/provider-validation-matrix.md",
        "docs/status/dk1-pilot-parity-runbook.md",
        "docs/status/kernel-readiness-dashboard.md",
    ]

    checks: list[dict[str, str]] = []
    for rel in required_docs:
        path = root / rel
        checks.append({"artifact": rel, "status": "present" if path.exists() else "missing"})

    present = sum(1 for c in checks if c["status"] == "present")
    total = len(checks)
    score = round((present / total) * 100, 1) if total else 0.0

    payload = {
        "generated_at": datetime.now(timezone.utc).isoformat(),
        "dashboard": "pilot-readiness",
        "score": score,
        "checks": checks,
    }

    md_lines = [
        "# Pilot Readiness Dashboard",
        "",
        f"- Generated: {payload['generated_at']}",
        f"- Readiness score: **{score}%** ({present}/{total} artifacts present)",
        "",
        "## Artifact Checks",
        "",
        "| Artifact | Status |",
        "|---|---|",
    ]
    md_lines.extend(f"| `{c['artifact']}` | {c['status']} |" for c in checks)
    md_lines.append("")

    out = Path(args.output)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text("\n".join(md_lines), encoding="utf-8")

    jout = Path(args.json_output)
    jout.parent.mkdir(parents=True, exist_ok=True)
    jout.write_text(json.dumps(payload, indent=2), encoding="utf-8")

    if args.summary:
        print(f"pilot-readiness: score={score}% artifacts={present}/{total}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
