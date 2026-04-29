#!/usr/bin/env python3
"""Run deterministic eval cases for the Meridian archive organizer skill."""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
from collections import OrderedDict
from pathlib import Path


ROOT_DIR = Path(__file__).resolve().parents[4]
SKILL_DIR = Path(__file__).resolve().parents[1]
TRACE_SCRIPT = Path(__file__).resolve().with_name("trace_archive_candidates.py")
EVALS_PATH = SKILL_DIR / "evals" / "evals.json"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    selector = parser.add_mutually_exclusive_group(required=True)
    selector.add_argument("--eval-id", type=int, help="Run a single eval by numeric id.")
    selector.add_argument("--all", action="store_true", help="Run all configured evals.")
    parser.add_argument("--dry-run", action="store_true", help="Print commands without executing them.")
    parser.add_argument("--json", action="store_true", help="Emit machine-readable JSON.")
    return parser.parse_args()


def load_cases() -> dict[str, object]:
    return json.loads(EVALS_PATH.read_text(encoding="utf-8"))


def select_cases(payload: dict[str, object], eval_id: int | None) -> list[dict[str, object]]:
    cases = payload["evals"]
    if eval_id is None:
        return cases
    selected = [case for case in cases if case["id"] == eval_id]
    if not selected:
        raise ValueError(f"Eval id {eval_id} was not found in {EVALS_PATH}.")
    return selected


def resolve_repo_root(case: dict[str, object]) -> Path:
    repo_root = case.get("repo_root")
    if repo_root is None:
        return ROOT_DIR

    resolved = Path(repo_root)
    if not resolved.is_absolute():
        resolved = (ROOT_DIR / resolved).resolve()
    return resolved


def evaluate_case(case: dict[str, object], command: list[str], result: dict[str, object]) -> dict[str, object]:
    checks: list[str] = []
    failures: list[str] = []

    expected_recommendation = case.get("expected_recommendation")
    if expected_recommendation is not None:
        if result["recommendation"] == expected_recommendation:
            checks.append(f"recommendation matched `{expected_recommendation}`")
        else:
            failures.append(
                f"recommendation expected `{expected_recommendation}` but got `{result['recommendation']}`"
            )

    if "must_exist" in case:
        if result["exists"] == case["must_exist"]:
            checks.append(f"exists matched `{case['must_exist']}`")
        else:
            failures.append(f"exists expected `{case['must_exist']}` but got `{result['exists']}`")

    min_strong_refs = case.get("min_strong_refs")
    if min_strong_refs is not None:
        if result["strong_ref_count"] >= min_strong_refs:
            checks.append(f"strong_ref_count >= {min_strong_refs}")
        else:
            failures.append(
                f"strong_ref_count expected >= {min_strong_refs} but got {result['strong_ref_count']}"
            )

    max_strong_refs = case.get("max_strong_refs")
    if max_strong_refs is not None:
        if result["strong_ref_count"] <= max_strong_refs:
            checks.append(f"strong_ref_count <= {max_strong_refs}")
        else:
            failures.append(
                f"strong_ref_count expected <= {max_strong_refs} but got {result['strong_ref_count']}"
            )

    max_weak_refs = case.get("max_weak_refs")
    if max_weak_refs is not None:
        if result["weak_ref_count"] <= max_weak_refs:
            checks.append(f"weak_ref_count <= {max_weak_refs}")
        else:
            failures.append(
                f"weak_ref_count expected <= {max_weak_refs} but got {result['weak_ref_count']}"
            )

    return {
        "id": case["id"],
        "description": case["description"],
        "path": case["path"],
        "command": command,
        "status": "pass" if not failures else "fail",
        "checks": checks + failures,
        "trace": result,
    }


def run_cases(cases: list[dict[str, object]]) -> list[dict[str, object]]:
    cases_by_root: OrderedDict[str, list[dict[str, object]]] = OrderedDict()
    for case in cases:
        repo_root = str(resolve_repo_root(case))
        cases_by_root.setdefault(repo_root, []).append(case)

    evaluated: dict[tuple[str, str], dict[str, object]] = {}
    for repo_root, grouped_cases in cases_by_root.items():
        command = [
            sys.executable,
            str(TRACE_SCRIPT),
            "--repo-root",
            repo_root,
        ]
        for case in grouped_cases:
            command.extend(["--path", case["path"]])
        command.append("--json")

        completed = subprocess.run(command, capture_output=True, text=True, check=False)
        if completed.returncode != 0:
            for case in grouped_cases:
                evaluated[(repo_root, case["path"])] = {
                    "id": case["id"],
                    "description": case["description"],
                    "path": case["path"],
                    "command": command,
                    "status": "fail",
                    "checks": [f"trace script failed with exit code {completed.returncode}"],
                    "stderr": completed.stderr.strip(),
                }
            continue

        payload = json.loads(completed.stdout)
        results_by_path = {result["path"]: result for result in payload["results"]}
        for case in grouped_cases:
            evaluated[(repo_root, case["path"])] = evaluate_case(case, command, results_by_path[case["path"]])

    return [evaluated[(str(resolve_repo_root(case)), case["path"])] for case in cases]


def print_markdown(results: list[dict[str, object]]) -> None:
    passed = sum(1 for result in results if result["status"] == "pass")
    print(f"## Eval Summary `{passed}/{len(results)}` passed")
    print()
    for result in results:
        print(f"### Eval {result['id']} `{result['status']}`")
        print(f"- description: {result['description']}")
        print(f"- path: `{result['path']}`")
        print(f"- command: `{' '.join(result['command'])}`")
        for check in result["checks"]:
            print(f"- check: {check}")
        trace = result.get("trace")
        if trace:
            print(f"- recommendation: `{trace['recommendation']}`")
            print(f"- strong_ref_count: `{trace['strong_ref_count']}`")
            print(f"- weak_ref_count: `{trace['weak_ref_count']}`")
        stderr = result.get("stderr")
        if stderr:
            print(f"- stderr: {stderr}")
        print()


def main() -> int:
    args = parse_args()
    try:
        payload = load_cases()
        cases = select_cases(payload, args.eval_id)
    except (ValueError, json.JSONDecodeError) as exc:
        print(str(exc), file=sys.stderr)
        return 2

    if args.dry_run:
        results = []
        for case in cases:
            command = [
                sys.executable,
                str(TRACE_SCRIPT),
                "--repo-root",
                str(ROOT_DIR),
                "--path",
                case["path"],
                "--json",
            ]
            results.append(
                {
                    "id": case["id"],
                    "description": case["description"],
                    "path": case["path"],
                    "command": command,
                    "status": "dry-run",
                    "checks": ["execution skipped because --dry-run was used"],
                }
            )
        if args.json:
            json.dump({"skill_name": payload["skill_name"], "results": results}, sys.stdout, indent=2)
            sys.stdout.write("\n")
        else:
            print_markdown(results)
        return 0

    results = run_cases(cases)
    output = {"skill_name": payload["skill_name"], "results": results}
    if args.json:
        json.dump(output, sys.stdout, indent=2)
        sys.stdout.write("\n")
    else:
        print_markdown(results)
    return 0 if all(result["status"] == "pass" for result in results) else 1


if __name__ == "__main__":
    raise SystemExit(main())
