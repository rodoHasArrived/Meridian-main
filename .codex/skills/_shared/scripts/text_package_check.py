#!/usr/bin/env python3
"""Shared text-surface checker for compact Meridian Codex skill packages."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any

try:
    import tomllib
except ModuleNotFoundError:
    import tomli as tomllib

REPO_ROOT = Path(__file__).resolve().parents[4]


def parse_args(scenarios: dict[str, Any]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--scenario", choices=sorted(scenarios), help="Run one deterministic scenario coverage check.")
    parser.add_argument("--json", action="store_true", help="Emit machine-readable JSON.")
    parser.add_argument("--summary", action="store_true", help="Print compact summary.")
    return parser.parse_args()


def normalize_text(text: str) -> str:
    return " ".join(text.lower().split())


def parse_agent_profile(skill_name: str) -> tuple[dict[str, object], list[str]]:
    profile = REPO_ROOT / ".codex" / "agents" / f"{skill_name}.toml"
    if not profile.exists():
        return {}, ["missing agent profile"]
    try:
        return tomllib.loads(profile.read_text(encoding="utf-8")), []
    except tomllib.TOMLDecodeError as exc:
        return {}, [f"invalid TOML: {exc}"]


def check_scenario(scenario_id: str | None, scenarios: dict[str, Any], combined_text: str) -> dict[str, object] | None:
    if not scenario_id:
        return None
    scenario = scenarios[scenario_id]
    missing_terms = [term for term in scenario["required_terms"] if normalize_text(term) not in combined_text]
    return {"id": scenario_id, "description": scenario["description"], "status": "pass" if not missing_terms else "fail", "missing_terms": missing_terms, "required_terms": len(scenario["required_terms"])}


def main(config: dict[str, Any]) -> int:
    skill_name = config["skill"]
    skill_dir = REPO_ROOT / ".codex" / "skills" / skill_name
    scenarios = config.get("scenarios", {})
    args = parse_args(scenarios)
    skill_text_raw = (skill_dir / "SKILL.md").read_text(encoding="utf-8")
    skill_text = normalize_text(skill_text_raw)
    reference_texts = []
    for relative_path in config.get("references", []):
        path = skill_dir / relative_path
        if path.exists():
            reference_texts.append(path.read_text(encoding="utf-8"))
    agent_payload, agent_parse_errors = parse_agent_profile(skill_name)
    agent_text = normalize_text(str(agent_payload.get("developer_instructions", "")))
    route_text = normalize_text((REPO_ROOT / "docs" / "ai" / "codex" / "prompt-route-rules.json").read_text(encoding="utf-8"))
    combined_text = normalize_text("\n".join([skill_text_raw, *reference_texts, agent_text, route_text]))
    missing_terms = [term for term in config["required_terms"] if normalize_text(term) not in combined_text]
    missing_docs = [doc for doc in config.get("required_docs", []) if normalize_text(doc) not in skill_text]
    missing_handoffs = [handoff for handoff in config.get("required_handoffs", []) if normalize_text(handoff) not in combined_text]
    scenario = check_scenario(args.scenario, scenarios, combined_text)
    route_mentioned = normalize_text(skill_name) in route_text
    status = "pass"
    if missing_terms or missing_docs or missing_handoffs or agent_parse_errors or not route_mentioned or (scenario and scenario["status"] != "pass"):
        status = "fail"
    payload = {"skill": skill_name, "status": status, "missing_terms": missing_terms, "missing_repo_docs": missing_docs, "missing_handoffs": missing_handoffs, "agent_profile": {"parse_errors": agent_parse_errors}, "route_mentioned": route_mentioned, "scenario": scenario, "required_terms": len(config["required_terms"]), "required_handoffs": len(config.get("required_handoffs", []))}
    if args.json:
        print(json.dumps(payload, indent=2))
    elif args.summary:
        scenario_suffix = f" scenario={scenario['id']}:{scenario['status']}" if scenario else ""
        print(f"{skill_name}: {status} terms={len(config['required_terms']) - len(missing_terms)}/{len(config['required_terms'])} handoffs={len(config.get('required_handoffs', [])) - len(missing_handoffs)}/{len(config.get('required_handoffs', []))} route={'pass' if route_mentioned else 'fail'}{scenario_suffix}")
    else:
        for key, value in payload.items():
            print(f"{key}: {value}")
    return 0 if status == "pass" else 1
