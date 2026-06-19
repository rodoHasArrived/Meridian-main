#!/usr/bin/env python3
"""Run deterministic eval cases for the Meridian roadmap strategist skill."""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
from pathlib import Path
from typing import Any

REPO_ROOT = Path(__file__).resolve().parents[4]
SKILL_DIR = Path(__file__).resolve().parents[1]
EVALS_JSON = SKILL_DIR / "evals" / "evals.json"
BASELINE_JSON = SKILL_DIR / "evals" / "benchmark_baseline.json"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    selector = parser.add_mutually_exclusive_group(required=True)
    selector.add_argument("--eval-id", type=int)
    selector.add_argument("--all", action="store_true")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--live-run", action="store_true", help="Reserved for opt-in live traces from an isolated worktree.")
    parser.add_argument("--summary", action="store_true")
    parser.add_argument("--json", action="store_true")
    return parser.parse_args()


def json_at(payload: Any, path: str) -> Any:
    current = payload
    for part in path.split("."):
        current = current[part]
    return current


def expand(command: list[str]) -> list[str]:
    return [
        part.replace("{python}", sys.executable).replace("{skill_dir}", str(SKILL_DIR)).replace("{repo_root}", str(REPO_ROOT))
        for part in command
    ]


def load_cases(eval_id: int | None) -> list[dict[str, Any]]:
    cases = json.loads(EVALS_JSON.read_text(encoding="utf-8"))["evals"]
    if eval_id is None:
        return cases
    selected = [case for case in cases if case["id"] == eval_id]
    if not selected:
        raise ValueError(f"eval-id {eval_id} not found")
    return selected


def run_case(case: dict[str, Any]) -> dict[str, Any]:
    command = expand(case["deterministic_command"])
    completed = subprocess.run(command, cwd=REPO_ROOT, capture_output=True, text=True, encoding="utf-8", check=False)
    checks: list[str] = []
    failures: list[str] = []
    expect = case.get("expect", {})
    expected_exit = expect.get("exit_code", 0)
    if completed.returncode == expected_exit:
        checks.append(f"exit_code={expected_exit}")
    else:
        failures.append(f"expected exit_code={expected_exit}, got {completed.returncode}")
    payload = json.loads(completed.stdout) if completed.stdout.strip() else {}
    if payload:
        checks.append("stdout parsed as JSON")
    for path, expected in expect.get("json_equals", {}).items():
        actual = json_at(payload, path)
        (checks if actual == expected else failures).append(f"{path} == {expected!r}" if actual == expected else f"{path} expected {expected!r}, got {actual!r}")
    for path, minimum in expect.get("json_min", {}).items():
        actual = json_at(payload, path)
        (checks if actual >= minimum else failures).append(f"{path} >= {minimum}" if actual >= minimum else f"{path} expected >= {minimum}, got {actual}")
    return {
        "eval_id": case["id"],
        "description": case["description"],
        "status": "pass" if not failures else "fail",
        "checks": checks + failures,
        "command": command,
        "exit_code": completed.returncode,
    }


def baseline_warnings(results: list[dict[str, Any]]) -> list[str]:
    if not BASELINE_JSON.exists():
        return []
    accepted = {entry["eval_id"]: entry["accepted_status"] for entry in json.loads(BASELINE_JSON.read_text(encoding="utf-8")).get("baselines", [])}
    return [
        f"eval-{result['eval_id']} status {result['status']} differs from baseline {accepted[result['eval_id']]}"
        for result in results
        if result["eval_id"] in accepted and result["status"] != accepted[result["eval_id"]]
    ]


def main() -> int:
    args = parse_args()
    if args.live_run:
        print("error: live-run is not implemented for this dry-run package eval", file=sys.stderr)
        return 2
    results = [run_case(case) for case in load_cases(args.eval_id)]
    warnings = baseline_warnings(results)
    if args.json:
        print(json.dumps({"results": results, "regressions": warnings}, indent=2))
    else:
        for result in results:
            print(f"eval-{result['eval_id']} {result['status']}: {result['description']}")
            for check in result["checks"]:
                print(f"  - {check}")
        if args.summary:
            print(f"summary {sum(1 for result in results if result['status'] == 'pass')}/{len(results)} passed")
            for warning in warnings:
                print(f"regression: {warning}")
    return 0 if all(result["status"] == "pass" for result in results) and not warnings else 1


if __name__ == "__main__":
    raise SystemExit(main())
