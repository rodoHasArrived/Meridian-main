#!/usr/bin/env python3
"""Validate full design-document adaptation against source and documentation evidence."""

from __future__ import annotations

import re
from pathlib import Path

from common import Finding, build_arg_parser, emit_findings, load_data, read_text, repo_path, repo_root

ADAPTATION_MAP = Path("docs/architecture/design-document-adaptation.yml")
CONFORMANCE_MAP = Path("docs/architecture/design-module-conformance.yml")
ROADMAP_REGISTRY = Path("docs/roadmap/data/roadmap-items.yml")
DECISION_REGISTRY = Path("docs/roadmap/data/decision-log.yml")
ADOPTION_DECISION = "DEC-TRADING-STRATEGY-DIRECTION-001"
DESIGN_VERSION = "1.6"
DIRECTION_SECTION = "22.7 Strategy and Order-Management Direction"

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

EXPECTED_WORKFLOW = [
    "Import",
    "Validate",
    "Reconcile",
    "Investigate",
    "Approve",
    "Report",
]

EXPECTED_ROOT_WORKSPACES = [
    "Trading",
    "Portfolio",
    "Accounting",
    "Reporting",
    "Strategy",
    "Data",
    "Settings",
]

EXPECTED_COMPATIBILITY_GROUPINGS = [
    "Research",
    "Data Operations",
    "Governance",
]

EXPECTED_MVP_CONTEXTS = {
    "Identity & Access",
    "Entity & Relationship",
    "Data Provider & Integration",
    "Reference Data",
    "Instrument, Contract & Obligation",
    "Portfolio Records",
    "Financial Operations",
    "Workflow & Task",
    "Audit & Compliance",
    "Reporting & Client Delivery",
    "Document & Knowledge",
}

EXPECTED_DEFERRED_CONTEXTS = {
    "Treasury & Payments",
    "Alternative Assets",
    "Financing & Capital Structure",
    "Planning & Forecasting",
    "Risk Management",
}

EXPECTED_PLANNED_CONTEXTS = {
    "Research & Analytics": {
        "roadmap_item": "W13-STRAT-001",
        "runtime_projects": [
            "src/Meridian.Strategies",
            "src/Meridian.Backtesting",
            "src/Meridian.Backtesting.Sdk",
            "src/Meridian.QuantScript",
        ],
    },
    "Trading & Execution": {
        "roadmap_item": "W13-OMS-001",
        "runtime_projects": [
            "src/Meridian.Execution",
            "src/Meridian.Execution.Sdk",
            "src/Meridian.Risk",
            "src/Meridian.Strategies",
            "src/Meridian.Backtesting",
        ],
    },
}

EXPECTED_SCREENS = [
    "Home Dashboard",
    "Provider Center",
    "Import Runs",
    "Data Quality",
    "Entity Directory",
    "Account Directory",
    "Instrument / Contract Registry",
    "Portfolio Records",
    "Reconciliation Workbench",
    "Exception Queue",
    "Workflow Tasks",
    "Audit Log",
    "Document Vault",
    "Reporting Packages",
    "Administration Settings",
]


def _list(value: object) -> list:
    return value if isinstance(value, list) else []


def _module_id(name: str) -> str:
    local_name = name.removeprefix("Meridian.")
    words = re.sub(r"(?<!^)(?=[A-Z])", "-", local_name).upper()
    return f"SRC-DESIGN-{words}"


def _validate_exact_list(findings: list[Finding], path: Path, root: Path, actual: object, expected: list[str], label: str) -> None:
    values = [str(item) for item in _list(actual)]
    if values != expected:
        findings.append(Finding("error", repo_path(path, root), f"{label} must be {expected}, found {values}"))


def _validate_design_source(findings: list[Finding], root: Path, data: dict, map_path: Path) -> str:
    source = data.get("design_source", {})
    design_path = root / str(source.get("path", ""))
    if not design_path.exists():
        findings.append(Finding("error", repo_path(map_path, root), f"design source does not exist: {source.get('path')}"))
        return ""

    text = read_text(design_path)
    if str(source.get("version", "")) != DESIGN_VERSION:
        findings.append(Finding("error", repo_path(map_path, root), f"design_source version must be {DESIGN_VERSION}"))
    if not re.search(rf"^# Meridian Design Document[^\n]*Version {re.escape(DESIGN_VERSION)}\s*$", text, re.MULTILINE):
        findings.append(Finding("error", repo_path(design_path, root), f"design document must declare Version {DESIGN_VERSION}"))
    if DIRECTION_SECTION not in _list(source.get("sections")):
        findings.append(Finding("error", repo_path(map_path, root), f"design_source must include {DIRECTION_SECTION}"))
    if not re.search(rf"^#{{1,6}} {re.escape(DIRECTION_SECTION)}\s*$", text, re.MULTILINE):
        findings.append(Finding("error", repo_path(design_path, root), f"design document is missing {DIRECTION_SECTION}"))
    for token in [
        *EXPECTED_MODULES,
        *EXPECTED_MVP_CONTEXTS,
        *EXPECTED_DEFERRED_CONTEXTS,
        *EXPECTED_PLANNED_CONTEXTS,
        *EXPECTED_ROOT_WORKSPACES,
        *EXPECTED_SCREENS,
    ]:
        if token not in text:
            findings.append(Finding("error", repo_path(design_path, root), f"design document does not mention required token: {token}"))

    for token in ["W1-W5", "operational record baseline", "modular monolith", "no mobile development lane"]:
        if token not in text:
            findings.append(Finding("error", repo_path(design_path, root), f"design document is missing adaptation anchor: {token}"))

    return text


def _validate_modules(findings: list[Finding], root: Path, data: dict, map_path: Path) -> None:
    module_requirements = data.get("module_requirements", {})
    _validate_exact_list(findings, map_path, root, module_requirements.get("required_facets"), EXPECTED_FACETS, "required module facets")

    modules = _list(data.get("design_modules"))
    seen_modules = {str(module.get("name")) for module in modules if isinstance(module, dict)}
    for name in EXPECTED_MODULES:
        if name not in seen_modules:
            findings.append(Finding("error", repo_path(map_path, root), f"missing design module adaptation entry {name}"))
            continue

        physical_project = root / "src" / name
        descriptor_path = physical_project / "DesignModule.cs"
        project_path = physical_project / f"{name}.csproj"
        if not project_path.exists():
            findings.append(Finding("error", repo_path(map_path, root), f"{name} project file is missing: {repo_path(project_path, root)}"))
        if not descriptor_path.exists():
            findings.append(Finding("error", repo_path(map_path, root), f"{name} descriptor is missing: {repo_path(descriptor_path, root)}"))
            continue

        descriptor = read_text(descriptor_path)
        if "public const string BoundedContext" not in descriptor:
            findings.append(Finding("error", repo_path(descriptor_path, root), f"{name} descriptor must expose BoundedContext"))
        if "RequiredFacets" not in descriptor:
            findings.append(Finding("error", repo_path(descriptor_path, root), f"{name} descriptor must expose RequiredFacets"))
        for facet in EXPECTED_FACETS:
            if facet not in descriptor:
                findings.append(Finding("error", repo_path(descriptor_path, root), f"{name} descriptor is missing required facet {facet}"))

    extra_modules = sorted(seen_modules - set(EXPECTED_MODULES))
    for name in extra_modules:
        findings.append(Finding("error", repo_path(map_path, root), f"unexpected design module adaptation entry {name}"))

    conformance_path = root / CONFORMANCE_MAP
    if not conformance_path.exists():
        findings.append(Finding("error", repo_path(map_path, root), "design-module conformance map is missing"))
    else:
        conformance_text = read_text(conformance_path)
        for name in EXPECTED_MODULES:
            if f"name: {name}" not in conformance_text:
                findings.append(Finding("error", repo_path(conformance_path, root), f"conformance map is missing {name}"))
            if f"physical_project: src/{name}" not in conformance_text:
                findings.append(Finding("error", repo_path(conformance_path, root), f"conformance map is missing physical project for {name}"))

    source_modules = read_text(root / "docs/source/data/source-modules.yml")
    source_coverage = read_text(root / "docs/source/data/source-readme-coverage.yml")
    for name in EXPECTED_MODULES:
        module_id = _module_id(name)
        if module_id not in source_modules:
            findings.append(Finding("error", "docs/source/data/source-modules.yml", f"source registry is missing {module_id}"))
        if module_id not in source_coverage:
            findings.append(Finding("error", "docs/source/data/source-readme-coverage.yml", f"source README coverage is missing {module_id}"))


def _validate_contexts(findings: list[Finding], root: Path, data: dict, map_path: Path) -> None:
    contexts = _list(data.get("bounded_contexts"))
    names = [str(context.get("name")) for context in contexts if isinstance(context, dict)]
    for name in sorted(set(names)):
        if names.count(name) > 1:
            findings.append(Finding("error", repo_path(map_path, root), f"duplicate bounded context adaptation entry {name}"))
    by_name = {str(context.get("name")): context for context in contexts if isinstance(context, dict)}
    expected_all = EXPECTED_MVP_CONTEXTS | EXPECTED_DEFERRED_CONTEXTS | set(EXPECTED_PLANNED_CONTEXTS)

    for name in sorted(expected_all):
        context = by_name.get(name)
        if context is None:
            findings.append(Finding("error", repo_path(map_path, root), f"missing bounded context adaptation entry {name}"))
            continue
        stage = str(context.get("stage", ""))
        if name in EXPECTED_MVP_CONTEXTS and stage not in {"mvp", "mvp-limited"}:
            findings.append(Finding("error", repo_path(map_path, root), f"{name} must be an MVP context, found stage {stage}"))
        if name in EXPECTED_DEFERRED_CONTEXTS and stage != "deferred":
            findings.append(Finding("error", repo_path(map_path, root), f"{name} must be deferred, found stage {stage}"))
        if name in EXPECTED_PLANNED_CONTEXTS:
            if stage != "planned" or context.get("scope_status") != "adopted":
                findings.append(Finding("error", repo_path(map_path, root), f"{name} must have adopted scope with planned delivery, found stage {stage}"))
            if context.get("deferral_reason"):
                findings.append(Finding("error", repo_path(map_path, root), f"{name} cannot retain a blanket deferral_reason after scope adoption"))
            if "design_module" in context:
                findings.append(Finding("error", repo_path(map_path, root), f"{name} must map existing runtime_projects rather than a design_module"))
        elif not str(context.get("design_module", "")).startswith("Meridian."):
            findings.append(Finding("error", repo_path(map_path, root), f"{name} is missing design_module"))

    extra = sorted(set(by_name) - expected_all)
    for name in extra:
        findings.append(Finding("error", repo_path(map_path, root), f"unexpected bounded context adaptation entry {name}"))

    _validate_planned_direction(findings, root, data, by_name, map_path)


def _validate_planned_direction(findings: list[Finding], root: Path, data: dict, contexts: dict, map_path: Path) -> None:
    evidence = data.get("source_evidence", {})
    for key, expected_path in [("roadmap_registry", ROADMAP_REGISTRY), ("direction_decisions", DECISION_REGISTRY)]:
        if not isinstance(evidence, dict) or evidence.get(key) != expected_path.as_posix():
            findings.append(Finding("error", repo_path(map_path, root), f"source_evidence {key} must reference {expected_path.as_posix()}"))

    registries = {}
    for path in [ROADMAP_REGISTRY, DECISION_REGISTRY]:
        if not (root / path).is_file():
            findings.append(Finding("error", repo_path(map_path, root), f"planned direction registry is missing: {path.as_posix()}"))
            continue
        registry = load_data(root / path)
        if not isinstance(registry, dict):
            findings.append(Finding("error", path.as_posix(), "planned direction registry must be a mapping"))
            continue
        registries[path] = registry

    items = {
        str(item.get("id")): item
        for item in _list(registries.get(ROADMAP_REGISTRY, {}).get("items"))
        if isinstance(item, dict)
    }
    decisions = {
        str(item.get("id")): item
        for item in _list(registries.get(DECISION_REGISTRY, {}).get("decisions"))
        if isinstance(item, dict)
    }
    decision = decisions.get(ADOPTION_DECISION, {})
    if decision.get("status") != "accepted":
        findings.append(Finding("error", DECISION_REGISTRY.as_posix(), f"{ADOPTION_DECISION} must record accepted scope adoption"))

    for name, expected in EXPECTED_PLANNED_CONTEXTS.items():
        context = contexts.get(name)
        if context is None:
            continue
        item_id = str(expected["roadmap_item"])
        _validate_exact_list(findings, map_path, root, context.get("roadmap_items"), [item_id], f"{name} planned roadmap references")
        if context.get("adoption_decision") != ADOPTION_DECISION:
            findings.append(Finding("error", repo_path(map_path, root), f"{name} must reference {ADOPTION_DECISION}"))
        if item_id not in _list(decision.get("related_roadmap_items")):
            findings.append(Finding("error", DECISION_REGISTRY.as_posix(), f"{ADOPTION_DECISION} must include {item_id}"))
        item = items.get(item_id, {})
        if item.get("status") != "planned" or item.get("evidence_posture") != "planned_evidence":
            findings.append(Finding("error", ROADMAP_REGISTRY.as_posix(), f"{item_id} must retain planned delivery and planned_evidence until the adaptation is reviewed"))

        projects = expected["runtime_projects"]
        _validate_exact_list(findings, map_path, root, context.get("runtime_projects"), projects, f"{name} runtime projects")
        for project in projects:
            project_path = Path(project)
            if not (root / project_path / f"{project_path.name}.csproj").is_file():
                findings.append(Finding("error", repo_path(map_path, root), f"{name} runtime project does not exist: {project}"))

    trading = contexts.get("Trading & Execution", {})
    if trading.get("ownership_roadmap_item") != "W11-EXEC-001" or "W11-EXEC-001" not in items:
        findings.append(Finding("error", repo_path(map_path, root), "Trading & Execution must retain the separate W11-EXEC-001 ownership reference"))


def _validate_product_baseline(findings: list[Finding], root: Path, data: dict, map_path: Path) -> None:
    baseline = data.get("product_baseline", {})
    if baseline.get("scope_role") != "bounded-financial-foundation" or baseline.get("development_ceiling") is not False:
        findings.append(Finding("error", repo_path(map_path, root), "financial baseline must be a bounded-financial-foundation with development_ceiling false"))
    _validate_exact_list(findings, map_path, root, baseline.get("canonical_workflow"), EXPECTED_WORKFLOW, "canonical workflow")
    _validate_exact_list(
        findings,
        map_path,
        root,
        baseline.get("visible_root_workspaces"),
        EXPECTED_ROOT_WORKSPACES,
        "visible root workspaces",
    )
    _validate_exact_list(
        findings,
        map_path,
        root,
        baseline.get("compatibility_groupings"),
        EXPECTED_COMPATIBILITY_GROUPINGS,
        "compatibility groupings",
    )
    if baseline.get("no_mobile_development_lane") is not True:
        findings.append(Finding("error", repo_path(map_path, root), "no_mobile_development_lane must be true"))


def _validate_screen_inventory(findings: list[Finding], root: Path, data: dict, map_path: Path) -> None:
    screens = _list(data.get("mvp_screen_inventory"))
    names = [str(screen.get("name")) for screen in screens if isinstance(screen, dict)]
    if names != EXPECTED_SCREENS:
        findings.append(Finding("error", repo_path(map_path, root), f"MVP screen inventory must be {EXPECTED_SCREENS}, found {names}"))

    valid_workspaces = set(EXPECTED_ROOT_WORKSPACES)
    valid_contexts = EXPECTED_MVP_CONTEXTS | {"Platform Runtime"}
    for screen in screens:
        if not isinstance(screen, dict):
            findings.append(Finding("error", repo_path(map_path, root), "screen inventory entry must be a mapping"))
            continue
        name = str(screen.get("name", ""))
        workspace = str(screen.get("workspace", ""))
        context = str(screen.get("bounded_context", ""))
        if workspace not in valid_workspaces:
            findings.append(Finding("error", repo_path(map_path, root), f"{name} has invalid workspace {workspace}"))
        if context not in valid_contexts:
            findings.append(Finding("error", repo_path(map_path, root), f"{name} has invalid bounded_context {context}"))
        evidence = _list(screen.get("evidence"))
        if not evidence:
            findings.append(Finding("error", repo_path(map_path, root), f"{name} must list evidence paths"))
            continue
        for evidence_path in evidence:
            path = root / str(evidence_path)
            if not path.exists():
                findings.append(Finding("error", repo_path(map_path, root), f"{name} evidence path does not exist: {evidence_path}"))


def _validate_source_evidence(findings: list[Finding], root: Path, data: dict, map_path: Path) -> None:
    evidence = data.get("source_evidence", {})
    if not isinstance(evidence, dict):
        findings.append(Finding("error", repo_path(map_path, root), "source_evidence must be a mapping"))
        return

    for key, value in evidence.items():
        path = root / str(value)
        if not path.exists():
            findings.append(Finding("error", repo_path(map_path, root), f"source_evidence path for {key} does not exist: {value}"))

    shared_catalog_path = root / str(evidence.get("shared_workspace_catalog", ""))
    dashboard_catalog_path = root / str(evidence.get("dashboard_workspace_catalog", ""))
    wpf_catalog_path = root / str(evidence.get("wpf_workspace_catalog", ""))
    for label in EXPECTED_ROOT_WORKSPACES:
        for path in [shared_catalog_path, dashboard_catalog_path, wpf_catalog_path]:
            if path.exists() and label not in read_text(path):
                findings.append(Finding("error", repo_path(path, root), f"workspace catalog is missing root workspace {label}"))

    dashboard_text = read_text(dashboard_catalog_path) if dashboard_catalog_path.exists() else ""
    dashboard_labels = re.findall(r'label:\s*"([^"]+)"', dashboard_text)
    if dashboard_labels and dashboard_labels != EXPECTED_ROOT_WORKSPACES:
        findings.append(Finding("error", repo_path(dashboard_catalog_path, root), f"dashboard root workspace order drifted: {dashboard_labels}"))


def validate(root: Path) -> list[Finding]:
    findings: list[Finding] = []
    map_path = root / ADAPTATION_MAP
    if not map_path.exists():
        return [Finding("error", repo_path(map_path, root), "design-document adaptation map is missing")]

    data = load_data(map_path)
    if not isinstance(data, dict):
        return [Finding("error", repo_path(map_path, root), "design-document adaptation map must be a mapping")]

    schema = data.get("schema", {})
    if schema.get("id") != "meridian.design-document-adaptation":
        findings.append(Finding("error", repo_path(map_path, root), "expected schema id meridian.design-document-adaptation"))
    if not str(schema.get("version", "")).startswith("1."):
        findings.append(Finding("error", repo_path(map_path, root), f"unsupported schema major version {schema.get('version')}"))
    if data.get("adaptation_status") != "active":
        findings.append(Finding("error", repo_path(map_path, root), "adaptation_status must be active"))

    _validate_design_source(findings, root, data, map_path)
    _validate_product_baseline(findings, root, data, map_path)
    _validate_modules(findings, root, data, map_path)
    _validate_contexts(findings, root, data, map_path)
    _validate_screen_inventory(findings, root, data, map_path)
    _validate_source_evidence(findings, root, data, map_path)
    return findings


def main() -> int:
    parser = build_arg_parser("Validate full design-document adaptation.")
    args = parser.parse_args()
    return emit_findings(validate(repo_root(args.root)), args.summary, "design-document adaptation")


if __name__ == "__main__":
    raise SystemExit(main())
