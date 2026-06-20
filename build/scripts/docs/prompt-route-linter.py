#!/usr/bin/env python3
"""Validate and apply deterministic prompt-to-route rules for Codex AI execution.

Examples:
    python3 build/scripts/docs/prompt-route-linter.py --summary
    python3 build/scripts/docs/prompt-route-linter.py --prompt "review this PR for regressions"
    python3 build/scripts/docs/prompt-route-linter.py --prompt-file prompt.txt --json-output docs/status/prompt-route-lint.json
"""

from __future__ import annotations

import argparse
import json
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any


DEFAULT_RULES_PATH = Path("docs/ai/codex/prompt-route-rules.json")
DEFAULT_JSON_OUTPUT = Path("docs/status/prompt-route-lint-report.json")
STABLE_GENERATED_AT = "1970-01-01T00:00:00+00:00"
ALLOWED_MODES = {"Lightweight", "Standard", "Deep Review"}


@dataclass
class MatchResult:
    lane: str
    skill: str
    mode: str
    model_route_id: str
    rationale: str
    validation_floor: list[str]
    validation_scripts: list[str]
    required_telemetry: list[str]
    escalation_triggers: list[str]
    matched_rule: str | None
    score: float
    confidence: float
    fallback_applied: bool


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--rules",
        default=str(DEFAULT_RULES_PATH),
        help=f"Path to prompt-route rules JSON (default: {DEFAULT_RULES_PATH}).",
    )
    parser.add_argument("--prompt", help="Prompt text to classify against route rules.")
    parser.add_argument("--prompt-file", help="Path to a file containing prompt text to classify.")
    parser.add_argument("--summary", action="store_true", help="Print a compact validation summary.")
    parser.add_argument(
        "--json-output",
        default=str(DEFAULT_JSON_OUTPUT),
        help=f"Output path for lint + match JSON report (default: {DEFAULT_JSON_OUTPUT}).",
    )
    return parser.parse_args()


def load_json(path: Path) -> dict[str, Any]:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError as exc:
        raise ValueError(f"Rules file not found: {path}") from exc
    except json.JSONDecodeError as exc:
        raise ValueError(f"Invalid JSON in rules file {path}: {exc}") from exc


def _require_field(container: dict[str, Any], field: str, expected_type: type, context: str) -> None:
    value = container.get(field)
    if value is None:
        raise ValueError(f"{context}: missing required field '{field}'.")
    if not isinstance(value, expected_type):
        raise ValueError(f"{context}: field '{field}' must be {expected_type.__name__}.")


def _read_rule_weight_map(rule: dict[str, Any], context: str) -> dict[str, float]:
    if "matchWeighted" in rule:
        weighted = rule["matchWeighted"]
        if not isinstance(weighted, dict):
            raise ValueError(f"{context}.matchWeighted must be an object.")
        result: dict[str, float] = {}
        for token, weight in weighted.items():
            if not isinstance(token, str) or not token.strip():
                raise ValueError(f"{context}.matchWeighted contains invalid token.")
            if not isinstance(weight, (int, float)) or weight <= 0:
                raise ValueError(f"{context}.matchWeighted['{token}'] must be positive number.")
            result[token] = float(weight)
        return result

    # Backward-compatible fallback from matchAny.
    tokens = rule.get("matchAny", [])
    if not isinstance(tokens, list):
        raise ValueError(f"{context}.matchAny must be a list.")
    return {token: 1.0 for token in tokens if isinstance(token, str) and token.strip()}


def validate_ruleset(data: dict[str, Any]) -> None:
    _require_field(data, "schemaVersion", int, "ruleset")
    _require_field(data, "defaultRoute", dict, "ruleset")
    _require_field(data, "rules", list, "ruleset")

    default_route = data["defaultRoute"]
    route_fields = (
        "lane",
        "skill",
        "mode",
        "modelRouteId",
        "rationale",
        "validationFloor",
        "validationScripts",
        "requiredTelemetry",
        "escalationTriggers",
    )
    list_fields = {"validationFloor", "validationScripts", "requiredTelemetry", "escalationTriggers"}

    for field in route_fields:
        if field in list_fields:
            _require_field(default_route, field, list, "defaultRoute")
        else:
            _require_field(default_route, field, str, "defaultRoute")
    if default_route["mode"] not in ALLOWED_MODES:
        raise ValueError(f"defaultRoute.mode must be one of {sorted(ALLOWED_MODES)}.")

    threshold = data.get("confidenceThreshold", 0.55)
    if not isinstance(threshold, (int, float)) or threshold <= 0 or threshold > 1:
        raise ValueError("ruleset.confidenceThreshold must be in (0, 1].")

    for idx, rule in enumerate(data["rules"], start=1):
        if not isinstance(rule, dict):
            raise ValueError(f"rules[{idx}] must be an object.")
        context = f"rules[{idx}]"
        for field in ("id", "description", "route"):
            expected = dict if field == "route" else str
            _require_field(rule, field, expected, context)
        route = rule["route"]
        for field in route_fields:
            if field in list_fields:
                _require_field(route, field, list, f"{context}.route")
            else:
                _require_field(route, field, str, f"{context}.route")
        if route["mode"] not in ALLOWED_MODES:
            raise ValueError(f"{context}.route.mode must be one of {sorted(ALLOWED_MODES)}.")

        weight_map = _read_rule_weight_map(rule, context)
        if not weight_map:
            raise ValueError(f"{context} must define at least one keyword in matchAny/matchWeighted.")


def resolve_prompt(args: argparse.Namespace) -> str | None:
    if args.prompt and args.prompt_file:
        raise ValueError("Use only one of --prompt or --prompt-file.")
    if args.prompt_file:
        return Path(args.prompt_file).read_text(encoding="utf-8")
    if args.prompt:
        return args.prompt
    return None


def _score_rule(prompt_lower: str, rule: dict[str, Any]) -> float:
    weighted = _read_rule_weight_map(rule, "rule")
    score = 0.0
    for token, weight in weighted.items():
        if token.lower() in prompt_lower:
            score += weight
    return score


def _confidence_from_score(score: float, max_score: float) -> float:
    if max_score <= 0:
        return 0.0
    # Confidence should represent enough evidence for a lane, not every possible keyword firing.
    return min(1.0, score / min(max_score, 4.0))


def match_route(prompt: str, data: dict[str, Any]) -> MatchResult:
    prompt_lower = prompt.lower()
    best_rule: dict[str, Any] | None = None
    best_score = 0.0
    best_max_score = 1.0

    for rule in data["rules"]:
        score = _score_rule(prompt_lower, rule)
        if score <= 0:
            continue
        max_score = sum(_read_rule_weight_map(rule, "rule").values())
        if score > best_score:
            best_rule = rule
            best_score = score
            best_max_score = max_score

    threshold = float(data.get("confidenceThreshold", 0.55))
    default_route = data["defaultRoute"]

    if best_rule is None:
        return MatchResult(
            lane=default_route["lane"],
            skill=default_route["skill"],
            mode=default_route["mode"],
            model_route_id=default_route["modelRouteId"],
            rationale=default_route["rationale"],
            validation_floor=default_route["validationFloor"],
            validation_scripts=default_route["validationScripts"],
            required_telemetry=default_route["requiredTelemetry"],
            escalation_triggers=default_route["escalationTriggers"],
            matched_rule=None,
            score=0.0,
            confidence=0.0,
            fallback_applied=False,
        )

    confidence = _confidence_from_score(best_score, best_max_score)
    route = best_rule["route"]

    if confidence < threshold:
        fallback_skill = default_route.get("fallbackSkill", default_route["skill"])
        return MatchResult(
            lane=default_route["lane"],
            skill=fallback_skill,
            mode=default_route["mode"],
            model_route_id=default_route["modelRouteId"],
            rationale=(
                f"{default_route['rationale']} "
                f"Low-confidence specialist match ({confidence:.2f} < {threshold:.2f}) fell back from rule '{best_rule['id']}'."
            ),
            validation_floor=default_route["validationFloor"],
            validation_scripts=default_route["validationScripts"],
            required_telemetry=default_route["requiredTelemetry"],
            escalation_triggers=default_route["escalationTriggers"],
            matched_rule=best_rule["id"],
            score=best_score,
            confidence=confidence,
            fallback_applied=True,
        )

    return MatchResult(
        lane=route["lane"],
        skill=route["skill"],
        mode=route["mode"],
        model_route_id=route["modelRouteId"],
        rationale=route["rationale"],
        validation_floor=route["validationFloor"],
        validation_scripts=route["validationScripts"],
        required_telemetry=route["requiredTelemetry"],
        escalation_triggers=route["escalationTriggers"],
        matched_rule=best_rule["id"],
        score=best_score,
        confidence=confidence,
        fallback_applied=False,
    )


def write_json_report(path: Path, valid: bool, rules_path: str, result: MatchResult | None) -> None:
    payload: dict[str, Any] = {
        "valid": valid,
        "rulesPath": Path(rules_path).as_posix(),
        "generatedAt": STABLE_GENERATED_AT,
    }
    if result:
        payload["match"] = {
            "lane": result.lane,
            "skill": result.skill,
            "mode": result.mode,
            "modelRouteId": result.model_route_id,
            "rationale": result.rationale,
            "validationFloor": result.validation_floor,
            "validationScripts": result.validation_scripts,
            "requiredTelemetry": result.required_telemetry,
            "escalationTriggers": result.escalation_triggers,
            "matchedRule": result.matched_rule,
            "score": round(result.score, 3),
            "confidence": round(result.confidence, 3),
            "fallbackApplied": result.fallback_applied,
        }
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")


def main() -> int:
    args = parse_args()
    rules_path = Path(args.rules)

    try:
        data = load_json(rules_path)
        validate_ruleset(data)
        prompt = resolve_prompt(args)
    except ValueError as exc:
        print(f"[prompt-route-linter] FAILED: {exc}", file=sys.stderr)
        if args.json_output:
            write_json_report(Path(args.json_output), False, str(rules_path), None)
        return 1

    result: MatchResult | None = None
    if prompt:
        result = match_route(prompt, data)
        print(f"lane={result.lane}")
        print(f"skill={result.skill}")
        print(f"mode={result.mode}")
        print(f"model_route_id={result.model_route_id}")
        print(f"matched_rule={result.matched_rule or 'default'}")
        print(f"confidence={result.confidence:.3f}")
        print(f"fallback_applied={str(result.fallback_applied).lower()}")
        print(f"rationale={result.rationale}")
        print("validation_floor:")
        for command in result.validation_floor:
            print(f"  - {command}")
        print("validation_scripts:")
        for script_name in result.validation_scripts:
            print(f"  - {script_name}")
        print("required_telemetry:")
        for signal in result.required_telemetry:
            print(f"  - {signal}")
        print("escalation_triggers:")
        for trigger in result.escalation_triggers:
            print(f"  - {trigger}")
    elif args.summary:
        result = match_route("", data)
        print("[prompt-route-linter] OK")
        print(f"rules={rules_path}")
        print(f"rules_count={len(data['rules'])}")
        print(f"default_skill={data['defaultRoute']['skill']}")
        print(f"confidence_threshold={float(data.get('confidenceThreshold', 0.55)):.2f}")

    write_json_report(Path(args.json_output), True, rules_path.as_posix(), result)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
