#!/usr/bin/env python3
"""Validate roadmap evidence contracts and canonical roadmap enum fixtures.

Usage:
  python3 tools/roadmap/validate_roadmap.py
  python3 tools/roadmap/validate_roadmap.py --roadmap docs/roadmap/data/roadmap-items.yml
  python3 tools/roadmap/validate_roadmap.py --fixtures-dir tools/roadmap/fixtures
"""

from __future__ import annotations

import argparse
import datetime as dt
import json
import re
import sys
from pathlib import Path
from typing import Any

SHA_PATTERN = re.compile(r"^[0-9a-fA-F]{7,40}$")
CANONICAL = {
    "status": {"not_started", "in_progress", "blocked", "completed", "cancelled"},
    "health": {"green", "yellow", "red", "unknown"},
    "priority": {"low", "medium", "high", "critical"},
    "evidence_posture": {"none", "draft", "partial", "verified", "audited"},
    "completion_term": {"done", "complete", "completed", "closed", "shipped"},
}


def _try_import_yaml() -> Any:
    try:
        import yaml  # type: ignore[import-untyped]

        return yaml
    except ImportError:
        return None


def _strip_yaml_quotes(value: str) -> str:
    value = value.strip()
    if len(value) >= 2 and value[0] == value[-1] and value[0] in ('"', "'"):
        return value[1:-1]
    return value


def _parse_yaml_value(raw: str) -> Any:
    stripped = raw.strip()
    if stripped in ("[]",):
        return []
    if stripped in ("{}",):
        return {}
    if stripped in ("~", "null", ""):
        return None
    if stripped in ("true", "True", "yes", "Yes", "on", "On"):
        return True
    if stripped in ("false", "False", "no", "No", "off", "Off"):
        return False
    try:
        return int(stripped)
    except ValueError:
        pass
    try:
        return float(stripped)
    except ValueError:
        pass
    return _strip_yaml_quotes(stripped)


def _indent_level(line: str) -> int:
    return len(line) - len(line.lstrip(" "))


def _minimal_yaml_load(text: str) -> Any:
    return _parse_block(text.splitlines(), 0)[0]


def _parse_block(lines: list[str], idx: int) -> tuple[Any, int]:
    while idx < len(lines) and (lines[idx].strip() == "" or lines[idx].strip().startswith("#")):
        idx += 1
    if idx >= len(lines):
        return None, idx

    stripped = lines[idx].lstrip(" ")
    if stripped.startswith("- "):
        return _parse_sequence(lines, idx, _indent_level(lines[idx]))
    if ":" in stripped:
        return _parse_mapping(lines, idx, _indent_level(lines[idx]))
    return _parse_yaml_value(stripped), idx + 1


def _parse_sequence(lines: list[str], idx: int, base_indent: int) -> tuple[list[Any], int]:
    items: list[Any] = []
    while idx < len(lines):
        raw = lines[idx]
        stripped = raw.strip()
        if stripped == "" or stripped.startswith("#"):
            idx += 1
            continue

        indent = _indent_level(raw)
        if indent < base_indent or not raw.lstrip(" ").startswith("- "):
            break

        content = raw.lstrip(" ")[2:].strip()
        if content == "":
            sequence_item, idx = _parse_block(lines, idx + 1)
            items.append(sequence_item)
            continue

        if ":" in content:
            key, value = content.split(":", 1)
            key = key.strip()
            value = value.strip()
            mapping_item: dict[str, Any] = {}
            if value:
                mapping_item[key] = _parse_yaml_value(value)
                idx += 1
            else:
                nested, idx = _parse_block(lines, idx + 1)
                mapping_item[key] = nested
            while idx < len(lines):
                child = lines[idx]
                child_stripped = child.strip()
                if child_stripped == "" or child_stripped.startswith("#"):
                    idx += 1
                    continue
                child_indent = _indent_level(child)
                if child_indent <= indent:
                    break
                if ":" not in child.lstrip(" "):
                    break
                child_key, child_value = child.lstrip(" ").split(":", 1)
                child_key = child_key.strip()
                child_value = child_value.strip()
                if child_value:
                    mapping_item[child_key] = _parse_yaml_value(child_value)
                    idx += 1
                else:
                    nested, idx = _parse_block(lines, idx + 1)
                    mapping_item[child_key] = nested
            items.append(mapping_item)
            continue

        items.append(_parse_yaml_value(content))
        idx += 1

    return items, idx


def _parse_mapping(lines: list[str], idx: int, base_indent: int) -> tuple[dict[str, Any], int]:
    mapping: dict[str, Any] = {}
    while idx < len(lines):
        raw = lines[idx]
        stripped = raw.strip()
        if stripped == "" or stripped.startswith("#"):
            idx += 1
            continue

        indent = _indent_level(raw)
        if indent < base_indent:
            break
        if indent > base_indent:
            raise ValueError(f"Unexpected indentation at line: {raw}")

        if ":" not in raw.lstrip(" "):
            break

        key, value = raw.lstrip(" ").split(":", 1)
        key = key.strip()
        value = value.strip()
        if value:
            mapping[key] = _parse_yaml_value(value)
            idx += 1
            continue

        nested, idx = _parse_block(lines, idx + 1)
        mapping[key] = nested

    return mapping, idx


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Validate roadmap tooling inputs.")
    parser.add_argument(
        "--roadmap",
        help="Validate roadmap evidence rules from the specified YAML file.",
    )
    parser.add_argument(
        "--fixtures-dir",
        help="Validate canonical enum JSON fixtures from this directory. Defaults to tools/roadmap/fixtures when --roadmap is omitted.",
    )
    return parser.parse_args()


def load_yaml(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8") as handle:
        text = handle.read()

    yaml_module = _try_import_yaml()
    payload = yaml_module.safe_load(text) if yaml_module is not None else _minimal_yaml_load(text)
    payload = payload or {}
    if not isinstance(payload, dict):
        raise ValueError("Top-level roadmap payload must be a mapping/object.")
    return payload


def _is_non_empty(value: Any) -> bool:
    return isinstance(value, str) and bool(value.strip())


def _valid_date(value: Any) -> bool:
    if not _is_non_empty(value):
        return False
    try:
        dt.date.fromisoformat(str(value))
    except ValueError:
        return False
    return True


def validate_evidence_record(item_key: str, index: int, record: Any) -> list[str]:
    prefix = f"item '{item_key}' evidence[{index}]"
    errors: list[str] = []
    if not isinstance(record, dict):
        return [f"{prefix}: expected object, got {type(record).__name__}."]

    required_fields = ["type", "produced_by", "produced_on", "commit_sha", "reviewed_by", "review_status"]
    for field in required_fields:
        if not _is_non_empty(record.get(field)):
            errors.append(f"{prefix}: missing required field '{field}'.")

    has_path = _is_non_empty(record.get("path"))
    has_uri = _is_non_empty(record.get("uri"))
    if not (has_path or has_uri):
        errors.append(f"{prefix}: include either non-empty 'path' or non-empty 'uri'.")

    commit_sha = record.get("commit_sha")
    if _is_non_empty(commit_sha) and not SHA_PATTERN.match(str(commit_sha)):
        errors.append(f"{prefix}: 'commit_sha' must be 7-40 hex chars; got '{commit_sha}'.")

    produced_on = record.get("produced_on")
    if produced_on is not None and not _valid_date(produced_on):
        errors.append(f"{prefix}: 'produced_on' must be ISO date YYYY-MM-DD; got '{produced_on}'.")

    return errors


def validate_transition(item: dict[str, Any], transition: str, rules: dict[str, Any], item_key: str) -> list[str]:
    errors: list[str] = []
    evidence = item.get("evidence")
    if evidence is None:
        evidence = []
    elif not isinstance(evidence, list):
        errors.append(f"item '{item_key}': 'evidence' must be a list when present; got {type(evidence).__name__}.")
        evidence = []

    min_records = int(rules.get("minimum_evidence_records", 0))
    if len(evidence) < min_records:
        errors.append(
            f"item '{item_key}': status '{transition}' requires at least {min_records} evidence records; found {len(evidence)}."
        )

    for idx, rec in enumerate(evidence):
        errors.extend(validate_evidence_record(item_key, idx, rec))

    if bool(rules.get("require_reviewer_metadata", False)):
        for idx, rec in enumerate(evidence):
            if not isinstance(rec, dict):
                continue
            if not _is_non_empty(rec.get("reviewed_by")):
                errors.append(f"item '{item_key}' evidence[{idx}]: missing 'reviewed_by'.")
            if not _is_non_empty(rec.get("review_status")):
                errors.append(f"item '{item_key}' evidence[{idx}]: missing 'review_status'.")

    allowed_statuses = [status for status in (rules.get("allowed_review_statuses") or []) if _is_non_empty(status)]
    if allowed_statuses:
        for idx, rec in enumerate(evidence):
            if not isinstance(rec, dict):
                continue
            review_status = rec.get("review_status")
            if _is_non_empty(review_status) and review_status not in allowed_statuses:
                errors.append(
                    f"item '{item_key}' evidence[{idx}]: review_status '{review_status}' not allowed for '{transition}'; allowed: {allowed_statuses}."
                )

    required_types = {evidence_type for evidence_type in (rules.get("required_evidence_types") or []) if _is_non_empty(evidence_type)}
    if required_types:
        present_types = {rec.get("type") for rec in evidence if isinstance(rec, dict) and _is_non_empty(rec.get("type"))}
        missing_types = sorted(required_types - present_types)
        if missing_types:
            errors.append(
                f"item '{item_key}': status '{transition}' requires evidence type(s) {missing_types}; present: {sorted(present_types)}."
            )

    return errors


def validate_roadmap(roadmap: dict[str, Any]) -> list[str]:
    errors: list[str] = []
    contract = roadmap.get("contract")
    items = roadmap.get("items")

    if not isinstance(contract, dict):
        return ["Missing or invalid 'contract' section in roadmap YAML."]
    if not isinstance(items, list):
        return ["Missing or invalid 'items' list in roadmap YAML."]

    statuses = contract.get("statuses")
    if not isinstance(statuses, list):
        return ["Missing or invalid 'contract.statuses' list."]
    allowed_item_statuses = {status for status in statuses if _is_non_empty(status)}
    if len(allowed_item_statuses) != len(statuses):
        errors.append("'contract.statuses' must only contain non-empty string values.")

    transition_rules = contract.get("transition_evidence_requirements")
    if not isinstance(transition_rules, dict):
        return ["Missing 'contract.transition_evidence_requirements' mapping."]

    for target, rules in transition_rules.items():
        if not _is_non_empty(target):
            errors.append("Transition rule keys must be non-empty strings.")
            continue
        if allowed_item_statuses and target not in allowed_item_statuses:
            errors.append(f"Contract transition rule '{target}' is not listed in 'contract.statuses'.")
        if not isinstance(rules, dict):
            errors.append(f"Contract transition rules for '{target}' must be an object.")

    for idx, item in enumerate(items):
        if not isinstance(item, dict):
            errors.append(f"items[{idx}] is not an object.")
            continue

        item_key = str(item.get("id") or item.get("title") or f"index-{idx}")
        status = item.get("status")
        if not _is_non_empty(status):
            errors.append(f"item '{item_key}': missing non-empty 'status'.")
            continue
        if status not in allowed_item_statuses:
            errors.append(f"item '{item_key}': status '{status}' is not allowed; expected one of {sorted(allowed_item_statuses)}.")
            continue
        if status not in transition_rules:
            continue

        rules = transition_rules.get(status)
        if not isinstance(rules, dict):
            continue
        errors.extend(validate_transition(item, status, rules, item_key))

    return errors


def _check_canonical_enums(node: Any, path: str = "$") -> list[str]:
    errors: list[str] = []
    if isinstance(node, dict):
        for key, value in node.items():
            child_path = f"{path}.{key}"
            if key in CANONICAL and value not in CANONICAL[key]:
                allowed = ", ".join(sorted(CANONICAL[key]))
                errors.append(f"{child_path}: invalid '{value}' for {key}; allowed: [{allowed}]")
            errors.extend(_check_canonical_enums(value, child_path))
    elif isinstance(node, list):
        for index, item in enumerate(node):
            errors.extend(_check_canonical_enums(item, f"{path}[{index}]"))
    return errors


def validate_fixture_file(path: Path) -> int:
    data = json.loads(path.read_text(encoding="utf-8"))
    errors = _check_canonical_enums(data)
    if errors:
        print(f"{path} failed enum validation:")
        for error in errors:
            print(f"  - {error}")
        return 1
    print(f"{path} passed enum validation")
    return 0


def validate_fixture_dir(path: Path) -> int:
    exit_code = 0
    for json_file in sorted(path.glob("*.json")):
        rc = validate_fixture_file(json_file)
        is_invalid_fixture = json_file.name.startswith("invalid-")
        if is_invalid_fixture and rc == 0:
            print(f"{json_file} expected failure but passed")
            exit_code = 1
        elif not is_invalid_fixture and rc != 0:
            print(f"{json_file} expected pass but failed")
            exit_code = 1
        elif is_invalid_fixture and rc != 0:
            print(f"{json_file} correctly failed")
        else:
            print(f"{json_file} correctly passed")
    return exit_code


def run_roadmap_validation(path: Path) -> int:
    if not path.exists():
        print(f"Error: roadmap file not found: {path}", file=sys.stderr)
        return 2

    try:
        roadmap = load_yaml(path)
    except Exception as exc:
        print(f"Error: failed to parse roadmap YAML: {exc}", file=sys.stderr)
        return 2

    errors = validate_roadmap(roadmap)
    if errors:
        print("Roadmap validation failed:", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        print(
            "Fix guidance: add evidence records with required metadata before promoting items to ready_for_acceptance/accepted/done.",
            file=sys.stderr,
        )
        return 1

    print("Roadmap validation passed.")
    return 0


def main() -> int:
    args = parse_args()
    if args.roadmap:
        return run_roadmap_validation(Path(args.roadmap))

    fixtures_dir = Path(args.fixtures_dir) if args.fixtures_dir else Path(__file__).parent / "fixtures"
    if not fixtures_dir.exists():
        print(f"Error: fixtures directory not found: {fixtures_dir}", file=sys.stderr)
        return 2
    return validate_fixture_dir(fixtures_dir)


if __name__ == "__main__":
    raise SystemExit(main())
