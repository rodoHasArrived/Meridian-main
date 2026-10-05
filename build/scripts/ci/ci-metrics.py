#!/usr/bin/env python3
"""Report Actions latency and runner use; compare only explicit, matched rollout cohorts."""
from __future__ import annotations

import argparse
from collections import Counter, defaultdict
from datetime import datetime, timedelta, timezone
import gzip
import hashlib
import json
import math
from pathlib import Path
from statistics import median


METRICS = ("allRequiredChecksSeconds", "qualityGateLatencySeconds",
           "qualityGateExecutionSeconds", "qualityGateElapsedSeconds", "qualityGateJobExecutionSeconds",
           "queueSeconds", "runnerMinutes")
FAILURES = {"failure", "timed_out", "startup_failure", "action_required"}


def timestamp(value: str | None) -> datetime | None:
    try:
        result = datetime.fromisoformat(value.replace("Z", "+00:00")) if value else None
        return result.astimezone(timezone.utc) if result and result.tzinfo else None
    except (TypeError, ValueError, AttributeError):
        return None


def seconds(start: str | None, end: str | None) -> float | None:
    first, last = timestamp(start), timestamp(end)
    if first is None or last is None or last < first:
        return None
    return (last - first).total_seconds()


def distribution(values: list) -> dict:
    observed = sorted(v for v in values if v is not None and math.isfinite(v))
    return {"samples": len(observed), "missing": len(values) - len(observed),
            "median": median(observed) if observed else None,
            "p95": observed[math.ceil(.95 * len(observed)) - 1] if observed else None}


def total(values: list) -> float | None:
    return sum(values) if values and all(v is not None for v in values) else None



def active_execution_seconds(jobs: list[dict], completed_at: str | None) -> float | None:
    """Union occupied intervals: parallel jobs once; queue/retry idle gaps excluded."""
    end = timestamp(completed_at)
    if end is None:
        return None
    intervals = []
    for job in jobs:
        if job.get("conclusion") == "skipped":
            continue
        start, finish = timestamp(job.get("started_at")), timestamp(job.get("completed_at"))
        if start is None:
            return None
        if start >= end:
            continue
        if finish is None or finish < start:
            return None
        intervals.append((start, min(finish, end)))
    if not intervals:
        return None
    intervals.sort()
    first, last = intervals[0]
    elapsed = 0.0
    for start, finish in intervals[1:]:
        if start > last:
            elapsed += (last - first).total_seconds()
            first, last = start, finish
        else:
            last = max(last, finish)
    return elapsed + (last - first).total_seconds()

def job_record(job: dict) -> dict:
    execution = seconds(job.get("started_at"), job.get("completed_at"))
    # Explicitly skipped jobs never occupied a runner; other missing times stay unknown.
    if job.get("conclusion") == "skipped":
        execution = 0.0
    return {"id": job.get("id"), "name": job["name"], "conclusion": job.get("conclusion"),
            "queueSeconds": seconds(job.get("created_at"), job.get("started_at")),
            "executionSeconds": execution, "labels": sorted(job.get("labels") or []),
            "created_at": job.get("created_at"), "started_at": job.get("started_at"),
            "completed_at": job.get("completed_at")}


def change_category(run: dict) -> str | None:
    change = run.get("changes", {})
    paths = change.get("paths")
    if change.get("complete") is not True or not isinstance(paths, list):
        return None
    if not paths:
        return "no-file-change / 0 files"
    areas = set()
    for path in paths:
        if path.startswith((".github/", "build/", "scripts/", "tests/scripts/")):
            areas.add("ci-tooling")
        elif path.startswith(("src/Meridian.Ui/dashboard/", "src/Meridian.Ui/wwwroot/")):
            areas.add("browser")
        elif path.startswith(("src/Meridian.Wpf/", "tests/Meridian.Wpf")):
            areas.add("desktop")
        elif path.startswith(("src/", "tests/")) or path.endswith((".sln", ".props", ".targets")):
            areas.add("dotnet")
        elif path.startswith(("docs/", ".codex/", ".claude/", ".agents/")) or path.endswith(".md"):
            areas.add("docs")
        else:
            areas.add("other")
    size = "1-5" if len(paths) <= 5 else "6-20" if len(paths) <= 20 else "21-100" if len(paths) <= 100 else "101+"
    return "+".join(sorted(areas)) + f" / {size} files"


def event_name(run: dict) -> str:
    if run["event"] == "push":
        return "main_push" if run.get("head_branch") == "main" else "other_push"
    return run["event"]


def workflow_name(run: dict) -> str:
    return run.get("path", run["name"]).split("@", 1)[0]


def check_matches(check: dict, run: dict, job: dict) -> bool:
    app_id = check.get("appId")
    app_matches = app_id in (None, -1) or job.get("check_app_id", job.get("check_run_app_id")) == app_id
    return (app_matches and job["name"] == check["name"] and
            (not check.get("workflow") or check["workflow"] in (run["name"], workflow_name(run))))


def checks_from(value: list) -> list[dict]:
    return [dict(name=item) if isinstance(item, str) else item for item in value]


def attempt_summary(run: dict) -> dict:
    jobs = [job_record(j) for j in run.get("jobs", [])]
    runner = total([j["executionSeconds"] for j in jobs])
    if run.get("collectionComplete") is False:
        runner = None
    return {"runId": run["id"], "attempt": run.get("run_attempt", 1),
            "workflow": run["name"], "event": event_name(run), "commitSha": run["head_sha"],
            "conclusion": run.get("conclusion"), "status": run.get("status"), "jobs": jobs,
            "queueSeconds": total([j["queueSeconds"] for j in jobs]),
            "knownRunnerSeconds": sum(j["executionSeconds"] for j in jobs if j["executionSeconds"] is not None),
            "knownRunnerMinutes": sum(j["executionSeconds"] for j in jobs if j["executionSeconds"] is not None) / 60,
            "runnerSeconds": runner, "runnerMinutes": runner / 60 if runner is not None else None}


def waste_summary(attempts: list[dict]) -> dict:
    def bucket(selected):
        values = [a["runnerMinutes"] for a in selected]
        return {"attempts": len(values), "runnerMinutes": total(values) if values else 0.0,
                "knownRunnerMinutes": sum(a["knownRunnerMinutes"] for a in selected),
                "unknownAttempts": sum(v is None for v in values)}
    failures = [a for a in attempts if a["conclusion"] in FAILURES]
    cancellations = [a for a in attempts if a["conclusion"] == "cancelled"]
    retries = [a for a in attempts if a["attempt"] > 1]
    unsuccessful = [a for a in attempts if a["conclusion"] != "success"]
    # Failure and cancellation are mutually exclusive; retry is an overlapping dimension.
    return {"total": bucket(attempts), "failures": bucket(failures),
            "cancellations": bucket(cancellations), "retries": bucket(retries),
            "unsuccessful": bucket(unsuccessful),
            "incomplete": bucket([a for a in attempts if a["status"] != "completed"]),
            "unsuccessfulAttempts": [{k: a[k] for k in ("runId", "attempt", "workflow", "conclusion", "status", "runnerMinutes")}
                                     for a in unsuccessful],
            "failureCancellationOrRetry": bucket([a for a in attempts if a in failures or a in cancellations or a["attempt"] > 1]),
            "note": "Failure/cancellation minutes include every job in attempts with that outcome. Retry minutes (attempt > 1) overlap those buckets; use their union, never add them. Unsuccessful also includes incomplete/other outcomes. Missing duration totals are null; known minutes are a lower bound."}


def change_summary(key: str, runs: list[dict], required: list[dict], quality_gate: dict) -> dict:
    reasons = []
    starts = [r.get("created_at") for r in runs]
    origin = min(starts, key=lambda v: timestamp(v) or datetime.max.replace(tzinfo=timezone.utc)) if starts else None
    if not all(timestamp(s) for s in starts):
        origin = None
        reasons.append("Missing workflow creation time.")
    categories = {change_category(r) for r in runs}
    category = next(iter(categories)) if len(categories) == 1 and None not in categories else None
    if category is None:
        reasons.append("Exact change category is unavailable or inconsistent across workflows.")
    scopes = {(r.get("changes", {}).get("baseSha"), r.get("changes", {}).get("headSha"),
               tuple(sorted(r.get("changes", {}).get("paths") or []))) for r in runs}
    if len(scopes) != 1 or any(r.get("changes", {}).get("headSha") not in (None, r["head_sha"]) for r in runs):
        reasons.append("Exact base/head/path snapshots differ across workflows for this change.")
    if not runs[0].get("change_key"):
        reasons.append("Explicit change identity is unavailable.")
    # Keep all attempts for cost, but resolve the latest job result in each workflow run.
    run_groups = defaultdict(list)
    for run in runs:
        run_groups[run["id"]].append(run)
    latest_runs = {}
    effective = {}
    ambiguous_checks = set()
    for run_id, attempts in run_groups.items():
        attempts.sort(key=lambda r: r.get("run_attempt", 1))
        last = attempts[-1]
        expected = list(range(1, last.get("run_attempt", 1) + 1))
        if [r.get("run_attempt", 1) for r in attempts] != expected:
            reasons.append("One or more prior workflow attempts are missing.")
        if any(r.get("collectionComplete") is not True for r in attempts):
            reasons.append("Workflow/attempt/job collection is incomplete or unverified.")
        effective[run_id] = {}
        for attempt in attempts:
            names = [j["name"] for j in attempt.get("jobs", [])]
            if len(names) != len(set(names)):
                reasons.append("Duplicate job names cannot be resolved unambiguously.")
                ambiguous_checks.update((workflow_name(attempt), name) for name, count in Counter(names).items() if count > 1)
            for job in attempt.get("jobs", []):
                effective[run_id][job["name"]] = job
        workflow = workflow_name(last)
        if workflow in latest_runs:
            reasons.append("Multiple independent workflow runs exist for the same change.")
        previous = latest_runs.get(workflow)
        if previous is None or (timestamp(last.get("created_at")) or datetime.min.replace(tzinfo=timezone.utc)) > (timestamp(previous.get("created_at")) or datetime.min.replace(tzinfo=timezone.utc)):
            latest_runs[workflow] = last
    selected = [(r, j) for r in latest_runs.values() for j in effective[r["id"]].values()]
    required_jobs = []
    missing_checks = []
    for check in required:
        found = [(r, j) for r, j in selected if check_matches(check, r, j)]
        if len(found) != 1 or any((workflow_name(r), j["name"]) in ambiguous_checks for r, j in found):
            missing_checks.append(check["name"])
        else:
            required_jobs.extend(found)
    if not required or missing_checks:
        reasons.append("Required check set is missing, absent or ambiguous.")
    check_success = bool(required) and not missing_checks and all(j.get("conclusion") == "success" for _, j in required_jobs)
    completed = all(r.get("status") == "completed" for r in latest_runs.values())
    successful = completed and check_success and all(r.get("conclusion") == "success" for r in latest_runs.values())
    if not successful:
        reasons.append("All relevant workflows and required checks must complete successfully.")
    endpoints = [j.get("completed_at") for _, j in required_jobs]
    end = max(endpoints, key=lambda v: timestamp(v) or datetime.min.replace(tzinfo=timezone.utc)) if endpoints and all(timestamp(v) for v in endpoints) else None
    check_terminal = bool(required) and not missing_checks and all(j.get("conclusion") is not None for _, j in required_jobs)
    all_required = seconds(origin, end) if check_terminal else None
    gates = [(r, j) for r, j in selected if check_matches(quality_gate, r, j)]
    gate_latency = gate_execution = gate_elapsed = gate_job = None
    if len(gates) == 1 and gates[0][1].get("conclusion") == "success":
        gate_run, gate = gates[0]
        gate_latency = seconds(origin, gate.get("completed_at"))
        gate_job = seconds(gate.get("started_at"), gate.get("completed_at"))
        gate_jobs = [j for a in run_groups[gate_run["id"]] for j in a.get("jobs", [])]
        execution_jobs = quality_gate.get("executionJobs")
        missing_execution_jobs = False
        if execution_jobs is not None:
            missing_execution_jobs = not execution_jobs or bool(set(execution_jobs) - {j["name"] for j in gate_jobs})
            gate_jobs = [j for j in gate_jobs if j["name"] in execution_jobs]
        job_starts = [j.get("started_at") for j in gate_jobs if j.get("conclusion") != "skipped"]
        first = min(job_starts, key=lambda v: timestamp(v)) if job_starts and all(timestamp(v) for v in job_starts) else None
        gate_elapsed = seconds(first, gate.get("completed_at"))
        if missing_execution_jobs:
            reasons.append("One or more configured quality-gate execution jobs are absent.")
        else:
            gate_execution = active_execution_seconds(gate_jobs, gate.get("completed_at"))
    attempts = [attempt_summary(r) for r in runs]
    runner = total([a["runnerMinutes"] for a in attempts])
    queues = [j["queueSeconds"] for a in attempts for j in a["jobs"] if j["conclusion"] != "skipped"]
    labels = [tuple(j.get("labels") or []) for r in runs for j in r.get("jobs", []) if j.get("conclusion") != "skipped"]
    runner_signature = sorted(set(labels)) if labels and all(labels) else None
    if runner_signature is None:
        reasons.append("Runner labels are unavailable.")
    check_signature = sorted((workflow_name(r), j["name"]) for r, j in required_jobs)
    attempt_signature = sorted((workflow_name(r), r.get("run_attempt", 1)) for r in latest_runs.values())
    # Required workflow internals can shed duplicate jobs. Unrelated specialist
    # workflows must retain their executed job roster; fewer validators are not savings.
    required_workflows = {workflow_name(r) for r, _ in required_jobs}
    optional_signature = sorted((workflow_name(r), sorted(j["name"] for j in effective[r["id"]].values()
                                                         if j.get("conclusion") != "skipped"))
                                for r in latest_runs.values() if workflow_name(r) not in required_workflows)
    if any(v is None for v in (runner, all_required, gate_execution)):
        reasons.append("Required latency, execution or runner duration is unavailable.")
    return {"changeKey": key, "event": event_name(runs[0]), "commitSha": runs[0]["head_sha"],
            "createdAt": origin, "category": category, "completed": completed,
            "successful": successful, "missingRequiredChecks": missing_checks,
            "comparable": not reasons, "exclusionReasons": sorted(set(reasons)),
            "requiredCheckSignature": check_signature, "runnerSignature": runner_signature,
            "attemptSignature": attempt_signature, "optionalWorkflowSignature": optional_signature, "workflowAttempts": len(attempts),
            "allRequiredChecksSeconds": all_required, "qualityGateLatencySeconds": gate_latency,
            "qualityGateExecutionSeconds": gate_execution, "qualityGateElapsedSeconds": gate_elapsed, "qualityGateJobExecutionSeconds": gate_job,
            "queueSeconds": total(queues), "jobQueueSeconds": distribution(queues), "runnerMinutes": runner}


def comparison_policy(policy: dict | None) -> list[str]:
    if policy is None:
        return ["Explicit baseline and rollout windows and a reviewed required-check snapshot are required."]
    errors = []
    windows = []
    for name in ("baseline", "rollout"):
        period = policy.get(name, {})
        start, end = timestamp(period.get("start")), timestamp(period.get("end"))
        if not start or not end or start >= end:
            errors.append(f"{name} requires a valid start < end with timezone.")
        windows.append((start, end))
    if not errors and windows[0][1] > windows[1][0]:
        errors.append("Baseline must end at or before rollout starts.")
    if not policy.get("requiredChecks"):
        errors.append("A historically applicable required-check snapshot is required.")
    if not policy.get("requiredChecksEvidence"):
        errors.append("requiredChecksEvidence must identify the evidence for historical check equivalence.")
    return errors


def compare_changes(changes: list[dict], policy: dict | None, evidence_errors: list[str]) -> dict:
    reasons = comparison_policy(policy) + evidence_errors
    answer = {"minimumComparableCompletedRuns": 20, "policy": policy,
              "method": "Within each event, match category, required checks, runner labels, all workflow attempts and optional workflow/job roster. Chronological 1:1 matching without replacement; discard unmatched observations. Percentages use matched median execution and matched total runner minutes.",
              "reasons": sorted(set(reasons)), "events": {}}
    if policy is None or any("requires a valid" in reason or "Baseline must end" in reason for reason in reasons):
        return answer
    for event in ("pull_request", "main_push"):
        eligible = [c for c in changes if c["event"] == event and c["comparable"]]
        periods = {}
        for name in ("baseline", "rollout"):
            start, end = (timestamp(policy[name][k]) for k in ("start", "end"))
            periods[name] = [c for c in eligible if timestamp(c["createdAt"]) and start <= timestamp(c["createdAt"]) < end]
        strata = defaultdict(lambda: {"baseline": [], "rollout": []})
        for period, entries in periods.items():
            for change in entries:
                signature = json.dumps([change[k] for k in ("category", "requiredCheckSignature", "runnerSignature", "attemptSignature", "optionalWorkflowSignature")], sort_keys=True)
                strata[signature][period].append(change)
        pairs = []
        cohorts = []
        for signature, values in sorted(strata.items()):
            before, after = [sorted(values[p], key=lambda c: (c["createdAt"], c["changeKey"])) for p in ("baseline", "rollout")]
            matched = list(zip(before, after))
            pairs.extend(matched)
            cohorts.append({"cohortId": hashlib.sha256(signature.encode()).hexdigest()[:12],
                            "signature": json.loads(signature), "baseline": len(before), "rollout": len(after), "matched": len(matched)})
        enough = len(pairs) >= 20 and not reasons
        before, after = ([p[index] for p in pairs] for index in (0, 1))
        baseline_stats = {m: distribution([c[m] for c in before]) for m in METRICS}
        rollout_stats = {m: distribution([c[m] for c in after]) for m in METRICS}
        base_runner, post_runner = total([c["runnerMinutes"] for c in before]), total([c["runnerMinutes"] for c in after])
        base_exec = baseline_stats["qualityGateExecutionSeconds"]["median"]
        post_exec = rollout_stats["qualityGateExecutionSeconds"]["median"]
        execution_reduction = 1 - post_exec / base_exec if enough and base_exec and post_exec is not None else None
        runner_reduction = 1 - post_runner / base_runner if enough and base_runner and post_runner is not None else None
        event_reasons = list(reasons)
        if len(pairs) < 20:
            event_reasons.append("At least 20 matched completed successful rollout changes are required for this event.")
        answer["events"][event] = {
            "baselineComparableCompleted": len(periods["baseline"]), "rolloutComparableCompleted": len(periods["rollout"]),
            "rolloutSampleRequirementMet": len(periods["rollout"]) >= 20,
            "matchedCompletedRuns": len(pairs), "unmatchedBaseline": len(periods["baseline"]) - len(pairs),
            "unmatchedRollout": len(periods["rollout"]) - len(pairs), "cohorts": cohorts,
            "pairs": [{"baseline": b["changeKey"], "rollout": a["changeKey"]} for b, a in pairs],
            "baseline": baseline_stats, "rollout": rollout_stats,
            "baselineObservationMetrics": {m: distribution([c[m] for c in periods["baseline"]]) for m in METRICS},
            "rolloutObservationMetrics": {m: distribution([c[m] for c in periods["rollout"]]) for m in METRICS},
            "matchedBaselineRunnerMinutes": base_runner, "matchedRolloutRunnerMinutes": post_runner,
            "qualityGateExecutionTarget": {"requiredReduction": .25, "observedReduction": execution_reduction,
                                           "met": execution_reduction >= .25 if execution_reduction is not None else None},
            "runnerMinuteTarget": {"requiredReduction": .30, "observedReduction": runner_reduction,
                                   "met": runner_reduction >= .30 if runner_reduction is not None else None},
            "eligibleForTargetEvaluation": enough, "reasons": event_reasons}
    return answer


def summarize(payload: dict, policy: dict | None = None) -> dict:
    collection = payload.get("collection", {})
    required = checks_from((policy or {}).get("requiredChecks") or collection.get("requiredChecks") or payload.get("requiredChecks") or [])
    quality_gate = (policy or {}).get("qualityGate", {"name": "quality-gate", "workflow": "Meridian CI"})
    required_set_complete = bool((policy or {}).get("requiredChecks")) or collection.get("requiredChecksComplete") is True
    metric_required = required if required_set_complete else []
    groups, changes, events = defaultdict(list), defaultdict(list), defaultdict(list)
    seen, jobs_seen, errors = {}, set(), []
    for original in sorted(payload["workflow_runs"], key=lambda r: (r["id"], r.get("run_attempt", 1))):
        identity = (original["id"], original.get("run_attempt", 1))
        if identity in seen:
            if seen[identity] != original:
                errors.append("Conflicting duplicate workflow attempt records.")
            continue
        seen[identity] = original
        run = dict(original)
        run["jobs"] = []
        for job in original.get("jobs", []):
            job_id = job.get("id")
            # Attempt endpoints can carry inherited jobs. Count a real job ID once.
            if job_id is not None and job_id in jobs_seen:
                continue
            if job_id is not None:
                jobs_seen.add(job_id)
            run["jobs"].append(job)
        attempt = attempt_summary(run)
        group = f"{run['name']} / {run['event']} / attempt {run.get('run_attempt', 1)}"
        groups[group].append(attempt)
        events[event_name(run)].append(attempt)
        change_key = run.get("change_key") or f"{event_name(run)}:{run['head_sha']}"
        changes[(event_name(run), change_key)].append(run)
    result_groups = {}
    for key, attempts in groups.items():
        values = [a["runnerSeconds"] for a in attempts if a["conclusion"] == "success" and a["runnerSeconds"] is not None]
        result_groups[key] = {"runs": attempts, "successfulSamples": len(values), "medianRunnerSeconds": median(values) if values else None}
    observations = [change_summary(key, runs, metric_required, quality_gate) for (_, key), runs in sorted(changes.items())]
    event_reports = {}
    for event, attempts in sorted(events.items()):
        members = [c for c in observations if c["event"] == event]
        success = [c for c in members if c["successful"]]
        event_reports[event] = {"changes": len(members), "completedChanges": sum(c["completed"] for c in members),
                                "successfulChanges": len(success), "comparableCompletedChanges": sum(c["comparable"] for c in members),
                                "metrics": {m: distribution([c[m] for c in success]) for m in METRICS},
                                "terminalRequiredCheckLatencySeconds": distribution([c["allRequiredChecksSeconds"] for c in members if c["completed"]]),
                                "jobQueueSeconds": distribution([j["queueSeconds"] for a in attempts for j in a["jobs"] if j["conclusion"] != "skipped"]),
                                "categories": dict(Counter(c["category"] or "unavailable" for c in members)),
                                "runnerAccounting": waste_summary(attempts)}
    if policy:
        windows = payload.get("sourceCollectionWindows") or [payload.get("window", {})]
        intervals = []
        for window in windows:
            first = timestamp(window.get("since", collection.get("windowStart")))
            last = timestamp(window.get("until", collection.get("windowEnd")))
            if last and window.get("untilInclusive") is True:
                last += timedelta(seconds=1)
            intervals.append((first, last))
        collected_at = timestamp(payload.get("collectedAt"))
        for period in ("baseline", "rollout"):
            requested = policy.get(period, {})
            start, end = timestamp(requested.get("start")), timestamp(requested.get("end"))
            covered_until = start
            for first, last in sorted((a, b) for a, b in intervals if a and b):
                if covered_until and first <= covered_until < last:
                    covered_until = last
            if not start or not end or covered_until < end:
                errors.append(f"Collected window does not fully cover the requested {period} window.")
            if not collected_at or not end or collected_at < end:
                errors.append(f"Requested {period} period had not elapsed at the verified collection time.")
    if any(check.get("appId") not in (None, -1) for check in required) and collection.get("checkDataComplete") is not True:
        errors.append("App-bound required-check evidence collection is incomplete or unverified.")
    if collection.get("runDataComplete", collection.get("complete")) is not True:
        errors.append("Workflow/attempt/job collection completeness is unverified or false.")
    return {"schemaVersion": 2, "source": {k: payload.get(k) for k in ("repository", "collectedAt", "window", "sourceCollectionWindows", "collection")},
            "definitions": {
                "allRequiredChecksSeconds": "Earliest relevant workflow creation to final terminal required-check completion across workflows, including queues and retries. Event metrics use successful changes; terminalRequiredCheckLatencySeconds additionally includes unsuccessful changes. Actions creation is the observable trigger proxy, not PR opening time.",
                "qualityGateLatencySeconds": "Same origin to quality-gate completion.",
                "qualityGateExecutionSeconds": "Union of occupied execution intervals for qualityGate.executionJobs up to gate completion across attempts; without a configured roster, all jobs in its workflow provide a proxy. Parallel intervals count once; initial/dependency queues and idle retry gaps are excluded. This is the 25% execution target.",
                "qualityGateElapsedSeconds": "First executing job in quality-gate workflow to gate completion, including dependency queues and idle retry gaps.",
                "qualityGateJobExecutionSeconds": "Aggregator job started_at to completed_at; not the 25% pipeline-execution target.",
                "queueSeconds": "Sum of observed created_at-to-started_at job waits; includes dependency waiting, not a pure runner-scheduler measure. Null when any timestamp is missing.",
                "runnerMinutes": "Sum of job execution minutes across every relevant workflow and attempt for a change; explicit skipped jobs consumed zero execution, even when GitHub supplies synthetic timestamps. Other missing/reversed timestamps remain unknown. No billing multiplier, rounding or cache inference.",
                "p95": "Nearest-rank percentile, ceil(0.95 * n); missing evidence excluded and counted.",
                "comparison": "Observational matched successful-change cohorts, not certified billing savings or causal proof. Prior unsuccessful attempts on matched changes are included; unsuccessful-only changes are reported separately, outside target estimates. Optional specialist roster changes require reviewed coverage equivalence and currently remain unmatched. Separate PR and main-push targets; benchmark promotion still requires five reliable pairs and 15% improvement."},
            "requiredChecks": required, "requiredCheckSetComplete": required_set_complete,
            "requiredChecksSource": "explicit policy" if (policy or {}).get("requiredChecks") else "current collector snapshot",
            "qualityGate": quality_gate, "groups": result_groups, "events": event_reports, "changes": observations,
            "comparison": compare_changes(observations, policy, errors)}


def evaluate_pairs(pairs: list[dict]) -> dict:
    """A tuning candidate is advisory until five distinct, comparable pairs pass."""
    reasons = []
    if len(pairs) < 5 or any(not p.get("pairId") for p in pairs) or len({p.get("pairId") for p in pairs}) != len(pairs):
        reasons.append("Five distinct paired runs are required.")
    ratios = []
    local_evidence = False
    for pair in pairs:
        base, candidate = pair.get("baseline") or {}, pair.get("candidate") or {}
        if not base or not candidate:
            reasons.append("Both baseline and candidate measurements are required for every pair.")
        if not base.get("commitSha") or base.get("commitSha") != candidate.get("commitSha"):
            reasons.append("Both sides of every pair must test the same commit.")
        if base.get("executionEnvironment") != candidate.get("executionEnvironment"):
            reasons.append("Both sides of every pair must use the same execution environment.")
        for field in ("testIdentityDigest", "passed", "skipped", "runnerLabel"):
            if field not in base or base.get(field) != candidate.get(field):
                reasons.append(f"Paired {field} evidence is absent or differs.")
        for item in (base, candidate):
            if not item.get("testIdentityDigest") or not item.get("runnerLabel") or not isinstance(item.get("passed"), int) or item["passed"] <= 0:
                reasons.append("Nonempty discovery and runner evidence are required.")
            if any(not isinstance(item.get(field), int) or isinstance(item[field], bool) or item[field] < 0 for field in ("passed", "failed", "skipped")):
                reasons.append("Nonnegative passed, failed and skipped counts are required.")
            if item.get("conclusion") != "success" or item.get("failed", 1) != 0:
                reasons.append("Every paired run must succeed without test failures.")
            if item.get("executionEnvironment") == "local":
                local_evidence = True
            if item.get("resourceFailure") is not False:
                reasons.append("A pair has missing resource evidence, a hang, or memory exhaustion.")
        if any(not isinstance(item.get("testSeconds"), (int, float)) or isinstance(item["testSeconds"], bool) or not math.isfinite(item["testSeconds"]) or item["testSeconds"] <= 0 for item in (base, candidate)):
            reasons.append("Positive measured test times are required.")
        else:
            ratios.append(candidate["testSeconds"] / base["testSeconds"])
    improvement = 1 - median(ratios) if ratios and not reasons else None
    if improvement is None:
        reasons.append("A valid median paired improvement is unavailable.")
    elif improvement < 0.15:
        reasons.append("Median paired test-time reduction is below 15%.")
    # A valid local timing comparison is still informative. Its measured ratio
    # cannot establish that a hosted runner will improve by the same amount.
    if local_evidence:
        reasons.append("Local benchmark evidence cannot promote hosted defaults.")
    return {"schemaVersion": 1, "eligibleForAdoption": not reasons,
            "medianPairedImprovement": improvement, "reasons": sorted(set(reasons))}



def render_markdown(report: dict) -> str:
    def display(value):
        return "unavailable" if value is None else f"{value:.2f}"
    lines = ["# Actions performance evidence", "", "Observational measurements; unmatched samples do not establish savings.", ""]
    source = report.get("source", {})
    lines += [f"Repository: {source.get('repository') or 'unavailable'}. Collected: {source.get('collectedAt') or 'unavailable'}.", ""]
    policy_basis = (report["comparison"].get("policy") or {}).get("policyBasis")
    if policy_basis:
        lines += [policy_basis, ""]
    for event, summary in report["events"].items():
        lines += [f"## {event}", "", f"{summary['completedChanges']} completed changes; {summary['successfulChanges']} successful; {summary['comparableCompletedChanges']} comparable.", "",
                  "| Metric | Samples | Missing | Median | p95 |", "| --- | ---: | ---: | ---: | ---: |"]
        for name, data in summary["metrics"].items():
            lines.append(f"| {name} | {data['samples']} | {data['missing']} | {display(data['median'])} | {display(data['p95'])} |")
        terminal = summary["terminalRequiredCheckLatencySeconds"]
        queues = summary["jobQueueSeconds"]
        lines += ["", "The table uses successful completed changes.", "",
                  f"All terminal required checks, including unsuccessful changes: {terminal['samples']} samples; median {display(terminal['median'])} seconds; p95 {display(terminal['p95'])} seconds.",
                  f"Individual job queue/dependency waits: {queues['samples']} observed, {queues['missing']} unavailable; median {display(queues['median'])} seconds; p95 {display(queues['p95'])} seconds."]
        lines += ["", "| Runner use | Attempts | Minutes | Known minutes (lower bound) | Unknown attempts |", "| --- | ---: | ---: | ---: | ---: |"]
        for name in ("total", "failures", "cancellations", "retries", "unsuccessful", "incomplete", "failureCancellationOrRetry"):
            item = summary["runnerAccounting"][name]
            lines.append(f"| {name} | {item['attempts']} | {display(item['runnerMinutes'])} | {display(item['knownRunnerMinutes'])} | {item['unknownAttempts']} |")
        lines += ["", summary["runnerAccounting"]["note"], "", "Change categories: " + json.dumps(summary["categories"], sort_keys=True), ""]
    lines += ["## Rollout target evaluation", ""]
    policy = report["comparison"].get("policy") or {}
    for period in ("baseline", "rollout"):
        window = policy.get(period, {})
        lines += [f"{period.capitalize()} window (start inclusive, end exclusive): {window.get('start', 'unavailable')} to {window.get('end', 'unavailable')}.", ""]
    for reason in report["comparison"]["reasons"]:
        lines.append(f"- {reason}")
    for event, result in report["comparison"]["events"].items():
        lines += ["", f"### {event}", "", f"{result['rolloutComparableCompleted']} comparable completed rollout changes; {result['matchedCompletedRuns']} matched baseline/rollout pairs; {result['unmatchedRollout']} unmatched rollout changes.", ""]
        for key, label in (("qualityGateExecutionTarget", "25% execution reduction"), ("runnerMinuteTarget", "30% runner-minute reduction")):
            item = result[key]
            reduction = item["observedReduction"]
            observed = "unavailable" if reduction is None else f"{reduction:.1%} observed; target met: {item['met']}"
            lines.append(f"- {label}: {observed}.")
        lines += [f"- {reason}" for reason in result["reasons"]]
        lines += ["", "Rollout observations, including unmatched samples; these describe the rollout and do not establish a reduction.", "",
                  "| Observed rollout metric | Samples | Median | p95 |", "| --- | ---: | ---: | ---: |"]
        for metric, data in result["rolloutObservationMetrics"].items():
            lines.append(f"| {metric} | {data['samples']} | {display(data['median'])} | {display(data['p95'])} |")
        lines += ["", "| Matched metric | Baseline median | Baseline p95 | Rollout median | Rollout p95 |",
                  "| --- | ---: | ---: | ---: | ---: |"]
        for metric in METRICS:
            before, after = result["baseline"][metric], result["rollout"][metric]
            lines.append(f"| {metric} | {display(before['median'])} | {display(before['p95'])} | {display(after['median'])} | {display(after['p95'])} |")
        lines += ["", f"Matched total runner minutes: baseline {display(result['matchedBaselineRunnerMinutes'])}; rollout {display(result['matchedRolloutRunnerMinutes'])}.", "",
                  "| Cohort | Category | Baseline | Rollout | Matched |", "| --- | --- | ---: | ---: | ---: |"]
        for cohort in result["cohorts"]:
            lines.append(f"| {cohort['cohortId']} | {cohort['signature'][0]} | {cohort['baseline']} | {cohort['rollout']} | {cohort['matched']} |")
    lines += ["", "## Definitions", ""]
    lines += [f"- **{key}**: {value}" for key, value in report["definitions"].items()]
    return "\n".join(lines) + "\n"


def read_json(path: Path) -> dict:
    """Read a plain export or its deterministic gzip archival representation."""
    opener = gzip.open if path.suffix == ".gz" else open
    with opener(path, "rt", encoding="utf-8") as handle:
        return json.load(handle)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--paired-benchmark", action="store_true")
    parser.add_argument("--comparison", type=Path, help="JSON windows and historically reviewed required checks")
    parser.add_argument("--markdown", type=Path, help="Optional readable Actions report")
    args = parser.parse_args()
    data = read_json(args.input)
    policy = read_json(args.comparison) if args.comparison else None
    result = evaluate_pairs(data["pairs"]) if args.paired_benchmark else summarize(data, policy)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    if args.markdown and not args.paired_benchmark:
        args.markdown.parent.mkdir(parents=True, exist_ok=True)
        args.markdown.write_text(render_markdown(result), encoding="utf-8")
    print(json.dumps(result, indent=2, allow_nan=False))
    return 0 if not args.paired_benchmark or result["eligibleForAdoption"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
