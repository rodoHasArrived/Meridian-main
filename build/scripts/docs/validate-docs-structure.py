#!/usr/bin/env python3
"""
Docs structure validator for Meridian.

Checks:
  1. Every docs/ top-level subdirectory has a README.md
  2. docs/ top-level directories are known to the documentation rebuild model
  3. Hand-authored markdown files have front-matter lifecycle fields
     (Status, Owner, Reviewed) — warns when absent
  4. Invalid Reviewed dates and dates older than 180 days — warns

Lifecycle fields must appear in opening YAML front matter or title/metadata, not examples
or document body text. Generated outputs are counted separately using directory,
automation-registry, or explicit generated-header ownership.

Exit codes:
  0 — all checks passed (warnings printed but not fatal)
  1 — errors found (missing READMEs or unexpected top-level docs folders)

Usage:
    python3 validate-docs-structure.py
    python3 validate-docs-structure.py --docs-dir /path/to/docs
    python3 validate-docs-structure.py --strict   # treat warnings as errors
    python3 validate-docs-structure.py --top-level ai --summary
"""

from __future__ import annotations

import argparse
import ast
import re
import sys
from collections import Counter
from datetime import date, datetime
from pathlib import Path

try:
    import yaml
except ImportError as exc:
    raise SystemExit(
        "PyYAML is required; install it with "
        "python3 -m pip install --requirement build/scripts/docs/requirements.txt"
    ) from exc

# Directories whose contents are generated or build-produced
# and should be excluded from hand-authored structure checks.
GENERATED_DIRS = {"generated", "_site"}

# Canonical top-level folders for the current documentation model.
CANONICAL_TOP_LEVEL_DIRS = {
    "ai",
    "architecture",
    "domain",
    "engineering",
    "generated",
    "operators",
    "product",
    "reference",
    "roadmap",
    "source",
    "start",
}

# Active specialist and automation-owned folders that are routed through a canonical entrypoint.
# These are supported locations, but they must not compete with their canonical owner for durable
# product, roadmap, source-module, or workflow truth.
SUPPORTING_TOP_LEVEL_DIRS = {
    "adr",
    "development",
    "diagrams",
    "docfx",
    "examples",
    "integrations",
    "prompts",
    "screenshots",
    "security",
    "status",
    "testing",
}

# Compatibility lanes that still have active tool, test, or strong-link consumers. They remain
# allowed but warn so new durable guidance is not added there.
TRANSITIONAL_TOP_LEVEL_DIRS = {"operations", "plans"}

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
    p.add_argument(
        "--top-level",
        action="append",
        default=[],
        help=(
            "Limit lifecycle front-matter checks to one or more top-level docs folders "
            "(for example: --top-level ai). In scoped mode, global taxonomy checks are skipped."
        ),
    )
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
    """Return warnings for missing canonical documentation folders."""
    warnings = []
    for name in sorted(CANONICAL_TOP_LEVEL_DIRS):
        path = docs_dir / name
        if not path.is_dir():
            msg = f"Missing canonical documentation folder docs/{name}/"
            emit("warning", str(path), msg, github_actions)
            warnings.append(msg)
    return warnings


def check_top_level_model(docs_dir: Path, github_actions: bool) -> tuple[list[str], list[str]]:
    """Validate top-level docs folders against the current documentation model.

    Returns (errors, warnings). Unknown or retired folders are errors because they would create
    another ambiguous taxonomy. Transitional compatibility folders warn until their consumers move.
    """
    errors = []
    warnings = []
    allowed = (
        CANONICAL_TOP_LEVEL_DIRS
        | SUPPORTING_TOP_LEVEL_DIRS
        | TRANSITIONAL_TOP_LEVEL_DIRS
        | GENERATED_DIRS
    )

    for subdir in sorted(docs_dir.iterdir()):
        if not subdir.is_dir() or subdir.name.startswith("."):
            continue
        rel = subdir.relative_to(docs_dir.parent)
        if subdir.name not in allowed:
            msg = (
                f"Unexpected top-level docs folder docs/{subdir.name}/. "
                "Choose a documented canonical or supporting lane from docs/documentation-inventory.md, "
                "or use archive/docs for historical material."
            )
            emit("error", str(rel), msg, github_actions)
            errors.append(msg)
        elif subdir.name in TRANSITIONAL_TOP_LEVEL_DIRS:
            msg = (
                f"Transitional compatibility folder docs/{subdir.name}/ is retained for active "
                "tooling or strong-link consumers; do not add new durable guidance here."
            )
            emit("warning", str(rel), msg, github_actions)
            warnings.append(msg)

    return errors, warnings


def read_yaml_front_matter(lines: list[str]) -> dict:
    """Parse an opening YAML block; incomplete or malformed blocks are findings."""
    end = next((i for i in range(1, len(lines)) if lines[i].strip() in {"---", "..."}), None)
    if end is None:
        raise ValueError("Unterminated YAML front matter; expected closing ---")
    try:
        metadata = yaml.safe_load("\n".join(lines[1:end]))
    except (yaml.YAMLError, ValueError) as exc:
        raise ValueError(f"Invalid YAML front matter: {exc}") from exc
    if not isinstance(metadata, dict):
        raise ValueError("YAML front matter must be a mapping")
    return metadata


def extract_lifecycle_header(content: str) -> dict[str, str]:
    """Read opening YAML front matter or the Markdown title/metadata block.

    Stop at prose, a section, or a code fence. A later example or an Owner label
    in a procedure must not supply the document's lifecycle metadata.
    """
    lines = content.lstrip("\ufeff \t\r\n").splitlines()
    if lines and lines[0].strip() == "---":
        metadata = read_yaml_front_matter(lines)
        return {
            key.casefold(): str(value).strip()
            for key, value in metadata.items()
            if isinstance(key, str) and isinstance(value, (str, date))
        }

    fields: dict[str, str] = {}
    title_seen = False
    for raw_line in lines:
        line = raw_line.strip()
        if not line:
            continue
        if not title_seen and re.match(r"^#\s+", line):
            title_seen = True
            continue
        match = re.fullmatch(r"\*\*([^*]+):\*\*[ \t]*(.*)", line)
        if not match:
            break
        fields.setdefault(match.group(1).casefold(), match.group(2).strip())
    return fields


def extract_field(content: str, field: str) -> str | None:
    """Read one lifecycle field; empty values are missing metadata."""
    return extract_lifecycle_header(content).get(field.casefold()) or None


def automation_outputs(repo_root: Path) -> set[Path]:
    """Read literal output declarations without executing the automation runner.

    A registry entry containing computed values cannot establish an exemption.
    Keep checking its documents unless they have another ownership signal.
    """
    registry = repo_root / "build/scripts/docs/run-docs-automation.py"
    if not registry.is_file():
        return set()
    tree = ast.parse(registry.read_text(encoding="utf-8"), filename=str(registry))
    outputs: set[Path] = set()
    for node in tree.body:
        if not (isinstance(node, ast.AnnAssign) and isinstance(node.target, ast.Name)
                and node.target.id == "SCRIPT_CONFIG" and isinstance(node.value, ast.Dict)):
            continue
        for entry in node.value.values:
            try:
                config = ast.literal_eval(entry)
            except (ValueError, TypeError):
                continue
            if not isinstance(config, dict):
                continue
            candidates = [config.get("output")]
            args = config.get("args", [])
            if isinstance(args, (list, tuple)):
                candidates.extend(args[i + 1] for i, arg in enumerate(args[:-1]) if arg == "--output")
            for output in candidates:
                if isinstance(output, str) and output.endswith(".md"):
                    outputs.add((repo_root / output).resolve())
    return outputs


def generated_ownership(md_file: Path, docs_dir: Path, content: str, outputs: set[Path]) -> str | None:
    """Identify file-level generation contracts, never a whole status folder.

    Headers are only recognized before body text, so a guide discussing a
    generator or showing a generated-header example still needs lifecycle fields.
    """
    if any(part in GENERATED_DIRS for part in md_file.relative_to(docs_dir).parts):
        return "generated directory"
    if md_file.resolve() in outputs:
        return "automation registry"

    header = content.lstrip("\ufeff \t\r\n")
    lines = header.splitlines()
    if lines and lines[0].strip() == "---":
        try:
            metadata = read_yaml_front_matter(lines)
        except ValueError:
            return None  # Lifecycle validation reports the malformed header.
        generator = metadata.get("generator")
        if metadata.get("generated") is True and isinstance(generator, str) and generator.strip():
            return "generated header"
    if header.startswith("# "):
        header = header.partition("\n")[2].lstrip()
    if header.startswith("<!--"):
        comment, end, _ = header.partition("-->")
        if end and (
            (re.search(r"^generated:\s*true\s*$", comment, re.MULTILINE)
             and re.search(r"^generator:\s*\S+", comment, re.MULTILINE))
            or re.match(r"<!--\s*Generated by\s+\S+.*Do not edit", comment, re.IGNORECASE | re.DOTALL)
        ):
            return "generated header"
    first_line = header.partition("\n")[0].strip()
    if (re.match(r"^(?:>\s*|[_*])Auto-generated\b", first_line, re.IGNORECASE)
            or re.match(r"^This file is generated from `[^`]+`", first_line)):
        return "generated header"
    return None


def check_front_matter(docs_dir: Path, github_actions: bool, strict: bool) -> list[str]:
    """Check lifecycle metadata in all non-hidden top-level docs directories."""
    check_dirs = [d for d in docs_dir.iterdir() if d.is_dir() and not d.name.startswith(".")]
    return check_front_matter_in_dirs(docs_dir, check_dirs, github_actions, strict)


def resolve_top_level_scope(docs_dir: Path, names: list[str], github_actions: bool) -> tuple[list[Path], list[str]]:
    """Resolve requested top-level folders for scoped front-matter checks."""
    errors: list[str] = []
    scoped_dirs: list[Path] = []
    seen: set[str] = set()

    for raw_name in names:
        name = raw_name.strip().strip("/\\")
        if not name or name in seen:
            continue
        seen.add(name)
        scope_dir = docs_dir / name
        if not scope_dir.is_dir():
            msg = f"Requested top-level docs folder docs/{name}/ does not exist."
            emit("error", str(scope_dir), msg, github_actions)
            errors.append(msg)
            continue
        scoped_dirs.append(scope_dir)

    return scoped_dirs, errors


def check_front_matter_in_dirs(
    docs_dir: Path,
    markdown_dirs: list[Path],
    github_actions: bool,
    strict: bool,
) -> list[str]:
    """
    Check that hand-authored markdown files in the provided directories have lifecycle front matter.
    Returns list of warning/error messages.
    """
    issues = []
    today = date.today()
    outputs = automation_outputs(docs_dir.parent)
    generated: Counter[str] = Counter()
    checked = 0
    entrypoints = 0

    for subdir in sorted(markdown_dirs):
        for md_file in sorted(subdir.rglob("*.md")):
            if md_file.name.lower() in {r.lower() for r in README_EXEMPT_FILES}:
                entrypoints += 1
                continue
            content = md_file.read_text(encoding="utf-8", errors="replace")
            ownership = generated_ownership(md_file, docs_dir, content, outputs)
            if ownership:
                generated[ownership] += 1
                continue

            checked += 1
            rel = md_file.relative_to(docs_dir.parent).as_posix()
            try:
                fields = extract_lifecycle_header(content)
            except ValueError as exc:
                msg = f"Invalid lifecycle header in {rel}: {exc}"
                emit("warning", str(rel), msg, github_actions)
                issues.append(msg)
                continue

            for field in ENCOURAGED_FIELDS:
                value = fields.get(field.casefold()) or None
                if value is None:
                    msg = f"Missing front-matter field '**{field}:**' in {rel}"
                    emit("warning", str(rel), msg, github_actions)
                    issues.append(msg)
                elif field == "Reviewed":
                    try:
                        if not re.fullmatch(r"\d{4}-\d{2}-\d{2}", value):
                            raise ValueError("Expected YYYY-MM-DD")
                        reviewed_date = datetime.strptime(value, "%Y-%m-%d").date()
                        age = (today - reviewed_date).days
                        if age > STALE_DAYS:
                            msg = (f"Stale document in {rel}: "
                                   f"'Reviewed: {value}' is {age} days old (threshold: {STALE_DAYS})")
                            emit("warning", str(rel), msg, github_actions)
                            issues.append(msg)
                    except ValueError:
                        msg = f"Invalid Reviewed date '{value}' in {rel}; expected YYYY-MM-DD"
                        emit("warning", str(rel), msg, github_actions)
                        issues.append(msg)

    print(f"  Lifecycle scope: {checked} hand-authored document(s) checked; "
          f"{sum(generated.values())} generated output(s) excluded; "
          f"{entrypoints} entrypoint/meta file(s) excluded.")
    if generated:
        print("  Generated ownership: " + "; ".join(
            f"{reason}: {count}" for reason, count in sorted(generated.items())
        ))
    return issues


def main() -> int:
    args = parse_args()
    docs_dir = Path(args.docs_dir)

    if not docs_dir.is_dir():
        print(f"ERROR: docs directory not found: {docs_dir}", file=sys.stderr)
        return 1

    print(f"Validating docs structure in: {docs_dir.resolve()}")
    print()

    scoped_dirs: list[Path] = []
    scope_errors: list[str] = []
    if args.top_level:
        scoped_dirs, scope_errors = resolve_top_level_scope(docs_dir, args.top_level, args.github_actions)
        scope_labels = ", ".join(path.name for path in scoped_dirs) if scoped_dirs else ", ".join(args.top_level)
        print(f"Scoped lifecycle validation for: {scope_labels}")
        print()

    readme_errors: list[str] = []
    canonical_warnings: list[str] = []
    top_level_errors: list[str] = []
    top_level_warnings: list[str] = []

    if args.top_level:
        if scope_errors:
            print(f"  Found {len(scope_errors)} scope error(s) — see above for details.")
            print()
        else:
            print("Scoped mode skips global top-level README and taxonomy checks.")
            print()
    else:
        # ── Check 1: README presence ──────────────────────────────────────────
        print("Check 1: README.md present in every top-level subdirectory")
        readme_errors = check_readme_exists(docs_dir, args.github_actions)
        if not readme_errors:
            print("  OK All subdirectories have README.md")
        print()

        # ── Check 1b: Canonical documentation model ─────────────────────────
        print("Check 1b: Canonical documentation folders are present")
        canonical_warnings = check_canonical_model(docs_dir, args.github_actions)
        if not canonical_warnings:
            print("  OK Canonical rebuild folders are present")
        else:
            print(f"  Found {len(canonical_warnings)} canonical-model warning(s) — see above for details.")
        print()

        # ── Check 1c: Top-level taxonomy guard ───────────────────────────────
        print("Check 1c: Top-level docs folders match the current model")
        top_level_errors, top_level_warnings = check_top_level_model(docs_dir, args.github_actions)
        if not top_level_errors and not top_level_warnings:
            print("  OK Top-level docs folders match canonical and supporting lanes")
        else:
            print(
                f"  Found {len(top_level_errors)} top-level folder error(s), "
                f"{len(top_level_warnings)} transition warning(s) — see above for details."
            )
        print()

    # ── Check 2: Front-matter lifecycle fields ────────────────────────────
    print("Check 2: Lifecycle front-matter fields (Status, Owner, Reviewed)")
    print("         Note: These are encouraged, not required — warnings only.")
    if args.top_level:
        fm_issues = check_front_matter_in_dirs(docs_dir, scoped_dirs, args.github_actions, args.strict)
    else:
        fm_issues = check_front_matter(docs_dir, args.github_actions, args.strict)
    if not fm_issues:
        print("  OK All checked files have lifecycle front matter")
    else:
        print(f"  Found {len(fm_issues)} front-matter warning(s) — see above for details.")
    print()

    # ── Summary ───────────────────────────────────────────────────────────
    has_errors = bool(readme_errors or top_level_errors or scope_errors)
    has_warnings = bool(fm_issues or canonical_warnings or top_level_warnings)

    if args.strict and has_warnings:
        has_errors = True

    if has_errors:
        print(
            f"FAILED: {len(readme_errors) + len(top_level_errors) + len(scope_errors)} error(s), "
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
