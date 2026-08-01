#!/usr/bin/env python3
"""Validate Meridian roadmap registry data."""

from __future__ import annotations

from pathlib import Path

from common import (
    Finding,
    build_arg_parser,
    emit_findings,
    is_usable_sequence,
    load_data,
    repo_path,
    repo_root,
)

STATUSES = {"planned", "in_progress", "ready_for_acceptance", "accepted", "done"}
HEALTH = {"green", "yellow", "red", "on_track", "watch", "at_risk", "blocked"}
REQUIRED_ITEM_FIELDS = {
    "id",
    "title",
    "wave",
    "status",
    "health",
    "priority",
    "owner_lane",
    "evidence_posture",
    "current_summary",
    "exit_criteria",
    "source_modules",
    "last_reviewed",
}


def schema_version(data: dict) -> str:
    schema = data.get("schema", {}) if isinstance(data, dict) else {}
    return f"{schema.get('id', 'unknown')}@{schema.get('version', 'unknown')}"


def validate_schema_header(path: Path, data: dict, expected_id: str) -> list[Finding]:
    findings: list[Finding] = []
    schema = data.get("schema")
    if not isinstance(schema, dict):
        return [Finding("error", repo_path(path), "missing schema header")]
    if schema.get("id") != expected_id:
        findings.append(Finding("error", repo_path(path), f"expected schema id {expected_id}, got {schema.get('id')}"))
    version = str(schema.get("version", ""))
    if not version.startswith("1."):
        findings.append(Finding("error", repo_path(path), f"unsupported schema major version {version}"))
    return findings


def collect_ids(entries: list[dict], key: str, path: Path) -> tuple[set[str], list[Finding]]:
    seen: set[str] = set()
    findings: list[Finding] = []
    for entry in entries:
        value = entry.get(key)
        if not value:
            findings.append(Finding("error", repo_path(path), f"entry missing {key}"))
            continue
        if value in seen:
            findings.append(Finding("error", repo_path(path), f"duplicate id {value}"))
        seen.add(str(value))
    return seen, findings


def validate(root: Path) -> list[Finding]:
    findings: list[Finding] = []
    data_dir = root / "docs" / "roadmap" / "data"
    source_modules_path = root / "docs" / "source" / "data" / "source-modules.yml"
    roadmap_path = data_dir / "roadmap-items.yml"
    stage_path = data_dir / "stage-gates.yml"

    required_files = {
        "program-state.yml": "meridian.program-state",
        "roadmap-items.yml": "meridian.roadmap-items",
        "stage-gates.yml": "meridian.stage-gates",
        "document-index.yml": "meridian.document-index",
        "decision-log.yml": "meridian.decision-log",
        "risk-register.yml": "meridian.risk-register",
    }
    loaded: dict[str, dict] = {}
    for name, expected_schema in required_files.items():
        path = data_dir / name
        if not path.exists():
            findings.append(Finding("error", repo_path(path), "required registry file is missing"))
            continue
        data = load_data(path)
        if not isinstance(data, dict):
            findings.append(Finding("error", repo_path(path), "registry file must contain a mapping"))
            continue
        findings.extend(validate_schema_header(path, data, expected_schema))
        loaded[name] = data

    source_ids: set[str] = set()
    if source_modules_path.exists():
        source_data = load_data(source_modules_path)
        modules = source_data.get("modules", []) if isinstance(source_data, dict) else []
        source_ids = {str(module.get("id")) for module in modules if isinstance(module, dict) and module.get("id")}

    stage_ids: set[str] = set()
    if stage_path.exists():
        stage_data = loaded.get("stage-gates.yml", {})
        stages = stage_data.get("stage_gates", [])
        stage_ids, duplicate_findings = collect_ids(stages, "id", stage_path)
        findings.extend(duplicate_findings)

    if roadmap_path.exists():
        roadmap_data = loaded.get("roadmap-items.yml", {})
        items = roadmap_data.get("items", [])
        _, duplicate_findings = collect_ids(items, "id", roadmap_path)
        findings.extend(duplicate_findings)
        for item in items:
            item_id = item.get("id", "<missing>")
            missing = sorted(REQUIRED_ITEM_FIELDS - set(item.keys()))
            for field in missing:
                findings.append(Finding("error", repo_path(roadmap_path), f"{item_id} missing required field {field}"))
            if item.get("status") not in STATUSES:
                findings.append(Finding("error", repo_path(roadmap_path), f"{item_id} has invalid status {item.get('status')}"))
            if item.get("health") not in HEALTH:
                findings.append(Finding("error", repo_path(roadmap_path), f"{item_id} has invalid health {item.get('health')}"))
            for stage in item.get("stage_gates", []) or []:
                if stage_ids and stage not in stage_ids:
                    findings.append(Finding("error", repo_path(roadmap_path), f"{item_id} references unknown stage gate {stage}"))
            for module_id in item.get("source_modules", []) or []:
                if source_ids and module_id not in source_ids:
                    findings.append(Finding("error", repo_path(roadmap_path), f"{item_id} references unknown source module {module_id}"))
            if item.get("status") in {"accepted", "done"} and not item.get("evidence"):
                findings.append(Finding("error", repo_path(roadmap_path), f"{item_id} is {item.get('status')} without evidence"))
            if not item.get("exit_criteria"):
                findings.append(Finding("error", repo_path(roadmap_path), f"{item_id} must declare exit criteria"))
            # The JSON Schema bounds `sequence` at integer >= 1, but nothing loads it, so an
            # out-of-range or wrong-typed value would otherwise reach the renderers. They fall back
            # to the identifier suffix rather than fail, which would silently order a generated view
            # against its adopted rank. Reject it here, where the documented CI lane runs.
            if "sequence" in item and not is_usable_sequence(item.get("sequence")):
                findings.append(Finding(
                    "error",
                    repo_path(roadmap_path),
                    f"{item_id} has invalid sequence {item.get('sequence')!r}; expected an integer >= 1",
                ))

    return findings


def main() -> int:
    parser = build_arg_parser("Validate Meridian roadmap registry data.")
    args = parser.parse_args()
    return emit_findings(validate(repo_root(args.root)), args.summary, "roadmap registry validation")


if __name__ == "__main__":
    raise SystemExit(main())
