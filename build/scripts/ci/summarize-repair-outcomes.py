#!/usr/bin/env python3
"""Summarize attempt-bound repair receipts; workflow success is not repair success."""
from __future__ import annotations

import argparse
from collections import Counter, defaultdict
import importlib.util
import io
import json
import os
from pathlib import Path
import re
import subprocess
import zipfile


WORKFLOW = ".github/workflows/generated-repair-publish.yml"
OUTCOMES = {"published", "noop", "ineligible", "review-required", "stale-retry",
            "configuration-missing", "failed", "ready", "refreshed", "generate",
            "trusted-generation-requested"}
FILES = {"publish": "generated-repair-result.json", "refresh": "generated-repair-refresh.json"}
MAX_BYTES = 2 * 1024 * 1024
SPEC = importlib.util.spec_from_file_location("repair_ci_metrics", Path(__file__).with_name("ci-metrics.py"))
METRICS = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(METRICS)


def sha(value) -> bool:
    return isinstance(value, str) and re.fullmatch(r"[0-9a-f]{40}", value) is not None


def request_identity(request: dict) -> tuple | None:
    if not isinstance(request, dict):
        return None
    number = request.get("pr_number")
    if (isinstance(number, int) and not isinstance(number, bool) and number > 0
            and sha(request.get("head_sha")) and sha(request.get("base_sha"))):
        return number, request["head_sha"], request["base_sha"]
    return None


def associated_pr(run: dict, number: int, commit: str) -> bool:
    if run.get("event") != "pull_request" or run.get("head_sha") != commit:
        return False
    change = run.get("changes") or {}
    if change.get("headSha") == commit and change.get("pullNumber") == number:
        return True
    return any(pull.get("number") == number and pull.get("head", {}).get("sha") == commit
               for pull in run.get("pull_requests", []))


def bound_check(snapshot: dict, run: dict, job: dict, number: int, commit: str) -> bool:
    """Require the collector's raw check and its immutable job/check link."""
    check_id = str(job.get("check_run_url", "")).rstrip("/").rsplit("/", 1)[-1]
    if not check_id.isdigit():
        return False
    candidates = list(snapshot.get("check_runs_by_sha", {}).get(commit, []))
    direct = snapshot.get("check_runs_by_id", {}).get(check_id)
    if direct:
        candidates.append(direct)
    matches = {json.dumps(check, sort_keys=True) for check in candidates if str(check.get("id")) == check_id}
    if len(matches) != 1:
        return False
    check = json.loads(matches.pop())
    allowed = {commit}
    merge = (run.get("changes") or {}).get("mergeEvidence") or {}
    if merge.get("headSha") == commit and merge.get("pullNumber") == number:
        allowed.update(value for value in merge.get("mergeShas", []) if sha(value))
    return (check.get("head_sha") in allowed and check.get("name") == job.get("name")
            and check.get("app", {}).get("id") == job.get("check_app_id")
            and check.get("status") == job.get("status")
            and check.get("conclusion") == job.get("conclusion"))


def revision_checks(snapshot: dict, number: int, commit: str, required: list[dict]) -> dict:
    """Evaluate exact PR revision checks with the CI metrics app/context rules."""
    reasons, checks = [], []
    collection = snapshot.get("collection", {})
    if collection.get("runDataComplete") is not True:
        reasons.append("Workflow/run/attempt inventory is incomplete.")
    if collection.get("checkDataComplete") is not True:
        reasons.append("Raw check evidence collection is incomplete.")
    if collection.get("requiredChecksComplete") is not True or not required:
        reasons.append("Required-check policy is missing, incomplete, or empty.")
    if any(not isinstance(check.get("appId"), int) or isinstance(check.get("appId"), bool)
           or check["appId"] <= 0 for check in required):
        reasons.append("Required-check producer App binding is missing or unrestricted.")
    runs = [run for run in snapshot.get("workflow_runs", []) if associated_pr(run, number, commit)]
    if not runs:
        reasons.append("No exact repaired-commit PR workflow evidence in the collected window.")
    groups = defaultdict(list)
    for run in runs:
        groups[run["id"]].append(run)
    selected, workflows = [], set()
    for run_id, attempts in groups.items():
        numbers = [run.get("run_attempt") for run in attempts]
        if (not all(isinstance(value, int) and not isinstance(value, bool) and value > 0 for value in numbers)
                or sorted(numbers) != list(range(1, max(numbers) + 1))):
            reasons.append("Prior attempts are missing or duplicated.")
            continue
        attempts.sort(key=lambda run: run["run_attempt"])
        last = attempts[-1]
        workflow = METRICS.workflow_name(last)
        if workflow in workflows:
            reasons.append("Multiple independent runs make required contexts ambiguous.")
        workflows.add(workflow)
        effective, prior = {}, []
        for run in attempts:
            if run.get("collectionComplete") is not True or not isinstance(run.get("jobs"), list):
                reasons.append("Attempt/job evidence is incomplete.")
                continue
            try:
                prepared = METRICS.prepare_jobs(run, prior, snapshot.get("collectedAt"))
            except (ValueError, KeyError, TypeError) as exc:
                reasons.append("Attempt inheritance is unverified: " + str(exc))
                continue
            names = [job["name"] for job in prepared]
            if len(names) != len(set(names)):
                reasons.append("Duplicate job contexts are ambiguous.")
            for job in prepared:
                if METRICS.job_record(job)["inheritedFromAttempt"] is None:
                    effective[job["name"]] = job
            prior.extend(prepared)
        selected.extend((last, job) for job in effective.values())
    for check in required:
        found = [(run, job) for run, job in selected if METRICS.check_matches(check, run, job)]
        if len(found) != 1:
            reasons.append(f"Required context is missing or ambiguous: {check['name']}.")
            continue
        run, job = found[0]
        if not bound_check(snapshot, run, job, number, commit):
            reasons.append(f"Raw check is not bound to this revision/job: {check['name']}.")
        if run.get("status") != "completed" or job.get("status") != "completed" or job.get("conclusion") is None:
            reasons.append(f"Required context is not terminal: {check['name']}.")
        checks.append({"name": check["name"], "appId": check.get("appId"), "runId": run["id"],
                       "attempt": run["run_attempt"], "jobId": job.get("id"),
                       "conclusion": job.get("conclusion"), "checkRunUrl": job.get("check_run_url")})
    result = "unavailable" if reasons else ("success" if all(check["conclusion"] == "success" for check in checks) else "failure")
    return {"result": result, "reasons": sorted(set(reasons)), "checks": checks}


def first_revision_success(snapshot: dict, rows: list[dict]) -> dict:
    collection = snapshot.get("collection", {})
    required = METRICS.checks_from(collection.get("requiredChecks") or [])
    if any(not isinstance(check, dict) or not isinstance(check.get("name"), str) or not check["name"]
           for check in required):
        required = []
    groups, rejected = defaultdict(list), []
    for row in rows:
        receipt = row["receipt"]
        # The publisher can push successfully and then observe a newer base.
        # Its stale-retry receipt still identifies the first repaired commit.
        if row["kind"] != "publish" or (receipt["outcome"] != "published" and "commit_sha" not in receipt):
            continue
        request = receipt.get("request") or {}
        identity = request_identity(request)
        if not identity or not sha(request.get("root_sha")) or not sha(receipt.get("commit_sha")):
            rejected.append({"runId": row["runId"], "attempt": row["attempt"],
                             "reason": "Published receipt lacks pinned PR/root/repaired-commit identity."})
            continue
        groups[(identity[0], request["root_sha"])].append(row)
    observations = []
    for (number, root), publications in sorted(groups.items()):
        # Artifact creation is after the publication receipt; run creation can
        # precede a slow earlier publication and is not an ordering substitute.
        timed = [row for row in publications if METRICS.timestamp(row.get("publishedObservedAt"))]
        stamps = [row["publishedObservedAt"] for row in timed]
        unambiguous = len(timed) == len(publications) and len(set(stamps)) == len(stamps)
        first = min(timed, key=lambda row: METRICS.timestamp(row["publishedObservedAt"])) if timed else publications[0]
        commit = first["receipt"]["commit_sha"]
        assessment = revision_checks(snapshot, number, commit, required) if unambiguous else {
            "result": "unavailable", "checks": [],
            "reasons": ["Publication chronology is missing or ambiguous."]}
        observations.append({"prNumber": number, "rootSha": root, "commitSha": commit,
                             "publisherRunId": first["runId"], "publisherAttempt": first["attempt"],
                             "publisherOutcome": first["receipt"]["outcome"],
                             "publishedObservedAt": first.get("publishedObservedAt"),
                             "observedPublications": len(publications), **assessment})
    counts = Counter(item["result"] for item in observations)
    evaluated = counts["success"] + counts["failure"]
    return {"successRate": counts["success"] / evaluated if evaluated else None,
            "successful": counts["success"], "unsuccessful": counts["failure"],
            "evaluated": evaluated, "unavailable": counts["unavailable"] + len(rejected),
            "observedGroups": len(observations), "unusablePublishedReceipts": rejected,
            "requiredChecks": required, "requiredChecksProvenance": collection.get("requiredChecksProvenance", "unverified"),
            "observations": observations,
            "qualification": "First observed published commit per PR/root within this collection window; earlier publications outside the window are unknown. "
                             "A verified publisher receipt's commit_sha remains publication evidence when a later ref check reports stale-retry or another outcome. "
                             "Success uses the collector's current required-context/app policy, not a historical protection-policy claim. "
                             "Terminal failures, cancellations, skips and neutral results count as unsuccessful; pending or incomplete evidence is excluded."}


def refresh_order(row: dict) -> tuple:
    receipt = row.get("receipt") or {}
    moment = next((value for name in (
        receipt.get("observed_at"), row.get("publishedObservedAt"), row.get("startedAt"), row.get("createdAt"))
                   if (value := METRICS.timestamp(name))), None)
    return (moment.isoformat() if moment else "", row["attempt"])


def backlog_observations(rows: list[dict], missing: list[dict], *, run_data_complete: bool = False) -> dict:
    first_seen, previous_time, latest, invalid = {}, None, [], 0
    scans = [row for row in rows if row["kind"] == "refresh"]
    scans += [dict(row, receipt={}) for row in missing if row["kind"] == "refresh"]
    scans.sort(key=refresh_order)
    for row in scans:
        receipt = row["receipt"]
        observed = METRICS.timestamp(receipt.get("observed_at"))
        waiting = receipt.get("waiting_requests")
        identities = [request_identity(request) if isinstance(request, dict) else None
                      for request in waiting] if isinstance(waiting, list) else []
        if (observed is None or not isinstance(waiting, list) or None in identities
                or len(identities) != len(set(identities)) or receipt.get("backlog") != len(identities)
                or (previous_time is not None and observed <= previous_time)):
            invalid += 1
            first_seen, previous_time, latest = {}, None, []
            continue
        first_seen = {identity: first_seen.get(identity, receipt["observed_at"]) for identity in identities}
        latest = [{"prNumber": number, "headSha": head, "baseSha": base,
                   "firstObservedAt": first_seen[(number, head, base)], "observedAt": receipt["observed_at"],
                   "observedAgeLowerBoundSeconds": METRICS.seconds(first_seen[(number, head, base)], receipt["observed_at"])}
                  for number, head, base in identities]
        previous_time = observed
    ages = [item["observedAgeLowerBoundSeconds"] for item in latest]
    reasons = []
    if not run_data_complete:
        reasons.append("Workflow run/attempt inventory is incomplete; missing scans make waiting continuity unknown.")
        ages = []
        for item in latest:
            item.update(firstObservedAt=None, observedAgeLowerBoundSeconds=None)
    return {"waiting": latest, "available": bool(scans) and previous_time is not None and run_data_complete,
            "runDataComplete": run_data_complete, "unavailableReasons": reasons,
            "observedAt": previous_time.isoformat() if previous_time else None,
            "oldestObservedAgeLowerBoundSeconds": max(ages) if ages else None,
            "unusableRefreshObservations": invalid,
            "qualification": "Age is a lower bound from consecutive observed waiting scans for the identical PR/head/base, measured at the last scan. "
                             "It is not PR age or the true stale timestamp; gaps and disappeared identities reset the observed interval."}


def review_causes(rows: list[dict]) -> dict:
    reviews = [row for row in rows if row["receipt"]["outcome"] == "review-required"]
    reasons = Counter(str(row["receipt"].get("reason") or "Reason unavailable") for row in reviews)
    paths = Counter(path for row in reviews for path in (row["receipt"].get("manual_paths") or [])
                    if isinstance(path, str))
    return {"receipts": len(reviews), "byReason": dict(sorted(reasons.items())),
            "manualPaths": dict(sorted(paths.items())),
            "note": "Publisher-provided causes and paths; counts are receipts, not unique PRs."}


def collection_health(snapshot: dict) -> dict:
    """Separate known descriptive-report limits from broken evidence collection."""
    collection = snapshot.get("collection") or {}
    failures, unavailable = [], []
    for field in ("runDataComplete", "checkDataComplete", "workflowInventoryComplete"):
        if collection.get(field) is not True:
            failures.append(f"{field} is false or unverified.")
    errors = collection.get("errors", [])
    denied_policy = False
    if not isinstance(errors, list):
        errors = []
        failures.append("Collection errors are not a valid list.")
    for error in errors:
        message = str(error.get("message", "")).lower() if isinstance(error, dict) else ""
        denied = (isinstance(error, dict) and error.get("scope") == "requiredChecks"
                  and error.get("status") == 403 and any(text in message for text in
                      ("resource not accessible", "must have admin", "requires admin", "administration permission"))
                  and not any(text in message for text in ("rate limit", "abuse", "secondary limit")))
        if denied:
            denied_policy = True
        else:
            failures.append("Collection API/pagination failure: " + json.dumps(error, sort_keys=True))
    if collection.get("requiredChecksComplete") is not True:
        if denied_policy:
            unavailable.append("Required-check policy is unavailable: the read-only token cannot read branch administration settings. "
                               "Required-check success and historical comparisons remain unavailable.")
        else:
            failures.append("Required-check policy is incomplete without an explained administration permission denial.")
    if collection.get("changesComplete") is not True:
        unavailable.append("Historical change boundaries are unavailable for some runs; matching/comparison metrics remain unavailable.")
    if collection.get("complete") is not True and not unavailable and not failures:
        failures.append("Overall collection completeness is false without a recognized explanation.")
    return {"status": "failed" if failures else "partial" if unavailable else "complete",
            "failures": failures, "unavailable": unavailable,
            "note": "The collector's original step outcome remains in workflow logs; completeness flags and errors remain in the raw artifact."}


class GitHub:
    def __init__(self, repository: str):
        if not re.fullmatch(r"[\w.-]+/[\w.-]+", repository):
            raise ValueError("Repository must be OWNER/REPO")
        self.prefix = f"/repos/{repository}"

    def read(self, endpoint: str) -> bytes:
        result = subprocess.run(["gh", "api", "--method", "GET", self.prefix + endpoint],
                                capture_output=True, timeout=60)
        if result.returncode:
            raise ValueError("GitHub read failed: " + result.stderr.decode(errors="replace")[:500])
        return result.stdout

    def artifacts(self, run_id: int) -> list[dict]:
        items = []
        expected = None
        for page in range(1, 11):
            data = json.loads(self.read(f"/actions/runs/{run_id}/artifacts?per_page=100&page={page}"))
            if expected is None:
                expected = data["total_count"]
            if expected != data["total_count"] or expected >= 1000:
                raise ValueError("Artifact pagination changed or reached its bound")
            items.extend(data["artifacts"])
            if len(items) == expected:
                if len({item["id"] for item in items}) != len(items):
                    raise ValueError("Duplicate artifact IDs")
                return items
            if len(data["artifacts"]) < 100 or len(items) > expected:
                break
        raise ValueError("Artifact pagination is incomplete")

    def download(self, artifact: dict) -> bytes:
        if artifact.get("size_in_bytes", MAX_BYTES + 1) > MAX_BYTES:
            raise ValueError("Repair outcome archive exceeds its size limit")
        data = self.read(f"/actions/artifacts/{artifact['id']}/zip")
        if len(data) > MAX_BYTES:
            raise ValueError("Downloaded archive exceeds its size limit")
        return data


def decode_receipt(data: bytes, kind: str, run: dict) -> dict:
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        entries = archive.infolist()
        if len(entries) != 1 or entries[0].filename != FILES[kind]:
            raise ValueError("Receipt archive must contain its exact expected JSON file")
        info = entries[0]
        if info.is_dir() or info.file_size > MAX_BYTES or (info.external_attr >> 16) & 0o170000 == 0o120000:
            raise ValueError("Receipt must be a bounded regular file")
        receipt = json.loads(archive.read(info))
    if (not isinstance(receipt, dict) or receipt.get("schema_version") != 1
            or str(receipt.get("workflow_run_id")) != str(run["id"])
            or str(receipt.get("run_attempt")) != str(run["run_attempt"])
            or receipt.get("outcome") not in OUTCOMES):
        raise ValueError("Receipt schema, run, attempt, or outcome does not match")
    return receipt


def summarize(snapshot: dict, api, default_branch: str = "main") -> dict:
    rows, missing = [], []
    runs = [run for run in snapshot.get("workflow_runs", [])
            if run.get("path", "").split("@", 1)[0] == WORKFLOW]
    seen = set()
    artifact_cache = {}
    for run in runs:
        identity = (run["id"], run["run_attempt"])
        if identity in seen:
            raise ValueError("Duplicate workflow run attempt in input")
        seen.add(identity)
        for kind in FILES:
            # One of these jobs normally does not run for a given trigger. Do not
            # turn a legitimately skipped producer into a missing receipt.
            job_name = ("Validate and publish generated repair" if kind == "publish"
                        else "Refresh stale open PR branches after main advances")
            jobs = [job for job in run.get("jobs", []) if job.get("name") == job_name]
            if len(jobs) == 1 and jobs[0].get("conclusion") == "skipped":
                continue
            name = f"generated-repair-outcome-{kind}-{run['id']}-{run['run_attempt']}"
            try:
                if run.get("event") not in {"workflow_run", "push", "schedule", "workflow_dispatch"}:
                    raise ValueError("Run event is not an authorized publisher entrypoint")
                if run["event"] != "workflow_run" and run.get("head_branch") != default_branch:
                    raise ValueError("Publisher run is not on the default branch")
                if len(jobs) != 1:
                    raise ValueError("Producer job evidence is missing or ambiguous")
                if run["id"] not in artifact_cache:
                    artifact_cache[run["id"]] = api.artifacts(run["id"])
                matches = [item for item in artifact_cache[run["id"]]
                           if item.get("name") == name and not item.get("expired")]
                if len(matches) != 1:
                    raise ValueError("Attempt-bound receipt is missing, expired, or ambiguous")
                artifact = matches[0]
                provenance = artifact.get("workflow_run") or {}
                if (provenance.get("id") != run["id"]
                        or provenance.get("head_sha") != run.get("head_sha")):
                    raise ValueError("Artifact provenance differs from the workflow run")
                receipt = decode_receipt(api.download(artifact), kind, run)
                rows.append({"runId": run["id"], "attempt": run["run_attempt"], "kind": kind,
                             "createdAt": run.get("created_at"), "artifactId": artifact["id"],
                             "startedAt": run.get("run_started_at"),
                             "publishedObservedAt": artifact.get("created_at"),
                             "workflowConclusion": run.get("conclusion"), "receipt": receipt})
            except (ValueError, OSError, KeyError, zipfile.BadZipFile, subprocess.TimeoutExpired) as exc:
                missing.append({"runId": run["id"], "attempt": run["run_attempt"], "kind": kind,
                                "createdAt": run.get("created_at"),
                                "startedAt": run.get("run_started_at"),
                                "reason": str(exc)})
    refresh = [row for row in rows if row["kind"] == "refresh"]
    latest = max(refresh, key=refresh_order) if refresh else None
    revisions = first_revision_success(snapshot, rows)
    revisions["unavailablePublisherReceipts"] = sum(item["kind"] == "publish" for item in missing)
    return {
        "schemaVersion": 1, "window": snapshot.get("window"),
        "collectionComplete": snapshot.get("collection", {}).get("complete", False),
        "runDataComplete": snapshot.get("collection", {}).get("runDataComplete", False),
        "operationalCollection": collection_health(snapshot),
        "observedRunAttempts": len(runs), "receipts": rows, "missingReceipts": missing,
        "outcomeCounts": dict(sorted(Counter(row["receipt"]["outcome"] for row in rows).items())),
        "latestRefreshBacklog": latest["receipt"].get("backlog") if latest else None,
        "observedRefreshBacklog": backlog_observations(
            rows, missing, run_data_complete=snapshot.get("collection", {}).get("runDataComplete") is True),
        "reviewRequiredCauses": review_causes(rows),
        "firstRepairedRevisionCiSuccessRate": revisions["successRate"],
        "firstRepairedRevisionCi": revisions,
        "note": "Outcome counts describe verified repair receipts, not authoritative CI success. "
                "Repaired-revision checks are assessed separately from publication with explicit "
                "current-policy provenance, evaluated denominators, and unavailable reasons.",
    }


def markdown(report: dict) -> str:
    lines = ["## Generated repair outcomes", "",
             f"Observed publisher attempts: {report['observedRunAttempts']}. "
             f"Missing/unusable receipts: {len(report['missingReceipts'])}.", "",
             "| Outcome | Receipts |", "| --- | ---: |"]
    lines += [f"| {name} | {count} |" for name, count in report["outcomeCounts"].items()]
    revisions = report["firstRepairedRevisionCi"]
    rate = f"{revisions['successRate']:.1%}" if revisions["successRate"] is not None else "unavailable"
    lines += ["", f"Latest observed refresh backlog: {report['latestRefreshBacklog'] if report['latestRefreshBacklog'] is not None else 'unavailable'}.",
              f"Oldest observed waiting interval lower bound: "
              f"{report['observedRefreshBacklog']['oldestObservedAgeLowerBoundSeconds'] if report['observedRefreshBacklog']['oldestObservedAgeLowerBoundSeconds'] is not None else 'unavailable'} seconds.",
              "", f"First observed repaired-revision CI success: {rate} "
              f"({revisions['successful']}/{revisions['evaluated']} evaluated; {revisions['unavailable']} unavailable; "
              f"{revisions['unavailablePublisherReceipts']} publisher receipts unavailable).",
              "", revisions["qualification"],
              "", report["observedRefreshBacklog"]["qualification"],
              "", "Review-required receipt causes:"]
    lines += [f"- {reason.replace(chr(10), ' ')}: {count}"
              for reason, count in report["reviewRequiredCauses"]["byReason"].items()]
    lines += [
              "", report["note"], ""]
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo")
    parser.add_argument("--branch", default="main")
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--markdown", type=Path)
    parser.add_argument("--check-collection", action="store_true",
                        help="Classify operational collection failures separately from known unavailable policy/change history")
    args = parser.parse_args()
    snapshot = json.loads(args.input.read_text(encoding="utf-8"))
    if args.check_collection:
        health = collection_health(snapshot)
        text = f"CI metrics collection: {health['status']}.\n"
        for message in health["unavailable"]:
            print("::warning::" + message)
            text += "\n- Unavailable: " + message
        for message in health["failures"]:
            print("::error::" + message)
            text += "\n- Failed: " + message
        if summary := os.environ.get("GITHUB_STEP_SUMMARY"):
            with Path(summary).open("a", encoding="utf-8") as stream:
                stream.write("\n" + text + "\n\n" + health["note"] + "\n")
        print(text)
        return 1 if health["status"] == "failed" else 0
    if not args.repo or not args.output:
        parser.error("--repo and --output are required when summarizing repair outcomes")
    result = summarize(snapshot, GitHub(args.repo), args.branch)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    if args.markdown:
        args.markdown.write_text(markdown(result), encoding="utf-8")
    print(f"Retained {len(result['receipts'])} verified receipts; {len(result['missingReceipts'])} unavailable.")
    return 0 if not result["missingReceipts"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
