#!/usr/bin/env python3
"""Score Meridian brainstorm skill outputs with a compact rubric."""

from __future__ import annotations

import argparse
import json
import sys

RUBRIC = {
    "receipt_and_mode": "Starts with the skill selection receipt and states a concrete detected mode.",
    "triage_table": "Uses a compact Ideas at a Glance table before long-form idea narratives.",
    "meridian_anchor": "Grounds each idea in current Meridian capabilities, workspaces, or abstractions.",
    "tradeoff_clarity": "Names implementation shape, prerequisites, risks, or migration costs.",
    "handoff_quality": "Ends with synthesis and the right next skill or artifact.",
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--scenario", required=True, help="Scenario name or eval id.")
    parser.add_argument("--scores", required=True, help="JSON object with rubric keys scored 0-2.")
    parser.add_argument("--threshold", type=float, default=8.0, help="Minimum passing score.")
    parser.add_argument("--summary", action="store_true", help="Print compact summary.")
    parser.add_argument("--json", action="store_true", help="Emit machine-readable JSON.")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    try:
        scores = json.loads(args.scores)
    except json.JSONDecodeError as exc:
        print(f"error: invalid --scores JSON: {exc}", file=sys.stderr)
        return 2

    missing = [key for key in RUBRIC if key not in scores]
    invalid = [key for key, value in scores.items() if key in RUBRIC and (not isinstance(value, (int, float)) or value < 0 or value > 2)]
    total = sum(float(scores.get(key, 0)) for key in RUBRIC)
    status = "pass" if not missing and not invalid and total >= args.threshold else "fail"
    payload = {
        "scenario": args.scenario,
        "status": status,
        "total": total,
        "threshold": args.threshold,
        "missing": missing,
        "invalid": invalid,
        "rubric": RUBRIC,
        "scores": scores,
    }

    if args.json:
        print(json.dumps(payload, indent=2))
    else:
        print(f"score_eval scenario={args.scenario} status={status} total={total:g}/{args.threshold:g}")
        if args.summary:
            for key, description in RUBRIC.items():
                print(f"- {key}: {scores.get(key, 0)} - {description}")
            for key in missing:
                print(f"missing: {key}")
            for key in invalid:
                print(f"invalid: {key}")
    return 0 if status == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main())
