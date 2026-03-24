#!/usr/bin/env python3
"""Validate Meridian Agent Skill packages for portable-package conventions."""

from __future__ import annotations

import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
SKILLS_ROOT = REPO_ROOT / ".claude" / "skills"
NAME_RE = re.compile(r"^[a-z0-9]+(?:-[a-z0-9]+)*$")
FIELD_RE = re.compile(r"^(name|description|license|compatibility|metadata|allowed-tools):", re.MULTILINE)
RESOURCE_DIRS = ("references", "scripts", "assets")


def read_frontmatter(text: str) -> tuple[str, str]:
    if not text.startswith("---\n"):
        raise ValueError("missing starting frontmatter fence")
    end = text.find("\n---\n", 4)
    if end == -1:
        raise ValueError("missing closing frontmatter fence")
    return text[4:end], text[end + 5 :]


def extract_field(frontmatter: str, name: str) -> str | None:
    pattern = re.compile(rf"^{re.escape(name)}:\s*(.*)$", re.MULTILINE)
    match = pattern.search(frontmatter)
    if not match:
        return None

    value = match.group(1).rstrip()
    if value and value != ">":
        return value.strip('"')

    start = match.end()
    lines: list[str] = []
    for line in frontmatter[start:].splitlines():
        if line.startswith((" ", "\t")):
            lines.append(line.strip())
            continue
        if not line.strip():
            lines.append("")
            continue
        break
    return " ".join(part for part in lines if part).strip() or None


def validate_skill(skill_dir: Path) -> list[str]:
    errors: list[str] = []
    skill_file = skill_dir / "SKILL.md"
    if not skill_file.exists():
        return [f"{skill_dir.relative_to(REPO_ROOT)}: missing SKILL.md"]

    text = skill_file.read_text(encoding="utf-8")
    try:
        frontmatter, body = read_frontmatter(text)
    except ValueError as exc:
        return [f"{skill_file.relative_to(REPO_ROOT)}: {exc}"]

    field_names = set(FIELD_RE.findall(frontmatter))
    required_fields = {"name", "description", "license", "compatibility", "metadata"}
    missing = sorted(required_fields - field_names)
    if missing:
        errors.append(f"{skill_file.relative_to(REPO_ROOT)}: missing required fields: {', '.join(missing)}")

    name = extract_field(frontmatter, "name")
    if not name:
        errors.append(f"{skill_file.relative_to(REPO_ROOT)}: name is missing or empty")
    else:
        if len(name) > 64:
            errors.append(f"{skill_file.relative_to(REPO_ROOT)}: name exceeds 64 characters")
        if not NAME_RE.fullmatch(name):
            errors.append(f"{skill_file.relative_to(REPO_ROOT)}: name is not portable hyphen-case")
        if name != skill_dir.name:
            errors.append(
                f"{skill_file.relative_to(REPO_ROOT)}: name {name!r} does not match directory {skill_dir.name!r}"
            )

    description = extract_field(frontmatter, "description")
    if not description:
        errors.append(f"{skill_file.relative_to(REPO_ROOT)}: description is missing or empty")
    elif len(description) > 1024:
        errors.append(f"{skill_file.relative_to(REPO_ROOT)}: description exceeds 1024 characters")

    compatibility = extract_field(frontmatter, "compatibility")
    if compatibility and len(compatibility) > 500:
        errors.append(f"{skill_file.relative_to(REPO_ROOT)}: compatibility exceeds 500 characters")

    body_lines = len(body.splitlines())
    if body_lines > 500:
        errors.append(f"{skill_file.relative_to(REPO_ROOT)}: SKILL.md body exceeds 500 lines ({body_lines})")

    for resource_dir_name in RESOURCE_DIRS:
        resource_dir = skill_dir / resource_dir_name
        if resource_dir.exists() and not any(resource_dir.iterdir()):
            errors.append(f"{resource_dir.relative_to(REPO_ROOT)}: directory exists but is empty")
        if resource_dir.exists() and resource_dir_name not in body:
            errors.append(
                f"{skill_file.relative_to(REPO_ROOT)}: does not mention {resource_dir_name}/ despite bundling that directory"
            )

    return errors


def main() -> int:
    skill_dirs = sorted(path.parent for path in SKILLS_ROOT.glob("*/SKILL.md"))
    if not skill_dirs:
        print("No skill packages found under .claude/skills/", file=sys.stderr)
        return 1

    failures: list[str] = []
    for skill_dir in skill_dirs:
        failures.extend(validate_skill(skill_dir))

    if failures:
        print("Skill package validation failed:")
        for failure in failures:
            print(f" - {failure}")
        return 1

    print(f"Validated {len(skill_dirs)} skill packages in {SKILLS_ROOT.relative_to(REPO_ROOT)}/")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
