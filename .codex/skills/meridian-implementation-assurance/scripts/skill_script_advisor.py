#!/usr/bin/env python3
"""Audit and scaffold bundled scripts for Meridian Codex skills."""

from __future__ import annotations

import argparse
import json
import re
import sys
import tempfile
from dataclasses import asdict, dataclass
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[4]
CODEX_SKILLS_ROOT = REPO_ROOT / ".codex" / "skills"
SCRIPT_NAME_RE = re.compile(r"^[a-z][a-z0-9_]*\.py$")

OPPORTUNITY_RULES = (
    (
        ("eval", "rubric", "score", "benchmark", "judge"),
        "Consider a deterministic eval or rubric script when scoring is repeated.",
    ),
    (
        ("route", "documentation", "docs", "cross-link", "catalog"),
        "Consider a routing or catalog-check script when placement rules are repeated.",
    ),
    (
        ("classify", "archive", "cleanup", "duplicate", "trace"),
        "Consider a classifier or trace script when file decisions need repeatable evidence.",
    ),
    (
        ("provider", "manifest", "schema", "credential", "capability"),
        "Consider a schema or capability-check script when provider setup has fixed checks.",
    ),
    (
        ("browser", "workstation", "vitest", "playwright", "screenshot"),
        "Consider a rendered-state or package-test wrapper when UI proof is repeated.",
    ),
)


@dataclass(frozen=True)
class Finding:
    severity: str
    path: str
    message: str


@dataclass(frozen=True)
class SkillReport:
    skill: str
    path: str
    script_count: int
    findings: list[Finding]
    recommendations: list[str]


def repo_relative(path: Path) -> str:
    try:
        return path.resolve().relative_to(REPO_ROOT.resolve()).as_posix()
    except ValueError:
        return path.as_posix()


def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def current_skill_dirs() -> list[Path]:
    if not CODEX_SKILLS_ROOT.exists():
        return []
    return sorted(
        path
        for path in CODEX_SKILLS_ROOT.iterdir()
        if path.is_dir() and not path.name.startswith("_") and (path / "SKILL.md").exists()
    )


def resolve_skill_dirs(skill: str | None) -> list[Path]:
    if skill is None:
        return current_skill_dirs()

    candidate = Path(skill)
    if candidate.exists():
        return [candidate.resolve()]

    skill_dir = CODEX_SKILLS_ROOT / skill
    if skill_dir.exists():
        return [skill_dir]

    raise FileNotFoundError(f"Skill not found: {skill}")


def slug_to_script_name(value: str) -> str:
    slug = re.sub(r"[^a-z0-9]+", "_", value.lower()).strip("_")
    slug = re.sub(r"_+", "_", slug)
    if not slug:
        slug = "new_skill_helper"
    if not slug[0].isalpha():
        slug = f"skill_{slug}"
    if not slug.endswith(".py"):
        slug = f"{slug}.py"
    return slug


def script_template(purpose: str) -> str:
    purpose = " ".join(purpose.strip().split()).rstrip(".") or "Support this skill with a deterministic helper"
    purpose = purpose.replace('"""', '\\"\\"\\"')
    return f'''#!/usr/bin/env python3
"""{purpose}."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[4]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--summary", action="store_true", help="Emit a compact human-readable summary.")
    parser.add_argument("--json", action="store_true", help="Emit machine-readable JSON.")
    return parser.parse_args()


def build_payload() -> dict[str, object]:
    return {{
        "status": "todo",
        "repo_root": REPO_ROOT.as_posix(),
        "message": "Replace this scaffold with the deterministic check this skill needs.",
    }}


def main() -> int:
    args = parse_args()
    payload = build_payload()

    if args.json:
        print(json.dumps(payload, indent=2))
    else:
        print(f"Status: {{payload['status']}}")
        if args.summary:
            print(payload["message"])

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
'''


def scaffold_script(skill_dir: Path, script_name: str, purpose: str, force: bool = False) -> Path:
    script_name = slug_to_script_name(script_name)
    if not SCRIPT_NAME_RE.fullmatch(script_name):
        raise ValueError("Script name must normalize to lowercase snake_case Python, for example audit_skill.py.")

    scripts_dir = skill_dir / "scripts"
    scripts_dir.mkdir(parents=True, exist_ok=True)
    target = scripts_dir / script_name
    if target.exists() and not force:
        raise FileExistsError(f"Script already exists: {repo_relative(target)}")

    target.write_text(script_template(purpose), encoding="utf-8", newline="\n")
    return target


def script_findings(script_path: Path, skill_text: str) -> list[Finding]:
    findings: list[Finding] = []
    rel = repo_relative(script_path)
    text = read_text(script_path)

    if not script_path.name.endswith(".py"):
        findings.append(Finding("warning", rel, "Bundled script is not Python; test it with its native runner."))
        return findings

    if not SCRIPT_NAME_RE.fullmatch(script_path.name):
        findings.append(Finding("warning", rel, "Use lowercase snake_case names for Python skill scripts."))
    if "argparse" not in text:
        findings.append(Finding("warning", rel, "Add argparse so future agents can discover supported options."))
    if 'if __name__ == "__main__"' not in text:
        findings.append(Finding("error", rel, "Add a main guard so the script can be executed directly."))
    if 'encoding="utf-8"' not in text and ".read_text(" in text:
        findings.append(Finding("warning", rel, 'Pass encoding="utf-8" when reading text files.'))
    if 'encoding="utf-8"' not in text and ".write_text(" in text:
        findings.append(Finding("warning", rel, 'Pass encoding="utf-8" when writing text files.'))
    if script_path.name not in skill_text:
        findings.append(Finding("warning", rel, "Mention this script in SKILL.md so it is discoverable."))

    return findings


def opportunity_recommendations(skill_text: str, has_scripts: bool) -> list[str]:
    folded = skill_text.casefold()
    recommendations: list[str] = []
    for keywords, recommendation in OPPORTUNITY_RULES:
        if any(keyword in folded for keyword in keywords):
            recommendations.append(recommendation)

    if not has_scripts and recommendations:
        recommendations.insert(0, "This skill describes repeated deterministic work but has no bundled scripts.")

    return sorted(dict.fromkeys(recommendations))


def audit_skill(skill_dir: Path) -> SkillReport:
    skill_md = skill_dir / "SKILL.md"
    findings: list[Finding] = []

    if not skill_md.exists():
        findings.append(Finding("error", repo_relative(skill_md), "Missing SKILL.md."))
        return SkillReport(skill_dir.name, repo_relative(skill_dir), 0, findings, [])

    skill_text = read_text(skill_md)
    scripts_dir = skill_dir / "scripts"
    scripts = sorted(path for path in scripts_dir.glob("*") if path.is_file()) if scripts_dir.exists() else []

    if scripts and "scripts/" not in skill_text and "scripts\\" not in skill_text:
        findings.append(Finding("warning", repo_relative(skill_md), "SKILL.md does not describe bundled scripts/."))

    for script_path in scripts:
        findings.extend(script_findings(script_path, skill_text))

    recommendations = opportunity_recommendations(skill_text, bool(scripts))
    return SkillReport(skill_dir.name, repo_relative(skill_dir), len(scripts), findings, recommendations)


def report_to_json(report: SkillReport) -> dict[str, object]:
    return {
        "skill": report.skill,
        "path": report.path,
        "script_count": report.script_count,
        "findings": [asdict(finding) for finding in report.findings],
        "recommendations": report.recommendations,
    }


def print_report(report: SkillReport) -> None:
    error_count = sum(1 for finding in report.findings if finding.severity == "error")
    warning_count = sum(1 for finding in report.findings if finding.severity == "warning")
    print(f"{report.skill}: {report.script_count} script(s), {error_count} error(s), {warning_count} warning(s)")
    for finding in report.findings:
        print(f"  {finding.severity}: {finding.path}: {finding.message}")
    for recommendation in report.recommendations:
        print(f"  recommend: {recommendation}")


def run_audit(args: argparse.Namespace) -> int:
    try:
        reports = [audit_skill(path) for path in resolve_skill_dirs(args.skill)]
    except FileNotFoundError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2

    error_count = sum(1 for report in reports for finding in report.findings if finding.severity == "error")
    warning_count = sum(1 for report in reports for finding in report.findings if finding.severity == "warning")
    payload = {
        "status": "fail" if error_count else "pass",
        "skill_count": len(reports),
        "error_count": error_count,
        "warning_count": warning_count,
        "reports": [report_to_json(report) for report in reports],
    }

    if args.json:
        print(json.dumps(payload, indent=2))
    else:
        for report in reports:
            print_report(report)
        if args.summary:
            print(
                f"Skill script audit: {payload['status']}; "
                f"{payload['skill_count']} skill(s), {error_count} error(s), {warning_count} warning(s)."
            )

    if args.fail_on_warning and warning_count:
        return 1
    return 1 if error_count else 0


def run_scaffold(args: argparse.Namespace) -> int:
    try:
        skill_dirs = resolve_skill_dirs(args.skill)
    except FileNotFoundError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2
    if len(skill_dirs) != 1:
        print("error: scaffold requires exactly one --skill target.", file=sys.stderr)
        return 2

    try:
        target = scaffold_script(skill_dirs[0], args.name, args.purpose, force=args.force)
    except (FileExistsError, ValueError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2

    payload = {
        "created": repo_relative(target),
        "next_steps": [
            "Replace the scaffold body with deterministic skill-specific logic.",
            "Mention the script in SKILL.md under Automation Scripts or Validation.",
            "Run the script directly and run check-codex-skills.py after editing the skill.",
        ],
    }

    if args.json:
        print(json.dumps(payload, indent=2))
    else:
        print(f"Created {payload['created']}")
        for step in payload["next_steps"]:
            print(f"- {step}")
    return 0


def run_self_test() -> int:
    with tempfile.TemporaryDirectory() as tmp:
        skill_dir = Path(tmp) / "sample-skill"
        skill_dir.mkdir()
        (skill_dir / "SKILL.md").write_text(
            "---\nname: sample-skill\ndescription: sample\n---\n\n"
            "# Sample\n\nUse deterministic eval and documentation checks.\n",
            encoding="utf-8",
            newline="\n",
        )
        target = scaffold_script(skill_dir, "Sample Tool", "Sample helper")
        text = read_text(target)
        report = audit_skill(skill_dir)

        assert target.exists()
        assert "argparse" in text
        assert 'if __name__ == "__main__"' in text
        assert report.script_count == 1
        assert report.recommendations

    print("Self-test passed.")
    return 0


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    subcommands = parser.add_subparsers(dest="command")

    audit = subcommands.add_parser("audit", help="Audit skill script resources.")
    audit.add_argument("--skill", help="Skill name or path. Omit to audit every Codex skill.")
    audit.add_argument("--summary", action="store_true", help="Print an aggregate summary.")
    audit.add_argument("--json", action="store_true", help="Emit machine-readable JSON.")
    audit.add_argument("--fail-on-warning", action="store_true", help="Exit nonzero when warnings exist.")
    audit.set_defaults(func=run_audit)

    scaffold = subcommands.add_parser("scaffold", help="Create a repo-convention Python script scaffold.")
    scaffold.add_argument("--skill", required=True, help="Skill name or path.")
    scaffold.add_argument("--name", required=True, help="New script name, normalized to snake_case.py.")
    scaffold.add_argument("--purpose", required=True, help="Short script purpose used in the module docstring.")
    scaffold.add_argument("--force", action="store_true", help="Overwrite an existing scaffold target.")
    scaffold.add_argument("--json", action="store_true", help="Emit machine-readable JSON.")
    scaffold.set_defaults(func=run_scaffold)

    self_test = subcommands.add_parser("self-test", help="Run script smoke checks.")
    self_test.set_defaults(func=lambda _args: run_self_test())

    args = parser.parse_args()
    if not hasattr(args, "func"):
        parser.print_help()
        return parser.parse_args(["audit", "--summary"])
    return args


def main() -> int:
    args = parse_args()
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
