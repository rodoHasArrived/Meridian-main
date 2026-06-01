#!/usr/bin/env python3
"""
Docs structure validator for Meridian.

Checks:
  1. Every docs/ top-level subdirectory has a README.md
  2. docs/ top-level directories are known to the documentation rebuild model
  3. Hand-authored markdown files have front-matter lifecycle fields
     (Status, Owner, Reviewed) — warns when absent
  4. Stale documents: Reviewed date older than 180 days — warns

Exit codes:
  0 — all checks passed (warnings printed but not fatal)
  1 — errors found (missing READMEs or unexpected top-level docs folders)

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

# Directories whose contents are generated or build-produced
# and should be excluded from hand-authored structure checks.
GENERATED_DIRS = {"generated", "_site"}

# Canonical top-level folders for the documentation rebuild. Legacy folders are still allowed
# during migration, but this list is used to report which active lanes belong to the target model.
CANONICAL_TOP_LEVEL_DIRS = {
    "ai",
    "engineering",
    "generated",
    "operators",
    "product",
    "reference",
    "roadmap",
    "source",
    "start",
}

# Legacy folders allowed during the staged documentation rebuild. These folders should not be used
# as new canonical destinations; they remain so current content can be mined, redirected, generated,
# or archived in reviewable batches.
LEGACY_TOP_LEVEL_DIRS = {
    "adr",
    "api",
    "architecture",
    "audits",
    "design",
    "developer",
    "development",
    "diagrams",
    "docfx",
    "evaluations",
    "examples",
    "getting-started",
    "integrations",
    "operations",
    "plans",
    "prompts",
    "providers",
    "screenshots",
    "security",
    "status",
    "testing",
    "ui",
}

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
    p.add_argument("--summary", action="store_true", help="Accepted for compatibility; output is already summarized")
    p.add_argument("--strict", action="store_true", help="Treat warnings as errors")
    p.add_argument("--github-actions", action="store_true",
                   help="Emit GitHub Actions annotation format")
    return p.parse_args()


def emit(level: str, path: str, message: str, github_actions: bool) -> None:
    if github_actions:
        print(f"::{level} file={path}::{message}")
    else:
        icon = "ERROR" if level == "error" else "WARN"
        print(f"  {icon} {level.upper()}: {path}: {message}")


def check_readme_exists(docs_dir: Path, github_actions: bool) -> list[str]:
    """Return list of error messages for subdirectories missing README.md."""
    errors = []
    for subdir in sorted(docs_dir.iterdir()):
        if not subdir.is_dir():
            continue
        if subdir.name.startswith("."):
            continue
        if subdir.name in GENERATED_DIRS:
            continue
        readme = subdir / "README.md"
        if not readme.exists():
            msg = f"Missing README.md in docs/{subdir.name}/"
            emit("error", str(readme), msg, github_actions)
            errors.append(msg)
    return errors


def check_canonical_model(docs_dir: Path, github_actions: bool) -> list[str]:
    """Return warnings for missing canonical rebuild folders."""
    warnings = []
    for name in sorted(CANONICAL_TOP_LEVEL_DIRS):
        path = docs_dir / name
        if not path.is_dir():
            msg = f"Missing canonical docs rebuild folder docs/{name}/"
            emit("warning", str(path), msg, github_actions)
            warnings.append(msg)
    return warnings


def check_top_level_model(docs_dir: Path, github_actions: bool) -> tuple[list[str], list[str]]:
    """Validate top-level docs folders against the rebuild model.

    Returns (errors, warnings). Unknown folders are errors because they would create another
    ambiguous taxonomy. Known legacy folders are warnings so the staged migration can proceed.
    """
    errors = []
    warnings = []
    allowed = CANONICAL_TOP_LEVEL_DIRS | LEGACY_TOP_LEVEL_DIRS | GENERATED_DIRS

    for subdir in sorted(docs_dir.iterdir()):
        if not subdir.is_dir() or subdir.name.startswith("."):
            continue
        rel = subdir.relative_to(docs_dir.parent)
        if subdir.name not in allowed:
            msg = (
                f"Unexpected top-level docs folder docs/{subdir.name}/. "
                "Choose docs/start, docs/product, docs/engineering, docs/operators, "
                "docs/reference, docs/ai, docs/roadmap, docs/source, docs/generated, "
                "or archive/docs."
            )
            emit("error", str(rel), msg, github_actions)
            errors.append(msg)
        elif subdir.name in LEGACY_TOP_LEVEL_DIRS:
            msg = (
                f"Known legacy/source-material folder docs/{subdir.name}/ remains during migration; "
                "do not add new canonical docs here."
            )
            emit("warning", str(rel), msg, github_actions)
            warnings.append(msg)

    return errors, warnings


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
        print("  OK All subdirectories have README.md")
    print()

    # ── Check 1b: Canonical rebuild model ────────────────────────────────
    print("Check 1b: Canonical documentation rebuild folders are present")
    canonical_warnings = check_canonical_model(docs_dir, args.github_actions)
    if not canonical_warnings:
        print("  OK Canonical rebuild folders are present")
    else:
        print(f"  Found {len(canonical_warnings)} canonical-model warning(s) — see above for details.")
    print()

    # ── Check 1c: Top-level taxonomy guard ───────────────────────────────
    print("Check 1c: Top-level docs folders match the rebuild model")
    top_level_errors, top_level_warnings = check_top_level_model(docs_dir, args.github_actions)
    if not top_level_errors and not top_level_warnings:
        print("  OK Top-level docs folders are canonical")
    else:
        print(
            f"  Found {len(top_level_errors)} top-level folder error(s), "
            f"{len(top_level_warnings)} migration warning(s) — see above for details."
        )
    print()

    # ── Check 2: Front-matter lifecycle fields ────────────────────────────
    print("Check 2: Lifecycle front-matter fields (Status, Owner, Reviewed)")
    print("         Note: These are encouraged, not required — warnings only.")
    fm_issues = check_front_matter(docs_dir, args.github_actions, args.strict)
    if not fm_issues:
        print("  OK All checked files have lifecycle front matter")
    else:
        print(f"  Found {len(fm_issues)} front-matter warning(s) — see above for details.")
    print()

    # ── Summary ───────────────────────────────────────────────────────────
    has_errors = bool(readme_errors or top_level_errors)
    has_warnings = bool(fm_issues or canonical_warnings or top_level_warnings)

    if args.strict and has_warnings:
        has_errors = True

    if has_errors:
        print(
            f"FAILED: {len(readme_errors) + len(top_level_errors)} error(s), "
            f"{len(fm_issues) + len(canonical_warnings) + len(top_level_warnings)} warning(s)"
        )
        return 1

    if has_warnings:
        print(
            "Validation passed with "
            f"{len(fm_issues) + len(canonical_warnings) + len(top_level_warnings)} warning(s)"
        )
    else:
        print("Docs structure validation passed")

    return 0


if __name__ == "__main__":
    sys.exit(main())
