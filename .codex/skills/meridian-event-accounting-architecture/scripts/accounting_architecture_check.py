#!/usr/bin/env python3
"""Check that the event accounting skill and agent profile keep core controls visible."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
try:
    import tomllib
except ModuleNotFoundError:  # pragma: no cover - Python < 3.11 fallback for local operators.
    import tomli as tomllib

REPO_ROOT = Path(__file__).resolve().parents[4]
SKILL_DIR = Path(__file__).resolve().parents[1]
SKILL_MD = SKILL_DIR / "SKILL.md"
REFERENCE = SKILL_DIR / "references" / "event-accounting-patterns.md"
AGENT_PROFILE = REPO_ROOT / ".codex" / "agents" / "meridian-event-accounting-architecture.toml"

REQUIRED_TERMS = [
    "double-entry",
    "immutable",
    "reversal/rebook",
    "idempotency",
    "period",
    "approval",
    "source evidence",
    "projection",
    "replay",
    "audit evidence",
]

REQUIRED_REPO_DOCS = [
    "docs/ai/context/accounting-context.md",
    "docs/ai/context/operational-evidence-context.md",
    "docs/architecture/module-map.md",
    "docs/domain/fund-event.md",
    "docs/domain/operational-evidence-graph.md",
]

REQUIRED_AGENT_LINKS = [
    "`.codex/skills/meridian-event-accounting-architecture/SKILL.md`",
    "`.codex/skills/_shared/project-context.md`",
    "`.codex/skills/_shared/codex-execution-contract.md`",
]

REQUIRED_AGENT_SECTIONS = [
    "## Use / Do Not Use",
    "## Context Loading",
    "## Required Output Packet",
    "## Handoff Triggers",
    "## Complementary Lanes",
]

REQUIRED_OUTPUT_TERMS = [
    "event scope",
    "source evidence",
    "approval/period gates",
    "immutable facts",
    "rebuildable projections",
    "posting consequences",
    "invariant coverage",
    "impacted seams",
    "validation commands",
    "residual risk",
]

REQUIRED_HANDOFFS = [
    "meridian-contract-governance",
    "meridian-test-writer",
    "meridian-code-architecture",
    "meridian-implementation-assurance",
    "diagnostics-audit-timeline",
]

REQUIRED_COMPLEMENTARY_LANE_TERMS = [
    "complementary lanes",
    "trigger",
    "expected output",
    "validation owner",
    "contract impact map",
    "scenario-first test plan",
    "evidence/recovery timeline",
    "assurance summary",
    "architecture finding map",
]

REQUIRED_PERSONA_PARTNERS = [
    "meridian-user-testing-fund-accountant",
    "meridian-user-testing-auditor",
    "meridian-user-testing-controller",
    "meridian-user-testing-reconciliation-analyst",
    "meridian-user-testing-reporting-analyst",
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
    "capital-event-posting": {
        "description": "capital-call or distribution posting includes evidence, double-entry, approval, period, and journal consequences",
        "required_terms": [
            "capital call",
            "distribution",
            "source evidence",
            "double-entry",
            "approval",
            "period",
            "journal",
        ],
    },
    "duplicate-replay-controls": {
        "description": "duplicate and out-of-order replay fails closed through idempotency and ordering controls",
        "required_terms": [
            "duplicate",
            "out-of-order",
            "idempotency",
            "ordering",
            "fail closed",
            "replay",
        ],
    },
    "period-lock-reversal": {
        "description": "period-lock reversal/rebook preserves the original fact and evidence chain",
        "required_terms": [
            "period lock",
            "reversal/rebook",
            "original",
            "evidence chain",
            "immutable",
        ],
    },
    "projection-reporting-handoff": {
        "description": "projection and reporting handoff separates immutable facts from rebuildable read models",
        "required_terms": [
            "projection",
            "report",
            "immutable",
            "rebuildable",
            "read model",
        ],
    },
    "complementary-lanes": {
        "description": "complementary lanes define triggers, expected outputs, validation owners, and persona partners",
        "required_terms": [
            "complementary lanes",
            "trigger",
            "expected output",
            "validation owner",
            "contract impact map",
            "scenario-first test plan",
            "evidence/recovery timeline",
            "assurance summary",
            "architecture finding map",
            "meridian-user-testing-fund-accountant",
            "meridian-user-testing-auditor",
            "meridian-user-testing-controller",
            "meridian-user-testing-reconciliation-analyst",
            "meridian-user-testing-reporting-analyst",
        ],
    },
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--scenario", choices=sorted(SCENARIOS), help="Run one deterministic scenario coverage check.")
    parser.add_argument("--json", action="store_true", help="Emit machine-readable JSON.")
    parser.add_argument("--summary", action="store_true", help="Print a compact summary.")
    return parser.parse_args()


def parse_agent_profile() -> tuple[dict[str, object], list[str]]:
    if not AGENT_PROFILE.exists():
        return {}, ["missing agent profile"]
    try:
        return tomllib.loads(AGENT_PROFILE.read_text(encoding="utf-8")), []
    except tomllib.TOMLDecodeError as exc:
        return {}, [f"invalid TOML: {exc}"]


def normalize_text(text: str) -> str:
    return " ".join(text.lower().split())


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

    missing_terms = [term for term in REQUIRED_TERMS if term not in skill_text]
    missing_docs = [doc for doc in REQUIRED_REPO_DOCS if doc.lower() not in skill_text]
    missing_reference = [] if reference_text and "martinfowler.com" in reference_text else ["event-accounting-patterns.md research anchors"]
    missing_agent_links = [link for link in REQUIRED_AGENT_LINKS if link.lower() not in agent_instructions]
    missing_agent_sections = [section for section in REQUIRED_AGENT_SECTIONS if section.lower() not in agent_instructions]
    missing_output_terms = [term for term in REQUIRED_OUTPUT_TERMS if term not in agent_instructions]
    missing_handoffs = [handoff for handoff in REQUIRED_HANDOFFS if handoff not in agent_instructions]
    missing_complementary_terms = [term for term in REQUIRED_COMPLEMENTARY_LANE_TERMS if term not in combined_text]
    missing_persona_partners = [partner for partner in REQUIRED_PERSONA_PARTNERS if partner not in combined_text]
    forbidden_overrides = [key for key in FORBIDDEN_AGENT_OVERRIDES if key in agent_payload]
    scenario = check_scenario(args.scenario, combined_text)

    status = "pass"
    if (
        missing_terms
        or missing_docs
        or missing_reference
        or agent_parse_errors
        or missing_agent_links
        or missing_agent_sections
        or missing_output_terms
        or missing_handoffs
        or missing_complementary_terms
        or missing_persona_partners
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
            "missing_sections": missing_agent_sections,
            "missing_output_terms": missing_output_terms,
            "missing_handoffs": missing_handoffs,
            "missing_complementary_terms": missing_complementary_terms,
            "missing_persona_partners": missing_persona_partners,
            "forbidden_overrides": forbidden_overrides,
        },
        "scenario": scenario,
        "required_terms": len(REQUIRED_TERMS),
        "required_repo_docs": len(REQUIRED_REPO_DOCS),
        "required_agent_links": len(REQUIRED_AGENT_LINKS),
        "required_agent_sections": len(REQUIRED_AGENT_SECTIONS),
        "required_output_terms": len(REQUIRED_OUTPUT_TERMS),
        "required_handoffs": len(REQUIRED_HANDOFFS),
        "required_complementary_terms": len(REQUIRED_COMPLEMENTARY_LANE_TERMS),
        "required_persona_partners": len(REQUIRED_PERSONA_PARTNERS),
    }

    if args.json:
        print(json.dumps(payload, indent=2))
    elif args.summary:
        agent_ok = (
            len(agent_parse_errors)
            + len(missing_agent_links)
            + len(missing_agent_sections)
            + len(missing_output_terms)
            + len(missing_handoffs)
            + len(missing_complementary_terms)
            + len(missing_persona_partners)
            + len(forbidden_overrides)
        ) == 0
        scenario_suffix = ""
        if scenario:
            scenario_suffix = f" scenario={scenario['id']}:{scenario['status']}"
        print(
            f"{payload['skill']}: {payload['status']} "
            f"terms={len(REQUIRED_TERMS) - len(missing_terms)}/{len(REQUIRED_TERMS)} "
            f"docs={len(REQUIRED_REPO_DOCS) - len(missing_docs)}/{len(REQUIRED_REPO_DOCS)} "
            f"agent={'pass' if agent_ok else 'fail'}{scenario_suffix}"
        )
    else:
        for key, value in payload.items():
            print(f"{key}: {value}")

    return 0 if payload["status"] == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main())
