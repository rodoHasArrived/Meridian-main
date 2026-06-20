#!/usr/bin/env python3
"""Shared deterministic eval runner for compact Meridian Codex skill packages."""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Any

REPO_ROOT = Path(__file__).resolve().parents[4]


@dataclass
class EvalResult:
    eval_id: int
    description: str
    status: str
    checks: list[str] = field(default_factory=list)
    command: list[str] = field(default_factory=list)
    exit_code: int = 0


def parse_args(description: str) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=description)
    selector = parser.add_mutually_exclusive_group(required=True)
    selector.add_argument("--eval-id", type=int, help="Run one eval by numeric id.")
    selector.add_argument("--all", action="store_true", help="Run all evals.")
    parser.add_argument("--dry-run", action="store_true", help="Run deterministic local fixtures.")
    parser.add_argument("--live-run", action="store_true", help="Run live codex exec traces from an isolated worktree.")
    parser.add_argument("--summary", action="store_true", help="Print aggregate summary.")
    parser.add_argument("--json", action="store_true", help="Emit machine-readable JSON.")
    return parser.parse_args()


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


def expand_command(command: list[str], skill_dir: Path) -> list[str]:
    replacements = {"{python}": sys.executable, "{repo_root}": str(REPO_ROOT), "{skill_dir}": str(skill_dir)}
    expanded: list[str] = []
    for part in command:
        if part in replacements:
            expanded.append(replacements[part])
        else:
            expanded.append(part.format(repo_root=REPO_ROOT, skill_dir=skill_dir))
    return expanded


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
            try:
                actual = json_at(stdout_json, path)
            except KeyError:
                failures.append(f"{path} missing from stdout JSON")
                continue
            if actual == expected:
                checks.append(f"{path} == {expected!r}")
            else:
                failures.append(f"{path} expected {expected!r}, got {actual!r}")
        for path, minimum in expect.get("json_min", {}).items():
            try:
                actual = json_at(stdout_json, path)
            except KeyError:
                failures.append(f"{path} missing from stdout JSON")
                continue
            if actual >= minimum:
                checks.append(f"{path} >= {minimum}")
            else:
                failures.append(f"{path} expected >= {minimum}, got {actual}")

    return EvalResult(case["id"], case["description"], "pass" if not failures else "fail", checks + failures, [], completed.returncode)


def select_cases(cases: list[dict[str, Any]], eval_id: int | None) -> list[dict[str, Any]]:
    if eval_id is None:
        return cases
    selected = [case for case in cases if case["id"] == eval_id]
    if not selected:
        raise ValueError(f"eval-id {eval_id} not found")
    return selected


def run_live(case: dict[str, Any], skill_dir: Path) -> EvalResult:
    artifacts_dir = skill_dir / "evals" / "artifacts"
    artifacts_dir.mkdir(parents=True, exist_ok=True)
    trace_path = artifacts_dir / f"eval-{case['id']}.jsonl"
    prompt = case.get("prompt")
    if not prompt:
        return EvalResult(case["id"], case["description"], "fail", ["live-run prompt is missing"], [])
    command = ["codex", "exec", "--json", "--full-auto", prompt]
    completed = subprocess.run(command, cwd=REPO_ROOT, capture_output=True, text=True, encoding="utf-8", check=False)
    trace_path.write_text(completed.stdout or "", encoding="utf-8")
    checks = [f"trace={trace_path.relative_to(skill_dir).as_posix()}", f"codex exec exit_code={completed.returncode}"]
    return EvalResult(case["id"], case["description"], "pass" if completed.returncode == 0 else "fail", checks, command, completed.returncode)


def regression_warnings(skill_dir: Path, results: list[EvalResult]) -> list[str]:
    baseline_json = skill_dir / "evals" / "benchmark_baseline.json"
    if not baseline_json.exists():
        return []
    baseline = json.loads(baseline_json.read_text(encoding="utf-8"))
    accepted = {entry["eval_id"]: entry["accepted_status"] for entry in baseline.get("baselines", [])}
    return [
        f"eval-{result.eval_id} status {result.status} differs from baseline {accepted[result.eval_id]}"
        for result in results
        if result.eval_id in accepted and result.status != accepted[result.eval_id]
    ]


def main(skill_dir: Path, description: str | None = None) -> int:
    args = parse_args(description or __doc__ or "")
    if args.dry_run and args.live_run:
        print("error: --dry-run and --live-run are mutually exclusive", file=sys.stderr)
        return 2
    if args.live_run:
        print("[live-run] Running codex exec; use only from an isolated worktree or scratch clone.", file=sys.stderr)
    try:
        cases = select_cases(json.loads((skill_dir / "evals" / "evals.json").read_text(encoding="utf-8"))["evals"], args.eval_id)
    except ValueError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2

    results: list[EvalResult] = []
    for case in cases:
        if args.live_run:
            results.append(run_live(case, skill_dir))
            continue
        command = expand_command(case["deterministic_command"], skill_dir)
        completed = subprocess.run(command, cwd=REPO_ROOT, capture_output=True, text=True, encoding="utf-8", check=False)
        result = check_expectations(case, completed)
        result.command = command
        results.append(result)

    warnings = regression_warnings(skill_dir, results)
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
