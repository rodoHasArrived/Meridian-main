#!/usr/bin/env python3
"""Score a manual archive-skill evaluation run."""

from __future__ import annotations

import argparse
import json
import sys


CATEGORIES = (
    "classification_accuracy",
    "reference_trace_quality",
    "placement_correctness",
    "safety_guardrails",
    "cleanup_follow_through",
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--scenario", choices=["A", "B", "C", "D"], required=True)
    parser.add_argument("--scores", required=True, help="JSON object with 0-2 scores for each rubric category.")
    parser.add_argument("--failed-check", action="append", default=[], help="Optional failed checks to list.")
    parser.add_argument("--follow-up", action="append", default=[], help="Optional follow-up actions to list.")
    parser.add_argument("--json", action="store_true", help="Emit machine-readable JSON.")
    return parser.parse_args()


def load_scores(raw_scores: str) -> dict[str, int]:
    parsed = json.loads(raw_scores)
    scores: dict[str, int] = {}
    for category in CATEGORIES:
        value = parsed.get(category)
        if value is None:
            raise ValueError(f"Missing score for {category}.")
        if not isinstance(value, int) or value < 0 or value > 2:
            raise ValueError(f"Score for {category} must be an integer between 0 and 2.")
        scores[category] = value
    return scores


def build_payload(args: argparse.Namespace) -> dict[str, object]:
    scores = load_scores(args.scores)
    total = sum(scores.values())
    passing = total >= 8 and all(value > 0 for value in scores.values())
    return {
        "scenario": args.scenario,
        "scores": scores,
        "total": total,
        "max_total": len(CATEGORIES) * 2,
        "failed_checks": args.failed_check,
        "follow_up": args.follow_up,
        "result": "pass" if passing else "fail",
    }


def print_markdown(payload: dict[str, object]) -> None:
    print("### Archive Skill Eval")
    print(f"- scenario: `{payload['scenario']}`")
    print(f"- result: `{payload['result']}`")
    print(f"- total: `{payload['total']}/{payload['max_total']}`")
    for category, score in payload["scores"].items():
        print(f"- {category}: `{score}`")
    failed_checks = payload["failed_checks"]
    if failed_checks:
        for failed_check in failed_checks:
            print(f"- failed_check: {failed_check}")
    follow_up = payload["follow_up"]
    if follow_up:
        for item in follow_up:
            print(f"- follow_up: {item}")


def main() -> int:
    args = parse_args()
    try:
        payload = build_payload(args)
    except (ValueError, json.JSONDecodeError) as exc:
        print(str(exc), file=sys.stderr)
        return 2

    if args.json:
        json.dump(payload, sys.stdout, indent=2)
        sys.stdout.write("\n")
    else:
        print_markdown(payload)
    return 0 if payload["result"] == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main())
