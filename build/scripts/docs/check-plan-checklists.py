#!/usr/bin/env python3
"""Check that active plan documents include implementation checklists."""

from __future__ import annotations

from pathlib import Path

from common import Finding, build_arg_parser, emit_findings, repo_path, repo_root


def validate(root: Path) -> list[Finding]:
    findings: list[Finding] = []
    plan_dir = root / "docs" / "plans"
    if not plan_dir.exists():
        return findings
    for path in sorted(plan_dir.glob("*.md")):
        text = path.read_text(encoding="utf-8", errors="ignore")
        lowered = text.lower()
        if "archive" in path.parts or path.name.lower().startswith("readme"):
            continue
        has_checklist_heading = "todo checklist" in lowered or "implementation checklist" in lowered or "checklist" in lowered
        has_checkboxes = "- [ ]" in text or "- [x]" in lowered
        if not has_checklist_heading or not has_checkboxes:
            findings.append(Finding("warning", repo_path(path, root), "plan has no explicit checklist heading with checkbox items"))
    return findings


def main() -> int:
    parser = build_arg_parser("Check plan documents for implementation checklists.")
    args = parser.parse_args()
    findings = validate(repo_root(args.root))
    # Advisory by design: warnings document gaps without blocking current migration.
    emit_findings(findings, args.summary, "plan checklist advisory")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
