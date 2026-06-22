#!/usr/bin/env python3
"""Score Meridian simulated user panel outputs with a compact deterministic rubric."""

from __future__ import annotations

import argparse
import json
import sys

RUBRIC = {
    "artifact_grounding": "Inspects or names concrete artifacts before simulating reactions.",
    "persona_specificity": "Separates feedback by persona with role-credible concerns.",
    "evidence_boundary": "Separates verified evidence, inference, and missing evidence.",
    "owner_actions": "Groups buildable recommendations into Now, Next, and Later.",
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--scenario", required=True)
    parser.add_argument("--scores", required=True)
    parser.add_argument("--threshold", type=float, default=7.0)
    parser.add_argument("--summary", action="store_true")
    parser.add_argument("--json", action="store_true")
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
    payload = {"scenario": args.scenario, "status": status, "total": total, "threshold": args.threshold, "missing": missing, "invalid": invalid, "rubric": RUBRIC, "scores": scores}
    if args.json:
        print(json.dumps(payload, indent=2))
    else:
        print(f"score_eval scenario={args.scenario} status={status} total={total:g}/{args.threshold:g}")
        if args.summary:
            for key, description in RUBRIC.items():
                print(f"- {key}: {scores.get(key, 0)} - {description}")
    return 0 if status == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main())
