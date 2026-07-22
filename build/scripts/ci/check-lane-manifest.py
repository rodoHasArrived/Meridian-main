#!/usr/bin/env python3
"""Validate Meridian CI lane metadata."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[3]
DEFAULT_MANIFEST = REPO_ROOT / "build" / "ci" / "lane-manifest.json"
REQUIRED_STATUS_CHECK = "Meridian CI / quality-gate"
REQUIRED_LANES = {
    "bootstrap",
    "quality-gate",
    "verify-dotnet",
    "verify-browser",
    "verify-docs",
    "verify-workflows",
    "verify-fast",
    "targeted-test",
    "verify-desktop",
    "verify-release",
    "verify-full",
}
REQUIRED_LANE_FIELDS = {
    "id",
    "displayName",
    "runner",
    "localCommand",
    "hostedMode",
    "artifacts",
    "timeoutMinutes",
    "pathTriggerNotes",
    "requiredCheckRole",
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Validate build/ci/lane-manifest.json.")
    parser.add_argument("--manifest", default=str(DEFAULT_MANIFEST), help="Lane manifest path.")
    parser.add_argument("--summary", action="store_true", help="Print a compact lane summary.")
    return parser.parse_args()


def load_manifest(path: Path) -> dict[str, Any]:
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError as exc:
        raise ValueError(f"Missing lane manifest: {path}") from exc
    except json.JSONDecodeError as exc:
        raise ValueError(f"Invalid JSON in lane manifest: {exc}") from exc

    if payload.get("schemaVersion") != 1:
        raise ValueError("Lane manifest schemaVersion must be 1.")

    lanes = payload.get("lanes")
    if not isinstance(lanes, list) or not lanes:
        raise ValueError("Lane manifest must contain a non-empty lanes array.")

    required_status_check = payload.get("requiredStatusCheck")
    if not isinstance(required_status_check, dict):
        raise ValueError("Lane manifest must declare requiredStatusCheck.")
    if required_status_check.get("checkName") != REQUIRED_STATUS_CHECK:
        raise ValueError(
            f"requiredStatusCheck.checkName must remain '{REQUIRED_STATUS_CHECK}' "
            "so branch protection can require one stable aggregator."
        )
    if required_status_check.get("workflow") != "Meridian CI" or required_status_check.get("job") != "quality-gate":
        raise ValueError("requiredStatusCheck must point to Meridian CI / quality-gate.")

    return payload


def validate_lane(lane: dict[str, Any], seen_ids: set[str]) -> list[str]:
    errors: list[str] = []
    lane_id = lane.get("id")
    if not isinstance(lane_id, str) or not lane_id:
        return ["lane id must be a non-empty string."]
    if lane_id in seen_ids:
        errors.append(f"duplicate lane id '{lane_id}'.")
    seen_ids.add(lane_id)

    missing = sorted(REQUIRED_LANE_FIELDS - set(lane))
    if missing:
        errors.append(f"lane '{lane_id}' missing fields: {', '.join(missing)}.")

    for field in ("displayName", "runner", "localCommand", "pathTriggerNotes", "requiredCheckRole"):
        value = lane.get(field)
        if not isinstance(value, str) or not value.strip():
            errors.append(f"lane '{lane_id}' field '{field}' must be a non-empty string.")

    artifacts = lane.get("artifacts")
    if not isinstance(artifacts, list):
        errors.append(f"lane '{lane_id}' artifacts must be a list.")
    elif any(not isinstance(entry, str) or not entry for entry in artifacts):
        errors.append(f"lane '{lane_id}' artifacts must contain only non-empty strings.")

    timeout = lane.get("timeoutMinutes")
    if not isinstance(timeout, int) or timeout < 0:
        errors.append(f"lane '{lane_id}' timeoutMinutes must be a non-negative integer.")

    if "hostedMode" in lane and lane["hostedMode"] is not None and not isinstance(lane["hostedMode"], str):
        errors.append(f"lane '{lane_id}' hostedMode must be a string or null.")

    if lane_id == "quality-gate" and lane.get("requiredCheckRole") != "aggregator":
        errors.append("quality-gate lane must have requiredCheckRole 'aggregator'.")
    if lane_id != "quality-gate" and lane.get("requiredCheckRole") == "aggregator":
        errors.append(f"lane '{lane_id}' must not claim the aggregator required-check role.")

    return errors


def validate_manifest(payload: dict[str, Any]) -> list[str]:
    errors: list[str] = []
    seen_ids: set[str] = set()
    for lane in payload["lanes"]:
        if not isinstance(lane, dict):
            errors.append("every lane entry must be an object.")
            continue
        errors.extend(validate_lane(lane, seen_ids))

    missing_required = sorted(REQUIRED_LANES - seen_ids)
    if missing_required:
        errors.append(f"lane manifest missing required lanes: {', '.join(missing_required)}.")

    unknown_required_role = [
        lane.get("id", "unknown")
        for lane in payload["lanes"]
        if isinstance(lane, dict)
        and lane.get("requiredCheckRole") == "aggregator"
        and lane.get("id") != "quality-gate"
    ]
    if unknown_required_role:
        errors.append(f"only quality-gate may be the required aggregator: {', '.join(unknown_required_role)}.")

    return errors


def main() -> int:
    args = parse_args()
    manifest_path = Path(args.manifest)
    if not manifest_path.is_absolute():
        manifest_path = REPO_ROOT / manifest_path

    try:
        payload = load_manifest(manifest_path)
        errors = validate_manifest(payload)
    except ValueError as exc:
        errors = [str(exc)]
        payload = None

    if errors:
        print("Lane manifest validation failed:", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1

    assert payload is not None
    if args.summary:
        print("Lane manifest validation passed.")
        print(f"Required check: {payload['requiredStatusCheck']['checkName']}")
        print("Lanes:")
        for lane in payload["lanes"]:
            print(f"- {lane['id']}: {lane['localCommand']}")
    else:
        print("Lane manifest validation passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
