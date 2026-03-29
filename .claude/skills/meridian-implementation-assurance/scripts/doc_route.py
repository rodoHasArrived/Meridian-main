#!/usr/bin/env python3
"""
Lightweight router for AI/agent documentation updates.

Given a `--kind` and optional `--topic`, it prints the recommended destination(s)
for catalog updates along with rationale to keep discovery in sync.
"""

from __future__ import annotations

import argparse
import json
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
