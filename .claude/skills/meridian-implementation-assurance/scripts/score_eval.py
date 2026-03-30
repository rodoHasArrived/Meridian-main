#!/usr/bin/env python3
"""Score meridian-implementation-assurance eval rubric and emit a report."""

from __future__ import annotations

import argparse
import json
import sys
from dataclasses import dataclass

CATEGORIES = [
    "behavior_correctness",
    "validation_evidence",
    "performance_safety",
    "documentation_sync",
    "traceable_summary",
]


@dataclass
class EvalResult:
    scenario: str
    scores: dict[str, int]
    failed_checks: list[str]
    follow_ups: list[str]

    @property
    def total(self) -> int:
        return sum(self.scores.values())

    @property
    def outcome(self) -> str:
        has_zero = any(v == 0 for v in self.scores.values())
        return "Pass" if self.total >= 8 and not has_zero else "Fail"


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description="Score rubric for Meridian Implementation Assurance skill.")
    p.add_argument("--scenario", required=True, choices=["A", "B", "C"], help="Evaluated scenario ID.")
    p.add_argument("--scores", required=True, help="JSON object of category->score (0-2).")
    p.add_argument("--failed-check", action="append", default=[], help="Failed check description.")
    p.add_argument("--follow-up", action="append", default=[], help="Corrective follow-up action.")
    p.add_argument("--json", action="store_true", help="Emit machine-readable JSON.")
    return p.parse_args()


def validate_scores(raw: dict[str, object]) -> dict[str, int]:
    missing = [c for c in CATEGORIES if c not in raw]
    extra = [k for k in raw if k not in CATEGORIES]
    if missing or extra:
        raise ValueError(
            f"Invalid score keys. Missing={missing or 'none'} Extra={extra or 'none'} Expected={CATEGORIES}"
        )

    scores: dict[str, int] = {}
    for k in CATEGORIES:
        try:
            val = int(raw[k])
        except (ValueError, TypeError) as exc:  # noqa: BLE001
            raise ValueError(f"Score for '{k}' is not an integer: {raw[k]!r}") from exc
        if val < 0 or val > 2:
            raise ValueError(f"Score for '{k}' must be between 0 and 2. Got {val}.")
        scores[k] = val
    return scores


def to_markdown(result: EvalResult) -> str:
    pretty = {
        "behavior_correctness": "Behavior Correctness",
        "validation_evidence": "Validation Evidence",
        "performance_safety": "Performance Safety",
        "documentation_sync": "Documentation Sync",
        "traceable_summary": "Traceable Summary",
    }
    rows = "\n".join(
        f"| {pretty[k]} | {v} |  |" for k, v in result.scores.items()
    )

    failed_lines = "\n".join(f"  - {x}" for x in (result.failed_checks or ["none"]))
    follow_lines = "\n".join(f"  - {x}" for x in (result.follow_ups or ["none"]))

    return (
        "### Skill Eval Report\n\n"
        f"- Scenario: {result.scenario}\n"
        f"- Total Score: {result.total}/10\n"
        f"- Outcome: {result.outcome}\n\n"
        "| Category | Score (0-2) | Evidence |\n"
        "|---|---:|---|\n"
        f"{rows}\n\n"
        "- Failed checks:\n"
        f"{failed_lines}\n"
        "- Corrective follow-ups:\n"
        f"{follow_lines}\n"
    )


def main() -> int:
    args = parse_args()
    try:
        raw = json.loads(args.scores)
        if not isinstance(raw, dict):
            raise ValueError("--scores must decode to a JSON object.")
        scores = validate_scores(raw)
    except ValueError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2

    result = EvalResult(
        scenario=args.scenario,
        scores=scores,
        failed_checks=args.failed_check,
        follow_ups=args.follow_up,
    )

    if args.json:
        print(
            json.dumps(
                {
                    "scenario": result.scenario,
                    "total": result.total,
                    "outcome": result.outcome,
                    "scores": result.scores,
                    "failed_checks": result.failed_checks,
                    "follow_ups": result.follow_ups,
                },
                indent=2,
            )
        )
    else:
        print(to_markdown(result))

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
