#!/usr/bin/env python3
"""Recommend or enforce mode escalation based on route + changed file risk."""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
from pathlib import Path
from typing import Any


DEFAULT_ROUTE_JSON = Path("docs/status/prompt-route-lint-report.json")
DEFAULT_SUMMARY_JSON = Path("docs/status/docs-automation-summary.json")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--route-json", default=str(DEFAULT_ROUTE_JSON), help="Prompt-route linter JSON output.")
    parser.add_argument("--summary-json", default=str(DEFAULT_SUMMARY_JSON), help="Docs automation summary JSON output.")
    parser.add_argument("--changed-file", action="append", default=[], help="Explicit changed file path(s).")
    parser.add_argument("--summary", action="store_true", help="Print compact summary output.")
    return parser.parse_args()


def load_json(path: Path) -> dict[str, Any]:
    if not path.exists():
        return {}
    return json.loads(path.read_text(encoding="utf-8"))


def changed_files(args: argparse.Namespace) -> list[str]:
    if args.changed_file:
        return sorted({item.replace("\\", "/") for item in args.changed_file})
    proc = subprocess.run(["git", "show", "--name-only", "--pretty=format:", "HEAD"], capture_output=True, text=True)
    if proc.returncode != 0:
        return []
    return sorted({line.strip().replace("\\", "/") for line in proc.stdout.splitlines() if line.strip()})


def infer_lanes(files: list[str]) -> set[str]:
    lanes: set[str] = set()
    for path in files:
        if path.startswith("docs/") or path.startswith("build/scripts/docs/") or path.startswith(".codex/"):
            lanes.add("docs")
        if path.startswith("src/Meridian.Ui/dashboard/"):
            lanes.add("browser")
        if path.startswith("src/Meridian.Wpf/") or path.startswith("tests/Meridian.Wpf.Tests/"):
            lanes.add("desktop")
        if "Provider" in path or "provider" in path:
            lanes.add("provider")
    return lanes


def has_failed_scripts(summary: dict[str, Any]) -> bool:
    results = summary.get("results")
    if not isinstance(results, list):
        return False
    for item in results:
        if isinstance(item, dict) and item.get("status") == "failed":
            return True
    return False


def has_trigger(route: dict[str, Any], *needles: str) -> bool:
    triggers = route.get("escalationTriggers", [])
    if not isinstance(triggers, list):
        return False
    lowered = [str(trigger).lower() for trigger in triggers]
    return any(any(needle in trigger for needle in needles) for trigger in lowered)


def main() -> int:
    args = parse_args()
    route = load_json(Path(args.route_json)).get("match", {})
    summary = load_json(Path(args.summary_json))
    files = changed_files(args)

    current_mode = str(route.get("mode", "Lightweight"))
    model_route_id = str(route.get("modelRouteId", ""))
    lanes = infer_lanes(files)

    reasons: list[str] = []

    # Cross-lane edits should not stay in Lightweight.
    if len(lanes) > 1 and current_mode == "Lightweight" and has_trigger(route, "cross-lane", "spans"):
        reasons.append("cross-lane edits detected while route mode is Lightweight")

    # Policy and contract files require deeper review.
    if any(path in {
        "docs/ai/assistant-workflow-contract.md",
        "docs/ai/model-routing-policy.json",
        "docs/ai/contract-policy.json",
    } for path in files) and current_mode != "Deep Review" and has_trigger(route, "policy", "shared ai"):
        reasons.append("policy/contract file touched without Deep Review mode")

    # If failures are already present, lightweight mode is under-scoped.
    if has_failed_scripts(summary) and current_mode == "Lightweight" and has_trigger(route, "validation", "fails"):
        reasons.append("validation failures observed under Lightweight mode")

    if model_route_id == "governance-research" and current_mode != "Deep Review":
        reasons.append("governance-research model route requires Deep Review mode")

    if args.summary:
        print("[check-mode-escalation] CHECK")
        print(f"mode={current_mode}")
        print(f"model_route_id={model_route_id or 'unknown'}")
        print(f"lane_count={len(lanes)}")

    if reasons:
        print("[check-mode-escalation] FAILED:", file=sys.stderr)
        for reason in reasons:
            print(f" - {reason}", file=sys.stderr)
        return 1

    if args.summary:
        print("[check-mode-escalation] OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
