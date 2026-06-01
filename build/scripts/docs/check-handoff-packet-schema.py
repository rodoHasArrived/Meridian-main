#!/usr/bin/env python3
"""Validate AI handoff packet schema and required telemetry for high-risk routes."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any


DEFAULT_PACKET_JSON = Path("docs/status/ai-handoff-packet.json")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--packet-json", default=str(DEFAULT_PACKET_JSON), help="Handoff packet JSON path.")
    parser.add_argument("--summary", action="store_true", help="Print compact summary output.")
    return parser.parse_args()


def load_json(path: Path) -> dict[str, Any]:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError as exc:
        raise ValueError(f"Missing handoff packet: {path}") from exc
    except json.JSONDecodeError as exc:
        raise ValueError(f"Invalid handoff packet JSON: {path} ({exc})") from exc


def require_key(obj: dict[str, Any], key: str, context: str) -> None:
    if key not in obj:
        raise ValueError(f"{context} missing key '{key}'.")


def validate(packet: dict[str, Any]) -> None:
    for top_key in (
        "generatedAt",
        "scope",
        "route",
        "routeOutcome",
        "telemetry",
        "inputsLoaded",
        "changesMade",
        "validation",
        "openRisks",
        "nextLane",
        "requiredContext",
        "optionalContext",
    ):
        require_key(packet, top_key, "packet")

    if not str(packet["scope"].get("requested", "")).strip():
        raise ValueError("scope.requested must be non-empty.")

    route = packet["route"]
    for key in ("lane", "skill", "mode", "matchedRule", "rationale"):
        require_key(route, key, "route")

    outcome = packet["routeOutcome"]
    require_key(outcome, "finalValidationStatus", "routeOutcome")
    require_key(outcome, "routeAssessment", "routeOutcome")

    telemetry = packet["telemetry"]
    required_telemetry = (
        "route_id",
        "selected_model",
        "input_tokens",
        "output_tokens",
        "estimated_cost_usd",
        "latency_ms",
        "handoff_path",
    )

    high_risk = route.get("mode") == "Deep Review" or route.get("lane") in {"provider", "governance"}
    if high_risk:
        for key in required_telemetry:
            value = telemetry.get(key)
            if value in (None, "", "unknown"):
                raise ValueError(f"telemetry.{key} required for high-risk route lane/mode.")

    validations = packet["validation"]
    if not isinstance(validations, list) or not validations:
        raise ValueError("validation must include at least one command result.")


def main() -> int:
    args = parse_args()
    path = Path(args.packet_json)

    try:
        packet = load_json(path)
        validate(packet)
    except ValueError as exc:
        print(f"[check-handoff-packet-schema] FAILED: {exc}", file=sys.stderr)
        return 1

    if args.summary:
        print("[check-handoff-packet-schema] OK")
        print(f"packet_json={path}")
        print(f"route_lane={packet['route'].get('lane')}")
        print(f"route_mode={packet['route'].get('mode')}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
