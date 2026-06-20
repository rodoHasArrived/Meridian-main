#!/usr/bin/env python3
"""Generate compact AI handoff packets from route output and local evidence.

Examples:
    python3 build/scripts/docs/handoff-packet-generator.py --summary
    python3 build/scripts/docs/handoff-packet-generator.py \
      --route-json docs/status/prompt-route-lint-report.json \
      --scope "Implement prompt route linter pilot" \
      --next-lane "implementation-assurance" \
      --validation "python build/scripts/docs/prompt-route-linter.py --summary::pass"
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any


DEFAULT_ROUTE_JSON = Path("docs/status/prompt-route-lint-report.json")
STABLE_GENERATED_AT = "1970-01-01T00:00:00+00:00"
DEFAULT_MARKDOWN_OUTPUT = Path("docs/status/ai-handoff-packet.md")
DEFAULT_JSON_OUTPUT = Path("docs/status/ai-handoff-packet.json")


@dataclass
class ValidationEntry:
    command: str
    outcome: str


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--summary", action="store_true", help="Validate inputs and print compact summary.")
    parser.add_argument("--route-json", default=str(DEFAULT_ROUTE_JSON), help="Route JSON from prompt-route-linter.")
    parser.add_argument("--scope", help="Scope summary for the handoff packet.")
    parser.add_argument("--excluded", action="append", default=[], help="Explicit exclusion(s) for scope.")
    parser.add_argument("--next-lane", default="tbd", help="Next lane or owner action.")
    parser.add_argument("--required-context", action="append", default=[], help="Required context file(s).")
    parser.add_argument("--optional-context", action="append", default=[], help="Optional context file(s).")
    parser.add_argument("--open-risk", action="append", default=[], help="Open risk item(s).")
    parser.add_argument(
        "--validation",
        action="append",
        default=[],
        help="Validation result in format '<command>::<pass|fail|not-run>'.",
    )
    parser.add_argument(
        "--final-status",
        choices=("pass", "fail", "partial"),
        default="partial",
        help="Overall final validation status for this lane.",
    )
    parser.add_argument("--model", help="Selected model identifier for telemetry capture.")
    parser.add_argument("--input-tokens", type=int, help="Telemetry input token count.")
    parser.add_argument("--output-tokens", type=int, help="Telemetry output token count.")
    parser.add_argument("--estimated-cost-usd", type=float, help="Telemetry estimated USD cost.")
    parser.add_argument("--latency-ms", type=int, help="Telemetry end-to-end latency in milliseconds.")
    parser.add_argument("--error-class", help="Structured telemetry error class when degraded.")
    parser.add_argument("--allow-incomplete", action="store_true", help="Allow incomplete packet fields in generation mode.")
    parser.add_argument(
        "--suppress-changed-files",
        action="store_true",
        help="Emit a stable no-change entry instead of sampling transient git diff state.",
    )
    parser.add_argument("--output", default=str(DEFAULT_MARKDOWN_OUTPUT), help="Markdown handoff output path.")
    parser.add_argument("--json-output", default=str(DEFAULT_JSON_OUTPUT), help="JSON handoff output path.")
    return parser.parse_args()


def load_route(path: Path) -> dict[str, Any]:
    if not path.exists():
        return {}
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        raise ValueError(f"Invalid route JSON: {path} ({exc})") from exc


def parse_validation_entries(items: list[str]) -> list[ValidationEntry]:
    parsed: list[ValidationEntry] = []
    for item in items:
        if "::" not in item:
            raise ValueError(f"Validation entry must use '<command>::<outcome>': {item}")
        command, outcome = item.split("::", 1)
        command = command.strip()
        outcome = outcome.strip().lower()
        if not command:
            raise ValueError(f"Validation command is empty in entry: {item}")
        if outcome not in {"pass", "fail", "not-run"}:
            raise ValueError(f"Validation outcome must be pass|fail|not-run in entry: {item}")
        parsed.append(ValidationEntry(command=command, outcome=outcome))
    return parsed


def run_git_name_only(*args: str) -> list[str]:
    command = ["git", *args]
    proc = subprocess.run(command, capture_output=True, text=True)
    if proc.returncode != 0:
        return []
    lines = [line.strip().replace("\\", "/") for line in proc.stdout.splitlines() if line.strip()]
    return lines


def collect_changed_files() -> list[str]:
    unstaged = run_git_name_only("diff", "--name-only", "--diff-filter=ACMR")
    staged = run_git_name_only("diff", "--cached", "--name-only", "--diff-filter=ACMR")
    combined = sorted(set(unstaged + staged))
    return combined


def infer_changed_lanes(changed_files: list[str]) -> list[str]:
    lanes: set[str] = set()
    for path in changed_files:
        if path.startswith("docs/") or path.startswith(".codex/") or path.startswith("build/scripts/docs/"):
            lanes.add("docs")
        if path.startswith("src/Meridian.Ui/dashboard/"):
            lanes.add("browser")
        if path.startswith("src/Meridian.Wpf/") or path.startswith("tests/Meridian.Wpf.Tests/"):
            lanes.add("desktop")
        if "Provider" in path or "provider" in path:
            lanes.add("provider")
    return sorted(lanes)


def assess_route_alignment(route_lane: str, changed_files: list[str]) -> dict[str, Any]:
    inferred = infer_changed_lanes(changed_files)
    if route_lane in {"unknown", "orient"}:
        status = "unknown"
    elif not inferred:
        status = "unknown"
    elif route_lane in inferred:
        status = "aligned"
    else:
        status = "possible-misroute"
    return {
        "status": status,
        "inferredLanes": inferred,
    }


def require_nonempty(value: str | None, field_name: str) -> None:
    if value is None or not value.strip():
        raise ValueError(f"{field_name} must be non-empty.")


def enforce_packet_quality(packet: dict[str, Any], allow_incomplete: bool) -> None:
    route_lane = str(packet.get("route", {}).get("lane", "unknown"))
    non_trivial_lane = route_lane not in {"orient", "unknown"}
    if allow_incomplete or not non_trivial_lane:
        return

    require_nonempty(packet["scope"]["requested"], "scope.requested")
    require_nonempty(packet.get("nextLane"), "nextLane")
    if packet.get("nextLane") == "tbd":
        raise ValueError("nextLane must not be 'tbd' for non-trivial lanes.")

    validations = packet.get("validation", [])
    if not isinstance(validations, list) or not validations:
        raise ValueError("validation must contain at least one entry for non-trivial lanes.")
    if all(item.get("outcome") == "not-run" for item in validations if isinstance(item, dict)):
        raise ValueError("validation entries cannot all be not-run for non-trivial lanes.")


def build_packet(args: argparse.Namespace, route: dict[str, Any], changed_files: list[str]) -> dict[str, Any]:
    route_match = route.get("match", {}) if isinstance(route, dict) else {}
    route_lane = route_match.get("lane", "unknown")
    route_skill = route_match.get("skill", "unknown")
    route_mode = route_match.get("mode", "unknown")
    route_model_route_id = route_match.get("modelRouteId", "unknown")
    route_rule = route_match.get("matchedRule") or "default-or-unmatched"
    route_rationale = route_match.get("rationale", "No rationale found in route output.")
    route_validation_floor = route_match.get("validationFloor", [])
    route_validation_scripts = route_match.get("validationScripts", [])
    route_required_telemetry = route_match.get("requiredTelemetry", [])
    route_escalation_triggers = route_match.get("escalationTriggers", [])
    route_confidence = route_match.get("confidence")

    validations = parse_validation_entries(args.validation)
    if not validations and isinstance(route_validation_floor, list):
        validations = [ValidationEntry(command=str(cmd), outcome="not-run") for cmd in route_validation_floor]

    scope = args.scope or ""
    exclusions = args.excluded or ["No explicit exclusions recorded."]
    required_context = args.required_context or [
        "docs/ai/agent-handoff-checklist.md",
        "docs/ai/codex/quickstart.md",
    ]
    optional_context = args.optional_context or [
        "docs/ai/parallel-task-manifest-template.md",
    ]
    open_risks = args.open_risk or ["No explicit open risks recorded."]

    if args.suppress_changed_files:
        changed_files = []

    changes = [{"path": path, "reason": "Touched in current git diff."} for path in changed_files]
    if not changes:
        reason = (
            "Git diff evidence suppressed for deterministic docs automation."
            if args.suppress_changed_files
            else "No staged/unstaged file change detected."
        )
        changes = [{"path": "(none)", "reason": reason}]

    handoff_path = str(Path(args.output).as_posix())
    telemetry = {
        "route_id": route_rule,
        "model_route_id": route_model_route_id,
        "selected_model": args.model or "unknown",
        "input_tokens": args.input_tokens,
        "output_tokens": args.output_tokens,
        "estimated_cost_usd": args.estimated_cost_usd,
        "latency_ms": args.latency_ms,
        "error_class": args.error_class,
        "handoff_path": handoff_path,
    }
    packet = {
        "generatedAt": STABLE_GENERATED_AT,
        "scope": {
            "requested": scope,
            "excluded": exclusions,
        },
        "route": {
            "lane": route_lane,
            "skill": route_skill,
            "mode": route_mode,
            "modelRouteId": route_model_route_id,
            "matchedRule": route_rule,
            "rationale": route_rationale,
            "confidence": route_confidence,
            "validationFloor": route_validation_floor,
            "validationScripts": route_validation_scripts,
            "requiredTelemetry": route_required_telemetry,
            "escalationTriggers": route_escalation_triggers,
        },
        "routeOutcome": {
            "finalValidationStatus": args.final_status,
            "routeAssessment": assess_route_alignment(route_lane, changed_files),
        },
        "telemetry": telemetry,
        "inputsLoaded": [
            {"path": "docs/status/prompt-route-lint-report.json", "reason": "Prompt route classification source."},
            {"path": "git diff", "reason": "Changed-file evidence for handoff packet."},
        ],
        "changesMade": changes,
        "validation": [{"command": entry.command, "outcome": entry.outcome} for entry in validations],
        "openRisks": open_risks,
        "nextLane": args.next_lane,
        "requiredContext": required_context,
        "optionalContext": optional_context,
    }
    enforce_required_telemetry(packet, args.allow_incomplete)
    enforce_packet_quality(packet, args.allow_incomplete)
    return packet


def enforce_required_telemetry(packet: dict[str, Any], allow_incomplete: bool) -> None:
    if allow_incomplete:
        return

    route = packet.get("route", {})
    high_risk = route.get("mode") == "Deep Review" or route.get("lane") in {"provider", "governance"}
    if not high_risk:
        return

    telemetry = packet.get("telemetry", {})
    required = route.get("requiredTelemetry", [])
    if not isinstance(required, list):
        raise ValueError("route.requiredTelemetry must be a list.")

    for signal in required:
        value = telemetry.get(str(signal))
        if value in (None, "", "unknown"):
            raise ValueError(f"telemetry.{signal} required for high-risk route.")


def render_markdown(packet: dict[str, Any]) -> str:
    lines: list[str] = []
    lines.append("# AI Handoff Packet")
    lines.append("")
    lines.append("## Scope")
    lines.append(f"- Requested: {packet['scope']['requested']}")
    for excluded in packet["scope"]["excluded"]:
        lines.append(f"- Excluded: {excluded}")
    lines.append("")
    lines.append("## Route")
    route = packet["route"]
    lines.append(f"- Lane: {route['lane']}")
    lines.append(f"- Skill: {route['skill']}")
    lines.append(f"- Mode: {route['mode']}")
    lines.append(f"- Model route: {route['modelRouteId']}")
    lines.append(f"- Matched rule: {route['matchedRule']}")
    lines.append(f"- Confidence: {route.get('confidence')}")
    lines.append(f"- Rationale: {route['rationale']}")
    lines.append("")
    lines.append("## Route outcome")
    outcome = packet["routeOutcome"]
    lines.append(f"- Final status: {outcome['finalValidationStatus']}")
    lines.append(f"- Route assessment: {outcome['routeAssessment']['status']}")
    lines.append(f"- Inferred lanes from changes: {', '.join(outcome['routeAssessment']['inferredLanes']) or '(none)'}")
    lines.append("")
    lines.append("## Telemetry")
    telemetry = packet["telemetry"]
    lines.append(f"- route_id: {telemetry['route_id']}")
    lines.append(f"- model_route_id: {telemetry['model_route_id']}")
    lines.append(f"- selected_model: {telemetry['selected_model']}")
    lines.append(f"- input_tokens: {telemetry['input_tokens']}")
    lines.append(f"- output_tokens: {telemetry['output_tokens']}")
    lines.append(f"- estimated_cost_usd: {telemetry['estimated_cost_usd']}")
    lines.append(f"- latency_ms: {telemetry['latency_ms']}")
    lines.append(f"- error_class: {telemetry['error_class']}")
    lines.append(f"- handoff_path: {telemetry['handoff_path']}")
    lines.append("")
    lines.append("## Inputs loaded")
    for item in packet["inputsLoaded"]:
        lines.append(f"- {item['path']}: {item['reason']}")
    lines.append("")
    lines.append("## Changes made")
    for change in packet["changesMade"]:
        lines.append(f"- {change['path']}: {change['reason']}")
    lines.append("")
    lines.append("## Validation")
    for item in packet["validation"]:
        lines.append(f"- [{item['outcome']}] {item['command']}")
    lines.append("")
    lines.append("## Open risks")
    for risk in packet["openRisks"]:
        lines.append(f"- {risk}")
    lines.append("")
    lines.append("## Next lane")
    lines.append(f"- {packet['nextLane']}")
    lines.append("")
    lines.append("## Required context")
    for item in packet["requiredContext"]:
        lines.append(f"- {item}")
    lines.append("")
    lines.append("## Optional context")
    for item in packet["optionalContext"]:
        lines.append(f"- {item}")
    lines.append("")
    return "\n".join(lines)


def write_text(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    normalized = text.replace("\r\n", "\n").replace("\r", "\n")
    if not normalized.endswith("\n"):
        normalized += "\n"
    path.write_text(normalized, encoding="utf-8")


def main() -> int:
    args = parse_args()
    route_path = Path(args.route_json)

    try:
        route_data = load_route(route_path)
        validations = parse_validation_entries(args.validation)
    except ValueError as exc:
        print(f"[handoff-packet-generator] FAILED: {exc}", file=sys.stderr)
        return 1

    if args.summary:
        valid_route = bool(route_data.get("valid")) if route_data else False
        print("[handoff-packet-generator] OK")
        print(f"route_json={route_path}")
        print(f"route_valid={valid_route}")
        print(f"validation_entries={len(validations)}")
        return 0

    try:
        packet = build_packet(args, route_data, collect_changed_files())
    except ValueError as exc:
        print(f"[handoff-packet-generator] FAILED: {exc}", file=sys.stderr)
        return 1

    markdown = render_markdown(packet)
    output_path = Path(args.output)
    json_output_path = Path(args.json_output)

    write_text(output_path, markdown)
    write_text(json_output_path, json.dumps(packet, indent=2))

    print(f"[handoff-packet-generator] wrote markdown: {output_path}")
    print(f"[handoff-packet-generator] wrote json: {json_output_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
