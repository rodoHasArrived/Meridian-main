#!/usr/bin/env python3
"""Validate the design-module conformance map against the product design document."""

from __future__ import annotations

from pathlib import Path

from common import Finding, build_arg_parser, emit_findings, load_data, read_text, repo_path, repo_root

EXPECTED_MODULES = [
    "Meridian.Platform",
    "Meridian.Identity",
    "Meridian.Entities",
    "Meridian.DataIntegration",
    "Meridian.ReferenceData",
    "Meridian.Instruments",
    "Meridian.PortfolioRecords",
    "Meridian.FinancialOperations",
    "Meridian.Workflow",
    "Meridian.Audit",
    "Meridian.Reporting",
    "Meridian.Documents",
]

EXPECTED_FACETS = [
    "Domain model",
    "Application services",
    "Contracts / APIs",
    "Infrastructure",
    "UI components",
    "Tests",
]

ALLOWED_CONFORMANCE = {"physical", "mapped", "partial", "planned"}


def validate(root: Path) -> list[Finding]:
    findings: list[Finding] = []
    map_path = root / "docs" / "architecture" / "design-module-conformance.yml"
    if not map_path.exists():
        return [Finding("error", repo_path(map_path, root), "design-module conformance map is missing")]

    data = load_data(map_path)
    schema = data.get("schema", {}) if isinstance(data, dict) else {}
    if schema.get("id") != "meridian.design-module-conformance":
        findings.append(Finding("error", repo_path(map_path, root), "expected schema id meridian.design-module-conformance"))
    if not str(schema.get("version", "")).startswith("1."):
        findings.append(Finding("error", repo_path(map_path, root), f"unsupported schema major version {schema.get('version')}"))

    design_source = data.get("design_source", {}) if isinstance(data, dict) else {}
    design_path = root / str(design_source.get("path", ""))
    if not design_path.exists():
        findings.append(Finding("error", repo_path(map_path, root), f"design source does not exist: {design_source.get('path')}"))
        design_text = ""
    else:
        design_text = read_text(design_path)

    modules = data.get("modules", []) if isinstance(data, dict) else []
    if not isinstance(modules, list):
        return [Finding("error", repo_path(map_path, root), "modules must be a list")]

    seen: set[str] = set()
    for module in modules:
        if not isinstance(module, dict):
            findings.append(Finding("error", repo_path(map_path, root), "module entry must be a mapping"))
            continue

        name = str(module.get("name", "")).strip()
        if not name:
            findings.append(Finding("error", repo_path(map_path, root), "module entry is missing name"))
            continue
        if name in seen:
            findings.append(Finding("error", repo_path(map_path, root), f"duplicate module {name}"))
        seen.add(name)

        conformance = str(module.get("conformance", "")).strip()
        if conformance not in ALLOWED_CONFORMANCE:
            findings.append(Finding("error", repo_path(map_path, root), f"{name} has invalid conformance '{conformance}'"))

        physical_project = str(module.get("physical_project", "")).strip()
        expected_physical_project = f"src/{name}"
        if not physical_project:
            findings.append(Finding("error", repo_path(map_path, root), f"{name} is missing physical_project"))
        elif physical_project != expected_physical_project:
            findings.append(
                Finding(
                    "error",
                    repo_path(map_path, root),
                    f"{name} physical_project must be {expected_physical_project}, found {physical_project}",
                )
            )
        else:
            physical_path = root / physical_project
            if not physical_path.exists():
                findings.append(Finding("error", repo_path(map_path, root), f"{name} physical project path does not exist: {physical_project}"))
            csproj_path = physical_path / f"{name}.csproj"
            if not csproj_path.exists():
                findings.append(Finding("error", repo_path(map_path, root), f"{name} project file is missing: {repo_path(csproj_path, root)}"))
            descriptor_path = physical_path / "DesignModule.cs"
            if not descriptor_path.exists():
                findings.append(Finding("error", repo_path(map_path, root), f"{name} DesignModule.cs is missing"))
            else:
                descriptor_text = read_text(descriptor_path)
                if "public const string BoundedContext" not in descriptor_text:
                    findings.append(Finding("error", repo_path(descriptor_path, root), f"{name} descriptor is missing BoundedContext"))
                if "RequiredFacets" not in descriptor_text:
                    findings.append(Finding("error", repo_path(descriptor_path, root), f"{name} descriptor is missing RequiredFacets"))
                for facet in EXPECTED_FACETS:
                    if facet not in descriptor_text:
                        findings.append(Finding("error", repo_path(descriptor_path, root), f"{name} descriptor is missing required facet {facet}"))

        if not str(module.get("primary_current_owner", "")).strip():
            findings.append(Finding("error", repo_path(map_path, root), f"{name} is missing primary_current_owner"))

        if not str(module.get("migration_next", "")).strip():
            findings.append(Finding("error", repo_path(map_path, root), f"{name} is missing migration_next"))

        current_sources = module.get("current_sources", [])
        if not isinstance(current_sources, list) or not current_sources:
            findings.append(Finding("error", repo_path(map_path, root), f"{name} must list at least one current source path"))
            continue

        for source in current_sources:
            source_path = root / str(source)
            if not source_path.exists():
                findings.append(Finding("error", repo_path(map_path, root), f"{name} source path does not exist: {source}"))

    expected = set(EXPECTED_MODULES)
    missing = sorted(expected - seen)
    extra = sorted(seen - expected)
    for name in missing:
        findings.append(Finding("error", repo_path(map_path, root), f"missing design module {name}"))
    for name in extra:
        findings.append(Finding("error", repo_path(map_path, root), f"unexpected design module {name}"))

    for name in EXPECTED_MODULES:
        if design_text and name not in design_text:
            findings.append(Finding("error", repo_path(design_path, root), f"design document does not mention {name}"))

    return findings


def main() -> int:
    parser = build_arg_parser("Validate design-module conformance.")
    args = parser.parse_args()
    return emit_findings(validate(repo_root(args.root)), args.summary, "design-module conformance")


if __name__ == "__main__":
    raise SystemExit(main())
