#!/usr/bin/env python3
"""Score simulated-user-panel candidates with a deterministic promotion rubric."""

from __future__ import annotations

import argparse
import json
import sys

RUBRIC = {
    "artifact_grounding": "Inspects concrete artifacts and identifies inaccessible or stale evidence.",
    "persona_specificity": "Produces distinct role-credible findings for at least four personas.",
    "persona_alignment": "Uses canonical Persona Matrix roles or explicitly labels advisory/custom lenses.",
    "evidence_boundary": "Separates verified evidence, inference, missing evidence, and the simulation disclaimer.",
    "recommendation_integrity": "Uses a mode-valid verdict and fails closed when release evidence is insufficient.",
    "owner_actions": "Prioritizes buildable actions into Now, Next, and Later with evidence and affected roles.",
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--scenario", required=True)
    parser.add_argument("--scores", required=True)
    parser.add_argument("--threshold", type=float, default=10.0)
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
    extra = [key for key in scores if key not in RUBRIC]
    invalid = [
        key
        for key, value in scores.items()
        if key in RUBRIC and (isinstance(value, bool) or not isinstance(value, (int, float)) or value < 0 or value > 2)
    ]
    total = sum(float(scores.get(key, 0)) for key in RUBRIC)
    zero_scores = [key for key in RUBRIC if scores.get(key) == 0]
    status = "pass" if not missing and not extra and not invalid and not zero_scores and total >= args.threshold else "fail"
    payload = {
        "scenario": args.scenario,
        "status": status,
        "total": total,
        "maximum": len(RUBRIC) * 2,
        "threshold": args.threshold,
        "missing": missing,
        "extra": extra,
        "invalid": invalid,
        "zero_scores": zero_scores,
        "rubric": RUBRIC,
        "scores": scores,
    }
    if args.json:
        print(json.dumps(payload, indent=2))
    else:
        print(f"score_eval scenario={args.scenario} status={status} total={total:g}/{len(RUBRIC) * 2}")
        if args.summary:
            for key, description in RUBRIC.items():
                print(f"- {key}: {scores.get(key, 0)} - {description}")
    return 0 if status == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main())
