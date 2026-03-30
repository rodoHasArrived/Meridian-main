#!/usr/bin/env python3
"""Suggest documentation location for Meridian changes."""

from __future__ import annotations

import argparse
import json

ROUTES = {
    "architecture": "docs/architecture/",
    "adr": "docs/adr/",
    "reference": "docs/reference/",
    "generated": "docs/generated/",
    "evaluation": "docs/evaluations/",
    "ai": "docs/ai/",
}


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description="Route documentation updates to the right docs subtree.")
    p.add_argument(
        "--kind",
        required=True,
        choices=sorted(ROUTES.keys()),
        help="Documentation kind.",
    )
    p.add_argument("--topic", required=True, help="Short topic name used for filename suggestion.")
    p.add_argument(
        "--existing-doc",
        action="store_true",
        help="Mark when an existing doc already covers this topic; recommendation becomes update-in-place.",
    )
    p.add_argument("--json", action="store_true", help="Emit JSON instead of human-readable markdown.")
    return p.parse_args()


def slugify(value: str) -> str:
    out = []
    prev_dash = False
    for ch in value.lower().strip():
        if ch.isalnum():
            out.append(ch)
            prev_dash = False
        else:
            if not prev_dash:
                out.append("-")
                prev_dash = True
    slug = "".join(out).strip("-")
    return slug or "new-doc"


def main() -> int:
    args = parse_args()
    base = ROUTES[args.kind]
    suggested = f"{base}{slugify(args.topic)}.md"
    action = "update-existing" if args.existing_doc else "create-new"

    payload = {
        "kind": args.kind,
        "target_directory": base,
        "suggested_file": suggested,
        "action": action,
        "cross_link_required": not args.existing_doc,
    }

    if args.json:
        print(json.dumps(payload, indent=2))
    else:
        print("### Documentation Route")
        print(f"- Kind: {payload['kind']}")
        print(f"- Directory: {payload['target_directory']}")
        print(f"- Suggested file: {payload['suggested_file']}")
        print(f"- Action: {payload['action']}")
        print(f"- Cross-link required: {'yes' if payload['cross_link_required'] else 'no'}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
