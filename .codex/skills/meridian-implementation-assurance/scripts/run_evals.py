#!/usr/bin/env python3
"""
Deterministic eval runner for the meridian-implementation-assurance skill.

Reads eval cases from evals/evals.json, validates fixture shape by default, and only captures
JSONL traces from `codex exec --json` when --live-run is explicitly passed. Live runs can mutate
the checkout through the prompts under test; run them only from an isolated worktree or scratch
clone.

Usage (single live case):
    python scripts/run_evals.py --eval-id 1 --live-run

Usage (all live cases):
    python scripts/run_evals.py --all --live-run

Usage (summary with regression check):
    python scripts/run_evals.py --all --summary --live-run

Usage (dry-run - validate setup without running codex):
    python scripts/run_evals.py --all --dry-run

The runner calls:
    codex exec --json --full-auto "<prompt>"

and saves JSONL traces to evals/artifacts/<eval-id>.jsonl for inspection.

JSONL event types checked:
- item.started / item.completed with item.type == "command_execution"
- item.completed with item.type == "file_write" (artifact existence)
- turn.completed (token usage in e.usage.input_tokens / e.usage.output_tokens)
"""

from __future__ import annotations

import argparse
import csv
import json
import re
import shlex
import subprocess
import sys
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

SKILL_DIR = Path(__file__).resolve().parent.parent
EVALS_DIR = SKILL_DIR / "evals"
ARTIFACTS_DIR = EVALS_DIR / "artifacts"
EVALS_JSON = EVALS_DIR / "evals.json"
BASELINE_JSON = EVALS_DIR / "benchmark_baseline.json"
PROMPTS_CSV = EVALS_DIR / "meridian-implementation-assurance.prompts.csv"
TRACE_CHECK_VERSION = "1"


# ---------------------------------------------------------------------------
# Data models
# ---------------------------------------------------------------------------

@dataclass
class EvalCase:
    id: int
    scenario: str
    description: str
    prompt: str
    assertions: list[str]


@dataclass
class CaseResult:
    eval_id: int
    description: str
    passed: list[str] = field(default_factory=list)
    failed: list[str] = field(default_factory=list)
    skipped: list[str] = field(default_factory=list)
    exit_code: int = 0
    token_usage: dict[str, int] = field(default_factory=dict)
    assertions_require_review: list[str] = field(default_factory=list)

    @property
    def pass_rate(self) -> float:
        total = len(self.passed) + len(self.failed)
        return len(self.passed) / total if total else 0.0

    @property
    def outcome(self) -> str:
        return "PASS" if not self.failed and self.exit_code == 0 else "FAIL"


# ---------------------------------------------------------------------------
# JSONL parsing helpers
# ---------------------------------------------------------------------------

def parse_jsonl(text: str) -> list[dict[str, Any]]:
    events = []
    for line_number, line in enumerate(text.splitlines(), 1):
        line = line.strip()
        if line:
            try:
                event = json.loads(line)
            except json.JSONDecodeError as exc:
                raise ValueError(f"Invalid JSONL event on line {line_number}: {exc.msg}") from exc
            if not isinstance(event, dict):
                raise ValueError(f"JSONL event on line {line_number} must be an object")
            if not isinstance(event.get("type"), str):
                raise ValueError(f"JSONL event on line {line_number} must have a string type")
            if event["type"] in ("item.started", "item.completed"):
                item = event.get("item")
                if not isinstance(item, dict) or not isinstance(item.get("type"), str):
                    raise ValueError(f"JSONL item on line {line_number} must be an object with a string type")
                if "id" in item and not isinstance(item["id"], str):
                    raise ValueError(f"JSONL item ID on line {line_number} must be a string")
                if item["type"] == "command_execution" and not isinstance(item.get("command"), str):
                    raise ValueError(f"JSONL command on line {line_number} must be a string")
            if event["type"] == "turn.completed" and not isinstance(event.get("usage", {}), dict):
                raise ValueError(f"JSONL usage on line {line_number} must be an object")
            events.append(event)
    return events


def get_command_events(events: list[dict]) -> list[dict]:
    """Return one terminal event per command, retaining unfinished commands for the budget."""
    by_id: dict[str, dict] = {}
    anonymous: list[dict] = []
    for event in events:
        if event.get("type") not in ("item.started", "item.completed"):
            continue
        item = event.get("item", {})
        if not isinstance(item, dict) or item.get("type") != "command_execution":
            continue
        item_id = item.get("id")
        if item_id:
            if item_id not in by_id or event["type"] == "item.completed":
                by_id[item_id] = event
        elif event["type"] == "item.completed":
            anonymous.append(event)
    return [
        *by_id.values(), *anonymous,
    ]


def commands_run(events: list[dict]) -> list[str]:
    """Extract command strings from the JSONL trace."""
    cmds = []
    for e in get_command_events(events):
        cmd = e.get("item", {}).get("command", "")
        if cmd:
            cmds.append(cmd)
    return cmds


def get_token_usage(events: list[dict]) -> dict[str, int]:
    """Extract token usage from turn.completed events."""
    for e in events:
        if e.get("type") == "turn.completed":
            usage = e.get("usage", {})
            if usage:
                return {
                    "input_tokens": usage.get("input_tokens", 0),
                    "output_tokens": usage.get("output_tokens", 0),
                }
    return {}


def get_file_writes(events: list[dict]) -> list[str]:
    """Extract file paths written during the run."""
    paths = []
    for e in events:
        if (
            e.get("type") == "item.completed"
            and e.get("item", {}).get("type") == "file_write"
        ):
            path = e.get("item", {}).get("path", "")
            if path:
                paths.append(path)
    return paths


# ---------------------------------------------------------------------------
# Deterministic checks
# ---------------------------------------------------------------------------

def command_invocations(command: str) -> list[list[str]]:
    """Recognize common CLI/shell invocations, excluding quoted command mentions.

    This is a trace heuristic, not a shell interpreter or a proof of behavior.
    """
    try:
        lexer = shlex.shlex(command, posix=True, punctuation_chars=";&|")
        lexer.whitespace_split = True
        tokens = list(lexer)
    except ValueError:
        return []
    segments: list[list[str]] = [[]]
    for token in tokens:
        if token in (";", "&&", "||", "|", "&"):
            segments.append([])
        else:
            segments[-1].append(token)
    invocations = []
    for segment in segments:
        if not segment:
            continue
        program = Path(segment[0]).name.casefold().removesuffix(".exe")
        if program in ("bash", "sh", "zsh", "pwsh", "powershell"):
            for index, token in enumerate(segment[1:], 1):
                if token.casefold() in ("-c", "-lc", "-command") and index + 1 < len(segment):
                    invocations.extend(command_invocations(segment[index + 1]))
                    break
            else:
                invocations.append(segment)
        else:
            invocations.append(segment)
    return invocations


def check_ran_build_or_test(cmds: list[str]) -> bool:
    """Recognize validation invocations; callers must first filter successful events."""
    for command in cmds:
        for tokens in command_invocations(command):
            program = Path(tokens[0]).name.casefold().removesuffix(".exe")
            args = tokens[1:]
            if program == "dotnet" and args and args[0] in ("build", "test"):
                return True
            if program in ("python", "python3", "py"):
                if len(args) >= 2 and args[0] == "-m" and args[1] in ("unittest", "pytest", "compileall"):
                    return True
                if args and not args[0].startswith("-"):
                    script = Path(args[0]).name.casefold()
                    if re.search(r"^(check[-_]|validate[-_]|test[-_])|test.*\.py$", script):
                        return True
            if program == "make" and any(re.search(r"(?:^|[-_])(test|build|verify|check)(?:$|[-_])", arg) for arg in args):
                return True
            if program in ("npm", "npm.cmd") and any(arg in ("test", "build", "lint", "typecheck", "check") for arg in args):
                return True
            if program == "git" and args[:1] == ["diff"] and "--check" in args:
                return True
            if program in ("bash", "sh") and args and Path(args[0]).name == "ci.sh":
                return True
    return False


def successful_command_events(events: list[dict]) -> list[dict]:
    return [event for event in get_command_events(events)
            if event.get("type") == "item.completed"
            and type(event["item"].get("exit_code")) is int
            and event["item"]["exit_code"] == 0
            and event["item"].get("status") not in ("failed", "in_progress")]


def check_ran_doc_route(cmds: list[str]) -> bool:
    """Did the run invoke doc_route.py?"""
    return any("doc_route" in cmd for cmd in cmds)


def check_ran_score_eval(cmds: list[str]) -> bool:
    """Did the run invoke score_eval.py?"""
    return any(
        len(tokens) >= 2
        and Path(tokens[0]).name.casefold().removesuffix(".exe") in ("python", "python3", "py")
        and Path(tokens[1]).name == "score_eval.py"
        for cmd in cmds for tokens in command_invocations(cmd)
    )


def _contains_rubric_marker(obj: Any, marker: str) -> bool:
    """Recursively check whether nested content contains a rubric marker.

    Supports both structured JSON keys and string payloads (stdout/messages)
    where rubric labels are commonly rendered as Markdown text.
    """
    marker_folded = marker.casefold()

    if isinstance(obj, dict):
        if any(k.casefold() == marker_folded for k in obj):
            return True
        return any(_contains_rubric_marker(v, marker) for v in obj.values())
    if isinstance(obj, list):
        return any(_contains_rubric_marker(item, marker) for item in obj)
    if isinstance(obj, str):
        return marker_folded in obj.casefold()
    return False


def check_produced_rubric_output(events: list[dict]) -> bool:
    """Look for all categories in completed agent output or successful scorer stdout.

    This checks output presence only. Rubric accuracy and case assertions need review.
    """
    outputs = []
    for event in events:
        item = event.get("item", {})
        if event.get("type") == "item.completed" and isinstance(item, dict) and item.get("type") == "agent_message":
            outputs.append(item.get("text", ""))
    for event in successful_command_events(events):
        item = event["item"]
        if check_ran_score_eval([item.get("command", "")]):
            outputs.append(item.get("aggregated_output", ""))
    categories = ("behavior_correctness", "validation_evidence", "performance_safety", "documentation_sync", "traceable_summary")
    return any(all(_contains_rubric_marker(output, category)
                   or _contains_rubric_marker(output, category.replace("_", " "))
                   for category in categories) for output in outputs)


def check_command_count(cmds: list[str], max_commands: int = 30) -> bool:
    """Guard against agent thrashing — fail if command count is unreasonably high."""
    return len(cmds) <= max_commands


# ---------------------------------------------------------------------------
# Core runner
# ---------------------------------------------------------------------------

def run_codex(prompt: str, trace_path: Path) -> tuple[int, str]:
    """Run codex exec and save JSONL trace. Returns (exit_code, stderr)."""
    ARTIFACTS_DIR.mkdir(parents=True, exist_ok=True)
    result = subprocess.run(
        ["codex", "exec", "--json", "--full-auto", prompt],
        capture_output=True,
        text=True,
        encoding="utf-8",
    )
    trace_path.write_text(result.stdout or "", encoding="utf-8")
    return result.returncode, result.stderr


def score_case(case: EvalCase, events: list[dict], cmds: list[str]) -> CaseResult:
    """Apply trace heuristics; prose assertions remain unevaluated."""
    result = CaseResult(eval_id=case.id, description=case.description)
    result.token_usage = get_token_usage(events)
    result.assertions_require_review = list(case.assertions)
    successful_cmds = [e["item"].get("command", "") for e in successful_command_events(events)]

    if any(event.get("type") in ("turn.failed", "error") for event in events):
        result.failed.append("Codex trace contains a failed turn or error")
    if not any(event.get("type") == "turn.completed" for event in events):
        result.failed.append("no completed turn in trace")

    # Deterministic checks that apply to every case
    if check_ran_build_or_test(successful_cmds):
        result.passed.append("successful completed validation command detected")
    else:
        result.failed.append("no successful completed validation command found in trace")

    if check_produced_rubric_output(events):
        result.passed.append("produced rubric output")
    else:
        result.failed.append("no rubric output detected in trace")

    if check_command_count(cmds):
        result.passed.append(f"command count within budget ({len(cmds)} <= 30)")
    else:
        result.failed.append(f"command thrashing detected ({len(cmds)} commands)")

    # Tool choice does not prove documentation placement or cross-link correctness.
    if case.scenario == "B":
        result.skipped.append("documentation placement and cross-links require assertion review; doc_route.py is advisory")

    if not check_ran_score_eval(successful_cmds):
        result.skipped.append("score_eval.py not detected (recommended but not required)")

    return result


def run_case(case: EvalCase, dry_run: bool = False) -> CaseResult:
    if dry_run:
        raise ValueError("Dry-run validates fixtures only; it must not score synthetic traces")
    trace_path = ARTIFACTS_DIR / f"eval-{case.id}.jsonl"
    exit_code, stderr = run_codex(case.prompt, trace_path)

    if not dry_run and not trace_path.exists():
        r = CaseResult(eval_id=case.id, description=case.description, exit_code=exit_code)
        r.failed.append("no JSONL trace produced — check codex exec installation")
        return r

    raw = trace_path.read_text(encoding="utf-8") if trace_path.exists() else ""
    try:
        events = parse_jsonl(raw)
    except ValueError as exc:
        result = CaseResult(eval_id=case.id, description=case.description, exit_code=exit_code,
                            assertions_require_review=list(case.assertions))
        result.failed.append(str(exc))
        return result
    cmds = commands_run(events)

    result = score_case(case, events, cmds)
    result.exit_code = exit_code
    if exit_code != 0:
        result.failed.append(f"codex exec exited with status {exit_code}")
    return result


# ---------------------------------------------------------------------------
# Prompt CSV validation (trigger classification check)
# ---------------------------------------------------------------------------

def validate_prompts_csv() -> tuple[int, int]:
    """Validate trigger fixture structure, without testing model trigger selection."""
    if not PROMPTS_CSV.exists():
        raise ValueError(f"Prompts CSV not found at {PROMPTS_CSV}")

    should_trigger = 0
    should_not = 0
    with PROMPTS_CSV.open(newline="", encoding="utf-8") as f:
        reader = csv.DictReader(f, strict=True)
        if reader.fieldnames != ["id", "should_trigger", "prompt"]:
            raise ValueError("Prompts CSV must have columns id,should_trigger,prompt")
        seen = set()
        for line, row in enumerate(reader, 2):
            if None in row or any(not isinstance(row.get(key), str) or not row[key].strip()
                                  for key in ("id", "should_trigger", "prompt")):
                raise ValueError(f"Prompts CSV row {line} must contain a non-empty id, label, and prompt")
            if row["id"] in seen:
                raise ValueError(f"Duplicate prompt ID {row['id']!r} on row {line}")
            seen.add(row["id"])
            label = row["should_trigger"].strip().lower()
            if label not in ("true", "false"):
                raise ValueError(f"Prompts CSV row {line} should_trigger must be true or false")
            if label == "true":
                should_trigger += 1
            else:
                should_not += 1
    if not should_trigger or not should_not:
        raise ValueError("Prompts CSV must include positive and negative trigger controls")
    return should_trigger, should_not


# ---------------------------------------------------------------------------
# Regression check
# ---------------------------------------------------------------------------

def baseline_comparison(baseline: dict, *, requested: bool) -> dict[str, Any]:
    """Keep historical checksets separate from the current trace heuristics."""
    baseline_version = baseline.get("trace_check_version")
    comparable = baseline_version == TRACE_CHECK_VERSION
    return {
        "status": ("not_requested" if not requested else
                   "compared" if comparable else "skipped_incompatible_version"),
        "comparable": comparable,
        "trace_check_version": TRACE_CHECK_VERSION,
        "baseline_trace_check_version": baseline_version,
    }


def regression_check(results: list[CaseResult], baseline: dict | None = None) -> list[str]:
    """Compare only baseline rates produced by the same trace checkset."""
    if baseline is None:
        baseline = load_baseline()
    if not baseline_comparison(baseline, requested=True)["comparable"]:
        return []
    threshold = baseline.get("regression_threshold_pp", 10) / 100.0
    warnings = []

    by_id = {r.eval_id: r for r in results}
    for entry in baseline.get("baselines", []):
        eid = entry["eval_id"]
        accepted = entry["accepted_pass_rate"]
        if eid in by_id:
            actual = by_id[eid].pass_rate
            if actual < accepted - threshold:
                warnings.append(
                    f"REGRESSION eval-{eid}: pass_rate={actual:.0%} is "
                    f"{(accepted - actual) * 100:.0f}pp below baseline {accepted:.0%}"
                )
    return warnings


def load_baseline(cases: list[EvalCase] | None = None) -> dict:
    """Validate baseline structure without treating its accepted rates as new evidence."""
    baseline = json.loads(BASELINE_JSON.read_text(encoding="utf-8"))
    if not isinstance(baseline, dict) or baseline.get("skill_name") != "meridian-implementation-assurance":
        raise ValueError("Baseline must identify meridian-implementation-assurance")
    entries = baseline.get("baselines")
    if not isinstance(entries, list) or not entries:
        raise ValueError("Baseline must contain a non-empty baselines list")
    threshold = baseline.get("regression_threshold_pp", 10)
    if type(threshold) not in (int, float) or not 0 <= threshold <= 100:
        raise ValueError("Baseline regression_threshold_pp must be a number from 0 to 100")
    seen = set()
    for entry in entries:
        if not isinstance(entry, dict):
            raise ValueError("Each baseline entry must be an object")
        case_id = entry.get("eval_id")
        if type(case_id) is not int or case_id <= 0 or case_id in seen:
            raise ValueError("Baseline entries must have unique positive integer eval_id values")
        seen.add(case_id)
        rate = entry.get("accepted_pass_rate")
        if type(rate) not in (int, float) or not 0 <= rate <= 1:
            raise ValueError(f"Baseline {case_id} accepted_pass_rate must be a number from 0 to 1")
    if cases is not None and seen != {case.id for case in cases}:
        raise ValueError("Baseline eval IDs must match eval fixture IDs")
    return baseline


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------

def load_evals() -> list[EvalCase]:
    data = json.loads(EVALS_JSON.read_text(encoding="utf-8"))
    if not isinstance(data, dict) or data.get("skill_name") != "meridian-implementation-assurance":
        raise ValueError("Eval manifest must identify meridian-implementation-assurance")
    if not isinstance(data.get("evals"), list) or not data["evals"]:
        raise ValueError("Eval manifest must contain a non-empty evals list")
    cases = []
    seen = set()
    for position, entry in enumerate(data["evals"], 1):
        if not isinstance(entry, dict):
            raise ValueError(f"Eval item {position} must be an object")
        case_id = entry.get("id")
        if type(case_id) is not int or case_id <= 0 or case_id in seen:
            raise ValueError(f"Eval item {position} must have a unique positive integer id")
        seen.add(case_id)
        if entry.get("scenario") not in ("A", "B", "C"):
            raise ValueError(f"Eval {case_id} scenario must be A, B, or C")
        for key in ("description", "prompt"):
            if not isinstance(entry.get(key), str) or not entry[key].strip():
                raise ValueError(f"Eval {case_id} must have a non-empty {key}")
        assertions = entry.get("assertions")
        if not isinstance(assertions, list) or not assertions or any(not isinstance(item, str) or not item.strip() for item in assertions):
            raise ValueError(f"Eval {case_id} assertions must be a non-empty list of non-empty strings")
        cases.append(EvalCase(case_id, entry["scenario"], entry["description"], entry["prompt"], assertions))
    return cases


def print_result(r: CaseResult) -> None:
    print(f"\n--- Eval {r.eval_id}: {r.description} ---")
    print(f"  Trace checks: {r.outcome}  (heuristic pass rate={r.pass_rate:.0%})")
    print(f"  Assertions: {len(r.assertions_require_review)} require semantic review; not evaluated")
    if r.token_usage:
        print(f"  Tokens  : in={r.token_usage.get('input_tokens', 0)} out={r.token_usage.get('output_tokens', 0)}")
    for msg in r.passed:
        print(f"  {_console_marker('✓', '+')}  {msg}")
    for msg in r.failed:
        print(f"  {_console_marker('✗', 'x')}  {msg}")
    for msg in r.skipped:
        print(f"  ~  {msg}")


def print_summary(results: list[CaseResult], regressions: list[str], comparison: dict) -> None:
    total = len(results)
    passing = sum(1 for r in results if r.outcome == "PASS")
    overall_rate = passing / total if total else 0.0

    print(f"\n{'='*60}")
    print(f"TRACE CHECK SUMMARY: {passing}/{total} traces passed  ({overall_rate:.0%})")
    print("  Prose assertions and trigger selection were not evaluated.")
    if comparison["status"] == "skipped_incompatible_version":
        print("  Baseline comparison skipped: historical trace checkset version "
              f"{comparison['baseline_trace_check_version']!r} does not match {TRACE_CHECK_VERSION!r}.")
    elif comparison["status"] == "not_requested":
        print("  Baseline comparison not requested.")
    elif regressions:
        print("\nREGRESSION WARNINGS:")
        for w in regressions:
            print(f"  {_console_marker('⚠', '!')}  {w}")
    else:
        print("  No trace-check regressions vs baseline; this is not behavior certification.")
    print("="*60)


def _console_marker(symbol: str, fallback: str) -> str:
    encoding = sys.stdout.encoding or "utf-8"
    try:
        symbol.encode(encoding)
    except UnicodeEncodeError:
        return fallback
    return symbol


def print_dry_run_summary(cases: list[EvalCase], should_trigger: int, should_not: int) -> None:
    print("\n--- Dry Run Fixture Validation Only ---")
    print("  No behavior, trigger selection, assertions, or baseline promotion evaluated.")
    print(f"  Eval cases: {len(cases)}")
    print(f"  Prompts CSV: {should_trigger} should-trigger, {should_not} should-not-trigger")
    for case in cases:
        print(f"  eval-{case.id}: Scenario {case.scenario} - {case.description}")


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description="Run meridian-implementation-assurance skill evals.")
    group = p.add_mutually_exclusive_group(required=True)
    group.add_argument("--eval-id", type=int, metavar="N", help="Run a single eval by ID.")
    group.add_argument("--all", action="store_true", help="Run all eval cases.")
    p.add_argument(
        "--dry-run",
        action="store_true",
        help="Validate setup without calling codex exec. This is the default unless --live-run is passed.",
    )
    p.add_argument(
        "--live-run",
        action="store_true",
        help="Run codex exec for selected cases. Use only from an isolated worktree or scratch clone.",
    )
    p.add_argument("--summary", action="store_true", help="Print aggregate summary and regression check.")
    p.add_argument("--json", action="store_true", help="Emit machine-readable JSON results to stdout.")
    return p.parse_args()


def main() -> int:
    args = parse_args()
    if args.dry_run and args.live_run:
        print("error: --dry-run and --live-run are mutually exclusive", file=sys.stderr)
        return 2

    try:
        cases = load_evals()
        st, sn = validate_prompts_csv()
        baseline = load_baseline(cases)
    except (OSError, ValueError, csv.Error) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2

    if args.eval_id is not None:
        cases = [c for c in cases if c.id == args.eval_id]
        if not cases:
            print(f"error: eval-id {args.eval_id} not found in {EVALS_JSON}", file=sys.stderr)
            return 2

    dry_run = args.dry_run or not args.live_run

    if dry_run:
        print(f"[dry-run] Validating {len(cases)} eval case(s) without calling codex exec.", file=sys.stderr)
        print(f"[dry-run] Prompts CSV: {st} should-trigger, {sn} should-not-trigger", file=sys.stderr)
        if args.json:
            print(json.dumps(
                {
                    "dry_run": True,
                    "validation_scope": "fixture_structure_only",
                    "behavior_evaluated": False,
                    "trigger_selection_evaluated": False,
                    "assertions_evaluated": 0,
                    "baseline_promoted": False,
                    "trace_check_version": TRACE_CHECK_VERSION,
                    "baseline_comparison": baseline_comparison(baseline, requested=False),
                    "eval_count": len(cases),
                    "prompts_csv": {
                        "should_trigger": st,
                        "should_not_trigger": sn,
                    },
                    "cases": [
                        {
                            "eval_id": case.id,
                            "scenario": case.scenario,
                            "description": case.description,
                        }
                        for case in cases
                    ],
                },
                indent=2,
            ))
        else:
            print_dry_run_summary(cases, st, sn)
        return 0

    print(
        "[live-run] Running codex exec. Ensure this is an isolated worktree or scratch clone.",
        file=sys.stderr,
    )

    results = []
    for case in cases:
        print(f"Running eval {case.id}: {case.description} ...", file=sys.stderr)
        try:
            r = run_case(case, dry_run=False)
        except OSError as exc:
            r = CaseResult(eval_id=case.id, description=case.description, exit_code=1,
                           assertions_require_review=list(case.assertions))
            r.failed.append(f"codex exec could not run: {exc}")
        results.append(r)
        if not args.json:
            print_result(r)

    comparison = baseline_comparison(baseline, requested=args.summary)
    regressions = regression_check(results, baseline) if args.summary else []

    if args.summary and not args.json:
        print_summary(results, regressions, comparison)

    if args.json:
        output = {
            "validation_scope": "trace_heuristics_only",
            "behavior_evaluated": False,
            "trigger_selection_evaluated": False,
            "baseline_promoted": False,
            "trace_check_version": TRACE_CHECK_VERSION,
            "baseline_comparison": comparison,
            "results": [
                {
                    "eval_id": r.eval_id,
                    "description": r.description,
                    "trace_check_outcome": r.outcome,
                    "trace_check_pass_rate": r.pass_rate,
                    "process_exit_code": r.exit_code,
                    "assertions_evaluated": 0,
                    "assertions_require_review": r.assertions_require_review,
                    "passed": r.passed,
                    "failed": r.failed,
                    "skipped": r.skipped,
                    "token_usage": r.token_usage,
                }
                for r in results
            ],
            "regressions": regressions,
        }
        print(json.dumps(output, indent=2))

    any_failed = any(r.outcome == "FAIL" for r in results)
    return 1 if any_failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
