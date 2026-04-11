#!/usr/bin/env python3
<<<<<<< ours
<<<<<<< ours
<<<<<<< Updated upstream
"""
Lightweight router for AI/agent documentation updates.

Given a `--kind` and optional `--topic`, it prints the recommended destination(s)
for catalog updates along with rationale to keep discovery in sync.
"""
=======
"""Suggest documentation location for Meridian changes."""
>>>>>>> Stashed changes
=======
"""Suggest documentation location for Meridian changes."""
>>>>>>> theirs
=======
"""Suggest documentation location for Meridian changes."""
>>>>>>> theirs

from __future__ import annotations

import argparse
import json
<<<<<<< ours
<<<<<<< ours
<<<<<<< Updated upstream
from dataclasses import dataclass, asdict
from typing import Dict, List


@dataclass
class Route:
    kind: str
    destination: str
    reason: str
    next_steps: List[str]


ROUTES: Dict[str, Route] = {
    "ai": Route(
        kind="ai",
        destination="docs/ai/agents/README.md + docs/ai/skills/README.md",
        reason="AI catalog symmetry: Copilot agents ↔ Claude skills",
        next_steps=[
            "Add/adjust agent entry under GitHub Copilot agents",
            "Add/adjust matching Claude skill entry under Available Skill Packages",
            "Refresh symmetry map rows if capability pairs change",
        ],
    ),
    "skill": Route(
        kind="skill",
        destination="docs/ai/skills/README.md",
        reason="Skill discoverability and references/scripts index",
        next_steps=[
            "Confirm SKILL.md frontmatter is valid and under 500 lines",
            "List references/scripts required for the skill",
            "Update Last Updated metadata in the catalog entry",
        ],
    ),
    "agent": Route(
        kind="agent",
        destination="docs/ai/agents/README.md",
        reason="Agent definition discoverability for GitHub Copilot",
        next_steps=[
            "Add trigger guidance and workflow bullets",
            "Link to the matching Claude skill (if exists) in the symmetry map",
            "Refresh the Last Updated stamp on the catalog page",
        ],
    ),
    "workflow": Route(
        kind="workflow",
        destination="docs/ai/README.md",
        reason="Master AI index for workflow changes and routing updates",
        next_steps=[
            "Reflect new lanes or validation flows in the master index",
            "Link to downstream catalogs (agents/skills) when scopes change",
        ],
    ),
}


def main() -> None:
    parser = argparse.ArgumentParser(description="Route AI/agent updates to the correct catalog.")
    parser.add_argument(
        "--kind",
        required=True,
        choices=sorted(ROUTES.keys()),
        help="Type of update (ai, skill, agent, workflow).",
    )
    parser.add_argument(
        "--topic",
        default="",
        help="Optional topic to echo back for traceability.",
    )
    parser.add_argument(
        "--json",
        action="store_true",
        help="Emit machine-readable JSON instead of text.",
    )

    args = parser.parse_args()
    route = ROUTES[args.kind]
    payload = asdict(route)
    payload["topic"] = args.topic

    if args.json:
        print(json.dumps(payload, indent=2))
        return

    print(f"[doc-route] kind={route.kind} topic={args.topic or '-'}")
    print(f"Destination: {route.destination}")
    print(f"Reason: {route.reason}")
    if route.next_steps:
        print("Next steps:")
        for step in route.next_steps:
            print(f"- {step}")


if __name__ == "__main__":
    main()
=======
=======
>>>>>>> theirs
=======
>>>>>>> theirs

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
<<<<<<< ours
<<<<<<< ours
>>>>>>> Stashed changes
=======
>>>>>>> theirs
=======
>>>>>>> theirs
