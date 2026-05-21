#!/usr/bin/env python3
"""Validate repo-local Codex skill consistency."""

from __future__ import annotations

import argparse
import json
from dataclasses import asdict, dataclass
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[3]
CODEX_SKILLS = REPO_ROOT / ".codex" / "skills"
CODEX_SKILLS_README = CODEX_SKILLS / "README.md"
CODEX_DOCS_README = REPO_ROOT / "docs" / "ai" / "codex" / "README.md"
CI_WORKFLOW = REPO_ROOT / ".github" / "workflows" / "ci.yml"
SHARED_CONTEXT_MARKER = "../_shared/project-context.md"
EXECUTION_CONTRACT_MARKER = "../_shared/codex-execution-contract.md"


@dataclass(frozen=True)
class Finding:
    severity: str
    path: str
    message: str


def repo_relative(path: Path) -> str:
    try:
        return path.resolve().relative_to(REPO_ROOT.resolve()).as_posix()
    except ValueError:
        return path.as_posix()


def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def current_skill_dirs() -> list[Path]:
    return sorted(
        path
        for path in CODEX_SKILLS.iterdir()
        if path.is_dir() and not path.name.startswith("_")
    )


def add_missing_file_finding(findings: list[Finding], path: Path) -> bool:
    if path.exists():
        return False

    findings.append(
        Finding("error", repo_relative(path), "Required Codex skill surface is missing.")
    )
    return True


def validate_skill(skill_dir: Path, catalog_text: str, docs_text: str) -> list[Finding]:
    findings: list[Finding] = []
    skill_name = skill_dir.name
    skill_md = skill_dir / "SKILL.md"
    openai_yaml = skill_dir / "agents" / "openai.yaml"

    if add_missing_file_finding(findings, skill_md):
        return findings

    skill_text = read_text(skill_md)
    if f"name: {skill_name}" not in skill_text:
        findings.append(
            Finding("error", repo_relative(skill_md), f"Frontmatter name does not match {skill_name}.")
        )

    if SHARED_CONTEXT_MARKER not in skill_text:
        findings.append(
            Finding("error", repo_relative(skill_md), "Skill does not reference shared project context.")
        )

    if EXECUTION_CONTRACT_MARKER not in skill_text:
        findings.append(
            Finding("error", repo_relative(skill_md), "Skill does not reference the Codex execution contract.")
        )

    if add_missing_file_finding(findings, openai_yaml):
        return findings

    openai_text = read_text(openai_yaml)
    for marker in ("display_name:", "short_description:", "default_prompt:"):
        if marker not in openai_text:
            findings.append(
                Finding("error", repo_relative(openai_yaml), f"openai.yaml is missing {marker}.")
            )

    if skill_name not in catalog_text:
        findings.append(
            Finding("error", repo_relative(CODEX_SKILLS_README), f"Catalog does not list {skill_name}.")
        )

    if skill_name not in docs_text:
        findings.append(
            Finding("warning", repo_relative(CODEX_DOCS_README), f"Codex docs do not mention {skill_name}.")
        )

    return findings


def collect_findings() -> list[Finding]:
    findings: list[Finding] = []
    for required in (CODEX_SKILLS, CODEX_SKILLS_README, CODEX_DOCS_README, CI_WORKFLOW):
        add_missing_file_finding(findings, required)

    if findings:
        return findings

    catalog_text = read_text(CODEX_SKILLS_README)
    docs_text = read_text(CODEX_DOCS_README)
    ci_text = read_text(CI_WORKFLOW)

    if "Validate AI contract drift" not in ci_text:
        findings.append(
            Finding("error", repo_relative(CI_WORKFLOW), "CI workflow is missing Validate AI contract drift.")
        )

    if "codex-execution-contract.md" not in catalog_text:
        findings.append(
            Finding("error", repo_relative(CODEX_SKILLS_README), "Catalog does not link the Codex execution contract.")
        )

    for skill_dir in current_skill_dirs():
        findings.extend(validate_skill(skill_dir, catalog_text, docs_text))

    return findings


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--summary", action="store_true", help="Print a one-line summary.")
    parser.add_argument("--json-output", type=Path, help="Write findings as JSON.")
    args = parser.parse_args()

    findings = collect_findings()
    payload = {
        "status": "pass" if not any(f.severity == "error" for f in findings) else "fail",
        "finding_count": len(findings),
        "findings": [asdict(finding) for finding in findings],
    }

    if args.json_output:
        args.json_output.parent.mkdir(parents=True, exist_ok=True)
        args.json_output.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")

    if args.summary or not args.json_output:
        print(
            "Codex skills status: "
            f"{payload['status']}; {len(current_skill_dirs())} skill(s), "
            f"{payload['finding_count']} finding(s)."
        )
        for finding in findings:
            print(f"{finding.severity}: {finding.path}: {finding.message}")

    return 0 if payload["status"] == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main())
