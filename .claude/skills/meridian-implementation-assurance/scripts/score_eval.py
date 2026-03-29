#!/usr/bin/env python3
"""
Lightweight scoring helper for implementation assurance.

Accepts a scenario label and a JSON payload of categorical scores. Emits a verdict plus totals.
"""

from __future__ import annotations

import argparse
import json
import sys
from dataclasses import dataclass, asdict
from typing import Dict, Tuple


DEFAULT_KEYS = [
    "behavior_correctness",
    "validation_evidence",
    "performance_safety",
    "documentation_sync",
    "traceable_summary",
]


@dataclass
class Evaluation:
    scenario: str
    scores: Dict[str, float]
    total: float
    average: float
    verdict: str


def load_scores(raw: str) -> Dict[str, float]:
    try:
        parsed = json.loads(raw)
    except json.JSONDecodeError as exc:
        raise SystemExit(f"Invalid JSON for --scores: {exc}")

    if not isinstance(parsed, dict):
        raise SystemExit("Scores payload must be a JSON object.")

    cleaned: Dict[str, float] = {}
    for key in DEFAULT_KEYS:
        value = float(parsed.get(key, 0))
        cleaned[key] = max(0.0, value)
    return cleaned


def verdict_from_scores(avg: float, minimum: float) -> str:
    if minimum >= 2.0:
        return "pass: full strength"
    if avg >= 1.5 and minimum >= 1.0:
        return "pass: needs follow-up"
    return "fail: gaps detected"


def evaluate(scenario: str, scores: Dict[str, float]) -> Evaluation:
    total = sum(scores.values())
    average = total / len(scores) if scores else 0.0
    minimum = min(scores.values()) if scores else 0.0
    verdict = verdict_from_scores(average, minimum)
    return Evaluation(
        scenario=scenario,
        scores=scores,
        total=round(total, 2),
        average=round(average, 2),
        verdict=verdict,
    )


def parse_args(argv: Tuple[str, ...] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Summarize implementation-assurance scores.")
    parser.add_argument("--scenario", required=True, help="Scenario label (e.g., A, B, C).")
    parser.add_argument(
        "--scores",
        required=True,
        help="JSON object of category scores, e.g. '{\"behavior_correctness\":2,\"validation_evidence\":2}'.",
    )
    parser.add_argument(
        "--json",
        action="store_true",
        help="Emit machine-readable JSON instead of text.",
    )
    return parser.parse_args(argv)


def main(argv: Tuple[str, ...] | None = None) -> None:
    args = parse_args(argv)
    scores = load_scores(args.scores)
    evaluation = evaluate(args.scenario, scores)

    if args.json:
        print(json.dumps(asdict(evaluation), indent=2))
        return

    print(f"[score-eval] scenario={evaluation.scenario}")
    for key, value in evaluation.scores.items():
        print(f"- {key}: {value}")
    print(f"Total: {evaluation.total}")
    print(f"Average: {evaluation.average}")
    print(f"Verdict: {evaluation.verdict}")


if __name__ == "__main__":
    main()
