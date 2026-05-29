#!/usr/bin/env python3
"""Validate and summarize the desktop workstation screen blueprint checklist."""

from __future__ import annotations

import argparse
import json
import re
import sys
from collections import Counter
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_CHECKLIST_PATH = REPO_ROOT / "docs" / "plans" / "desktop-workstation-screen-blueprint.checklist.json"
DEFAULT_WORKFLOWS_PATH = REPO_ROOT / "scripts" / "dev" / "desktop-workflows.json"
VALID_STATUSES = {"planned", "partial", "implemented", "blocked", "deferred"}
VALID_ROOT_WORKSPACES = {"Trading", "Portfolio", "Accounting", "Reporting", "Strategy", "Data", "Settings"}
VALID_MATURITY_LABELS = {
    "Ready",
    "Active but partial",
    "Fixture/demo-backed",
    "UI shell only",
    "TBI",
    "Needs verification",
}
ID_PATTERN = re.compile(r"^desktop-screen-[a-z0-9]+(?:-[a-z0-9]+)*$")


def _load_json(path: Path) -> dict[str, Any]:
    if not path.exists():
        raise FileNotFoundError(f"Required file was not found: {path}")
    return json.loads(path.read_text(encoding="utf-8"))


def _normalize_path(value: str) -> str:
    return value.replace("\\", "/").strip()


def _is_allowed_maturity_evidence_path(path_text: str) -> bool:
    if path_text.startswith("src/Meridian.Wpf/"):
        return any(segment in path_text for segment in ("/ViewModels/", "/Services/", "/Shell/ViewModels/"))
    return path_text.startswith(
        (
            "src/Meridian.Ui.Services/",
            "src/Meridian.Ui.Shared/",
            "tests/",
            "docs/status/",
            "artifacts/",
            "docs/screenshots/desktop/",
        )
    )


def _workflow_step_index(workflows: dict[str, Any]) -> tuple[dict[tuple[str, str, str], dict[str, Any]], set[str]]:
    index: dict[tuple[str, str, str], dict[str, Any]] = {}
    referenced_blueprint_ids: set[str] = set()

    for workflow in workflows.get("workflows", []):
        workflow_name = str(workflow.get("name", "")).strip()
        for step in workflow.get("steps", []):
            page_tag = str(step.get("pageTag", "")).strip()
            capture_name = str(step.get("captureName", "")).strip()
            if workflow_name and page_tag and capture_name:
                index[(workflow_name, page_tag, capture_name)] = step
            for blueprint_id in step.get("blueprintChecklistIds", []):
                referenced_blueprint_ids.add(str(blueprint_id).strip())

    return index, referenced_blueprint_ids


def validate_checklist(checklist_path: Path, workflows_path: Path) -> tuple[list[str], dict[str, Any]]:
    checklist = _load_json(checklist_path)
    workflows = _load_json(workflows_path)
    errors: list[str] = []

    blueprint_path_value = str(checklist.get("blueprintPath", "")).strip()
    if not blueprint_path_value:
        errors.append("Checklist is missing blueprintPath.")
        blueprint_text = ""
    else:
        blueprint_path = REPO_ROOT / _normalize_path(blueprint_path_value)
        if not blueprint_path.exists():
            errors.append(f"blueprintPath does not exist: {blueprint_path_value}")
            blueprint_text = ""
        else:
            blueprint_text = blueprint_path.read_text(encoding="utf-8")

    screens = checklist.get("screens", [])
    if not isinstance(screens, list) or not screens:
        errors.append("Checklist must contain a non-empty screens array.")
        screens = []

    allowed_maturity_labels = checklist.get("allowedMaturityLabels", [])
    if set(allowed_maturity_labels) != VALID_MATURITY_LABELS:
        errors.append(
            "Checklist allowedMaturityLabels must match the controlled maturity vocabulary: "
            f"{', '.join(sorted(VALID_MATURITY_LABELS))}."
        )

    workflow_index, workflow_blueprint_ids = _workflow_step_index(workflows)
    ids: set[str] = set()
    titles: set[str] = set()
    reciprocal_workflow_ids: set[str] = set()

    for index, screen in enumerate(screens, start=1):
        prefix = f"screens[{index}]"
        screen_id = str(screen.get("id", "")).strip()
        title = str(screen.get("title", "")).strip()
        status = str(screen.get("status", "")).strip()
        maturity = str(screen.get("maturity", "")).strip()
        root_workspace = str(screen.get("rootWorkspace", "")).strip()
        heading = str(screen.get("blueprintHeading", "")).strip()

        if not screen_id or not ID_PATTERN.match(screen_id):
            errors.append(f"{prefix} has invalid id: {screen_id!r}")
        elif screen_id in ids:
            errors.append(f"Duplicate screen id: {screen_id}")
        else:
            ids.add(screen_id)

        if not title:
            errors.append(f"{prefix} is missing title.")
        elif title in titles:
            errors.append(f"Duplicate screen title: {title}")
        else:
            titles.add(title)

        if status not in VALID_STATUSES:
            errors.append(f"{screen_id or prefix} has invalid status '{status}'.")

        if maturity not in VALID_MATURITY_LABELS:
            errors.append(f"{screen_id or prefix} has invalid maturity '{maturity}'.")

        if root_workspace not in VALID_ROOT_WORKSPACES:
            errors.append(f"{screen_id or prefix} has invalid rootWorkspace '{root_workspace}'.")

        if not heading:
            errors.append(f"{screen_id or prefix} is missing blueprintHeading.")
        elif blueprint_text and heading not in blueprint_text:
            errors.append(f"{screen_id or prefix} blueprintHeading was not found in the blueprint: {heading}")

        validation_commands = screen.get("validationCommands", [])
        if not isinstance(validation_commands, list) or not validation_commands:
            errors.append(f"{screen_id or prefix} must list at least one validation command.")
        else:
            for command in validation_commands:
                if not str(command).strip():
                    errors.append(f"{screen_id or prefix} contains an empty validation command.")

        for path_value in screen.get("implementationPaths", []):
            path_text = _normalize_path(str(path_value))
            if not path_text:
                errors.append(f"{screen_id or prefix} contains an empty implementation path.")
                continue
            if not (REPO_ROOT / path_text).exists():
                errors.append(f"{screen_id or prefix} implementation path does not exist: {path_text}")

        maturity_evidence = screen.get("maturityEvidence", [])
        if not isinstance(maturity_evidence, list) or not maturity_evidence:
            errors.append(f"{screen_id or prefix} must list at least one maturityEvidence item.")
            maturity_evidence = []

        for evidence_index, evidence in enumerate(maturity_evidence, start=1):
            evidence_prefix = f"{screen_id or prefix} maturityEvidence[{evidence_index}]"
            evidence_path = _normalize_path(str(evidence.get("path", "")))
            evidence_kind = str(evidence.get("kind", "")).strip()
            evidence_summary = str(evidence.get("summary", "")).strip()

            if not evidence_kind:
                errors.append(f"{evidence_prefix} is missing kind.")
            if not evidence_summary:
                errors.append(f"{evidence_prefix} is missing summary.")
            if not evidence_path:
                errors.append(f"{evidence_prefix} is missing path.")
                continue
            if not _is_allowed_maturity_evidence_path(evidence_path):
                errors.append(
                    f"{evidence_prefix} path is not an allowed maturity evidence source: {evidence_path}"
                )
            if not (REPO_ROOT / evidence_path).exists():
                errors.append(f"{evidence_prefix} path does not exist: {evidence_path}")

        workflow_coverage = screen.get("workflowCoverage", [])
        if not isinstance(workflow_coverage, list):
            errors.append(f"{screen_id or prefix} workflowCoverage must be an array.")
            workflow_coverage = []

        for coverage in workflow_coverage:
            workflow_name = str(coverage.get("workflow", "")).strip()
            page_tag = str(coverage.get("pageTag", "")).strip()
            capture_name = str(coverage.get("captureName", "")).strip()
            key = (workflow_name, page_tag, capture_name)
            step = workflow_index.get(key)
            if step is None:
                errors.append(
                    f"{screen_id or prefix} references missing workflow coverage "
                    f"{workflow_name}/{page_tag}/{capture_name}."
                )
                continue
            reciprocal_workflow_ids.add(screen_id)
            step_ids = {str(value).strip() for value in step.get("blueprintChecklistIds", [])}
            if step_ids and screen_id not in step_ids:
                errors.append(
                    f"{screen_id or prefix} references {workflow_name}/{capture_name}, "
                    "but that workflow step does not include the checklist id."
                )

    unknown_workflow_ids = sorted(workflow_blueprint_ids - ids)
    for unknown_id in unknown_workflow_ids:
        errors.append(f"desktop-workflows.json references unknown blueprintChecklistId: {unknown_id}")

    missing_reciprocal_ids = sorted((workflow_blueprint_ids & ids) - reciprocal_workflow_ids)
    for missing_id in missing_reciprocal_ids:
        errors.append(f"Checklist is missing workflowCoverage for workflow-referenced id: {missing_id}")

    status_counts = Counter(str(screen.get("status", "")).strip() for screen in screens)
    maturity_counts = Counter(str(screen.get("maturity", "")).strip() for screen in screens)
    workspace_counts = Counter(str(screen.get("rootWorkspace", "")).strip() for screen in screens)
    coverage_count = sum(1 for screen in screens if screen.get("workflowCoverage"))

    summary = {
        "checklist": _normalize_path(str(checklist_path.relative_to(REPO_ROOT))),
        "blueprint": _normalize_path(str(checklist.get("blueprintPath", ""))),
        "screenCount": len(screens),
        "workflowCoveredScreenCount": coverage_count,
        "statusCounts": dict(sorted(status_counts.items())),
        "maturityCounts": dict(sorted(maturity_counts.items())),
        "workspaceCounts": dict(sorted(workspace_counts.items())),
    }

    return errors, summary


def _print_summary(summary: dict[str, Any]) -> None:
    print(f"Desktop screen blueprint checklist: {summary['screenCount']} screens")
    print(f"Workflow-covered screens: {summary['workflowCoveredScreenCount']}")
    print("Status counts:")
    for status, count in summary["statusCounts"].items():
        print(f"  {status}: {count}")
    print("Maturity counts:")
    for maturity, count in summary["maturityCounts"].items():
        print(f"  {maturity}: {count}")
    print("Workspace counts:")
    for workspace, count in summary["workspaceCounts"].items():
        print(f"  {workspace}: {count}")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--checklist", type=Path, default=DEFAULT_CHECKLIST_PATH)
    parser.add_argument("--workflows", type=Path, default=DEFAULT_WORKFLOWS_PATH)
    parser.add_argument("--summary", action="store_true", help="Print a human-readable summary when validation passes.")
    parser.add_argument("--json", action="store_true", help="Print machine-readable summary JSON when validation passes.")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    checklist_path = args.checklist if args.checklist.is_absolute() else REPO_ROOT / args.checklist
    workflows_path = args.workflows if args.workflows.is_absolute() else REPO_ROOT / args.workflows

    try:
        errors, summary = validate_checklist(checklist_path, workflows_path)
    except Exception as exc:
        print(f"Desktop screen blueprint checklist validation failed: {exc}", file=sys.stderr)
        return 1

    if errors:
        print("Desktop screen blueprint checklist validation failed:", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1

    if args.json:
        print(json.dumps(summary, indent=2))
    elif args.summary:
        _print_summary(summary)
    else:
        print(
            "Desktop screen blueprint checklist validation passed "
            f"({summary['screenCount']} screens, {summary['workflowCoveredScreenCount']} workflow-covered)."
        )

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
