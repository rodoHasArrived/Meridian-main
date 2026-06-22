#!/usr/bin/env python3
"""Suggest documentation location for Meridian changes."""

from __future__ import annotations

import argparse
import json
from dataclasses import dataclass


@dataclass(frozen=True)
class Route:
    directory: str
    description: str
    generated: bool = False
    legacy_new_docs_discouraged: bool = False


ROUTES = {
    "adr": Route("docs/adr/", "durable architecture decision record"),
    "ai": Route("docs/ai/", "AI-agent workflow, Codex, prompt, skill, or eval guidance"),
    "architecture": Route("docs/architecture/", "architecture boundary or system design guidance"),
    "engineering": Route("docs/engineering/", "developer workflow, validation, build, or test guidance"),
    "evaluation": Route("docs/evaluations/", "analysis, assessment, option comparison, or eval report"),
    "evaluations": Route("docs/evaluations/", "analysis, assessment, option comparison, or eval report"),
    "generated": Route("docs/generated/", "generated documentation output", generated=True),
    "operations": Route(
        "docs/operations/",
        "legacy linked operator material; prefer operators for new operator-facing docs",
        legacy_new_docs_discouraged=True,
    ),
    "operators": Route("docs/operators/", "operator workflow, runbook, provider setup, or recovery guidance"),
    "product": Route("docs/product/", "product scope, stakeholder framing, or capability context"),
    "reference": Route("docs/reference/", "stable API, config, command, schema, or lookup reference"),
    "roadmap": Route("docs/roadmap/", "roadmap registry or generated roadmap workflow"),
    "source": Route("docs/source/", "source module README, registry, or source/docs hash workflow"),
    "start": Route("docs/start/", "first-run setup, quickstart, or entrypoint guidance"),
}


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description="Route documentation updates to the right docs subtree.")
    p.add_argument(
        "--kind",
        required=True,
        choices=sorted(ROUTES.keys()),
        help="Documentation kind. Use 'operators' for new operator-facing docs.",
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
    route = ROUTES[args.kind]
    base = route.directory
    suggested = f"{base}{slugify(args.topic)}.md"
    action = "update-existing" if args.existing_doc else "create-new"
    if route.generated and not args.existing_doc:
        action = "update-generator"
    if route.legacy_new_docs_discouraged and not args.existing_doc:
        action = "prefer-docs-operators-for-new-doc"

    payload = {
        "kind": args.kind,
        "target_directory": base,
        "suggested_file": suggested,
        "action": action,
        "description": route.description,
        "cross_link_required": not args.existing_doc and not route.generated,
        "nearest_index": f"{base}README.md",
    }

    if args.json:
        print(json.dumps(payload, indent=2))
    else:
        print("### Documentation Route")
        print(f"- Kind: {payload['kind']}")
        print(f"- Directory: {payload['target_directory']}")
        print(f"- Suggested file: {payload['suggested_file']}")
        print(f"- Action: {payload['action']}")
        print(f"- Description: {payload['description']}")
        print(f"- Nearest index: {payload['nearest_index']}")
        print(f"- Cross-link required: {'yes' if payload['cross_link_required'] else 'no'}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
