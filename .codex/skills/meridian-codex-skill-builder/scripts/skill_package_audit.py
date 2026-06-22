#!/usr/bin/env python3
"""Audit Meridian repo-local Codex skill package completeness."""

from __future__ import annotations

import argparse
import json
import re
import sys
from dataclasses import asdict, dataclass
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[4]
CODEX_SKILLS = REPO_ROOT / ".codex" / "skills"
CODEX_AGENTS = REPO_ROOT / ".codex" / "agents"
CODEX_CONFIG = REPO_ROOT / ".codex" / "config.toml"
CODEX_SKILLS_README = CODEX_SKILLS / "README.md"
CODEX_DOCS_README = REPO_ROOT / "docs" / "ai" / "codex" / "README.md"
AI_SKILLS_README = REPO_ROOT / "docs" / "ai" / "skills" / "README.md"
ROUTE_RULES = REPO_ROOT / "docs" / "ai" / "codex" / "prompt-route-rules.json"
REQUIRED_SECTIONS = (
    "## Use When",
    "## Do Not Use When",
    "## Workflow",
    "## Handoffs",
    "## Validation",
    "## Output Standards",
)
SCRIPT_NAME_RE = re.compile(r"^[a-z][a-z0-9_]*\.py$")


@dataclass(frozen=True)
class Finding:
    severity: str
    path: str
    message: str


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--skill", action="append", required=True, help="Skill name or path. Repeatable.")
    parser.add_argument("--summary", action="store_true", help="Emit a compact human-readable summary.")
    parser.add_argument("--json", action="store_true", help="Emit machine-readable JSON.")
    parser.add_argument("--fail-on-warning", action="store_true", help="Return nonzero on warnings.")
    return parser.parse_args()


def repo_relative(path: Path) -> str:
    try:
        return path.resolve().relative_to(REPO_ROOT.resolve()).as_posix()
    except ValueError:
        return path.as_posix()


def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8", errors="replace")


def resolve_skill(value: str) -> Path:
    candidate = Path(value)
    if candidate.exists():
        return candidate.resolve()
    return CODEX_SKILLS / value


def require_file(findings: list[Finding], path: Path, label: str) -> bool:
    if path.is_file():
        return True
    findings.append(Finding("error", repo_relative(path), f"Missing {label}."))
    return False


def contains(path: Path, marker: str) -> bool:
    return path.is_file() and marker in read_text(path)


def validate_skill(skill_dir: Path) -> dict[str, object]:
    findings: list[Finding] = []
    skill_name = skill_dir.name
    skill_md = skill_dir / "SKILL.md"
    openai_yaml = skill_dir / "agents" / "openai.yaml"
    evals_json = skill_dir / "evals" / "evals.json"
    baseline_json = skill_dir / "evals" / "benchmark_baseline.json"
    run_evals = skill_dir / "scripts" / "run_evals.py"
    score_eval = skill_dir / "scripts" / "score_eval.py"
    agent_profile = CODEX_AGENTS / f"{skill_name}.toml"

    if not skill_dir.exists():
        findings.append(Finding("error", repo_relative(skill_dir), "Skill directory is missing."))
        return {"skill": skill_name, "status": "fail", "findings": [asdict(f) for f in findings]}

    if require_file(findings, skill_md, "SKILL.md"):
        text = read_text(skill_md)
        for marker in (f"name: {skill_name}", "description:", "../_shared/project-context.md", "../_shared/codex-execution-contract.md"):
            if marker not in text:
                findings.append(Finding("error", repo_relative(skill_md), f"Missing marker {marker!r}."))
        for section in REQUIRED_SECTIONS:
            if section not in text:
                findings.append(Finding("error", repo_relative(skill_md), f"Missing required section {section}."))
        if "Trigger examples:" not in text:
            findings.append(Finding("error", repo_relative(skill_md), "Missing trigger examples."))
        if "Non-trigger examples:" not in text:
            findings.append(Finding("error", repo_relative(skill_md), "Missing non-trigger examples."))

    if require_file(findings, openai_yaml, "agents/openai.yaml"):
        openai_text = read_text(openai_yaml)
        for marker in ("display_name:", "short_description:", "default_prompt:", f"${skill_name}"):
            if marker not in openai_text:
                findings.append(Finding("error", repo_relative(openai_yaml), f"Missing marker {marker!r}."))

    for path, label in (
        (evals_json, "evals/evals.json"),
        (baseline_json, "evals/benchmark_baseline.json"),
        (run_evals, "scripts/run_evals.py"),
        (score_eval, "scripts/score_eval.py"),
        (agent_profile, "Codex agent profile"),
    ):
        require_file(findings, path, label)

    scripts = [path for path in (skill_dir / "scripts").glob("*.py")] if (skill_dir / "scripts").exists() else []
    helper_scripts = [path.name for path in scripts if path.name not in {"run_evals.py", "score_eval.py"}]
    if not helper_scripts:
        findings.append(Finding("error", repo_relative(skill_dir / "scripts"), "Missing a skill-specific helper script."))
    for script in scripts:
        if not SCRIPT_NAME_RE.fullmatch(script.name):
            findings.append(Finding("warning", repo_relative(script), "Python script should use lowercase snake_case."))

    for path, label in (
        (CODEX_CONFIG, ".codex/config.toml"),
        (CODEX_SKILLS_README, ".codex/skills/README.md"),
        (CODEX_DOCS_README, "docs/ai/codex/README.md"),
        (AI_SKILLS_README, "docs/ai/skills/README.md"),
        (ROUTE_RULES, "prompt-route-rules.json"),
    ):
        if not contains(path, skill_name):
            findings.append(Finding("error", repo_relative(path), f"{label} does not mention {skill_name}."))

    status = "fail" if any(finding.severity == "error" for finding in findings) else "pass"
    return {
        "skill": skill_name,
        "status": status,
        "helper_scripts": helper_scripts,
        "findings": [asdict(finding) for finding in findings],
    }


def main() -> int:
    args = parse_args()
    reports = [validate_skill(resolve_skill(value)) for value in args.skill]
    error_count = sum(
        1 for report in reports for finding in report["findings"] if finding["severity"] == "error"
    )
    warning_count = sum(
        1 for report in reports for finding in report["findings"] if finding["severity"] == "warning"
    )
    payload = {
        "status": "fail" if error_count else "pass",
        "summary": {
            "skill_count": len(reports),
            "error_count": error_count,
            "warning_count": warning_count,
        },
        "reports": reports,
    }

    if args.json:
        print(json.dumps(payload, indent=2))
    else:
        for report in reports:
            print(f"{report['skill']}: {report['status']}")
            for finding in report["findings"]:
                print(f"  {finding['severity']}: {finding['path']}: {finding['message']}")
        if args.summary:
            summary = payload["summary"]
            print(
                f"skill_package_audit status={payload['status']} "
                f"skills={summary['skill_count']} errors={summary['error_count']} warnings={summary['warning_count']}"
            )

    if error_count:
        return 1
    if args.fail_on_warning and warning_count:
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
