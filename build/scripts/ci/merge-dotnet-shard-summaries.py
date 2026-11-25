#!/usr/bin/env python3
"""Combine hosted .NET shard summaries and prove the complete roster ran exactly once.

Meridian CI runs the default .NET test roster as several runner shards. Each shard is a
complete `scripts/ci.sh --lane verify-dotnet` invocation restricted to its slice. This
script is the lane's single verdict: it fails unless every expected shard reported for the
same commit, the shards together ran every default roster entry once and only once, and no
entry failed. A missing, duplicated or foreign summary can therefore never read as green.
"""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import sys
from pathlib import Path

SUMMARY_NAME = "ci-dotnet-test-summary.json"
RUNNER_PATH = Path(__file__).resolve().parent / "run-dotnet-ci-tests.py"


def default_roster() -> list[str]:
    spec = importlib.util.spec_from_file_location("run_dotnet_ci_tests", RUNNER_PATH)
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return [name for name, _, _ in module.DEFAULT_TEST_PROJECTS]


def merge(shards_dir: Path, *, expected_shards: int, roster: list[str]) -> tuple[dict, list[str]]:
    """Return the combined summary and every reason it cannot be accepted."""
    errors: list[str] = []
    by_index: dict[int, dict] = {}
    for path in sorted(shards_dir.rglob(SUMMARY_NAME)):
        try:
            payload = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, ValueError) as exc:
            errors.append(f"{path}: unreadable summary ({exc})")
            continue
        shard = payload.get("shard") if isinstance(payload, dict) else None
        if (not isinstance(shard, dict) or type(shard.get("index")) is not int
                or shard.get("total") != expected_shards
                or not 1 <= shard["index"] <= expected_shards):
            errors.append(f"{path}: summary is not one of the {expected_shards} expected shards")
            continue
        if shard["index"] in by_index:
            errors.append(f"{path}: duplicate summary for shard {shard['index']}")
            continue
        by_index[shard["index"]] = payload

    for index in range(1, expected_shards + 1):
        if index not in by_index:
            errors.append(f"shard {index}/{expected_shards}: no summary was produced")

    shards = [by_index[index] for index in sorted(by_index)]
    commits = {payload.get("commitSha") for payload in shards}
    if len(commits) > 1:
        errors.append(f"shards report different commits: {sorted(str(commit) for commit in commits)}")

    results: dict[str, dict] = {}
    for payload in shards:
        index = payload["shard"]["index"]
        if payload.get("build_results") is None:
            # A failed build writes only build results, which must not pass for test results.
            errors.append(f"shard {index}: tests did not start")
            continue
        for result in payload.get("results", []):
            name = result.get("name")
            if name in results:
                errors.append(f"roster entry '{name}' ran in more than one shard")
                continue
            results[name] = result
            if result.get("exit_code") != 0:
                errors.append(f"roster entry '{name}' failed in shard {index}")
    for name in roster:
        if name not in results:
            errors.append(f"roster entry '{name}' did not run in any shard")
    for name in results:
        if name not in roster:
            errors.append(f"'{name}' is not a default roster entry")

    ordered = [results[name] for name in roster if name in results]
    identities = sorted(
        f"{result['path']}|{identity}"
        for result in ordered if result.get("evidence")
        for identity in result["evidence"]["testIdentities"]
    )
    failed = sum(1 for result in ordered if result.get("exit_code") != 0)
    combined = {
        "total": len(ordered),
        "passed": len(ordered) - failed,
        "failed": failed,
        "results": ordered,
        "counts": {
            key: sum(result["evidence"]["counts"][key] for result in ordered if result.get("evidence"))
            for key in ("passed", "failed", "skipped", "other")
        },
        "testIdentityDigest": (
            hashlib.sha256(json.dumps(identities, ensure_ascii=True).encode()).hexdigest() if identities else None
        ),
        # Shards overlap, so their sum is runner time and the longest is the lane's test time.
        "testSeconds": max((payload.get("testSeconds") or 0 for payload in shards), default=0) or None,
        "testRunnerSeconds": sum(payload.get("testSeconds") or 0 for payload in shards) or None,
        "commitSha": next(iter(commits)) if len(commits) == 1 else None,
        "shards": [
            {
                "index": payload["shard"]["index"],
                "runId": payload.get("runId"),
                "runAttempt": payload.get("runAttempt"),
                "cacheHit": payload.get("cacheHit"),
                "testSeconds": payload.get("testSeconds"),
                "entries": [result.get("name") for result in payload.get("results", [])],
            }
            for payload in shards
        ],
        "accepted": not errors,
        "errors": errors,
    }
    return combined, errors


def render(combined: dict, expected_shards: int) -> str:
    lines = [
        "### .NET CI combined shard summary",
        "",
        f"- Shards reporting: {len(combined['shards'])} of {expected_shards}",
        f"- Roster entries: {combined['total']} (passed {combined['passed']}, failed {combined['failed']})",
        f"- Tests: {json.dumps(combined['counts'])}",
        f"- Verdict: {'accepted' if combined['accepted'] else 'rejected'}",
        "",
    ]
    if combined["errors"]:
        lines.extend(["#### Rejected because", ""])
        lines.extend(f"- {error}" for error in combined["errors"])
        lines.append("")
    lines.extend(["| Shard | Run attempt | Test seconds | Entries |", "| ---: | --- | ---: | --- |"])
    for shard in combined["shards"]:
        seconds = f"{shard['testSeconds']:.1f}" if shard["testSeconds"] else "—"
        entries = ", ".join(f"`{name}`" for name in shard["entries"])
        lines.append(f"| {shard['index']} | {shard['runAttempt'] or 'local'} | {seconds} | {entries} |")
    return "\n".join(lines) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--shards-dir", type=Path, required=True,
                        help="Directory tree holding one downloaded evidence artifact per shard.")
    parser.add_argument("--expected-shards", type=int, required=True)
    parser.add_argument("--json-output", type=Path, required=True)
    parser.add_argument("--summary-output", type=Path, required=True)
    parser.add_argument("--github-step-summary", type=Path)
    args = parser.parse_args()
    if args.expected_shards < 1:
        parser.error("--expected-shards must be a positive integer")

    combined, errors = merge(args.shards_dir, expected_shards=args.expected_shards, roster=default_roster())
    markdown = render(combined, args.expected_shards)
    for output, text in ((args.json_output, json.dumps(combined, indent=2) + "\n"), (args.summary_output, markdown)):
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(text, encoding="utf-8")
    if args.github_step_summary:
        with args.github_step_summary.open("a", encoding="utf-8") as stream:
            stream.write(markdown)

    if errors:
        print("Combined .NET shard evidence was rejected:", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1
    print(f"All {combined['total']} .NET roster entries passed across {args.expected_shards} shards.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
