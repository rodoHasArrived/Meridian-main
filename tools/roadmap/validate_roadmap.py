#!/usr/bin/env python3
"""Validate roadmap transition evidence requirements.

Usage:
  python3 tools/roadmap/validate_roadmap.py \
    --roadmap docs/roadmap/data/roadmap-items.yml
"""

from __future__ import annotations

import argparse
import datetime as dt
import re
import sys
from pathlib import Path
from typing import Any

import yaml

TRANSITION_TARGETS = {"ready_for_acceptance", "accepted", "done"}
SHA_PATTERN = re.compile(r"^[0-9a-fA-F]{7,40}$")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Validate roadmap evidence contract rules.")
    parser.add_argument(
        "--roadmap",
        default="docs/roadmap/data/roadmap-items.yml",
        help="Path to roadmap-items.yml",
    )
    return parser.parse_args()


def load_yaml(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8") as handle:
        payload = yaml.safe_load(handle) or {}
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
        errors.append(
            f"{prefix}: 'commit_sha' must be 7-40 hex chars; got '{commit_sha}'."
        )

    produced_on = record.get("produced_on")
    if produced_on is not None and not _valid_date(produced_on):
        errors.append(
            f"{prefix}: 'produced_on' must be ISO date YYYY-MM-DD; got '{produced_on}'."
        )

    return errors


def validate_transition(item: dict[str, Any], transition: str, rules: dict[str, Any], item_key: str) -> list[str]:
    errors: list[str] = []
    evidence = item.get("evidence")
    if not isinstance(evidence, list):
        evidence = []

    min_records = int(rules.get("minimum_evidence_records", 0))
    if len(evidence) < min_records:
        errors.append(
            f"item '{item_key}': status '{transition}' requires at least {min_records} evidence records; found {len(evidence)}."
        )

    for idx, rec in enumerate(evidence):
        errors.extend(validate_evidence_record(item_key, idx, rec))

    require_reviewer = bool(rules.get("require_reviewer_metadata", False))
    if require_reviewer:
        for idx, rec in enumerate(evidence):
            if not isinstance(rec, dict):
                continue
            if not _is_non_empty(rec.get("reviewed_by")):
                errors.append(f"item '{item_key}' evidence[{idx}]: missing 'reviewed_by'.")
            if not _is_non_empty(rec.get("review_status")):
                errors.append(f"item '{item_key}' evidence[{idx}]: missing 'review_status'.")

    allowed_statuses = rules.get("allowed_review_statuses") or []
    if allowed_statuses:
        for idx, rec in enumerate(evidence):
            if not isinstance(rec, dict):
                continue
            review_status = rec.get("review_status")
            if _is_non_empty(review_status) and review_status not in allowed_statuses:
                errors.append(
                    f"item '{item_key}' evidence[{idx}]: review_status '{review_status}' not allowed for '{transition}'; allowed: {allowed_statuses}."
                )

    required_types = set(rules.get("required_evidence_types") or [])
    if required_types:
        present_types = {
            rec.get("type") for rec in evidence if isinstance(rec, dict) and _is_non_empty(rec.get("type"))
        }
        missing_types = sorted(required_types - present_types)
        if missing_types:
            errors.append(
                f"item '{item_key}': status '{transition}' requires evidence type(s) {missing_types}; present: {sorted(present_types)}."
            )

    return errors


def validate(roadmap: dict[str, Any]) -> list[str]:
    errors: list[str] = []
    contract = roadmap.get("contract")
    items = roadmap.get("items")

    if not isinstance(contract, dict):
        return ["Missing or invalid 'contract' section in roadmap YAML."]
    if not isinstance(items, list):
        return ["Missing or invalid 'items' list in roadmap YAML."]

    transition_rules = contract.get("transition_evidence_requirements")
    if not isinstance(transition_rules, dict):
        return ["Missing 'contract.transition_evidence_requirements' mapping."]

    for target in TRANSITION_TARGETS:
        if target not in transition_rules:
            errors.append(f"Contract missing transition rules for '{target}'.")

    for idx, item in enumerate(items):
        if not isinstance(item, dict):
            errors.append(f"items[{idx}] is not an object.")
            continue

        item_key = str(item.get("id") or item.get("title") or f"index-{idx}")
        status = item.get("status")
        if status in TRANSITION_TARGETS:
            rules = transition_rules.get(status, {})
            if not isinstance(rules, dict):
                errors.append(f"item '{item_key}': transition rules for '{status}' must be an object.")
                continue
            errors.extend(validate_transition(item, status, rules, item_key))

    return errors


def main() -> int:
    args = parse_args()
    roadmap_path = Path(args.roadmap)
    if not roadmap_path.exists():
        print(f"Error: roadmap file not found: {roadmap_path}", file=sys.stderr)
        return 2

    try:
        roadmap = load_yaml(roadmap_path)
    except Exception as exc:
        print(f"Error: failed to parse roadmap YAML: {exc}", file=sys.stderr)
        return 2

    errors = validate(roadmap)
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


if __name__ == "__main__":
    raise SystemExit(main())
