#!/usr/bin/env python3
"""Emit Meridian architecture surface evidence for projects or source paths."""

from __future__ import annotations

import argparse
import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[4]
MODULE_MAP = REPO_ROOT / "docs" / "architecture" / "module-map.md"
PROJECT_STRUCTURE = REPO_ROOT / "docs" / "architecture" / "project-structure.md"

LAYER_HINTS = {
    "Meridian": "host",
    "Meridian.Application": "application",
    "Meridian.Contracts": "contracts",
    "Meridian.Core": "core",
    "Meridian.Domain": "domain",
    "Meridian.Infrastructure": "infrastructure",
    "Meridian.ProviderSdk": "provider-sdk",
    "Meridian.Storage": "storage",
    "Meridian.Execution": "execution",
    "Meridian.Risk": "risk",
    "Meridian.Strategies": "strategy",
    "Meridian.Backtesting": "backtesting",
    "Meridian.Ledger": "ledger",
    "Meridian.Reporting": "reporting",
    "Meridian.Identity": "identity",
    "Meridian.Ui.Services": "ui-shared",
    "Meridian.Ui.Shared": "ui-shared",
    "Meridian.Wpf": "ui-surface",
}

LOW_LEVEL_PREFIXES = (
    "Meridian.Contracts",
    "Meridian.Core",
    "Meridian.Domain",
    "Meridian.ProviderSdk",
    "Meridian.Storage",
)

UI_REFERENCE_MARKERS = ("Meridian.Ui", "Meridian.Wpf")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--path", action="append", help="Source path or project path to inspect. Repeatable.")
    parser.add_argument("--summary", action="store_true", help="Emit a compact human-readable summary.")
    parser.add_argument("--json", action="store_true", help="Emit machine-readable JSON.")
    parser.add_argument("--fail-on-violation", action="store_true", help="Return nonzero if boundary violations are found.")
    return parser.parse_args()


def repo_relative(path: Path) -> str:
    try:
        return path.resolve().relative_to(REPO_ROOT.resolve()).as_posix()
    except ValueError:
        return path.as_posix()


def resolve_path(value: str) -> Path:
    path = Path(value)
    if not path.is_absolute():
        path = REPO_ROOT / path
    return path.resolve()


def project_name(project: Path) -> str:
    return project.stem


def layer_for(name: str) -> str:
    for prefix, layer in sorted(LAYER_HINTS.items(), key=lambda item: len(item[0]), reverse=True):
        if name == prefix or name.startswith(f"{prefix}."):
            return layer
    return "unknown"


def find_project_for_path(path: Path) -> Path | None:
    if path.is_file() and path.suffix == ".csproj":
        return path
    if path.is_dir():
        projects = sorted(path.glob("*.csproj"))
        if projects:
            return projects[0]

    current = path if path.is_dir() else path.parent
    while current != current.parent:
        projects = sorted(current.glob("*.csproj"))
        if projects:
            return projects[0]
        if current == REPO_ROOT:
            break
        current = current.parent
    return None


def all_projects() -> list[Path]:
    return sorted((REPO_ROOT / "src").glob("**/*.csproj"))


def find_nearest_readme(path: Path) -> Path | None:
    current = path if path.is_dir() else path.parent
    while current != current.parent:
        candidate = current / "README.md"
        if candidate.exists():
            return candidate
        if current == REPO_ROOT:
            break
        current = current.parent
    return None


def parse_project_references(project: Path) -> list[str]:
    try:
        root = ET.parse(project).getroot()
    except ET.ParseError:
        return []

    references: list[str] = []
    for element in root.iter():
        if not element.tag.endswith("ProjectReference"):
            continue
        include = element.attrib.get("Include")
        if not include:
            continue
        ref_path = (project.parent / include).resolve()
        references.append(repo_relative(ref_path))
    return sorted(references)


def boundary_violations(project: Path, refs: list[str]) -> list[str]:
    name = project_name(project)
    violations: list[str] = []
    for ref in refs:
        ref_name = Path(ref).stem
        if name.startswith(LOW_LEVEL_PREFIXES) and any(marker in ref_name for marker in UI_REFERENCE_MARKERS):
            violations.append(f"{name} must not reference UI project {ref_name}.")
        if name.startswith("Meridian.Contracts") and ref_name not in {"Meridian.Core"}:
            violations.append(f"{name} should keep dependencies minimal; review reference to {ref_name}.")
    return violations


def inspect_target(path: Path) -> dict[str, object]:
    project = find_project_for_path(path)
    readme = find_nearest_readme(path)
    if project is None:
        return {
            "target": repo_relative(path),
            "project": None,
            "layer": "unknown",
            "source_readme": repo_relative(readme) if readme else None,
            "project_references": [],
            "boundary_violations": ["No containing .csproj was found for this target."],
        }

    refs = parse_project_references(project)
    return {
        "target": repo_relative(path),
        "project": repo_relative(project),
        "project_name": project_name(project),
        "layer": layer_for(project_name(project)),
        "source_readme": repo_relative(readme) if readme else None,
        "project_references": refs,
        "boundary_violations": boundary_violations(project, refs),
    }


def build_payload(paths: list[Path]) -> dict[str, object]:
    results = [inspect_target(path) for path in paths]
    violation_count = sum(len(result["boundary_violations"]) for result in results)
    doc_paths = [path for path in (MODULE_MAP, PROJECT_STRUCTURE) if path.exists()]
    return {
        "status": "fail" if violation_count else "pass",
        "summary": {
            "target_count": len(paths),
            "project_count": sum(1 for result in results if result.get("project")),
            "doc_count": len(doc_paths),
            "violation_count": violation_count,
            "module_map_present": MODULE_MAP.exists(),
            "project_structure_present": PROJECT_STRUCTURE.exists(),
        },
        "docs": [repo_relative(path) for path in doc_paths],
        "results": results,
    }


def print_summary(payload: dict[str, object]) -> None:
    summary = payload["summary"]
    assert isinstance(summary, dict)
    print(
        "architecture_surface "
        f"status={payload['status']} "
        f"targets={summary['target_count']} "
        f"projects={summary['project_count']} "
        f"violations={summary['violation_count']}"
    )
    for result in payload["results"]:
        assert isinstance(result, dict)
        print(
            f"- {result['target']}: project={result.get('project_name') or 'none'} "
            f"layer={result['layer']} readme={result.get('source_readme') or 'none'}"
        )
        for violation in result["boundary_violations"]:
            print(f"  violation: {violation}")


def main() -> int:
    args = parse_args()
    paths = [resolve_path(value) for value in args.path] if args.path else all_projects()
    payload = build_payload(paths)

    if args.json:
        print(json.dumps(payload, indent=2))
    else:
        print_summary(payload)
        if args.summary:
            print("docs=" + ", ".join(payload["docs"]))

    if args.fail_on_violation and payload["status"] != "pass":
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
