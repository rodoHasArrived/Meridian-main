#!/usr/bin/env python3
"""Fail CI when AI/docs changes do not satisfy route-specific validation-floor evidence."""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path
from typing import Any


DEFAULT_SUMMARY_JSON = Path("docs/status/docs-automation-summary.json")
DEFAULT_ROUTE_JSON = Path("docs/status/prompt-route-lint-report.json")
DEFAULT_REQUIRED_SCRIPTS = (
    "check-ai-handoff-strict",
    "prompt-route-linter",
    "handoff-packet-generator",
)
LANE_REQUIRED_SCRIPTS: dict[str, tuple[str, ...]] = {
    "docs": ("check-ai-handoff-strict", "prompt-route-linter", "handoff-packet-generator"),
    "browser": ("prompt-route-linter", "handoff-packet-generator"),
    "desktop": ("prompt-route-linter", "handoff-packet-generator"),
    "provider": ("prompt-route-linter", "handoff-packet-generator", "check-handoff-packet-schema"),
    "review": ("prompt-route-linter",),
    "test": ("prompt-route-linter", "handoff-packet-generator"),
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--summary-json",
        default=str(DEFAULT_SUMMARY_JSON),
        help=f"run-docs-automation JSON summary path (default: {DEFAULT_SUMMARY_JSON}).",
    )
    parser.add_argument(
        "--route-json",
        default=str(DEFAULT_ROUTE_JSON),
        help=f"prompt-route-linter JSON output (default: {DEFAULT_ROUTE_JSON}).",
    )
    parser.add_argument(
        "--required-script",
        action="append",
        default=[],
        help="Required script name; repeat for multiple values.",
    )
    parser.add_argument(
        "--changed-file",
        action="append",
        default=[],
        help="Explicit changed file path (optional). If omitted, uses git history.",
    )
    parser.add_argument("--summary", action="store_true", help="Print compact status output.")
    return parser.parse_args()


def load_json(path: Path) -> dict[str, Any]:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError as exc:
        raise ValueError(f"Missing JSON: {path}") from exc
    except json.JSONDecodeError as exc:
        raise ValueError(f"Invalid JSON at {path}: {exc}") from exc


def detect_changed_files_from_git() -> list[str]:
    proc = subprocess.run(
        ["git", "show", "--name-only", "--pretty=format:", "HEAD"],
        capture_output=True,
        text=True,
    )
    if proc.returncode != 0:
        return []
    files = [line.strip().replace("\\", "/") for line in proc.stdout.splitlines() if line.strip()]
    return sorted(set(files))


def requires_validation_floor(changed_files: list[str]) -> bool:
    if not changed_files:
        return False
    ai_docs_pattern = re.compile(r"^(docs/ai/|\.codex/|build/scripts/docs/|docs/README\.md|AGENTS\.md|CLAUDE\.md)")
    return any(ai_docs_pattern.search(path) for path in changed_files)


def collect_script_statuses(summary: dict[str, Any]) -> dict[str, str]:
    results = summary.get("results")
    if not isinstance(results, list):
        return {}
    statuses: dict[str, str] = {}
    for item in results:
        if not isinstance(item, dict):
            continue
        name = item.get("name")
        status = item.get("status")
        if isinstance(name, str) and isinstance(status, str):
            statuses[name] = status
    return statuses


def required_scripts_for_route(route_summary: dict[str, Any]) -> list[str]:
    match = route_summary.get("match", {}) if isinstance(route_summary, dict) else {}
    lane = str(match.get("lane", "")).strip()
    lane_rules = list(LANE_REQUIRED_SCRIPTS.get(lane, ()))
    route_defined = match.get("validationScripts")
    if isinstance(route_defined, list) and route_defined:
        lane_rules.extend([str(item) for item in route_defined])
    if not lane_rules:
        return list(DEFAULT_REQUIRED_SCRIPTS)
    return sorted(set(lane_rules))


def main() -> int:
    args = parse_args()
    changed_files = [path.replace("\\", "/") for path in args.changed_file] or detect_changed_files_from_git()

    should_enforce = requires_validation_floor(changed_files)
    if not should_enforce:
        if args.summary:
            print("[check-validation-floor] SKIP")
            print("reason=no-ai-docs-related-changes-detected")
        return 0

    try:
        summary = load_json(Path(args.summary_json))
        route_summary = load_json(Path(args.route_json))
    except ValueError as exc:
        print(f"[check-validation-floor] FAILED: {exc}", file=sys.stderr)
        return 1

    required_scripts = args.required_script or required_scripts_for_route(route_summary)
    statuses = collect_script_statuses(summary)
    missing = [name for name in required_scripts if name not in statuses]
    failed = [name for name in required_scripts if statuses.get(name) != "success"]

    if args.summary:
        route_lane = route_summary.get("match", {}).get("lane", "unknown")
        print("[check-validation-floor] CHECK")
        print(f"changed_files={len(changed_files)}")
        print(f"route_lane={route_lane}")
        print(f"required_scripts={','.join(required_scripts)}")

    if missing or failed:
        if missing:
            print(f"[check-validation-floor] FAILED: missing required scripts in summary: {', '.join(missing)}", file=sys.stderr)
        if failed:
            print(f"[check-validation-floor] FAILED: required scripts not successful: {', '.join(failed)}", file=sys.stderr)
        return 1

    if args.summary:
        print("[check-validation-floor] OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
