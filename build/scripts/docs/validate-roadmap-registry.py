#!/usr/bin/env python3
"""Validate Meridian roadmap registry data."""

from __future__ import annotations

import datetime as dt
import importlib.util
import json
from pathlib import Path

from jsonschema import Draft202012Validator

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


def _schema_payload(value):
    """JSON Schema sees ISO strings for YAML's standard date scalars."""
    if isinstance(value, dict):
        return {key: _schema_payload(item) for key, item in value.items()}
    if isinstance(value, list):
        return [_schema_payload(item) for item in value]
    if isinstance(value, (dt.date, dt.datetime)):
        return value.isoformat()
    return value


def validate_v2_shape(path: Path, data: dict, schema_name: str) -> list[Finding]:
    schema_path = Path(__file__).resolve().parents[3] / "docs/roadmap/schemas" / schema_name
    schema = json.loads(schema_path.read_text(encoding="utf-8"))
    return [Finding("error", repo_path(path), f"{'.'.join(map(str, error.absolute_path)) or '$'}: "
                    + ("invalid sequence; " if list(error.absolute_path)[-1:] == ["sequence"] else "")
                    + error.message)
            for error in Draft202012Validator(schema).iter_errors(_schema_payload(data))]


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
    supported = ("1.", "2.") if expected_id in {"meridian.program-state", "meridian.roadmap-items"} else ("1.",)
    if not version.startswith(supported):
        findings.append(Finding("error", repo_path(path), f"unsupported schema major version {version}"))
    if version.startswith("2.") and expected_id in {"meridian.program-state", "meridian.roadmap-items"}:
        name = "program-state" if expected_id == "meridian.program-state" else "roadmap-items"
        findings.extend(validate_v2_shape(path, data, f"{name}.v2.schema.json"))
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

    # Do not traverse malformed v2 objects after the schema already identified their errors.
    # This keeps invalid field types fail-closed instead of raising during reference checks.
    if any(finding.severity == "error" for finding in findings):
        return findings

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
        if not isinstance(items, list) or any(not isinstance(item, dict) for item in items):
            return findings + [Finding("error", repo_path(roadmap_path), "items must be a list of mappings")]
        item_ids, duplicate_findings = collect_ids(items, "id", roadmap_path)
        findings.extend(duplicate_findings)
        decision_ids = {entry.get("id") for entry in loaded.get("decision-log.yml", {}).get("decisions", [])}
        is_v2 = str(roadmap_data.get("schema", {}).get("version", "")).startswith("2.")
        for item in items:
            item_id = item.get("id", "<missing>")
            required = REQUIRED_ITEM_FIELDS - {"current_summary"} if is_v2 else REQUIRED_ITEM_FIELDS
            missing = sorted(required - set(item.keys()))
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
            # Generated views sort on the wave parsed from the identifier but label with the
            # declared `wave`. A disagreement would place an item among one wave's work while
            # presenting it as another's, so the two must agree.
            declared_wave = str(item.get("wave", ""))
            identifier_wave = str(item.get("id", "")).split("-")[0]
            if declared_wave and identifier_wave and declared_wave != identifier_wave:
                findings.append(Finding(
                    "error",
                    repo_path(roadmap_path),
                    f"{item_id} declares wave {declared_wave} but its identifier belongs to {identifier_wave}",
                ))
            if "sequence" in item and not is_usable_sequence(item.get("sequence")):
                findings.append(Finding(
                    "error",
                    repo_path(roadmap_path),
                    f"{item_id} has invalid sequence {item.get('sequence')!r}; expected an integer >= 1",
                ))

            if is_v2:
                for decision in item.get("decision_ids", []) if isinstance(item.get("decision_ids"), list) else []:
                    if decision not in decision_ids:
                        findings.append(Finding("error", repo_path(roadmap_path), f"{item_id} references unknown decision {decision}"))
                findings.extend(validate_detail_link(root, roadmap_path, item.get("status_detail"), str(item_id)))

        program_data = loaded.get("program-state.yml", {})
        if str(program_data.get("schema", {}).get("version", "")).startswith("2."):
            program = program_data.get("program", {})
            scope = program.get("active_scope", {}) if isinstance(program, dict) else {}
            if isinstance(scope, dict):
                for field in ("roadmap_items", "work_queue"):
                    values = scope.get(field, [])
                    for item_id in values if isinstance(values, list) else []:
                        if item_id not in item_ids:
                            findings.append(Finding("error", repo_path(data_dir / "program-state.yml"), f"active_scope.{field} references unknown roadmap item {item_id}"))
                values = scope.get("decision_ids", [])
                for decision in values if isinstance(values, list) else []:
                    if decision not in decision_ids:
                        findings.append(Finding("error", repo_path(data_dir / "program-state.yml"), f"active_scope references unknown decision {decision}"))
                findings.extend(validate_detail_link(root, data_dir / "program-state.yml", scope.get("status_detail"), "active_scope"))

    readiness = data_dir / "production-readiness.yml"
    if readiness.exists():
        script = Path(__file__).with_name("render-production-readiness.py")
        spec = importlib.util.spec_from_file_location("production_readiness_registry", script)
        assert spec is not None and spec.loader is not None
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        findings.extend(Finding("error", repo_path(readiness), message) for message in module.validate_data(root))

    return findings


def validate_detail_link(root: Path, source: Path, value, item_id: str) -> list[Finding]:
    if not isinstance(value, str) or not value.strip():
        return []  # Shape validation reports the missing/invalid field.
    target = (root / value).resolve()
    if not target.is_relative_to(root.resolve()) or not target.is_file():
        return [Finding("error", repo_path(source), f"{item_id} status_detail must name an existing repository file: {value}")]
    return []


def main() -> int:
    parser = build_arg_parser("Validate Meridian roadmap registry data.")
    args = parser.parse_args()
    return emit_findings(validate(repo_root(args.root)), args.summary, "roadmap registry validation")


if __name__ == "__main__":
    raise SystemExit(main())
