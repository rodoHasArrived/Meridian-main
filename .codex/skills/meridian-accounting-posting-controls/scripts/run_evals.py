#!/usr/bin/env python3
"""Run deterministic eval cases for the Meridian accounting posting-controls skill."""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Any

REPO_ROOT = Path(__file__).resolve().parents[4]
SKILL_DIR = Path(__file__).resolve().parents[1]
EVALS_DIR = SKILL_DIR / "evals"
ARTIFACTS_DIR = EVALS_DIR / "artifacts"
EVALS_JSON = EVALS_DIR / "evals.json"
BASELINE_JSON = EVALS_DIR / "benchmark_baseline.json"


@dataclass
class EvalResult:
    eval_id: int
    description: str
    status: str
    checks: list[str] = field(default_factory=list)
    command: list[str] = field(default_factory=list)
    exit_code: int = 0


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    selector = parser.add_mutually_exclusive_group(required=True)
    selector.add_argument("--eval-id", type=int, help="Run one eval by numeric id.")
    selector.add_argument("--all", action="store_true", help="Run all evals.")
    parser.add_argument("--dry-run", action="store_true", help="Run deterministic local fixtures.")
    parser.add_argument("--live-run", action="store_true", help="Run live codex exec traces from an isolated worktree.")
    parser.add_argument("--summary", action="store_true", help="Print aggregate summary.")
    parser.add_argument("--json", action="store_true", help="Emit machine-readable JSON.")
    return parser.parse_args()


def load_cases() -> list[dict[str, Any]]:
    return json.loads(EVALS_JSON.read_text(encoding="utf-8"))["evals"]


def select_cases(cases: list[dict[str, Any]], eval_id: int | None) -> list[dict[str, Any]]:
    if eval_id is None:
        return cases
    selected = [case for case in cases if case["id"] == eval_id]
    if not selected:
        raise ValueError(f"eval-id {eval_id} not found")
    return selected


def expand_command(command: list[str]) -> list[str]:
    replacements = {
        "{python}": sys.executable,
        "{repo_root}": str(REPO_ROOT),
        "{skill_dir}": str(SKILL_DIR),
    }
    return [replacements[part] if part in replacements else part.format(repo_root=REPO_ROOT, skill_dir=SKILL_DIR) for part in command]


def json_at(payload: Any, dotted_path: str) -> Any:
    current = payload
    for part in dotted_path.split("."):
        if isinstance(current, dict):
            current = current[part]
        elif isinstance(current, list) and part.isdigit():
            current = current[int(part)]
        else:
            raise KeyError(dotted_path)
    return current


def check_expectations(case: dict[str, Any], completed: subprocess.CompletedProcess[str]) -> EvalResult:
    checks: list[str] = []
    failures: list[str] = []
    expect = case.get("expect", {})

    expected_exit = expect.get("exit_code", 0)
    if completed.returncode == expected_exit:
        checks.append(f"exit_code={expected_exit}")
    else:
        failures.append(f"expected exit_code={expected_exit}, got {completed.returncode}")

    stdout_json: dict[str, Any] | None = None
    if completed.stdout.strip():
        try:
            stdout_json = json.loads(completed.stdout)
            checks.append("stdout parsed as JSON")
        except json.JSONDecodeError as exc:
            failures.append(f"stdout was not JSON: {exc}")

    if stdout_json is not None:
        for path, expected in expect.get("json_equals", {}).items():
            actual = json_at(stdout_json, path)
            if actual == expected:
                checks.append(f"{path} == {expected!r}")
            else:
                failures.append(f"{path} expected {expected!r}, got {actual!r}")
        for path, minimum in expect.get("json_min", {}).items():
            actual = json_at(stdout_json, path)
            if actual >= minimum:
                checks.append(f"{path} >= {minimum}")
            else:
                failures.append(f"{path} expected >= {minimum}, got {actual}")

    return EvalResult(case["id"], case["description"], "pass" if not failures else "fail", checks + failures, [], completed.returncode)


def run_deterministic(case: dict[str, Any]) -> EvalResult:
    command = expand_command(case["deterministic_command"])
    completed = subprocess.run(command, cwd=REPO_ROOT, capture_output=True, text=True, encoding="utf-8", check=False)
    result = check_expectations(case, completed)
    result.command = command
    return result


def run_live(case: dict[str, Any]) -> EvalResult:
    ARTIFACTS_DIR.mkdir(parents=True, exist_ok=True)
    trace_path = ARTIFACTS_DIR / f"eval-{case['id']}.jsonl"
    prompt = case.get("prompt")
    if not prompt:
        return EvalResult(case["id"], case["description"], "fail", ["live-run prompt is missing"], [])
    command = ["codex", "exec", "--json", "--full-auto", prompt]
    completed = subprocess.run(command, cwd=REPO_ROOT, capture_output=True, text=True, encoding="utf-8", check=False)
    trace_path.write_text(completed.stdout or "", encoding="utf-8")
    checks = [f"trace={trace_path.relative_to(SKILL_DIR).as_posix()}", f"codex exec exit_code={completed.returncode}"]
    return EvalResult(case["id"], case["description"], "pass" if completed.returncode == 0 else "fail", checks, command, completed.returncode)


def regression_warnings(results: list[EvalResult]) -> list[str]:
    if not BASELINE_JSON.exists():
        return []
    baseline = json.loads(BASELINE_JSON.read_text(encoding="utf-8"))
    accepted = {entry["eval_id"]: entry["accepted_status"] for entry in baseline.get("baselines", [])}
    return [
        f"eval-{result.eval_id} status {result.status} differs from baseline {accepted[result.eval_id]}"
        for result in results
        if result.eval_id in accepted and result.status != accepted[result.eval_id]
    ]


def main() -> int:
    args = parse_args()
    if args.dry_run and args.live_run:
        print("error: --dry-run and --live-run are mutually exclusive", file=sys.stderr)
        return 2
    if args.live_run:
        print("[live-run] Running codex exec; use only from an isolated worktree or scratch clone.", file=sys.stderr)
    try:
        cases = select_cases(load_cases(), args.eval_id)
    except ValueError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2

    results = [run_live(case) if args.live_run else run_deterministic(case) for case in cases]
    warnings = regression_warnings(results)
    payload = {"results": [asdict(result) for result in results], "regressions": warnings}

    if args.json:
        print(json.dumps(payload, indent=2))
    else:
        for result in results:
            print(f"eval-{result.eval_id} {result.status}: {result.description}")
            for check in result.checks:
                print(f"  - {check}")
        if args.summary:
            passed = sum(1 for result in results if result.status == "pass")
            print(f"summary {passed}/{len(results)} passed")
            for warning in warnings:
                print(f"regression: {warning}")

    return 0 if all(result.status == "pass" for result in results) and not warnings else 1


if __name__ == "__main__":
    raise SystemExit(main())
