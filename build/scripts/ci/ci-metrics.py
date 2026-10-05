#!/usr/bin/env python3
"""Summarize exported Actions jobs, keeping queues and retries out of execution time."""
from __future__ import annotations

import argparse
from datetime import datetime
import json
import math
from pathlib import Path
from statistics import median


def timestamp(value: str | None) -> datetime | None:
    """Only timezone-qualified, usable API timestamps constitute timing evidence."""
    if not isinstance(value, str) or not value:
        return None
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        return None
    return parsed if parsed.tzinfo is not None and parsed.year > 1 else None


def seconds(start: str | None, end: str | None) -> float | None:
    first, last = timestamp(start), timestamp(end)
    if first is None or last is None or last < first:
        return None
    return (last - first).total_seconds()


def measured_sum(values) -> float | None:
    measured = [value for value in values if value is not None]
    return sum(measured) if measured else None


def job_record(job: dict, attempt: int, observed_at: str | None) -> dict:
    """Separate runner execution from synthetic starts and carried-forward results."""
    job_attempt = job.get("run_attempt", attempt)
    if job_attempt != attempt:
        raise ValueError("Job run_attempt differs from its containing run; export attempt-specific jobs.")
    created, started, completed = (job.get(key) for key in
                                   ("created_at", "started_at", "completed_at"))
    status, conclusion = job.get("status"), job.get("conclusion")
    queued = status in {"queued", "waiting", "pending", "requested"}
    step_started = any(
        step.get("conclusion") != "skipped" and
        (timestamp(step.get("started_at")) is not None or step.get("status") == "in_progress"
         or step.get("conclusion") in {"success", "failure", "timed_out"})
        for step in job.get("steps", [])
    )
    assigned = bool(job.get("runner_id") or job.get("runner_name"))
    unassigned = ("runner_id" in job and job["runner_id"] in (None, 0)
                  and not assigned and not step_started)
    valid_start = timestamp(started)
    if queued or conclusion == "skipped" or unassigned:
        state = "not_started"
    elif valid_start is not None:
        state = "started"
    else:
        # Missing start metadata alone cannot prove that a terminal job never ran.
        state = "unknown"

    issue = None
    if state == "started" and timestamp(created) is not None and valid_start < timestamp(created):
        # Partial reruns can copy successful jobs with NEW creation times/job IDs but
        # OLD execution timestamps. Their duration is not execution in this attempt.
        issue = "started_before_created"
    elif started and valid_start is None and state != "not_started":
        issue = "invalid_started_at"

    queue = seconds(created, started) if state == "started" and not issue else None
    execution = seconds(started, completed) if state == "started" and not issue else None
    wait, wait_end = None, None
    if state == "started" and not issue:
        wait, wait_end = queue, "started"
    elif state == "not_started":
        terminal = status == "completed" or conclusion is not None
        if terminal:
            wait, wait_end = seconds(created, completed), "completed"
        elif queued:
            wait, wait_end = seconds(created, observed_at), "observed"
    if wait is None:
        wait_end = None

    return {
        "jobId": job.get("id"), "name": job["name"], "status": status,
        "conclusion": conclusion, "runAttempt": attempt,
        "runnerId": job.get("runner_id"), "runnerName": job.get("runner_name"),
        "runnerGroupName": job.get("runner_group_name"), "labels": job.get("labels"),
        "createdAt": created, "startedAt": started, "completedAt": completed,
        "startState": state, "timingIssue": issue,
        "queueSeconds": queue, "waitSeconds": wait, "waitEnd": wait_end,
        "executionSeconds": execution,
    }


def summarize_records(runs: list[dict]) -> dict:
    jobs = [job for run in runs for job in run["jobs"]]
    distributions = {}
    for metric, field in (("queue", "queueSeconds"), ("execution", "executionSeconds"),
                          ("wait", "waitSeconds")):
        values = [job[field] for job in jobs if job[field] is not None]
        distributions[f"{metric}Samples"] = len(values)
        distributions[f"median{metric.capitalize()}Seconds"] = median(values) if values else None
    return {
        **distributions,
        "runAttempts": len(runs), "uniqueRuns": len({run["runId"] for run in runs}),
        "retryAttempts": sum(run["isRetry"] for run in runs),
        "cancelledRunAttempts": sum(run["conclusion"] == "cancelled" for run in runs),
        "jobs": len(jobs),
        "notStartedJobs": sum(job["startState"] == "not_started" for job in jobs),
        "cancelledJobs": sum(job["conclusion"] == "cancelled" for job in jobs),
        "cancelledBeforeStartJobs": sum(job["conclusion"] == "cancelled" and
                                        job["startState"] == "not_started" for job in jobs),
        "queuedJobs": sum(job["status"] in {"queued", "waiting", "pending", "requested"}
                          for job in jobs),
        "unavailableExecutionJobs": sum(job["executionSeconds"] is None for job in jobs),
        "knownRunnerSeconds": measured_sum(run["knownRunnerSeconds"] for run in runs),
        "cancelledRunKnownRunnerSeconds": measured_sum(
            run["knownRunnerSeconds"] for run in runs if run["conclusion"] == "cancelled"),
        "retryKnownRunnerSeconds": measured_sum(
            run["knownRunnerSeconds"] for run in runs if run["isRetry"]),
    }


def summarize(payload: dict) -> dict:
    """One row per run attempt, with all pages of attempt-specific embedded jobs.

    observed_at is an optional fixed export timestamp for still-queued jobs. The
    system clock is never substituted: rerunning an export gives the same result.
    """
    groups: dict[str, list[dict]] = {}
    history: dict[int, set[int]] = {}
    all_runs = []
    for run in payload["workflow_runs"]:
        attempt = run.get("run_attempt", 1)
        if not isinstance(attempt, int) or isinstance(attempt, bool) or attempt < 1:
            raise ValueError("run_attempt must be a positive integer.")
        attempts = history.setdefault(run["id"], set())
        if attempt in attempts:
            raise ValueError(f"Duplicate run attempt: {run['id']} / {attempt}")
        attempts.add(attempt)
        key = f"{run['name']} / {run['event']} / attempt {attempt}"
        jobs = run.get("jobs")
        if jobs is not None and not isinstance(jobs, list):
            raise ValueError("Embedded jobs must be a list containing every page for this attempt.")
        records = [job_record(job, attempt, payload.get("observed_at")) for job in jobs or []]
        complete = bool(records) and all(job["executionSeconds"] is not None for job in records)
        known = measured_sum(job["executionSeconds"] for job in records)
        record = {
            "runId": run["id"], "commitSha": run["head_sha"],
            "runAttempt": attempt, "isRetry": attempt > 1,
            "status": run.get("status"), "conclusion": run.get("conclusion"),
            "jobsAvailable": jobs is not None, "jobs": records,
            "runnerSeconds": known if complete else None, "knownRunnerSeconds": known,
        }
        groups.setdefault(key, []).append(record)
        all_runs.append(record)
    result = {}
    for name, runs in groups.items():
        eligible = [run["runnerSeconds"] for run in runs
                    if run["conclusion"] == "success" and run["runnerSeconds"] is not None
                    and all(job["conclusion"] == "success" for job in run["jobs"])]
        result[name] = {"runs": runs, "successfulSamples": len(eligible),
                        "medianRunnerSeconds": median(eligible) if eligible else None,
                        "summary": summarize_records(runs)}
    return {
        "schemaVersion": 2, "observedAt": payload.get("observed_at"),
        "groups": result, "summary": summarize_records(all_runs),
        "retryHistory": [
            {"runId": run_id, "observedAttempts": sorted(attempts),
             "missingAttempts": sorted(set(range(1, max(attempts) + 1)) - attempts)}
            for run_id, attempts in history.items()
        ],
    }


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
