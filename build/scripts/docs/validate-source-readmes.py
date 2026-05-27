#!/usr/bin/env python3
"""Validate source module registry and README coverage."""

from __future__ import annotations

from pathlib import Path

from common import Finding, build_arg_parser, emit_findings, front_matter, load_data, read_text, repo_path, repo_root

REQUIRED_FRONT_MATTER = {
    "doc_type",
    "doc_schema",
    "doc_schema_version",
    "module_id",
    "path",
    "status",
    "owner_lane",
    "last_reviewed",
}
REQUIRED_SECTIONS = [
    "## Purpose",
    "## Layer responsibility",
    "## Key folders and files",
    "## Important workflows",
    "## Diagrams",
    "## Roadmap traceability",
    "## TODO checklist",
    "## Validation",
    "## Change rules",
    "## Related docs",
]


def validate(root: Path) -> list[Finding]:
    findings: list[Finding] = []
    data_dir = root / "docs" / "source" / "data"
    modules_path = data_dir / "source-modules.yml"
    coverage_path = data_dir / "source-readme-coverage.yml"

    if not modules_path.exists():
        return [Finding("error", repo_path(modules_path, root), "source module registry is missing")]

    modules_data = load_data(modules_path)
    coverage_data = load_data(coverage_path) if coverage_path.exists() else {}
    modules = modules_data.get("modules", []) if isinstance(modules_data, dict) else []
    coverage_entries = coverage_data.get("modules", []) if isinstance(coverage_data, dict) else []
    coverage_by_module = {entry.get("module_id"): entry for entry in coverage_entries if isinstance(entry, dict)}
    seen: set[str] = set()

    schema = modules_data.get("schema", {}) if isinstance(modules_data, dict) else {}
    if schema.get("id") != "meridian.source-modules":
        findings.append(Finding("error", repo_path(modules_path, root), "expected schema id meridian.source-modules"))
    if not str(schema.get("version", "")).startswith("1."):
        findings.append(Finding("error", repo_path(modules_path, root), f"unsupported schema major version {schema.get('version')}"))

    for module in modules:
        module_id = module.get("id")
        if not module_id:
            findings.append(Finding("error", repo_path(modules_path, root), "module is missing id"))
            continue
        if module_id in seen:
            findings.append(Finding("error", repo_path(modules_path, root), f"duplicate module id {module_id}"))
        seen.add(module_id)
        module_path = root / module.get("path", "")
        readme_path = root / module.get("readme", "")
        if not module_path.exists():
            findings.append(Finding("error", repo_path(modules_path, root), f"{module_id} path does not exist: {module.get('path')}"))
        if not readme_path.exists():
            findings.append(Finding("error", repo_path(readme_path, root), f"{module_id} README is missing"))
            continue
        if module_id not in coverage_by_module:
            findings.append(Finding("error", repo_path(coverage_path, root), f"{module_id} missing README coverage entry"))

        markdown = read_text(readme_path)
        fm = front_matter(markdown)
        missing = sorted(REQUIRED_FRONT_MATTER - set(fm.keys()))
        for key in missing:
            findings.append(Finding("error", repo_path(readme_path, root), f"missing front matter key {key}"))
        if fm.get("module_id") and fm.get("module_id") != module_id:
            findings.append(Finding("error", repo_path(readme_path, root), f"front matter module_id {fm.get('module_id')} does not match {module_id}"))
        if fm.get("path") and fm.get("path") != module.get("path"):
            findings.append(Finding("error", repo_path(readme_path, root), f"front matter path {fm.get('path')} does not match registry path {module.get('path')}"))
        for heading in REQUIRED_SECTIONS:
            if heading not in markdown:
                findings.append(Finding("error", repo_path(readme_path, root), f"missing required section {heading}"))
        trace_begin = f"<!-- source-roadmap-traceability:begin module={module_id} -->"
        trace_end = "<!-- source-roadmap-traceability:end -->"
        todo_begin = f"<!-- source-todos:begin module={module_id} -->"
        todo_end = "<!-- source-todos:end -->"
        for marker in [trace_begin, trace_end, todo_begin, todo_end]:
            if marker not in markdown:
                findings.append(Finding("error", repo_path(readme_path, root), f"missing generated block marker {marker}"))

    return findings


def main() -> int:
    parser = build_arg_parser("Validate source README coverage.")
    args = parser.parse_args()
    return emit_findings(validate(repo_root(args.root)), args.summary, "source README validation")


if __name__ == "__main__":
    raise SystemExit(main())
