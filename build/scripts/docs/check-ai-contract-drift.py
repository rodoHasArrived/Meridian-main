#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def check_routing_semantics(route_rules: Path, host_docs: list[Path]) -> list[str]:
    failures: list[str] = []
    try:
        payload = json.loads(route_rules.read_text(encoding="utf-8"))
    except Exception as exc:  # noqa: BLE001
        return [f"invalid routing rules JSON: {route_rules} ({exc})"]

    rules = payload.get("rules", [])
    if not isinstance(rules, list) or not rules:
        return [f"routing rules missing non-empty rules array: {route_rules}"]

    required_route_fields = {
        "lane",
        "skill",
        "mode",
        "modelRouteId",
        "rationale",
        "validationFloor",
        "validationScripts",
        "requiredTelemetry",
        "escalationTriggers",
    }
    default_route = payload.get("defaultRoute", {})
    missing_default = sorted(required_route_fields - set(default_route))
    if missing_default:
        failures.append(f"defaultRoute missing routing fields: {', '.join(missing_default)}")

    for rule in rules:
        if not isinstance(rule, dict):
            failures.append("routing rule entry is not an object")
            continue
        route = rule.get("route", {})
        if not isinstance(route, dict):
            failures.append(f"routing rule {rule.get('id', '<unknown>')} missing route object")
            continue
        missing = sorted(required_route_fields - set(route))
        if missing:
            failures.append(f"routing rule {rule.get('id', '<unknown>')} missing fields: {', '.join(missing)}")

    for doc in host_docs:
        if not doc.exists():
            failures.append(f"missing host routing doc: {doc}")
            continue
        text = doc.read_text(encoding="utf-8")
        doc_name = doc.as_posix()
        if doc_name.endswith("docs/ai/codex/README.md"):
            required_tokens = {
                "prompt-route-rules.json",
                "prompt-route-linter.py",
                "handoff-packet-generator.py",
            }
        else:
            required_tokens = {
                "agent-handoff-checklist.md",
                "parallel-task-manifest-template.md",
                "work-modes.md",
            }
        for token in required_tokens:
            if token not in text:
                failures.append(f"routing semantics reference missing in {doc}: {token}")

    return failures


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("--canonical", required=True, type=Path)
    p.add_argument("--mirror", action="append", default=[], type=Path)
    p.add_argument("--routing-rules", type=Path, help="Optional routing rules JSON to validate host semantics.")
    p.add_argument("--routing-host-doc", action="append", default=[], type=Path, help="Host docs that must reference routing semantics.")
    args = p.parse_args()
    expected = digest(args.canonical)
    failures = []
    for m in args.mirror:
        if not m.exists():
            failures.append(f"missing mirror: {m}")
            continue
        if digest(m) != expected:
            failures.append(f"drift detected: {m}")

    if args.routing_rules:
        failures.extend(check_routing_semantics(args.routing_rules, args.routing_host_doc))

    if failures:
        print("AI contract drift check failed:")
        for failure in failures:
            print(f" - {failure}")
        return 1

    print("AI contract drift check passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
