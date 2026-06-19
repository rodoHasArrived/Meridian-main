#!/usr/bin/env python3
"""Validate Meridian simulated user panel output shape with a deterministic sample."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

SKILL_NAME = "meridian-simulated-user-panel"
REQUIRED_HEADINGS = [
    "## Executive Summary",
    "## Panel",
    "## Persona Findings",
    "## Cross-Persona Tensions",
    "## Owner Actions",
    "## Release Recommendation",
    "## Confidence Notes",
]
REQUIRED_TERMS = ["Verified", "Inferred", "Missing evidence", "Now", "Next", "Later", "Rubric"]
SAMPLES = {
    "standard": """**Skill Selection**
- Skill: `meridian-simulated-user-panel`
- Mode: `design_partner`
- Reason: concrete artifact needs persona critique
- Required Opening: review contract

## Executive Summary
The artifact is promising but needs clearer operational evidence.

## Panel
Financial operations professional, fund accountant, compliance officer, and portfolio manager.

## Persona Findings
Each persona includes liked, did not like, missing or risky, owner-minded improvements, adoption
verdict, and Rubric ratings.

## Cross-Persona Tensions
Speed and control needs disagree.

## Owner Actions
Now: clarify evidence. Next: tighten workflow handoffs. Later: deepen automation.

## Release Recommendation
ship_with_caveats

## Confidence Notes
Verified: source paths inspected. Inferred: persona reactions. Missing evidence: fresh screenshot.
""",
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--sample", choices=sorted(SAMPLES))
    source.add_argument("--text")
    source.add_argument("--file", type=Path)
    parser.add_argument("--summary", action="store_true")
    parser.add_argument("--json", action="store_true")
    return parser.parse_args()


def load_text(args: argparse.Namespace) -> str:
    if args.sample:
        return SAMPLES[args.sample]
    if args.file:
        return args.file.read_text(encoding="utf-8")
    return args.text or ""


def validate(text: str) -> dict[str, object]:
    missing_headings = [heading for heading in REQUIRED_HEADINGS if heading not in text]
    missing_terms = [term for term in REQUIRED_TERMS if term.lower() not in text.lower()]
    has_receipt = f"Skill: `{SKILL_NAME}`" in text or f"Skill: {SKILL_NAME}" in text
    failures = []
    if not has_receipt:
        failures.append("missing skill selection receipt for meridian-simulated-user-panel")
    failures.extend(f"missing heading {heading}" for heading in missing_headings)
    failures.extend(f"missing term {term}" for term in missing_terms)
    return {
        "status": "pass" if not failures else "fail",
        "failures": failures,
        "summary": {
            "skill": SKILL_NAME,
            "heading_count": len(REQUIRED_HEADINGS) - len(missing_headings),
            "required_terms_count": len(REQUIRED_TERMS) - len(missing_terms),
            "receipt_count": 1 if has_receipt else 0,
        },
    }


def main() -> int:
    args = parse_args()
    payload = validate(load_text(args))
    if args.json:
        print(json.dumps(payload, indent=2))
    else:
        print(
            f"simulated_user_output_check status={payload['status']} "
            f"headings={payload['summary']['heading_count']}"
        )
        if args.summary:
            for failure in payload["failures"]:
                print(f"- {failure}")
    return 0 if payload["status"] == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main())
