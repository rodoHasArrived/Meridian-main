#!/usr/bin/env python3
"""Validate simulated-user-panel review manifests without third-party dependencies."""

from __future__ import annotations

import argparse
import json
from datetime import datetime
from pathlib import Path
from typing import Any

SKILL_DIR = Path(__file__).resolve().parents[1]
SCHEMA_PATH = SKILL_DIR / "assets" / "review-manifest.schema.json"
REPO_ROOT = SKILL_DIR.parents[2]
FUNCTIONAL_EVIDENCE = {"workflow-manifest", "smoke-result", "test-result"}

SAMPLES: dict[str, dict[str, Any]] = {
    "valid": {
        "schema_version": "2026-07-19",
        "mode": "design_partner",
        "artifact_type": "screen-review",
        "artifact_paths": ["fixture://screen-review/welcome.png"],
        "artifact_evidence": [{"path": "fixture://screen-review/welcome.png", "kind": "screenshot", "status": "supplied"}],
        "artifact_freshness": "unknown",
        "persona_set": "core-finance",
        "focus_areas": ["first_impression"],
        "constraints": ["Use only supplied evidence."],
        "success_criteria": ["The first action is understandable."],
    },
    "invalid-empty": {
        "schema_version": "2026-07-19",
        "mode": "design_partner",
        "artifact_type": "screen-review",
        "artifact_paths": [],
        "artifact_evidence": [],
        "artifact_freshness": "unknown",
        "persona_set": {"panel": "custom", "required_roles": []},
        "focus_areas": [],
        "constraints": [],
        "success_criteria": [],
    },
    "invalid-evidence": {
        "schema_version": "2026-07-19",
        "mode": "release_gate",
        "artifact_type": "ship-readiness",
        "artifact_paths": ["fixture://ship/workflow.json", "fixture://ship/smoke.trx"],
        "artifact_evidence": [
            {
                "path": "fixture://ship/workflow.json",
                "kind": "source",
                "status": "verified",
                "captured_at": "yesterday",
                "unexpected": True,
            },
            {
                "path": "fixture://ship/workflow.json",
                "kind": "source",
                "status": "verified",
            },
            {
                "path": "fixture://ship/not-listed.png",
                "kind": "screenshot",
                "status": "supplied",
            },
        ],
        "artifact_freshness": "stale",
        "artifact_summary": "",
        "persona_set": {"panel": "operations-controls"},
        "focus_areas": ["release_readiness"],
        "constraints": ["Use only supplied evidence."],
        "success_criteria": ["The workflow is proven safe to ship."],
        "notes": ["", ""],
    },
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--file", type=Path)
    source.add_argument("--sample", choices=sorted(SAMPLES))
    parser.add_argument("--require-existing-paths", action="store_true")
    parser.add_argument("--summary", action="store_true")
    parser.add_argument("--json", action="store_true")
    return parser.parse_args()


def nonempty_unique_strings(value: Any, name: str, failures: list[str]) -> list[str]:
    if not isinstance(value, list) or not value:
        failures.append(f"{name} must be a non-empty array")
        return []
    if any(not isinstance(item, str) or not item.strip() for item in value):
        failures.append(f"{name} must contain non-empty strings")
        return []
    if len(value) != len(set(value)):
        failures.append(f"{name} must contain unique values")
    return value


def optional_string_array(value: Any, name: str, failures: list[str]) -> list[str]:
    if not isinstance(value, list):
        failures.append(f"{name} must be an array")
        return []
    if any(not isinstance(item, str) or not item.strip() for item in value):
        failures.append(f"{name} must contain non-empty strings")
        return []
    if len(value) != len(set(value)):
        failures.append(f"{name} must contain unique values")
    return value


def is_rfc3339(value: Any) -> bool:
    if not isinstance(value, str) or not value.strip():
        return False
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        return False
    return parsed.tzinfo is not None


def validate(payload: Any, require_existing_paths: bool) -> dict[str, Any]:
    schema = json.loads(SCHEMA_PATH.read_text(encoding="utf-8"))
    failures: list[str] = []
    if not isinstance(payload, dict):
        return {"status": "fail", "failures": ["manifest must be a JSON object"], "summary": {"failure_count": 1}}

    for field in schema["required"]:
        if field not in payload:
            failures.append(f"missing required field {field}")
    extra = sorted(set(payload) - set(schema["properties"]))
    if extra:
        failures.append(f"unexpected fields: {', '.join(extra)}")
    if payload.get("schema_version") != schema["properties"]["schema_version"]["const"]:
        failures.append("schema_version must be 2026-07-19")

    modes = set(schema["properties"]["mode"]["enum"])
    mode = payload.get("mode")
    if mode not in modes:
        failures.append(f"mode must be one of {sorted(modes)}")
    artifact_types = set(schema["properties"]["artifact_type"]["enum"])
    artifact_type = payload.get("artifact_type")
    if artifact_type not in artifact_types:
        failures.append(f"artifact_type must be one of {sorted(artifact_types)}")

    paths = nonempty_unique_strings(payload.get("artifact_paths"), "artifact_paths", failures)
    if artifact_type == "cross-surface-review" and len(paths) < 2:
        failures.append("cross-surface-review requires at least two artifact paths")
    freshness = payload.get("artifact_freshness")
    if freshness not in {"current", "stale", "unknown"}:
        failures.append("artifact_freshness must be current, stale, or unknown")

    evidence = payload.get("artifact_evidence")
    functional_verified = False
    evidence_paths: list[str] = []
    if not isinstance(evidence, list) or not evidence:
        failures.append("artifact_evidence must be a non-empty array")
    else:
        allowed_kinds = set(schema["$defs"]["evidence_item"]["properties"]["kind"]["enum"])
        allowed_status = {"verified", "supplied", "missing"}
        for index, item in enumerate(evidence):
            prefix = f"artifact_evidence[{index}]"
            if not isinstance(item, dict):
                failures.append(f"{prefix} must be an object")
                continue
            required_evidence_fields = {"path", "kind", "status"}
            missing_fields = sorted(required_evidence_fields - set(item))
            if missing_fields:
                failures.append(f"{prefix} is missing fields: {', '.join(missing_fields)}")
            allowed_evidence_fields = required_evidence_fields | {"captured_at", "notes"}
            extra_fields = sorted(set(item) - allowed_evidence_fields)
            if extra_fields:
                failures.append(f"{prefix} has unexpected fields: {', '.join(extra_fields)}")
            path = item.get("path")
            kind = item.get("kind")
            status = item.get("status")
            if not isinstance(path, str) or not path.strip():
                failures.append(f"{prefix}.path must be non-empty")
            else:
                evidence_paths.append(path)
            if kind not in allowed_kinds:
                failures.append(f"{prefix}.kind is invalid")
            if status not in allowed_status:
                failures.append(f"{prefix}.status is invalid")
            if "captured_at" in item and not is_rfc3339(item["captured_at"]):
                failures.append(f"{prefix}.captured_at must be an RFC 3339 date-time")
            if "notes" in item and not isinstance(item["notes"], str):
                failures.append(f"{prefix}.notes must be a string")
            if kind in FUNCTIONAL_EVIDENCE and status == "verified":
                functional_verified = True

    if len(evidence_paths) != len(set(evidence_paths)):
        failures.append("artifact_evidence paths must be unique")
    evidence_path_set = set(evidence_paths)
    missing_evidence_rows = sorted(set(paths) - evidence_path_set)
    if missing_evidence_rows:
        failures.append(f"artifact paths lack evidence rows: {', '.join(missing_evidence_rows)}")
    unlisted_evidence_rows = sorted(evidence_path_set - set(paths))
    if unlisted_evidence_rows:
        failures.append(f"evidence rows reference unlisted artifacts: {', '.join(unlisted_evidence_rows)}")

    if require_existing_paths:
        for raw_path in paths:
            if raw_path.startswith("fixture://"):
                continue
            candidate = Path(raw_path)
            if not candidate.is_absolute():
                candidate = REPO_ROOT / candidate
            if not candidate.exists():
                failures.append(f"artifact path does not exist: {raw_path}")

    panel_names = set(schema["$defs"]["panel_name"]["enum"])
    persona_set = payload.get("persona_set")
    if isinstance(persona_set, str):
        if persona_set not in panel_names:
            failures.append("persona_set string is not a known panel")
    elif isinstance(persona_set, dict):
        panel = persona_set.get("panel")
        if panel not in panel_names | {"custom"}:
            failures.append("persona_set.panel is invalid")
        roles = persona_set.get("required_roles", [])
        if "required_roles" in persona_set:
            roles = optional_string_array(roles, "persona_set.required_roles", failures)
        if panel == "custom" and not roles:
            failures.append("custom panels require at least one required role")
        for name in ("optional_roles", "advisory_lenses"):
            if name in persona_set:
                optional_string_array(persona_set[name], f"persona_set.{name}", failures)
        allowed_panel_fields = {"panel", "required_roles", "optional_roles", "advisory_lenses"}
        panel_extra = sorted(set(persona_set) - allowed_panel_fields)
        if panel_extra:
            failures.append(f"persona_set has unexpected fields: {', '.join(panel_extra)}")
    else:
        failures.append("persona_set must be a panel name or object")

    focus = nonempty_unique_strings(payload.get("focus_areas"), "focus_areas", failures)
    allowed_focus = set(schema["properties"]["focus_areas"]["items"]["enum"])
    invalid_focus = sorted(set(focus) - allowed_focus)
    if invalid_focus:
        failures.append(f"invalid focus areas: {', '.join(invalid_focus)}")
    nonempty_unique_strings(payload.get("constraints"), "constraints", failures)
    nonempty_unique_strings(payload.get("success_criteria"), "success_criteria", failures)

    for name in ("artifact_summary", "decision_deadline"):
        if name in payload and (not isinstance(payload[name], str) or not payload[name].strip()):
            failures.append(f"{name} must be a non-empty string")
    if "notes" in payload:
        optional_string_array(payload["notes"], "notes", failures)

    if mode == "release_gate":
        if freshness != "current":
            failures.append("release_gate requires current artifact freshness")
        if not functional_verified:
            failures.append("release_gate requires verified functional evidence")

    return {
        "status": "pass" if not failures else "fail",
        "failures": failures,
        "summary": {
            "failure_count": len(failures),
            "artifact_count": len(paths),
            "evidence_count": len(evidence) if isinstance(evidence, list) else 0,
            "functional_evidence_verified": functional_verified,
        },
    }


def main() -> int:
    args = parse_args()
    payload = SAMPLES[args.sample] if args.sample else json.loads(args.file.read_text(encoding="utf-8"))
    result = validate(payload, args.require_existing_paths)
    if args.json:
        print(json.dumps(result, indent=2))
    else:
        print(f"validate_review_manifest status={result['status']} failures={result['summary']['failure_count']}")
        if args.summary:
            for failure in result["failures"]:
                print(f"- {failure}")
    return 0 if result["status"] == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main())
