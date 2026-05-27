#!/usr/bin/env python3
"""Build the Refresh UI Screenshots workflow plan from screenshot definitions."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any


VALID_SURFACE_MODES = {"all", "desktop", "web"}
VALID_WORKFLOW_MODES = {"all", "catalog", "manuals"}
DEFAULT_DESKTOP_OUTPUT_ROOT = "docs/screenshots/desktop"
DEFAULT_WEB_OUTPUT_ROOT = "docs/screenshots/web"


def _load_json(path: Path) -> dict[str, Any]:
    if not path.exists():
        raise FileNotFoundError(f"Required screenshot definition file was not found: {path}")
    return json.loads(path.read_text(encoding="utf-8"))


def _normalize_path(value: str) -> str:
    normalized = value.replace("\\", "/").strip()
    while "//" in normalized:
        normalized = normalized.replace("//", "/")
    return normalized.rstrip("/")


def _join_path(base: str, *parts: str) -> str:
    segments = [_normalize_path(base), *(_normalize_path(part).strip("/") for part in parts)]
    return "/".join(segment for segment in segments if segment)


def _bool_output(value: bool) -> str:
    return "true" if value else "false"


def _select_desktop_workflows(workflows: list[dict[str, Any]], mode: str) -> list[dict[str, Any]]:
    named_workflows: dict[str, dict[str, Any]] = {}
    for workflow in workflows:
        name = str(workflow.get("name", "")).strip()
        if not name:
            continue
        if name in named_workflows:
            raise ValueError(f"Duplicate desktop workflow name found in desktop-workflows.json: {name}")
        named_workflows[name] = workflow

    if mode == "catalog":
        selected = [named_workflows.get("screenshot-catalog")]
    elif mode == "manuals":
        selected = [workflow for workflow in workflows if bool(workflow.get("includeInManual"))]
    else:
        selected = [
            workflow
            for workflow in workflows
            if workflow.get("name") == "screenshot-catalog" or bool(workflow.get("includeInManual"))
        ]

    selected = [workflow for workflow in selected if workflow is not None]
    if not selected:
        raise ValueError(f"No desktop screenshot workflows selected for workflow mode '{mode}'.")
    return selected


def _default_desktop_output_root(desktop_config: dict[str, Any], override: str | None) -> str:
    if override:
        return _normalize_path(override)

    workflows = desktop_config.get("workflows", [])
    catalog = next((workflow for workflow in workflows if workflow.get("name") == "screenshot-catalog"), None)
    publish_directory = None
    if isinstance(catalog, dict):
        publish = catalog.get("publish", {})
        if isinstance(publish, dict):
            publish_directory = publish.get("directory")

    return _normalize_path(str(publish_directory or DEFAULT_DESKTOP_OUTPUT_ROOT))


def _default_web_output_root(web_config: dict[str, Any], override: str | None) -> str:
    if override:
        return _normalize_path(override)
    return _normalize_path(str(web_config.get("outputRoot") or DEFAULT_WEB_OUTPUT_ROOT))


def _desktop_workflow_plan(
    workflow: dict[str, Any],
    desktop_output_root: str,
) -> dict[str, Any]:
    workflow_name = str(workflow.get("name", "")).strip()
    if not workflow_name:
        raise ValueError("Desktop workflow entry is missing a name.")

    is_catalog = workflow_name == "screenshot-catalog"
    group = "catalog" if is_catalog else "manuals"
    output_root = desktop_output_root if is_catalog else _join_path(desktop_output_root, "manuals", workflow_name)

    expected_files: list[str] = []
    for step in workflow.get("steps", []):
        capture_name = str(step.get("captureName", "")).strip()
        if capture_name:
            expected_files.append(_join_path(output_root, f"{capture_name}.png"))

    return {
        "name": workflow_name,
        "group": group,
        "output": output_root,
        "expectedFiles": expected_files,
        "managedScope": {
            "path": output_root,
            "recurse": not is_catalog,
            "surface": "desktop",
            "workflow": workflow_name,
        },
    }


def build_plan(
    *,
    surface_mode: str,
    workflow_mode: str,
    desktop_workflows_path: Path,
    web_routes_path: Path,
    desktop_output_root: str | None = None,
    web_output_root: str | None = None,
) -> dict[str, Any]:
    if surface_mode not in VALID_SURFACE_MODES:
        raise ValueError(f"Unsupported surface mode '{surface_mode}'. Expected one of {sorted(VALID_SURFACE_MODES)}.")
    if workflow_mode not in VALID_WORKFLOW_MODES:
        raise ValueError(
            f"Unsupported desktop workflow mode '{workflow_mode}'. Expected one of {sorted(VALID_WORKFLOW_MODES)}."
        )

    include_desktop = surface_mode in {"all", "desktop"}
    include_web = surface_mode in {"all", "web"}

    desktop_config = _load_json(desktop_workflows_path)
    web_config = _load_json(web_routes_path)
    resolved_desktop_root = _default_desktop_output_root(desktop_config, desktop_output_root)
    resolved_web_root = _default_web_output_root(web_config, web_output_root)

    desktop_plans: list[dict[str, Any]] = []
    if include_desktop:
        workflows = desktop_config.get("workflows", [])
        if not isinstance(workflows, list):
            raise ValueError("desktop-workflows.json must contain a workflows array.")
        selected_workflows = _select_desktop_workflows(workflows, workflow_mode)
        desktop_plans = [_desktop_workflow_plan(workflow, resolved_desktop_root) for workflow in selected_workflows]

    web_capture_plans: list[dict[str, Any]] = []
    if include_web:
        captures = web_config.get("captures", [])
        if not isinstance(captures, list) or not captures:
            raise ValueError("web-screenshot-routes.json must contain at least one capture.")

        for capture in captures:
            capture_name = str(capture.get("name", "")).strip()
            if capture_name:
                web_capture_plans.append(
                    {
                        "name": capture_name,
                        "path": str(capture.get("path", "")).strip(),
                        "output": _join_path(resolved_web_root, f"{capture_name}.png"),
                    }
                )

        if not web_capture_plans:
            raise ValueError("web-screenshot-routes.json did not contain any named captures.")

    expected_files = [
        *[expected for workflow in desktop_plans for expected in workflow["expectedFiles"]],
        *[capture["output"] for capture in web_capture_plans],
    ]

    managed_scopes = [workflow["managedScope"] for workflow in desktop_plans]
    if include_web:
        managed_scopes.append(
            {
                "path": resolved_web_root,
                "recurse": False,
                "surface": "web",
                "workflow": "web-dashboard",
            }
        )

    desktop_matrix_items = [
        {
            "name": workflow["name"],
            "group": workflow["group"],
            "output": workflow["output"],
        }
        for workflow in desktop_plans
    ]

    return {
        "surfaceMode": surface_mode,
        "workflowMode": workflow_mode,
        "includeDesktop": include_desktop,
        "includeWeb": include_web,
        "desktopOutputRoot": resolved_desktop_root,
        "webOutputRoot": resolved_web_root,
        "desktopWorkflowCount": len(desktop_plans),
        "webCaptureCount": len(web_capture_plans),
        "expectedCount": len(expected_files),
        "desktopMatrix": {"workflow": desktop_matrix_items},
        "desktopWorkflows": desktop_plans,
        "webCaptures": web_capture_plans,
        "expectedFiles": expected_files,
        "managedScopes": managed_scopes,
    }


def _write_github_outputs(path: Path, plan: dict[str, Any]) -> None:
    outputs = {
        "surface_mode": plan["surfaceMode"],
        "workflow_mode": plan["workflowMode"],
        "include_desktop": _bool_output(bool(plan["includeDesktop"])),
        "include_web": _bool_output(bool(plan["includeWeb"])),
        "desktop_output_root": plan["desktopOutputRoot"],
        "web_output_root": plan["webOutputRoot"],
        "desktop_workflow_count": str(plan["desktopWorkflowCount"]),
        "web_capture_count": str(plan["webCaptureCount"]),
        "expected_count": str(plan["expectedCount"]),
        "desktop_matrix": json.dumps(plan["desktopMatrix"], separators=(",", ":")),
    }
    with path.open("a", encoding="utf-8") as handle:
        for key, value in outputs.items():
            handle.write(f"{key}={value}\n")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--surfaces", default="all", choices=sorted(VALID_SURFACE_MODES))
    parser.add_argument("--workflows", default="all", choices=sorted(VALID_WORKFLOW_MODES))
    parser.add_argument("--desktop-output-root", default=None)
    parser.add_argument("--web-output-root", default=None)
    parser.add_argument("--desktop-workflows", type=Path, default=Path("scripts/dev/desktop-workflows.json"))
    parser.add_argument("--web-routes", type=Path, default=Path("scripts/dev/web-screenshot-routes.json"))
    parser.add_argument("--output-json", type=Path, default=None)
    parser.add_argument("--github-output", type=Path, default=None)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    plan = build_plan(
        surface_mode=args.surfaces,
        workflow_mode=args.workflows,
        desktop_workflows_path=args.desktop_workflows,
        web_routes_path=args.web_routes,
        desktop_output_root=args.desktop_output_root,
        web_output_root=args.web_output_root,
    )

    if args.output_json:
        args.output_json.parent.mkdir(parents=True, exist_ok=True)
        args.output_json.write_text(json.dumps(plan, indent=2) + "\n", encoding="utf-8")
    else:
        print(json.dumps(plan, indent=2))

    if args.github_output:
        _write_github_outputs(args.github_output, plan)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
