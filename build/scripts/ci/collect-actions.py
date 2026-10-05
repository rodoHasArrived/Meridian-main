#!/usr/bin/env python3
"""Export bounded Actions evidence using GET only; incomplete evidence exits nonzero.

Requires an authenticated GitHub CLI, or --api-cache for replay of read-only API
responses. Collection includes every workflow and attempt, including unsuccessful
attempts. The current rules snapshot does not establish historical requirements.
"""
from __future__ import annotations

import argparse
import copy
from datetime import datetime, timedelta, timezone
import hashlib
import json
from pathlib import Path
import re
import subprocess
from urllib.parse import parse_qsl, quote, urlencode, urlsplit


class CollectionError(RuntimeError):
    pass


class APIError(CollectionError):
    def __init__(self, message: str, status: int | None = None):
        super().__init__(message)
        self.status = status


def timestamp(value: str) -> datetime:
    result = datetime.fromisoformat(value.replace("Z", "+00:00"))
    if result.tzinfo is None or result.microsecond:
        raise ValueError("Timestamps need a UTC offset and whole-second precision.")
    return result.astimezone(timezone.utc)


def iso(value: datetime) -> str:
    return value.astimezone(timezone.utc).isoformat().replace("+00:00", "Z")


def endpoint_key(endpoint: str) -> str:
    parts = urlsplit(endpoint)
    return parts.path + ("?" + urlencode(sorted(parse_qsl(parts.query))) if parts.query else "")


class GitHubAPI:
    """No write-capable operation is exposed by this transport."""

    def get_text(self, endpoint: str):
        try:
            response = subprocess.run(
                ["gh", "api", "--method", "GET", endpoint,
                 "-H", "Accept: application/vnd.github+json",
                 "-H", "X-GitHub-Api-Version: 2022-11-28"],
                capture_output=True, text=True, check=False, timeout=60)
        except (OSError, subprocess.TimeoutExpired) as exc:
            raise CollectionError(str(exc)) from exc
        if response.returncode:
            match = re.search(r"HTTP (\d{3})", response.stderr)
            try:
                message = json.loads(response.stdout).get("message")
            except (ValueError, AttributeError):
                message = None
            raise APIError(message or response.stderr.strip()[:1000] or "GitHub API request failed",
                           int(match[1]) if match else None)
        return response.stdout

    def get(self, endpoint: str):
        try:
            return json.loads(self.get_text(endpoint))
        except ValueError as exc:
            raise CollectionError("GitHub API did not return JSON") from exc


class CachedAPI:
    """Replay connector/API exports; missing responses never fall back to network."""

    def __init__(self, responses: dict):
        self.responses = {endpoint_key(key): value for key, value in responses.items()}

    def get(self, endpoint: str):
        key = endpoint_key(endpoint)
        if key not in self.responses:
            raise CollectionError("No response in API cache")
        result = self.responses[key]
        if isinstance(result, dict) and "error" in result:
            raise APIError(str(result["error"]), result.get("status"))
        if isinstance(result, dict) and str(result.get("status", "")).isdigit() and int(result["status"]) >= 400:
            raise APIError(result.get("message", "Cached API error"), int(result["status"]))
        return copy.deepcopy(result)

    def get_text(self, endpoint: str):
        result = self.get(endpoint)
        if not isinstance(result, str):
            raise CollectionError("Cached log response is not text")
        return result


class Collector:
    def __init__(self, api, repository: str, since: datetime, until: datetime,
                 branch: str = "main", push_bases: dict | None = None, checkout_evidence: list | None = None):
        if not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository):
            raise ValueError("Repository must be OWNER/REPO.")
        if since > until:
            raise ValueError("The collection window is reversed.")
        self.started_at = datetime.now(timezone.utc)
        if until > self.started_at:
            raise ValueError("The collection window cannot include future time.")
        self.api, self.prefix = api, f"/repos/{repository}"
        self.repository, self.since, self.until = repository, since, until
        self.branch, self.push_bases = branch, push_bases or {}
        self.errors: list[dict] = []
        self.requests = 0
        self.change_cache: dict = {}
        self.check_cache: dict = {}
        self.check_detail_cache: dict = {}
        self.reference_candidates: dict = {}
        self.reference_sources: dict = {}
        self.merge_cache: dict = {}
        self.checkout_evidence = checkout_evidence or []
        self.checkout_run_references: dict = {}
        self.checkout_sources: dict = {}
        self.job_cache: dict = {}
        self.job_collection_complete: dict = {}

    def error(self, scope: str, endpoint: str, message: str):
        self.errors.append(dict(scope=scope, endpoint=endpoint, message=message))

    def get(self, endpoint: str, scope: str, text: bool = False):
        self.requests += 1
        try:
            return self.api.get_text(endpoint) if text else self.api.get(endpoint)
        except (CollectionError, OSError, ValueError) as exc:
            self.error(scope, endpoint, str(exc))
            if isinstance(exc, APIError) and exc.status is not None:
                self.errors[-1]["status"] = exc.status
            return None

    @staticmethod
    def page_url(endpoint: str, page: int) -> str:
        return endpoint + ("&" if "?" in endpoint else "?") + urlencode(dict(per_page=100, page=page))

    def pages(self, endpoint: str, key: str | None, scope: str, first=None) -> list:
        records, seen, expected, page = [], set(), None, 1
        while True:
            url = self.page_url(endpoint, page)
            payload = first if page == 1 and first is not None else self.get(url, scope)
            if payload is None:
                break
            items = payload.get(key) if key and isinstance(payload, dict) else payload
            if not isinstance(items, list):
                self.error(scope, url, "Expected a paginated array.")
                break
            if isinstance(payload, dict) and "total_count" in payload:
                total = payload["total_count"]
                if not isinstance(total, int) or total < 0:
                    self.error(scope, url, "Invalid pagination total_count.")
                    break
                if expected is not None and total != expected:
                    self.error(scope, url, "Pagination total changed during collection.")
                expected = total
            repeated = False
            for item in items:
                identity = item.get("id") if isinstance(item, dict) else None
                if identity is not None and identity in seen:
                    repeated = True
                    continue
                if identity is not None:
                    seen.add(identity)
                records.append(item)
            if repeated:
                self.error(scope, url, "Duplicate records across pages; snapshot is unstable.")
                break
            if len(items) < 100 or (expected is not None and len(records) >= expected):
                break
            page += 1
        if expected is not None and len(records) != expected:
            self.error(scope, endpoint, f"Collected {len(records)} of {expected} advertised records.")
        return records

    def runs(self, start: datetime, end: datetime) -> list:
        endpoint = self.prefix + "/actions/runs?" + urlencode({"created": f"{iso(start)}..{iso(end)}"})
        first = self.get(self.page_url(endpoint, 1), "runs")
        if first is None:
            return []
        count = first.get("total_count") if isinstance(first, dict) else None
        if not isinstance(count, int):
            self.error("runs", endpoint, "Missing run total_count; cannot verify the 1000-result cap.")
            return []
        # GitHub limits filtered run queries to 1000 results. Split before paging
        # even at exactly 1000, since capped totals cannot establish completeness.
        if count >= 1000:
            span = int((end - start).total_seconds())
            if span:
                middle = start + timedelta(seconds=span // 2)
                return self.runs(start, middle) + self.runs(middle + timedelta(seconds=1), end)
            self.error("runs", endpoint, "At least 1000 runs in one second; API cap prevents complete collection.")
        return self.pages(endpoint, "workflow_runs", "runs", first)

    def required_checks(self) -> dict:
        branch = quote(self.branch, safe="")
        classic_url = f"{self.prefix}/branches/{branch}/protection/required_status_checks"
        rules_url = f"{self.prefix}/rules/branches/{branch}"
        before = len(self.errors)
        classic = self.get(classic_url, "requiredChecks")
        if classic is None:
            failure = self.errors[-1]
            if failure.get("status") == 404 and failure["message"] == "Branch not protected":
                self.errors.remove(failure)
                classic = dict(contexts=[], checks=[], absenceEvidence="GitHub: Branch not protected")
            elif failure.get("status") == 404:
                # A protected branch can omit classic required-status checks.
                # Generic 404 alone cannot distinguish absence from denied access.
                protection = self.get(f"{self.prefix}/branches/{branch}/protection", "requiredChecks")
                if isinstance(protection, dict) and "url" in protection:
                    self.errors.remove(failure)
                    classic = protection.get("required_status_checks") or dict(
                        contexts=[], checks=[], absenceEvidence="Readable full classic protection has no required checks")
        rules = self.pages(rules_url, None, "requiredChecks")
        contexts = []
        if isinstance(classic, dict):
            contexts.extend(dict(name=check["context"], appId=check.get("app_id"), source=classic_url)
                            for check in classic.get("checks", []) if check.get("context"))
            known = {item["name"] for item in contexts}
            contexts.extend(dict(name=name, appId=None, source=classic_url)
                            for name in classic.get("contexts", []) if name not in known)
        for rule in rules:
            if rule.get("type") == "required_status_checks":
                contexts.extend(dict(name=check["context"], appId=check.get("integration_id"), source=rules_url)
                                for check in rule.get("parameters", {}).get("required_status_checks", [])
                                if check.get("context"))
        return dict(branch=self.branch, provenance="current_only", historicalApplicability="unverified",
                    complete=len(self.errors) == before, classic=classic, rules=rules, checks=contexts)

    def changes(self, run: dict) -> dict:
        head, base, source, caveats = run.get("head_sha"), None, None, []
        pulls = run.get("pull_requests", [])
        matching_pulls = [pull for pull in pulls if pull.get("head", {}).get("sha") == head]
        if run.get("event") in ("pull_request", "pull_request_target"):
            # GitHub can update this association when a branch is reused. Even
            # a matching current head does not establish the base at run creation.
            caveats.append("Actions PR associations are mutable; a historical base requires immutable merge evidence.")
        elif run.get("event") == "push":
            base = self.push_bases.get(head)
            source = "provided_push_before_sha" if base else None
            if not base:
                caveats.append("Actions metadata omits push.before; a commit parent does not prove a whole-push diff.")
        else:
            caveats.append("No unambiguous PR or push change boundary is available.")
        pull_number = matching_pulls[0].get("number") if len(matching_pulls) == 1 else None
        merge_evidence = None
        if not base and run.get("event") == "pull_request":
            recovered = self.merge_boundary(run)
            if recovered:
                base, pull_number = recovered["baseSha"], recovered["pullNumber"]
                merge_evidence = recovered
                source = ("actions_checkout_log_merge_commit_parents" if recovered.get("checkoutLogs")
                          else "actions_referenced_workflow_merge_commit_parents")
                caveats = ["PR base/head proven by the immutable synthetic merge commit referenced by Actions."]
        result = dict(baseSha=base, headSha=head, paths=[], complete=False, source=source, caveats=caveats)
        result["pullNumber"] = pull_number
        result["mergeEvidence"] = merge_evidence
        if not base or not head:
            return result
        if not all(re.fullmatch(r"[0-9a-fA-F]{40}", value) for value in (base, head)):
            result["caveats"].append("Change boundaries must be full immutable commit SHAs.")
            return result
        cache_key = (base, head)
        if cache_key not in self.change_cache:
            endpoint = f"{self.prefix}/compare/{base}...{head}"
            comparison = self.get(endpoint, "changes")
            files = comparison.get("files") if isinstance(comparison, dict) else None
            # Compare exposes at most 300 files, independent of commit pagination.
            complete = isinstance(files, list) and len(files) < 300
            paths = sorted({name for item in files or [] for name in
                            (item.get("filename"), item.get("previous_filename")) if name})
            self.change_cache[cache_key] = (paths, complete, comparison.get("status") if isinstance(comparison, dict) else None)
        result["paths"], result["complete"], comparison_status = self.change_cache[cache_key]
        if run.get("event") == "push" and comparison_status not in ("ahead", "identical"):
            result["complete"] = False
            result["caveats"].append("Push before is not proven an ancestor; three-dot compare cannot establish the whole push diff.")
        if not result["complete"]:
            result["caveats"].append("Compare files are unavailable or meet the 300-file truncation limit.")
        return result

    @staticmethod
    def merge_references(run: dict) -> list:
        refs = []
        for workflow in run.get("referenced_workflows", []):
            match = re.search(r"(?:^|/)refs/pull/(\d+)/merge$", workflow.get("ref", ""))
            sha = workflow.get("sha", "")
            if match and re.fullmatch(r"[0-9a-fA-F]{40}", sha):
                refs.append((sha, int(match[1])))
        return refs

    @staticmethod
    def reference_key(run: dict) -> tuple:
        repository = run.get("head_repository") or {}
        return (run.get("event"), run.get("head_sha"), run.get("head_branch"),
                repository.get("id") or repository.get("full_name"))

    def load_checkout_evidence(self, listed: list):
        """Bind saved log bytes to an actual Actions job before using a merge ref."""
        runs = {run["id"]: run for run in listed}
        for evidence in self.checkout_evidence:
            run = runs.get(evidence.get("runId"))
            if run is None:
                continue  # A shared manifest can span multiple collection windows.
            job_id, sha = evidence.get("jobId"), evidence.get("checkoutMergeSha", "")
            number = evidence.get("pullNumber")
            endpoint = f"{self.prefix}/actions/jobs/{job_id}"
            log_endpoint = endpoint + "/logs"
            known_numbers = {pull["number"] for pull in run.get("pull_requests", [])
                             if "number" in pull and pull.get("head", {}).get("sha") == run.get("head_sha")}
            if (not isinstance(job_id, int) or not isinstance(number, int) or number < 1
                    or not re.fullmatch(r"[0-9a-fA-F]{40}", sha)
                    or run.get("event") != "pull_request" or evidence.get("headSha") != run.get("head_sha")
                    or (known_numbers and number not in known_numbers)
                    or evidence.get("checkoutRef") != f"refs/pull/{number}/merge"
                    or urlsplit(evidence.get("jobLogUrl", "")).path != log_endpoint):
                self.error("changes", endpoint, "Checkout evidence does not bind this PR run, head, job and merge ref.")
                continue
            # The attempt-specific listing is authoritative job ownership evidence
            # and is accessible to integrations that cannot read /actions/jobs/ID.
            job = None
            maximum = run.get("run_attempt", 0)
            for attempt in range(1, maximum + 1):
                job = next((item for item in self.attempt_jobs(run["id"], attempt) if item.get("id") == job_id), None)
                if job is not None:
                    break
            if (not isinstance(job, dict) or job.get("id") != job_id or job.get("run_id") != run["id"]
                    or job.get("head_sha", run["head_sha"]) != run["head_sha"]):
                self.error("changes", endpoint, "Checkout evidence job does not belong to the recorded run and head.")
                continue
            try:
                if evidence.get("logPath"):
                    raw = Path(evidence["logPath"]).read_bytes()
                else:
                    log = self.get(log_endpoint, "changes", text=True)
                    if not isinstance(log, str):
                        continue
                    raw = log.encode("utf-8")
                text = raw.decode("utf-8-sig")
            except (OSError, UnicodeError) as exc:
                self.error("changes", log_endpoint, f"Cannot verify saved checkout log: {exc}")
                continue
            digest = hashlib.sha256(raw).hexdigest()
            fetch = re.search(r"(?m)^.*\bgit\b[^\r\n]*\bfetch\b[^\r\n]*\+" + re.escape(sha)
                              + rf":refs/remotes/pull/{number}/merge(?:\s|$)", text)
            selected = re.search(r"(?m)^.*\bgit log -1 --format=%H\s*\r?\n[^\r\n]*\b"
                                 + re.escape(sha) + r"\s*$", text)
            if digest != evidence.get("logSha256") or not fetch or not selected:
                self.error("changes", log_endpoint, "Checkout log digest, fetched merge ref or checked-out commit is unverified.")
                continue
            key = self.reference_key(run)
            self.reference_candidates.setdefault(key, set()).add((sha, number))
            self.reference_sources.setdefault((key, sha, number), set()).add(run["id"])
            self.checkout_run_references.setdefault(run["id"], set()).add((sha, number))
            self.checkout_sources[(key, sha, number, run["id"])] = dict(
                runId=run["id"], jobId=job_id, headSha=run["head_sha"], checkoutMergeSha=sha,
                pullNumber=number, checkoutRef=evidence["checkoutRef"], jobLogUrl=evidence["jobLogUrl"],
                logSha256=digest, verifiedLogBytes=True, evidenceLines=[fetch[0].strip(), selected[0].strip()])

    def attempt_jobs(self, run_id: int, attempt: int) -> list:
        key = (run_id, attempt)
        if key not in self.job_cache:
            before = len(self.errors)
            endpoint = f"{self.prefix}/actions/runs/{run_id}/attempts/{attempt}/jobs"
            self.job_cache[key] = self.pages(endpoint, "jobs", "jobs")
            self.job_collection_complete[key] = len(self.errors) == before
        return self.job_cache[key]

    def merge_boundary(self, run: dict):
        key = self.reference_key(run)
        own_refs = self.merge_references(run) + list(self.checkout_run_references.get(run["id"], []))
        refs = own_refs or self.reference_candidates.get(key, [])
        known_numbers = {pull["number"] for pull in run.get("pull_requests", [])
                         if "number" in pull and pull.get("head", {}).get("sha") == run.get("head_sha")}
        if known_numbers:
            refs = [(sha, number) for sha, number in refs if number in known_numbers]
        boundaries, merge_shas, sources, checkout_logs = set(), set(), set(), []
        for merge_sha, number in refs:
            if merge_sha not in self.merge_cache:
                self.merge_cache[merge_sha] = self.get(f"{self.prefix}/commits/{merge_sha}", "changes")
            commit = self.merge_cache[merge_sha]
            parents = commit.get("parents", []) if isinstance(commit, dict) else []
            if len(parents) == 2 and parents[1].get("sha") == run.get("head_sha"):
                boundaries.add((parents[0]["sha"], number))
                merge_shas.add(merge_sha)
                sources.update(self.reference_sources.get((key, merge_sha, number), []))
                checkout_logs.extend(evidence for (group, sha, pr, _), evidence in self.checkout_sources.items()
                                     if group == key and sha == merge_sha and pr == number)
            else:
                return None
        # Same head/branch can have multiple PRs or base updates. Never choose an
        # arbitrary candidate when the exact run has no merge reference of its own.
        if len(boundaries) != 1:
            return None
        base, number = next(iter(boundaries))
        return dict(baseSha=base, pullNumber=number, headSha=run.get("head_sha"), mergeShas=sorted(merge_shas),
                    sourceRunIds=sorted(sources or {run["id"]}), inferredFromPeerWorkflow=not bool(own_refs),
                    checkoutLogs=checkout_logs)

    def attempts(self, listed: dict) -> list:
        endpoint = f"{self.prefix}/actions/runs/{listed['id']}"
        latest = self.get(endpoint, "attempts")
        latest = latest if isinstance(latest, dict) else listed
        maximum = latest.get("run_attempt")
        if not isinstance(maximum, int) or maximum < 1:
            self.error("attempts", endpoint, "Missing positive run_attempt; attempt inventory is unknown.")
            return []
        results = []
        change = self.changes(latest)
        sha = latest.get("head_sha")
        if sha and sha not in self.check_cache:
            self.check_cache[sha] = self.pages(f"{self.prefix}/commits/{sha}/check-runs?filter=all",
                                               "check_runs", "checks")
            if len(self.check_cache[sha]) >= 1000:
                self.error("checks", f"{self.prefix}/commits/{sha}/check-runs",
                           "Check-run inventory may meet GitHub's per-suite 1000-check cap; job IDs use direct fallback.")
        checks = {str(check["id"]): check for check in self.check_cache.get(sha, [])}
        for number in range(1, maximum + 1):
            before = len(self.errors)
            attempt_url = f"{endpoint}/attempts/{number}"
            detail = self.get(attempt_url, "attempts")
            if not isinstance(detail, dict) or detail.get("run_attempt") != number:
                if detail is not None:
                    self.error("attempts", attempt_url, "Attempt metadata does not match the requested attempt.")
                detail = dict(latest, run_attempt=number, status=None, conclusion=None,
                              run_started_at=None, updated_at=None)
            record = dict(latest, **detail)
            record["jobs"] = self.attempt_jobs(listed["id"], number)
            for job in record["jobs"]:
                # Partial reruns can retain earlier successful jobs. Preserve their
                # IDs and original run_attempt so reports charge runner time once.
                if isinstance(job.get("run_attempt"), int) and job["run_attempt"] > number:
                    self.error("jobs", attempt_url, "A job belongs to a later run attempt.")
                check_id = str(job.get("check_run_url", "")).rstrip("/").rsplit("/", 1)[-1]
                check = checks.get(check_id, {})
                if not check and check_id.isdigit():
                    if check_id not in self.check_detail_cache:
                        self.check_detail_cache[check_id] = self.get(f"{self.prefix}/check-runs/{check_id}", "checks")
                    check = self.check_detail_cache[check_id] or {}
                if check and str(check.get("id")) != check_id:
                    self.error("checks", f"{self.prefix}/check-runs/{check_id}", "Check-run identity mismatch.")
                    check = {}
                job["check_app_id"] = check.get("app", {}).get("id")
            record["collectionComplete"] = (self.job_collection_complete[(listed["id"], number)] and
                                            not any(error["scope"] in ("attempts", "jobs") for error in self.errors[before:]))
            record["changes"] = change
            record["changed_files"] = change["paths"] if change["complete"] else None
            prs = str(change.get("pullNumber") or "")
            if not prs:
                prs = ",".join(str(p["number"]) for p in record.get("pull_requests", [])
                               if "number" in p and p.get("head", {}).get("sha") == record.get("head_sha"))
            record["change_key"] = f"{record.get('event')}:{prs or record.get('head_branch')}:{record.get('head_sha')}"
            results.append(record)
        # A concurrent rerun can otherwise create a silently missing attempt.
        final = self.get(endpoint, "attempts")
        if isinstance(final, dict) and final.get("run_attempt") != maximum:
            self.error("attempts", endpoint, "Run attempt changed during collection; rerun the export.")
        return results

    def collect(self) -> dict:
        workflows = self.pages(self.prefix + "/actions/workflows", "workflows", "workflows")
        required = self.required_checks()
        listed = self.runs(self.since, self.until)
        for run in listed:
            key = self.reference_key(run)
            refs = self.merge_references(run)
            self.reference_candidates.setdefault(key, set()).update(refs)
            for sha, number in refs:
                self.reference_sources.setdefault((key, sha, number), set()).add(run["id"])
        self.load_checkout_evidence(listed)
        if not workflows:
            workflows = list({run["workflow_id"]: dict(id=run["workflow_id"], name=run.get("name"),
                                                       path=run.get("path"), source="bounded_run_inventory")
                              for run in listed if run.get("workflow_id")}.values())
        attempts, seen = [], set()
        for run in listed:
            if run["id"] in seen:
                self.error("runs", self.prefix + "/actions/runs", "Duplicate run across time partitions.")
                continue
            seen.add(run["id"])
            attempts.extend(self.attempts(run))
        run_data_complete = not any(error["scope"] in ("runs", "attempts", "jobs")
                                    for error in self.errors)
        changes_complete = all(run["changes"]["complete"] for run in attempts
                               if run.get("event") == "pull_request" or
                               (run.get("event") == "push" and run.get("head_branch") == self.branch))
        checks_complete = not any(error["scope"] == "checks" for error in self.errors)
        workflows_complete = not any(error["scope"] == "workflows" for error in self.errors)
        return dict(schemaVersion=1, repository=self.repository, collectedAt=iso(datetime.now(timezone.utc)),
                    collectionStartedAt=iso(self.started_at),
                    window=dict(since=iso(self.since), until=iso(self.until), untilInclusive=True),
                    collection=dict(complete=run_data_complete and changes_complete and required["complete"]
                                             and checks_complete and workflows_complete,
                                    runDataComplete=run_data_complete, changesComplete=changes_complete,
                                    checkDataComplete=checks_complete,
                                    workflowInventoryComplete=workflows_complete,
                                    requiredChecksComplete=required["complete"], requiredChecks=required["checks"],
                                    requiredChecksProvenance="current_only", errors=self.errors,
                                    windowStart=iso(self.since), windowEnd=iso(self.until),
                                    runCount=len(seen), attemptCount=len(attempts), requestCount=self.requests,
                                    scope="All repository workflows/events; runs created within inclusive UTC bounds."
                                          " Attempts are captured as observed, even if retried outside those bounds."),
                    workflows=workflows, requiredCheckConfiguration=required,
                    check_runs_by_sha=self.check_cache, check_runs_by_id=self.check_detail_cache,
                    checkoutEvidence=list(self.checkout_sources.values()), workflow_runs=attempts)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", required=True, help="OWNER/REPO")
    parser.add_argument("--since", required=True, type=timestamp, help="Inclusive ISO timestamp with offset")
    parser.add_argument("--until", required=True, type=timestamp, help="Inclusive ISO timestamp with offset")
    parser.add_argument("--branch", default="main", help="Branch whose current required checks are inspected")
    parser.add_argument("--push-base-map", type=Path, help="JSON object mapping push head SHA to verified before SHA")
    parser.add_argument("--api-cache", type=Path, help="Replay JSON object mapping API endpoint to raw JSON response")
    parser.add_argument("--checkout-evidence", type=Path,
                        help="JSON list (or run-ID map) of saved checkout logs, hashes and immutable merge refs")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    api = CachedAPI(json.loads(args.api_cache.read_text(encoding="utf-8"))) if args.api_cache else GitHubAPI()
    bases = json.loads(args.push_base_map.read_text(encoding="utf-8")) if args.push_base_map else {}
    checkout = json.loads(args.checkout_evidence.read_text(encoding="utf-8")) if args.checkout_evidence else []
    if isinstance(checkout, dict):
        checkout = [dict(value, runId=int(key)) for key, value in checkout.items()]
    if not isinstance(checkout, list) or any(not isinstance(item, dict) for item in checkout):
        parser.error("Checkout evidence must be a list of objects or a run-ID map.")
    for item in checkout:
        if item.get("logPath"):
            item["logPath"] = str(args.checkout_evidence.parent / item["logPath"])
    try:
        result = Collector(api, args.repo, args.since, args.until, args.branch, bases, checkout).collect()
    except ValueError as exc:
        parser.error(str(exc))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result["collection"], indent=2))
    return 0 if result["collection"]["complete"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
