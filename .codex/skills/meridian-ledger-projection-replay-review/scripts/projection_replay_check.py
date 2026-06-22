#!/usr/bin/env python3
"""Validate the Meridian ledger projection/replay review skill package."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

try:
    import tomllib
except ModuleNotFoundError:  # pragma: no cover - Python < 3.11 fallback.
    import tomli as tomllib

REPO_ROOT = Path(__file__).resolve().parents[4]
SKILL_DIR = Path(__file__).resolve().parents[1]
SKILL_MD = SKILL_DIR / "SKILL.md"
REFERENCE = SKILL_DIR / "references" / "projection-replay-checklist.md"
AGENT_PROFILE = REPO_ROOT / ".codex" / "agents" / "meridian-ledger-projection-replay-review.toml"

REQUIRED_TERMS = [
    "projection",
    "replay",
    "idempotent replay",
    "duplicate detection",
    "out-of-order",
    "deterministic ordering",
    "projection rebuild",
    "projection versioning",
    "balance drift",
    "immutable journal",
    "read model",
    "report-line provenance",
    "audit replay evidence",
]

REQUIRED_REPO_DOCS = [
    "../_shared/project-context.md",
    "../_shared/codex-execution-contract.md",
    "../meridian-event-accounting-architecture/SKILL.md",
    "docs/ai/context/accounting-context.md",
    "docs/ai/context/operational-evidence-context.md",
]

REQUIRED_AGENT_LINKS = [
    "`.codex/skills/meridian-ledger-projection-replay-review/SKILL.md`",
    "`.codex/skills/meridian-event-accounting-architecture/SKILL.md`",
    "`.codex/skills/_shared/project-context.md`",
    "`.codex/skills/_shared/codex-execution-contract.md`",
]

REQUIRED_HANDOFFS = [
    "meridian-event-accounting-architecture",
    "meridian-accounting-posting-controls",
    "meridian-contract-governance",
    "meridian-test-writer",
    "diagnostics-audit-timeline",
    "meridian-implementation-assurance",
]

FORBIDDEN_AGENT_OVERRIDES = [
    "model",
    "model_provider",
    "model_providers",
    "model_reasoning_effort",
    "sandbox_mode",
    "mcp_servers",
    "otel",
]

SCENARIOS = {
    "duplicate-ordering": {
        "description": "duplicate and out-of-order replay controls are visible",
        "required_terms": [
            "duplicate detection",
            "out-of-order",
            "deterministic ordering",
            "idempotent replay",
            "correlation",
            "causation",
        ],
    },
    "rebuild-versioning": {
        "description": "projection rebuild, versioning, backfill, and drift controls are visible",
        "required_terms": [
            "projection rebuild",
            "projection versioning",
            "backfill",
            "balance drift",
            "rebuild checkpoints",
            "stale projection",
        ],
    },
    "report-handoff": {
        "description": "reporting and close handoff preserve immutable facts and provenance",
        "required_terms": [
            "close/reporting consumer",
            "report-line provenance",
            "immutable journal",
            "read model",
            "audit replay evidence",
            "delivery record",
        ],
    },
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--scenario", choices=sorted(SCENARIOS), help="Run one deterministic scenario coverage check.")
    parser.add_argument("--json", action="store_true", help="Emit machine-readable JSON.")
    parser.add_argument("--summary", action="store_true", help="Print a compact summary.")
    return parser.parse_args()


def normalize_text(text: str) -> str:
    return " ".join(text.lower().split())


def parse_agent_profile() -> tuple[dict[str, object], list[str]]:
    if not AGENT_PROFILE.exists():
        return {}, ["missing agent profile"]
    try:
        return tomllib.loads(AGENT_PROFILE.read_text(encoding="utf-8")), []
    except tomllib.TOMLDecodeError as exc:
        return {}, [f"invalid TOML: {exc}"]


def check_scenario(scenario_id: str | None, combined_text: str) -> dict[str, object] | None:
    if not scenario_id:
        return None
    scenario = SCENARIOS[scenario_id]
    missing_terms = [term for term in scenario["required_terms"] if term not in combined_text]
    return {
        "id": scenario_id,
        "description": scenario["description"],
        "status": "pass" if not missing_terms else "fail",
        "missing_terms": missing_terms,
        "required_terms": len(scenario["required_terms"]),
    }


def main() -> int:
    args = parse_args()
    skill_text = normalize_text(SKILL_MD.read_text(encoding="utf-8"))
    reference_text = normalize_text(REFERENCE.read_text(encoding="utf-8")) if REFERENCE.exists() else ""
    agent_payload, agent_parse_errors = parse_agent_profile()
    agent_instructions = normalize_text(str(agent_payload.get("developer_instructions", "")))
    combined_text = "\n".join([skill_text, reference_text, agent_instructions])

    missing_terms = [term for term in REQUIRED_TERMS if term not in combined_text]
    missing_docs = [doc for doc in REQUIRED_REPO_DOCS if doc.lower() not in skill_text]
    missing_reference = [] if reference_text and "fail-closed conditions" in reference_text else ["projection-replay-checklist.md fail-closed conditions"]
    missing_agent_links = [link for link in REQUIRED_AGENT_LINKS if link.lower() not in agent_instructions]
    missing_handoffs = [handoff for handoff in REQUIRED_HANDOFFS if handoff not in combined_text]
    forbidden_overrides = [key for key in FORBIDDEN_AGENT_OVERRIDES if key in agent_payload]
    scenario = check_scenario(args.scenario, combined_text)

    status = "pass"
    if (
        missing_terms
        or missing_docs
        or missing_reference
        or agent_parse_errors
        or missing_agent_links
        or missing_handoffs
        or forbidden_overrides
        or (scenario and scenario["status"] != "pass")
    ):
        status = "fail"

    payload = {
        "skill": SKILL_DIR.name,
        "status": status,
        "missing_terms": missing_terms,
        "missing_repo_docs": missing_docs,
        "missing_reference": missing_reference,
        "agent_profile": {
            "path": AGENT_PROFILE.relative_to(REPO_ROOT).as_posix(),
            "parse_errors": agent_parse_errors,
            "missing_links": missing_agent_links,
            "missing_handoffs": missing_handoffs,
            "forbidden_overrides": forbidden_overrides,
        },
        "scenario": scenario,
        "required_terms": len(REQUIRED_TERMS),
        "required_repo_docs": len(REQUIRED_REPO_DOCS),
        "required_agent_links": len(REQUIRED_AGENT_LINKS),
        "required_handoffs": len(REQUIRED_HANDOFFS),
    }

    if args.json:
        print(json.dumps(payload, indent=2))
    elif args.summary:
        scenario_suffix = f" scenario={scenario['id']}:{scenario['status']}" if scenario else ""
        print(
            f"{payload['skill']}: {payload['status']} "
            f"terms={len(REQUIRED_TERMS) - len(missing_terms)}/{len(REQUIRED_TERMS)} "
            f"docs={len(REQUIRED_REPO_DOCS) - len(missing_docs)}/{len(REQUIRED_REPO_DOCS)} "
            f"agent={'pass' if not agent_parse_errors and not missing_agent_links and not missing_handoffs and not forbidden_overrides else 'fail'}"
            f"{scenario_suffix}"
        )
    else:
        for key, value in payload.items():
            print(f"{key}: {value}")

    return 0 if payload["status"] == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main())
