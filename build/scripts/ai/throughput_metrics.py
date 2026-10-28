"""Record source-backed task events and summarize small matched throughput pilots.

Records are explicit observations, not inferred agent activity. Complete instrumentation
means every relevant read, correction and validation was recorded by the trial runner.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import statistics
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any
from urllib.parse import urlparse

EVENTS = {"task_started", "file_read", "useful_edit", "correction", "validation_started",
          "validation_finished", "task_finished", "passing_pr"}
METRICS = ("firstUsefulEditSeconds", "repeatedFileReads", "correctionCycles",
           "validationWaitSeconds", "passingPrSeconds")


def timestamp(value: str) -> datetime:
    try:
        result = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except (AttributeError, TypeError, ValueError) as exc:
        raise ValueError("timestamp must be an ISO 8601 timezone-aware timestamp") from exc
    if result.tzinfo is None:
        raise ValueError("timestamp must include a timezone")
    return result


def source(root: Path, name: str) -> dict[str, str]:
    path = (root / name).resolve()
    if not path.is_relative_to(root.resolve()) or not path.is_file():
        raise ValueError(f"source must be an existing file within --root: {name}")
    return {"path": path.relative_to(root.resolve()).as_posix(),
            "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}


def validate_event(event: dict[str, Any]) -> None:
    if not isinstance(event, dict) or event.get("event") not in EVENTS:
        raise ValueError("unknown event")
    if not isinstance(event.get("trial"), str) or not event["trial"].strip():
        raise ValueError("trial must be nonempty")
    timestamp(event.get("timestamp"))
    kind = event["event"]
    if kind == "task_started":
        if (event.get("cohort") not in {"baseline", "treatment"}
                or not isinstance(event.get("pair"), str) or not event["pair"].strip()):
            raise ValueError("task_started requires pair and baseline|treatment cohort")
    if kind in {"file_read", "useful_edit"}:
        if not isinstance(event.get("path"), str) or not event["path"]:
            raise ValueError(f"{kind} requires a path")
        digest = event.get("sha256", "")
        if not isinstance(digest, str) or len(digest) != 64 or any(c not in "0123456789abcdef" for c in digest):
            raise ValueError(f"{kind} requires a SHA256 source digest")
    if kind.startswith("validation_") and not event.get("validationId"):
        raise ValueError("validation events require validationId")
    if kind == "validation_finished" and event.get("result") not in {"pass", "fail"}:
        raise ValueError("validation_finished requires pass|fail result")
    if kind == "task_finished":
        if not isinstance(event.get("instrumentationComplete"), bool):
            raise ValueError("task_finished requires instrumentationComplete boolean")
        sha = event.get("commitSha")
        if sha is not None and (not isinstance(sha, str) or len(sha) != 40
                                or any(c not in "0123456789abcdef" for c in sha)):
            raise ValueError("task_finished commitSha must be a full commit SHA when supplied")
    if kind == "passing_pr":
        url = urlparse(event.get("url", ""))
        if url.scheme != "https" or url.netloc != "github.com" or "/pull/" not in url.path:
            raise ValueError("passing_pr requires a GitHub HTTPS pull request URL")
        sha = event.get("commitSha", "")
        if not isinstance(sha, str) or len(sha) != 40 or any(c not in "0123456789abcdef" for c in sha):
            raise ValueError("passing_pr requires a full commit SHA")
        checks = event.get("requiredChecks")
        if not isinstance(checks, list) or not checks:
            raise ValueError("passing_pr requires explicit required-check evidence")
        for check in checks:
            if (not isinstance(check, dict) or not check.get("name")
                    or check.get("conclusion") != "success" or check.get("commitSha") != sha
                    or not isinstance(check.get("evidenceUrl"), str)
                    or not check["evidenceUrl"].startswith("https://github.com/")):
                raise ValueError("each required check needs name, success, matching commitSha and evidenceUrl")
        if len({c["name"] for c in checks}) != len(checks):
            raise ValueError("required check names must be unique")


def read_logs(paths: list[Path]) -> list[dict[str, Any]]:
    events = []
    for path in paths:
        for line_number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            if not line.strip():
                continue
            try:
                event = json.loads(line)
                validate_event(event)
            except (ValueError, TypeError) as exc:
                raise ValueError(f"{path}:{line_number}: {exc}") from exc
            events.append(event)
    return events


def union_seconds(intervals: list[tuple[datetime, datetime]]) -> float:
    total = 0.0
    current = None
    for start, end in sorted(intervals):
        if current is None:
            current = (start, end)
        elif start <= current[1]:
            current = (current[0], max(end, current[1]))
        else:
            total += (current[1] - current[0]).total_seconds()
            current = (start, end)
    if current:
        total += (current[1] - current[0]).total_seconds()
    return total


def summarize_trial(events: list[dict[str, Any]]) -> dict[str, Any]:
    if not events or events[0]["event"] != "task_started":
        raise ValueError("trial must begin with task_started")
    start = events[0]
    started = timestamp(start["timestamp"])
    previous = started
    finish = None
    edits = []
    reads: set[tuple[str, str]] = set()
    repeats = corrections = 0
    active: dict[str, datetime] = {}
    used_validations: set[str] = set()
    intervals = []
    validation_outcomes = []
    passing = None
    for index, event in enumerate(events):
        validate_event(event)
        time = timestamp(event["timestamp"])
        if time < previous:
            raise ValueError("trial event timestamps must be nondecreasing")
        previous = time
        kind = event["event"]
        if kind == "task_started" and index:
            raise ValueError("duplicate task_started")
        if finish is not None and kind != "passing_pr":
            raise ValueError("only passing_pr may follow task_finished")
        if kind == "file_read":
            identity = (event["path"], event["sha256"])
            repeats += identity in reads
            reads.add(identity)
        elif kind == "useful_edit":
            edits.append(time)
        elif kind == "correction":
            corrections += 1
        elif kind == "validation_started":
            key = event["validationId"]
            if key in used_validations:
                raise ValueError("validation IDs must be unique within a trial")
            active[key] = time
            used_validations.add(key)
        elif kind == "validation_finished":
            key = event["validationId"]
            if key not in active:
                raise ValueError("validation_finished has no matching start")
            validation_start = active.pop(key)
            intervals.append((validation_start, time))
            validation_outcomes.append({"validationId": key, "result": event["result"],
                                        "startedAt": validation_start.isoformat(),
                                        "finishedAt": time.isoformat(),
                                        "seconds": (time - validation_start).total_seconds()})
        elif kind == "task_finished":
            finish = event
        elif kind == "passing_pr":
            if finish is None:
                raise ValueError("passing_pr must follow task_finished")
            if finish.get("commitSha") is not None and event["commitSha"] != finish["commitSha"]:
                raise ValueError("passing_pr commitSha does not match the completed task revision")
            if passing is not None:
                raise ValueError("duplicate passing_pr")
            passing = event
    complete = bool(finish and finish["instrumentationComplete"])
    successful_validation = bool(validation_outcomes and not active
                                 and validation_outcomes[-1]["result"] == "pass")
    first_edit_seconds = (edits[0] - started).total_seconds() if edits else None
    return {"trial": start["trial"], "pair": start["pair"], "cohort": start["cohort"],
            "instrumentationComplete": complete, "eventCount": len(events),
            "firstUsefulEditSeconds": first_edit_seconds if complete else None,
            "observedFirstUsefulEditSeconds": first_edit_seconds,
            "repeatedFileReads": repeats if complete else None,
            "observedRepeatedFileReads": repeats,
            "correctionCycles": corrections if complete else None,
            "observedCorrectionCycles": corrections,
            "validationWaitSeconds": union_seconds(intervals) if complete and intervals and not active else None,
            "unmatchedValidations": sorted(active),
            "validationCounts": {result: sum(item["result"] == result for item in validation_outcomes)
                                 for result in ("pass", "fail")},
            "validationOutcomes": validation_outcomes,
            "successfulValidation": successful_validation,
            "comparisonEligible": complete and successful_validation,
            "deliveryCommitSha": finish.get("commitSha") if finish else None,
            "passingPrSeconds": (timestamp(passing["timestamp"]) - started).total_seconds() if passing else None,
            "passingPrEvidence": passing,
            "missingEvidence": (["complete instrumentation"] if not complete else [])
            + (["first useful edit"] if not edits else [])
            + (["matched validation finishes"] if active else [])
            + (["validation evidence"] if not intervals and not active else [])
            + (["final successful validation"] if validation_outcomes and not successful_validation else [])
            + (["passing PR required-check evidence"] if not passing else [])}


def report(events: list[dict[str, Any]], paths: list[Path]) -> dict[str, Any]:
    trials: dict[str, list[dict[str, Any]]] = {}
    for event in events:
        trials.setdefault(event["trial"], []).append(event)
    rows = [summarize_trial(items) for items in trials.values()]
    pairs: dict[str, dict[str, dict[str, Any]]] = {}
    for row in rows:
        cohorts = pairs.setdefault(row["pair"], {})
        if row["cohort"] in cohorts:
            raise ValueError("a pair must have at most one trial per cohort")
        cohorts[row["cohort"]] = row
    comparisons = {}
    for metric in METRICS:
        matched = [p for p in pairs.values() if set(p) == {"baseline", "treatment"}
                   and p["baseline"]["comparisonEligible"] and p["treatment"]["comparisonEligible"]
                   and p["baseline"][metric] is not None and p["treatment"][metric] is not None]
        comparisons[metric] = {
            "matchedPairs": len(matched),
            "baselineMedian": statistics.median(p["baseline"][metric] for p in matched) if matched else None,
            "treatmentMedian": statistics.median(p["treatment"][metric] for p in matched) if matched else None,
            "medianPairedDifference": statistics.median(p["treatment"][metric] - p["baseline"][metric]
                                                       for p in matched) if matched else None}
    return {"schemaVersion": 1, "rawLogs": [str(p) for p in paths], "trials": rows,
            "comparisons": comparisons,
            "interpretation": "Observed small maintenance trials only; no causal or general delivery-speed claim. "
            "Missing evidence is null, not zero. Comparisons require complete instrumentation and a final "
            "successful recorded validation; failed attempt wait is retained. Passing PR evidence is supplied by the recorder; "
            "this offline reporter does not verify GitHub policy or check authenticity."}


def markdown(data: dict[str, Any]) -> str:
    lines = ["# AI throughput pilot observations", "", data["interpretation"], "",
             "| Trial | Cohort | First edit (s) | Repeated reads | Corrections | Validation wait (s) | Passing PR (s) |",
             "| --- | --- | ---: | ---: | ---: | ---: | ---: |"]
    for row in data["trials"]:
        values = ["unavailable" if row[key] is None else str(row[key]) for key in METRICS]
        lines.append("| " + " | ".join([row["trial"], row["cohort"], *values]) + " |")
    lines.extend(["", "Raw logs: " + ", ".join(data["rawLogs"]), ""])
    return "\n".join(lines)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    record = commands.add_parser("record", help="Append one explicit observation with current UTC time")
    record.add_argument("--log", type=Path, required=True)
    record.add_argument("--root", type=Path, default=Path.cwd())
    record.add_argument("--trial", required=True)
    record.add_argument("--event", choices=sorted(EVENTS), required=True)
    record.add_argument("--pair")
    record.add_argument("--cohort", choices=("baseline", "treatment"))
    record.add_argument("--path")
    record.add_argument("--validation-id")
    record.add_argument("--result", choices=("pass", "fail"))
    record.add_argument("--instrumentation-complete", action="store_true")
    record.add_argument("--url")
    record.add_argument("--commit-sha")
    record.add_argument("--checks-json", type=Path)
    output = commands.add_parser("report")
    output.add_argument("--log", type=Path, action="append", required=True)
    output.add_argument("--output", type=Path)
    output.add_argument("--markdown-output", type=Path)
    args = parser.parse_args(argv)
    try:
        if args.command == "record":
            event = {"trial": args.trial, "event": args.event,
                     "timestamp": datetime.now(timezone.utc).isoformat()}
            if args.event == "task_started":
                event.update(pair=args.pair, cohort=args.cohort)
            if args.event in {"file_read", "useful_edit"}:
                if not args.path:
                    raise ValueError("file_read and useful_edit require --path")
                event.update(source(args.root, args.path))
            if args.event.startswith("validation_"):
                event["validationId"] = args.validation_id
            if args.event == "validation_finished":
                event["result"] = args.result
            if args.event == "task_finished":
                event["instrumentationComplete"] = args.instrumentation_complete
                if args.commit_sha:
                    event["commitSha"] = args.commit_sha
            if args.event == "passing_pr":
                event.update(url=args.url, commitSha=args.commit_sha,
                             requiredChecks=json.loads(args.checks_json.read_text()) if args.checks_json else None)
            validate_event(event)
            existing = read_logs([args.log]) if args.log.exists() else []
            report([*existing, event], [args.log])  # reject malformed trial transitions before append
            args.log.parent.mkdir(parents=True, exist_ok=True)
            with args.log.open("a", encoding="utf-8") as stream:
                stream.write(json.dumps(event, sort_keys=True) + "\n")
        else:
            data = report(read_logs(args.log), args.log)
            encoded = json.dumps(data, indent=2) + "\n"
            destinations = [p.resolve() for p in (args.output, args.markdown_output) if p]
            if len(destinations) != len(set(destinations)):
                raise ValueError("JSON and Markdown outputs must be distinct")
            if set(destinations) & {p.resolve() for p in args.log}:
                raise ValueError("report output may not overwrite a raw event log")
            for path in (args.output, args.markdown_output):
                if path and path.exists() and not path.is_file():
                    raise ValueError(f"report output must be a file: {path}")
            for path, content in ((args.output, encoded), (args.markdown_output, markdown(data))):
                if path:
                    path.parent.mkdir(parents=True, exist_ok=True)
                    path.write_text(content, encoding="utf-8")
            if not args.output:
                print(encoded, end="")
        return 0
    except (OSError, ValueError, TypeError) as exc:
        print(f"throughput-metrics: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
