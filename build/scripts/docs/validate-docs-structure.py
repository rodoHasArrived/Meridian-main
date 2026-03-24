#!/usr/bin/env python3
"""
Docs structure validator for Meridian.

Checks:
  1. Every docs/ top-level subdirectory has a README.md
  2. Hand-authored markdown files have front-matter lifecycle fields
     (Status, Owner, Reviewed) — warns when absent
  3. Stale documents: Reviewed date older than 180 days — warns

Exit codes:
  0 — all checks passed (warnings printed but not fatal)
  1 — errors found (missing READMEs)

Usage:
    python3 validate-docs-structure.py
    python3 validate-docs-structure.py --docs-dir /path/to/docs
    python3 validate-docs-structure.py --strict   # treat warnings as errors
"""

import argparse
import re
import sys
from datetime import date, datetime, timezone
from pathlib import Path

# Directories whose READMEs are auto-generated (skip them for front-matter checks)
GENERATED_DIRS = {"generated"}

# Directories that are exempt from README requirement (meta files at top level)
README_EXEMPT_FILES = {"README.md", "HELP.md", "DEPENDENCIES.md", "toc.yml"}

# How old (days) before a 'Reviewed' date is considered stale
STALE_DAYS = 180

# Front-matter fields that are encouraged (not required → warnings only)
ENCOURAGED_FIELDS = ["Status", "Owner", "Reviewed"]

# Subdirectories that should NOT contain inline planning docs
# (planning belongs in status/ or archived/)
GOVERNANCE_ONLY_PLANNING = {"development", "architecture", "providers", "operations", "reference"}


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description="Validate docs/ directory structure and front matter.")
    p.add_argument("--docs-dir", default="docs", help="Path to docs directory (default: docs)")
    p.add_argument("--strict", action="store_true", help="Treat warnings as errors")
    p.add_argument("--github-actions", action="store_true",
                   help="Emit GitHub Actions annotation format")
    return p.parse_args()


def emit(level: str, path: str, message: str, github_actions: bool) -> None:
    if github_actions:
        print(f"::{level} file={path}::{message}")
    else:
        icon = "❌" if level == "error" else "⚠️ "
        print(f"  {icon} {level.upper()}: {path}: {message}")


def check_readme_exists(docs_dir: Path, github_actions: bool) -> list[str]:
    """Return list of error messages for subdirectories missing README.md."""
    errors = []
    for subdir in sorted(docs_dir.iterdir()):
        if not subdir.is_dir():
            continue
        if subdir.name.startswith("."):
            continue
        readme = subdir / "README.md"
        if not readme.exists():
            msg = f"Missing README.md in docs/{subdir.name}/"
            emit("error", str(readme), msg, github_actions)
            errors.append(msg)
    return errors


def extract_field(content: str, field: str) -> str | None:
    """Extract a front-matter field value like **Field:** value."""
    pattern = rf"\*\*{field}:\*\*\s*(.+)"
    m = re.search(pattern, content, re.IGNORECASE)
    if m:
        return m.group(1).strip()
    return None


def check_front_matter(docs_dir: Path, github_actions: bool, strict: bool) -> list[str]:
    """
    Check that hand-authored markdown files in key dirs have lifecycle front matter.
    Returns list of warning/error messages.
    """
    issues = []
    today = date.today()

    # Only check hand-authored sections
    check_dirs = [d for d in docs_dir.iterdir()
                  if d.is_dir() and d.name not in GENERATED_DIRS and not d.name.startswith(".")]

    for subdir in sorted(check_dirs):
        for md_file in sorted(subdir.rglob("*.md")):
            if md_file.name.lower() in {r.lower() for r in README_EXEMPT_FILES}:
                continue

            content = md_file.read_text(encoding="utf-8", errors="replace")
            rel = md_file.relative_to(docs_dir.parent)

            for field in ENCOURAGED_FIELDS:
                value = extract_field(content, field)
                if value is None:
                    msg = f"Missing front-matter field '**{field}:**' in {rel}"
                    emit("warning", str(rel), msg, github_actions)
                    issues.append(msg)
                elif field == "Reviewed":
                    # Check staleness
                    try:
                        reviewed_date = datetime.strptime(value, "%Y-%m-%d").date()
                        age = (today - reviewed_date).days
                        if age > STALE_DAYS:
                            msg = (f"Stale document in {rel}: "
                                   f"'Reviewed: {value}' is {age} days old (threshold: {STALE_DAYS})")
                            emit("warning", str(rel), msg, github_actions)
                            issues.append(msg)
                    except ValueError:
                        pass  # Non-standard date format — skip

    return issues


def main() -> int:
    args = parse_args()
    docs_dir = Path(args.docs_dir)

    if not docs_dir.is_dir():
        print(f"ERROR: docs directory not found: {docs_dir}", file=sys.stderr)
        return 1

    print(f"Validating docs structure in: {docs_dir.resolve()}")
    print()

    # ── Check 1: README presence ──────────────────────────────────────────
    print("Check 1: README.md present in every top-level subdirectory")
    readme_errors = check_readme_exists(docs_dir, args.github_actions)
    if not readme_errors:
        print("  ✅ All subdirectories have README.md")
    print()

    # ── Check 2: Front-matter lifecycle fields ────────────────────────────
    print("Check 2: Lifecycle front-matter fields (Status, Owner, Reviewed)")
    print("         Note: These are encouraged, not required — warnings only.")
    fm_issues = check_front_matter(docs_dir, args.github_actions, args.strict)
    if not fm_issues:
        print("  ✅ All checked files have lifecycle front matter")
    else:
        print(f"  Found {len(fm_issues)} front-matter warning(s) — see above for details.")
    print()

    # ── Summary ───────────────────────────────────────────────────────────
    has_errors = bool(readme_errors)
    has_warnings = bool(fm_issues)

    if args.strict and has_warnings:
        has_errors = True

    if has_errors:
        print(f"❌ Validation FAILED: {len(readme_errors)} error(s), {len(fm_issues)} warning(s)")
        return 1

    if has_warnings:
        print(f"⚠️  Validation passed with {len(fm_issues)} warning(s)")
    else:
        print("✅ Docs structure validation passed")

    return 0


if __name__ == "__main__":
    sys.exit(main())
