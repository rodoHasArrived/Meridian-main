"""Read-only summaries of local validation receipts; no defect or work-time inference."""
from __future__ import annotations

import argparse
from collections import Counter, defaultdict
from datetime import datetime, timezone
import json
import math
import sys
from pathlib import Path
from statistics import median


TERMINAL = frozenset({"passed", "failed", "blocked", "interrupted"})
STATUSES = ("started", "passed", "failed", "blocked", "interrupted")
SCOPE_FIELDS = ("project", "filter", "configuration", "framework", "runtime")
FULL_GATE_LANES = frozenset({"verify-dotnet", "verify-browser", "verify-docs", "verify-workflows"})


def number(value: object) -> float | None:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return None
    try:
        return float(value) if math.isfinite(value) and value >= 0 else None
    except OverflowError:
        return None


def timestamp(value: object) -> datetime | None:
    if not isinstance(value, str):
        return None
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
        return parsed.astimezone(timezone.utc) if parsed.tzinfo else None
    except (ValueError, OverflowError):
        return None


def elapsed(start: object, finish: object) -> float | None:
    first, last = timestamp(start), timestamp(finish)
    if first is None or last is None or last < first:
        return None
    return (last - first).total_seconds()


def distribution(values: list[object]) -> dict:
    observed = sorted(item for value in values if (item := number(value)) is not None)
    return {
        "samples": len(observed),
        "missing": len(values) - len(observed),
        "median": median(observed) if observed else None,
        "p95": observed[math.ceil(.95 * len(observed)) - 1] if observed else None,
    }


def feedback_seconds(record: dict) -> float | None:
    if record.get("status") not in TERMINAL:
        return None
    if "feedbackSeconds" in record:
        return number(record["feedbackSeconds"])
    return elapsed(record.get("startedAt"), record.get("finishedAt"))


def warm_feedback_seconds(record: dict) -> float | None:
    if record.get("command") != "test" or record.get("cacheState") != "profile-warm":
        return None
    return feedback_seconds(record)


def warm_edit_to_result_seconds(record: dict) -> float | None:
    feedback = warm_feedback_seconds(record)
    edit_start, invocation, finish = (timestamp(record.get(name)) for name in ("editStartedAt", "startedAt", "finishedAt"))
    observed = number(record.get("warmEditToResultSeconds"))
    if (feedback is None or edit_start is None or invocation is None or finish is None
            or not edit_start <= invocation <= finish or observed is None or observed < feedback):
        return None
    return observed


def cold_full_gate_seconds(record: dict) -> float | None:
    if (record.get("command") != "quality-gate" or record.get("cacheState") != "cold-isolated"
            or record.get("freshBuildOutputs") is not True or record.get("status") not in {"passed", "failed"}):
        return None
    completed = record.get("completedLanes")
    if (not isinstance(completed, list) or any(not isinstance(lane, str) for lane in completed)
            or len(completed) != len(FULL_GATE_LANES) or set(completed) != FULL_GATE_LANES):
        return None
    return number(record.get("coldFullGateSeconds"))


def scope(record: dict) -> tuple | None:
    if not all(isinstance(record.get(name), str) and record[name] for name in ("project", "configuration")):
        return None
    raw = tuple(record.get(name) for name in SCOPE_FIELDS)
    if not all(value is None or isinstance(value, str) for value in raw):
        return None
    return tuple(value or None for value in raw)


def failed_phase(record: dict) -> str | None:
    if record.get("status") != "failed":
        return None
    for step in record.get("steps", []):
        code = step.get("exitCode")
        if isinstance(code, int) and not isinstance(code, bool) and code != 0:
            return step["name"]
    declared = record.get("failurePhase")
    return declared if isinstance(declared, str) and declared else "unavailable"


def task_summary(records: list[dict]) -> dict:
    grouped: dict[tuple, list[dict]] = defaultdict(list)
    ungrouped = 0
    for record in records:
        task_id, identity = record.get("taskId"), scope(record)
        if not isinstance(task_id, str) or not task_id.strip() or identity is None:
            ungrouped += 1
            continue
        grouped[(task_id, *identity)].append(record)
    groups = []
    for key, attempts in sorted(grouped.items(), key=lambda item: repr(item[0])):
        starts = [timestamp(item.get("startedAt")) for item in attempts]
        ordered = list(attempts) if all(starts) else []
        # Sort parsed instants rather than textual offsets. Missing starts make ordering unknown.
        if ordered:
            ordered.sort(key=lambda item: timestamp(item["startedAt"]))
        first_pass = None
        if ordered:
            passes = [timestamp(item.get("finishedAt")) for item in ordered
                      if item.get("status") == "passed" and feedback_seconds(item) is not None
                      and elapsed(item.get("startedAt"), item.get("finishedAt")) is not None]
            if passes:
                first_pass = (min(passes) - min(starts)).total_seconds()
        repeats = [feedback_seconds(item) for item in ordered[1:]] if ordered else []
        repeat_seconds = sum(repeats) if ordered and all(value is not None for value in repeats) else None
        groups.append({
            "taskId": key[0],
            "scope": dict(zip(SCOPE_FIELDS, key[1:])),
            "attempts": len(attempts),
            "repeatedAttempts": max(len(attempts) - 1, 0),
            "timeToFirstPassSeconds": first_pass,
            "repeatedAttemptFeedbackSeconds": repeat_seconds,
        })
    return {
        "groupedRuns": len(records) - ungrouped,
        "ungroupedRuns": ungrouped,
        "groups": groups,
        "metrics": {
            name: distribution([item[name] for item in groups])
            for name in ("timeToFirstPassSeconds", "repeatedAttemptFeedbackSeconds")
        },
    }


def summarize(records: list[dict], warnings: list[str] | None = None) -> dict:
    counts = Counter(record.get("status", "unknown") for record in records)
    steps: dict[str, list[object]] = defaultdict(list)
    phases: Counter = Counter()
    for record in records:
        for step in record.get("steps", []):
            steps[step["name"]].append(step.get("durationSeconds"))
        phase = failed_phase(record)
        if phase:
            phases[phase] += 1
    return {
        "schemaVersion": 1,
        "runs": len(records),
        "statuses": {name: counts[name] for name in (*STATUSES, "unknown")},
        "warnings": warnings or [],
        "metrics": {
            "feedbackSeconds": distribution([feedback_seconds(item) for item in records]),
            "queueSeconds": distribution([item.get("queueSeconds") for item in records]),
            "executionSeconds": distribution([item.get("executionSeconds") for item in records]),
            "warmFeedbackSeconds": distribution([warm_feedback_seconds(item) for item in records]),
            "warmEditToResultSeconds": distribution([warm_edit_to_result_seconds(item) for item in records]),
            "coldFullGateSeconds": distribution([cold_full_gate_seconds(item) for item in records]),
        },
        "steps": {name: distribution(values) for name, values in sorted(steps.items())},
        "failurePhases": dict(sorted(phases.items())),
        "tasks": task_summary(records),
        "definitions": {
            "feedbackSeconds": "Explicit monotonic terminal latency including waiting; legacy receipts use valid startedAt-to-finishedAt timestamps. Invalid explicit values and unfinished runs are unavailable.",
            "queueSeconds": "Explicit measured waiting only; unavailable for legacy receipts. Never inferred from step totals.",
            "executionSeconds": "Explicit measured execution only; unavailable when not captured by the runner.",
            "warmFeedbackSeconds": "Feedback from test invocations with a previously successful compatible profile and existing output directories; first profile builds, explicit no-build reuse, and legacy receipts are excluded.",
            "warmEditToResultSeconds": "Explicit observed edit start to terminal result for warm profile test invocations, including queue and execution. Missing edit starts remain unavailable; feedback duration alone is not editing time.",
            "coldFullGateSeconds": "Monotonic full-gate latency with explicitly fresh build outputs and all four completed lanes. Includes failed complete attempts; blocked, interrupted and partial gates are unavailable. Dependency download caches need not be empty.",
            "steps": "Distributions cover recorded occurrences of each phase; phases not executed are excluded, invalid durations are missing.",
            "timeToFirstPassSeconds": "Earliest observed task/scope start to first valid observed pass; not task creation-to-completion or developer work time.",
            "repeatedAttemptFeedbackSeconds": "Sum of terminal feedback durations after the first observed attempt in an explicit task/scope group; not defect attribution, labor time, or a wall-clock union.",
            "tasks": "Only explicit taskId records with comparable project/filter/configuration/framework/runtime are grouped. Unobserved attempts and review rework are unknown.",
            "missing": "Missing, invalid, non-finite, negative and incomplete observations remain unavailable, never zero.",
        },
    }


def load_receipts(directory: Path, task_id: str | None = None) -> tuple[list[dict], list[str]]:
    records, warnings = [], []
    seen, duplicate_ids = set(), set()
    if not directory.exists():
        return records, ["Receipt directory does not exist; no observations."]
    if not directory.is_dir():
        raise ValueError("Receipt path must be a directory.")
    for path in sorted(directory.glob("*.json")):
        if path.is_symlink():
            warnings.append(f"{path.name}: symbolic link excluded")
            continue
        try:
            record = json.loads(path.read_text(encoding="utf-8"))
            if not isinstance(record, dict):
                raise ValueError("receipt must be an object")
            version = record.get("schemaVersion")
            if version is not None and (not isinstance(version, int) or isinstance(version, bool) or version not in (1, 2)):
                raise ValueError("unsupported schemaVersion")
            if record.get("status") not in STATUSES:
                raise ValueError("unrecognized status")
            if not isinstance(record.get("runId"), str) or not record["runId"]:
                raise ValueError("missing runId")
            steps = record.get("steps", [])
            if not isinstance(steps, list) or any(
                not isinstance(step, dict) or not isinstance(step.get("name"), str) or not step["name"]
                for step in steps
            ):
                raise ValueError("invalid steps")
            run_id = record["runId"]
            if run_id in seen:
                duplicate_ids.add(run_id)
                warnings.append(f"{path.name}: duplicate runId; all copies excluded")
            seen.add(run_id)
            if task_id is None or record.get("taskId") == task_id:
                records.append(record)
        except (OSError, UnicodeError, json.JSONDecodeError):
            warnings.append(f"{path.name}: unreadable or malformed JSON receipt")
        except ValueError as exc:
            warnings.append(f"{path.name}: {exc}")
    return [record for record in records if record["runId"] not in duplicate_ids], warnings


def add_parser(subparsers: argparse._SubParsersAction) -> argparse.ArgumentParser:
    parser = subparsers.add_parser("validation-metrics", help="Summarize local validation feedback and observed retries")
    parser.add_argument("--runs-directory", default=".ai/validation-runs", help="Receipt directory, relative to the checkout by default")
    parser.add_argument("--json-output", help="Optional report JSON path; source receipts remain unchanged")
    parser.add_argument("--summary", action="store_true", help="Print a compact readable summary")
    parser.add_argument("--task-id", help="Include only receipts with this explicit task ID")
    return parser


def cmd_validation_metrics(args: argparse.Namespace, repo_root: Path) -> int:
    directory = Path(args.runs_directory)
    if not directory.is_absolute():
        directory = repo_root / directory
    try:
        records, warnings = load_receipts(directory, getattr(args, "task_id", None))
        report = summarize(records, warnings)
        output = getattr(args, "json_output", None)
        if output:
            target = Path(output)
            if not target.is_absolute():
                target = repo_root / target
            # Reporting must never replace a receipt or create new input observations.
            if target.resolve().is_relative_to(directory.resolve()):
                raise ValueError("Report output must be outside the receipt directory.")
            if target.exists() and any(target.samefile(source) for source in directory.glob("*.json")):
                raise ValueError("Report output must not alias a source receipt.")
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(json.dumps(report, indent=2, allow_nan=False) + "\n", encoding="utf-8")
        if getattr(args, "summary", False):
            print(f"Validation observations: {report['runs']}; warnings: {len(warnings)}")
            print("Statuses: " + ", ".join(f"{name}={count}" for name, count in report["statuses"].items()))
            for name, metric in report["metrics"].items():
                print(f"{name}: samples={metric['samples']} missing={metric['missing']} median={metric['median']} p95={metric['p95']}")
            print(f"Task groups: {len(report['tasks']['groups'])}; ungrouped runs: {report['tasks']['ungroupedRuns']}")
            for warning in warnings:
                print(f"Warning: {warning}")
            print("Observed retries are not developer work time or defect attribution.")
        elif not output:
            print(json.dumps(report, indent=2, allow_nan=False))
        return 0
    except (OSError, ValueError) as exc:
        print(f"Unable to report validation metrics: {exc}", file=sys.stderr)
        return 2
