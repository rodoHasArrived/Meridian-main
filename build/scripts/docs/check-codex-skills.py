#!/usr/bin/env python3
"""Validate repo-local Codex skill consistency."""

from __future__ import annotations

import argparse
import json
import re
from dataclasses import asdict, dataclass
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[3]
CODEX_SKILLS = REPO_ROOT / ".codex" / "skills"
CODEX_SKILLS_README = CODEX_SKILLS / "README.md"
CODEX_DOCS_README = REPO_ROOT / "docs" / "ai" / "codex" / "README.md"
CI_WORKFLOW = REPO_ROOT / ".github" / "workflows" / "ci.yml"
SHARED_CONTEXT_MARKER = "../_shared/project-context.md"
EXECUTION_CONTRACT_MARKER = "../_shared/codex-execution-contract.md"
REQUIRED_SECTIONS = (
    "## Use When",
    "## Do Not Use When",
    "## Workflow",
    "## Handoffs",
    "## Validation",
    "## Output Standards",
)
SKILL_NAME_RE = re.compile(r"^[a-z0-9](?:[a-z0-9]|-(?!-)){0,62}[a-z0-9]$")
ALLOWED_FRONTMATTER_FIELDS = {
    "name",
    "description",
    "license",
    "compatibility",
    "metadata",
    "allowed-tools",
}


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


def parse_skill_frontmatter(skill_md: Path, skill_text: str) -> tuple[dict[str, object], str, list[Finding]]:
    """Parse the Agent Skills frontmatter subset used by repo-local skills.

    The Agent Skills reference validator is intentionally not a repository dependency, so this
    parser implements the spec checks that matter for Meridian's checked-in Codex skills while
    supporting the YAML scalar forms currently used by the skill pack.
    """

    findings: list[Finding] = []
    if not skill_text.startswith("---\n"):
        findings.append(
            Finding("error", repo_relative(skill_md), "SKILL.md must start with YAML frontmatter.")
        )
        return {}, skill_text, findings

    try:
        frontmatter_text, body = skill_text[4:].split("\n---", 1)
    except ValueError:
        findings.append(
            Finding("error", repo_relative(skill_md), "SKILL.md frontmatter is missing a closing delimiter.")
        )
        return {}, "", findings

    if body.startswith("\n"):
        body = body[1:]

    fields: dict[str, object] = {}
    lines = frontmatter_text.splitlines()
    index = 0
    while index < len(lines):
        line = lines[index]
        index += 1
        if not line.strip():
            continue

        match = re.match(r"^([A-Za-z0-9_-]+):(?:\s*(.*))?$", line)
        if not match:
            findings.append(
                Finding("error", repo_relative(skill_md), f"Unsupported frontmatter line: {line!r}.")
            )
            continue

        key, raw_value = match.group(1), match.group(2) or ""
        if key == "metadata" and raw_value == "":
            metadata: dict[str, str] = {}
            while index < len(lines) and (lines[index].startswith("  ") or not lines[index].strip()):
                metadata_line = lines[index]
                index += 1
                if not metadata_line.strip():
                    continue
                metadata_match = re.match(r"^\s{2}([A-Za-z0-9_.-]+):\s*(.*)$", metadata_line)
                if metadata_match:
                    metadata[metadata_match.group(1)] = metadata_match.group(2).strip().strip('"\'')
                else:
                    findings.append(
                        Finding(
                            "error",
                            repo_relative(skill_md),
                            f"Metadata entries must be a simple key-value mapping: {metadata_line!r}.",
                        )
                    )
            fields[key] = metadata
            continue

        if raw_value in {">", "|"}:
            continuation: list[str] = []
            while index < len(lines) and (lines[index].startswith("  ") or not lines[index].strip()):
                continuation.append(lines[index][2:] if lines[index].startswith("  ") else "")
                index += 1
            fields[key] = " ".join(part.strip() for part in continuation if part.strip())
            continue

        fields[key] = raw_value.strip().strip('"\'')

    return fields, body, findings


def validate_agent_skills_spec(skill_dir: Path, skill_md: Path, skill_text: str) -> list[Finding]:
    findings: list[Finding] = []
    fields, body, parse_findings = parse_skill_frontmatter(skill_md, skill_text)
    findings.extend(parse_findings)
    if parse_findings:
        return findings

    unknown_fields = sorted(set(fields) - ALLOWED_FRONTMATTER_FIELDS)
    for field in unknown_fields:
        findings.append(
            Finding("error", repo_relative(skill_md), f"Frontmatter field {field!r} is not in the Agent Skills spec.")
        )

    name = fields.get("name")
    if not isinstance(name, str) or not name:
        findings.append(Finding("error", repo_relative(skill_md), "Frontmatter name is required and must be non-empty."))
    else:
        if len(name) > 64:
            findings.append(Finding("error", repo_relative(skill_md), "Frontmatter name exceeds 64 characters."))
        if not SKILL_NAME_RE.match(name):
            findings.append(
                Finding(
                    "error",
                    repo_relative(skill_md),
                    "Frontmatter name must use lowercase letters, numbers, and single hyphens; it cannot start or end with a hyphen.",
                )
            )
        if name != skill_dir.name:
            findings.append(
                Finding("error", repo_relative(skill_md), f"Frontmatter name must match parent directory {skill_dir.name}.")
            )

    description = fields.get("description")
    if not isinstance(description, str) or not description:
        findings.append(
            Finding("error", repo_relative(skill_md), "Frontmatter description is required and must be non-empty.")
        )
    elif len(description) > 1024:
        findings.append(
            Finding("error", repo_relative(skill_md), "Frontmatter description exceeds 1024 characters.")
        )

    compatibility = fields.get("compatibility")
    if compatibility is not None and (not isinstance(compatibility, str) or not compatibility or len(compatibility) > 500):
        findings.append(
            Finding(
                "error",
                repo_relative(skill_md),
                "Frontmatter compatibility must be a non-empty string no longer than 500 characters when present.",
            )
        )

    metadata = fields.get("metadata")
    if metadata is not None and not isinstance(metadata, dict):
        findings.append(Finding("error", repo_relative(skill_md), "Frontmatter metadata must be a key-value mapping."))

    allowed_tools = fields.get("allowed-tools")
    if allowed_tools is not None and not isinstance(allowed_tools, str):
        findings.append(Finding("error", repo_relative(skill_md), "Frontmatter allowed-tools must be a string."))

    if not body.strip():
        findings.append(Finding("error", repo_relative(skill_md), "SKILL.md must include Markdown body instructions."))

    if len(skill_text.splitlines()) > 500:
        findings.append(
            Finding("warning", repo_relative(skill_md), "SKILL.md exceeds the Agent Skills 500-line recommendation.")
        )

    return findings


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
    findings.extend(validate_agent_skills_spec(skill_dir, skill_md, skill_text))

    if SHARED_CONTEXT_MARKER not in skill_text:
        findings.append(
            Finding("error", repo_relative(skill_md), "Skill does not reference shared project context.")
        )

    if EXECUTION_CONTRACT_MARKER not in skill_text:
        findings.append(
            Finding("error", repo_relative(skill_md), "Skill does not reference the Codex execution contract.")
        )

    for heading in REQUIRED_SECTIONS:
        if heading not in skill_text:
            findings.append(
                Finding("error", repo_relative(skill_md), f"Skill is missing required section {heading}.")
            )

    if "Trigger examples:" not in skill_text:
        findings.append(
            Finding("error", repo_relative(skill_md), "Skill is missing lightweight trigger examples.")
        )

    if "Non-trigger examples:" not in skill_text:
        findings.append(
            Finding("error", repo_relative(skill_md), "Skill is missing lightweight non-trigger examples.")
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
