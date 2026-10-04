#!/usr/bin/env python3
"""Summarize exported Actions jobs, keeping queues and retries out of execution time."""
from __future__ import annotations

import argparse
from datetime import datetime
import json
import math
from pathlib import Path
from statistics import median


def seconds(start: str | None, end: str | None) -> float | None:
    if not start or not end:
        return None
    value = (datetime.fromisoformat(end.replace("Z", "+00:00")) -
             datetime.fromisoformat(start.replace("Z", "+00:00"))).total_seconds()
    return max(0.0, value)


def summarize(payload: dict) -> dict:
    """Input: runs with event, run_attempt, head_sha, conclusion and embedded jobs."""
    groups: dict[str, list[dict]] = {}
    for run in payload["workflow_runs"]:
        key = f"{run['name']} / {run['event']} / attempt {run.get('run_attempt', 1)}"
        jobs = run.get("jobs", [])
        records = []
        for job in jobs:
            records.append({
                "name": job["name"], "conclusion": job.get("conclusion"),
                "queueSeconds": seconds(job.get("created_at"), job.get("started_at")),
                "executionSeconds": seconds(job.get("started_at"), job.get("completed_at")),
            })
        complete = bool(records) and all(j["executionSeconds"] is not None for j in records)
        groups.setdefault(key, []).append({
            "runId": run["id"], "commitSha": run["head_sha"],
            "conclusion": run.get("conclusion"), "jobs": records,
            "runnerSeconds": sum(j["executionSeconds"] for j in records) if complete else None,
        })
    result = {}
    for name, runs in groups.items():
        eligible = [r["runnerSeconds"] for r in runs
                    if r["conclusion"] == "success" and r["runnerSeconds"] is not None]
        result[name] = {"runs": runs, "successfulSamples": len(eligible),
                        "medianRunnerSeconds": median(eligible) if eligible else None}
    return {"schemaVersion": 1, "groups": result}


def evaluate_pairs(pairs: list[dict]) -> dict:
    """A tuning candidate is advisory until five distinct, comparable pairs pass."""
    reasons = []
    if len(pairs) < 5 or any(not p.get("pairId") for p in pairs) or len({p.get("pairId") for p in pairs}) != len(pairs):
        reasons.append("Five distinct paired runs are required.")
    ratios = []
    for pair in pairs:
        base, candidate = pair["baseline"], pair["candidate"]
        if not base.get("commitSha") or base.get("commitSha") != candidate.get("commitSha"):
            reasons.append("Both sides of every pair must test the same commit.")
        for field in ("testIdentityDigest", "passed", "skipped", "runnerLabel"):
            if field not in base or base.get(field) != candidate.get(field):
                reasons.append(f"Paired {field} evidence is absent or differs.")
        for item in (base, candidate):
            if not item.get("testIdentityDigest") or not item.get("runnerLabel") or item.get("passed", 0) <= 0:
                reasons.append("Nonempty discovery and runner evidence are required.")
            if item.get("conclusion") != "success" or item.get("failed", 1) != 0:
                reasons.append("Every paired run must succeed without test failures.")
            if item.get("resourceFailure", True):
                reasons.append("A pair has missing resource evidence, a hang, or memory exhaustion.")
        if any(not math.isfinite(item.get("testSeconds", 0)) or item.get("testSeconds", 0) <= 0 for item in (base, candidate)):
            reasons.append("Positive measured test times are required.")
        else:
            ratios.append(candidate["testSeconds"] / base["testSeconds"])
    improvement = 1 - median(ratios) if ratios else 0
    if improvement < 0.15:
        reasons.append("Median paired test-time reduction is below 15%.")
    return {"schemaVersion": 1, "eligibleForAdoption": not reasons,
            "medianPairedImprovement": improvement, "reasons": sorted(set(reasons))}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--paired-benchmark", action="store_true")
    args = parser.parse_args()
    data = json.loads(args.input.read_text(encoding="utf-8"))
    result = evaluate_pairs(data["pairs"]) if args.paired_benchmark else summarize(data)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result, indent=2))
    return 0 if not args.paired_benchmark or result["eligibleForAdoption"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
