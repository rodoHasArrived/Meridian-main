#!/usr/bin/env python3
"""
Deterministic eval runner for the meridian-implementation-assurance skill.

Reads eval cases from evals/evals.json, captures JSONL traces from `codex exec --json`,
applies deterministic checks, and reports pass/fail per case with optional regression
comparison against evals/benchmark_baseline.json.

Usage (single case):
    python3 scripts/run_evals.py --eval-id 1

Usage (all cases):
    python3 scripts/run_evals.py --all

Usage (summary with regression check):
    python3 scripts/run_evals.py --all --summary

Usage (dry-run — validate setup without running codex):
    python3 scripts/run_evals.py --all --dry-run

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
import os
import re
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

    @property
    def pass_rate(self) -> float:
        total = len(self.passed) + len(self.failed)
        return len(self.passed) / total if total else 0.0

    @property
    def outcome(self) -> str:
        return "PASS" if not self.failed else "FAIL"


# ---------------------------------------------------------------------------
# JSONL parsing helpers
# ---------------------------------------------------------------------------

def parse_jsonl(text: str) -> list[dict[str, Any]]:
    events = []
    for line in text.splitlines():
        line = line.strip()
        if line:
            try:
                events.append(json.loads(line))
            except json.JSONDecodeError:
                pass
    return events


def get_command_events(events: list[dict]) -> list[dict]:
    """Return all command_execution item events from a JSONL trace."""
    return [
        e for e in events
        if e.get("type") in ("item.started", "item.completed")
        and e.get("item", {}).get("type") == "command_execution"
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

def check_ran_build_or_test(cmds: list[str]) -> bool:
    """Did the run include a dotnet build, dotnet test, or python test/validation command?"""
    patterns = [
        r"\bdotnet\b.*(build|test)",
        r"\bpython3?\b.*test",
        r"\bpython3?\b.*validate",
        r"\bmake\b.*(test|build|verify)",
    ]
    for cmd in cmds:
        for pat in patterns:
            if re.search(pat, cmd, re.IGNORECASE):
                return True
    return False


def check_ran_doc_route(cmds: list[str]) -> bool:
    """Did the run invoke doc_route.py?"""
    return any("doc_route" in cmd for cmd in cmds)


def check_ran_score_eval(cmds: list[str]) -> bool:
    """Did the run invoke score_eval.py?"""
    return any("score_eval" in cmd for cmd in cmds)


def check_produced_rubric_output(events: list[dict]) -> bool:
    """Did the run produce a rubric score output (via score_eval.py or inline JSON)?"""
    for e in events:
        content = json.dumps(e)
        if '"behavior_correctness"' in content or '"Behavior Correctness"' in content:
            return True
    return False


def check_command_count(cmds: list[str], max_commands: int = 30) -> bool:
    """Guard against agent thrashing — fail if command count is unreasonably high."""
    return len(cmds) <= max_commands


# ---------------------------------------------------------------------------
# Core runner
# ---------------------------------------------------------------------------

def run_codex(prompt: str, trace_path: Path, dry_run: bool = False) -> tuple[int, str]:
    """Run codex exec and save JSONL trace. Returns (exit_code, stderr)."""
    if dry_run:
        trace_path.parent.mkdir(parents=True, exist_ok=True)
        trace_path.write_text('{"type":"turn.completed","usage":{"input_tokens":100,"output_tokens":200}}\n', encoding="utf-8")
        return 0, "[dry-run: codex exec skipped]"

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
    """Apply deterministic checks against an eval case and return CaseResult."""
    result = CaseResult(eval_id=case.id, description=case.description)
    result.token_usage = get_token_usage(events)

    # Deterministic checks that apply to every case
    if check_ran_build_or_test(cmds):
        result.passed.append("ran build/test command")
    else:
        result.failed.append("no build/test command found in trace")

    if check_produced_rubric_output(events):
        result.passed.append("produced rubric output")
    else:
        result.failed.append("no rubric output detected in trace")

    if check_command_count(cmds):
        result.passed.append(f"command count within budget ({len(cmds)} <= 30)")
    else:
        result.failed.append(f"command thrashing detected ({len(cmds)} commands)")

    # Scenario-specific checks
    if case.scenario in ("B",):
        if check_ran_doc_route(cmds):
            result.passed.append("doc_route.py invoked for new doc placement")
        else:
            result.failed.append("doc_route.py not invoked (required for Scenario B — new doc needed)")

    if case.scenario in ("A", "B", "C"):
        if check_ran_score_eval(cmds):
            result.passed.append("score_eval.py invoked for rubric scoring")
        else:
            result.skipped.append("score_eval.py not detected (recommended but not required)")

    return result


def run_case(case: EvalCase, dry_run: bool = False) -> CaseResult:
    trace_path = ARTIFACTS_DIR / f"eval-{case.id}.jsonl"
    exit_code, stderr = run_codex(case.prompt, trace_path, dry_run=dry_run)

    if not dry_run and not trace_path.exists():
        r = CaseResult(eval_id=case.id, description=case.description, exit_code=exit_code)
        r.failed.append("no JSONL trace produced — check codex exec installation")
        return r

    raw = trace_path.read_text(encoding="utf-8") if trace_path.exists() else ""
    events = parse_jsonl(raw)
    cmds = commands_run(events)

    result = score_case(case, events, cmds)
    result.exit_code = exit_code
    return result


# ---------------------------------------------------------------------------
# Prompt CSV validation (trigger classification check)
# ---------------------------------------------------------------------------

def validate_prompts_csv() -> tuple[int, int]:
    """Parse the prompts CSV and return (should_trigger_count, should_not_trigger_count)."""
    if not PROMPTS_CSV.exists():
        print(f"  warning: prompts CSV not found at {PROMPTS_CSV}", file=sys.stderr)
        return 0, 0

    should_trigger = 0
    should_not = 0
    with PROMPTS_CSV.open(newline="", encoding="utf-8") as f:
        reader = csv.DictReader(f)
        for row in reader:
            if row.get("should_trigger", "").strip().lower() == "true":
                should_trigger += 1
            else:
                should_not += 1
    return should_trigger, should_not


# ---------------------------------------------------------------------------
# Regression check
# ---------------------------------------------------------------------------

def regression_check(results: list[CaseResult]) -> list[str]:
    """Compare pass rates against benchmark_baseline.json. Return warning messages."""
    if not BASELINE_JSON.exists():
        return []

    baseline = json.loads(BASELINE_JSON.read_text(encoding="utf-8"))
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


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------

def load_evals() -> list[EvalCase]:
    data = json.loads(EVALS_JSON.read_text(encoding="utf-8"))
    return [
        EvalCase(
            id=e["id"],
            scenario=e["scenario"],
            description=e["description"],
            prompt=e["prompt"],
            assertions=e["assertions"],
        )
        for e in data["evals"]
    ]


def print_result(r: CaseResult) -> None:
    print(f"\n--- Eval {r.eval_id}: {r.description} ---")
    print(f"  Outcome : {r.outcome}  (pass_rate={r.pass_rate:.0%})")
    if r.token_usage:
        print(f"  Tokens  : in={r.token_usage.get('input_tokens', 0)} out={r.token_usage.get('output_tokens', 0)}")
    for msg in r.passed:
        print(f"  ✓  {msg}")
    for msg in r.failed:
        print(f"  ✗  {msg}")
    for msg in r.skipped:
        print(f"  ~  {msg}")


def print_summary(results: list[CaseResult], regressions: list[str]) -> None:
    total = len(results)
    passing = sum(1 for r in results if r.outcome == "PASS")
    overall_rate = passing / total if total else 0.0

    print(f"\n{'='*60}")
    print(f"SUMMARY: {passing}/{total} cases passed  ({overall_rate:.0%})")
    if regressions:
        print("\nREGRESSION WARNINGS:")
        for w in regressions:
            print(f"  ⚠  {w}")
    else:
        print("  No regressions vs baseline.")
    print("="*60)


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description="Run meridian-implementation-assurance skill evals.")
    group = p.add_mutually_exclusive_group(required=True)
    group.add_argument("--eval-id", type=int, metavar="N", help="Run a single eval by ID.")
    group.add_argument("--all", action="store_true", help="Run all eval cases.")
    p.add_argument("--dry-run", action="store_true", help="Validate setup without calling codex exec.")
    p.add_argument("--summary", action="store_true", help="Print aggregate summary and regression check.")
    p.add_argument("--json", action="store_true", help="Emit machine-readable JSON results to stdout.")
    return p.parse_args()


def main() -> int:
    args = parse_args()
    cases = load_evals()

    if args.eval_id:
        cases = [c for c in cases if c.id == args.eval_id]
        if not cases:
            print(f"error: eval-id {args.eval_id} not found in {EVALS_JSON}", file=sys.stderr)
            return 2

    if args.dry_run:
        print(f"[dry-run] Validating {len(cases)} eval case(s) without calling codex exec.", file=sys.stderr)
        st, sn = validate_prompts_csv()
        print(f"[dry-run] Prompts CSV: {st} should-trigger, {sn} should-not-trigger", file=sys.stderr)

    results = []
    for case in cases:
        if not args.dry_run:
            print(f"Running eval {case.id}: {case.description} ...", file=sys.stderr)
        r = run_case(case, dry_run=args.dry_run)
        results.append(r)
        if not args.json:
            print_result(r)

    regressions = regression_check(results) if args.summary else []

    if args.summary and not args.json:
        print_summary(results, regressions)

    if args.json:
        output = {
            "results": [
                {
                    "eval_id": r.eval_id,
                    "description": r.description,
                    "outcome": r.outcome,
                    "pass_rate": r.pass_rate,
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
