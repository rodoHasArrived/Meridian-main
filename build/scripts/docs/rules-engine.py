#!/usr/bin/env python3
"""Documentation validation rules engine for Meridian.

Loads validation rules from a YAML configuration file and checks documentation
files in the repository against those rules. Produces a Markdown report of
violations and passes.

Supported rule types:
    - must_contain:       file must contain a literal string or regex pattern
    - must_not_contain:   file must NOT contain a literal string or regex pattern
    - must_match:         file must have at least one line matching a regex
    - required_sections:  file must contain specific Markdown headings (any level)

Usage:
    python3 rules-engine.py --rules build/rules/doc-rules.yaml --root . --output report.md
"""

from __future__ import annotations

import argparse
import glob
import re
import sys
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


# ---------------------------------------------------------------------------
# Minimal YAML subset parser (stdlib-only fallback)
# ---------------------------------------------------------------------------

def _try_import_yaml() -> Any:
    """Attempt to import PyYAML; return the module or None."""
    try:
        import yaml  # type: ignore[import-untyped]
        return yaml
    except ImportError:
        return None


def _strip_yaml_quotes(value: str) -> str:
    """Remove surrounding single or double quotes from a YAML value."""
    value = value.strip()
    if len(value) >= 2 and value[0] == value[-1] and value[0] in ('"', "'"):
        return value[1:-1]
    return value


def _parse_yaml_value(raw: str) -> str | int | float | bool | None:
    """Coerce a scalar YAML value to a Python type."""
    stripped = raw.strip()
    if stripped in ("~", "null", ""):
        return None
    if stripped in ("true", "True", "yes", "Yes", "on", "On"):
        return True
    if stripped in ("false", "False", "no", "No", "off", "Off"):
        return False
    try:
        return int(stripped)
    except ValueError:
        pass
    try:
        return float(stripped)
    except ValueError:
        pass
    return _strip_yaml_quotes(stripped)


def _indent_level(line: str) -> int:
    """Return the number of leading spaces in *line*."""
    return len(line) - len(line.lstrip(" "))


def _minimal_yaml_load(text: str) -> Any:
    """Parse a minimal subset of YAML used by the rules files.

    Supports:
      - Top-level mapping
      - Sequences (``- item``)
      - Nested mappings inside sequences
      - Scalar values (string, int, float, bool, null)
      - Inline list items starting with ``- ``
      - Quoted strings

    This is intentionally limited and designed for the ``doc-rules.yaml``
    schema only.  For anything more complex, install PyYAML.
    """
    lines: list[str] = text.splitlines()
    return _parse_block(lines, 0, 0)[0]


def _parse_block(lines: list[str], idx: int, base_indent: int) -> tuple[Any, int]:
    """Recursively parse a YAML block starting at *idx*."""
    if idx >= len(lines):
        return None, idx

    # Skip blanks / comments to find the first meaningful line
    while idx < len(lines) and (lines[idx].strip() == "" or lines[idx].strip().startswith("#")):
        idx += 1
    if idx >= len(lines):
        return None, idx

    first = lines[idx]
    stripped = first.lstrip(" ")

    # Sequence item?
    if stripped.startswith("- "):
        return _parse_sequence(lines, idx, _indent_level(first))

    # Mapping?
    if ":" in stripped:
        return _parse_mapping(lines, idx, _indent_level(first))

    # Scalar
    return _parse_yaml_value(stripped), idx + 1


def _parse_sequence(lines: list[str], idx: int, base_indent: int) -> tuple[list[Any], int]:  # noqa: C901
    """Parse a YAML sequence (list)."""
    result: list[Any] = []

    while idx < len(lines):
        line = lines[idx]
        if line.strip() == "" or line.strip().startswith("#"):
            idx += 1
            continue

        indent = _indent_level(line)
        if indent < base_indent:
            break

        stripped = line.lstrip(" ")
        if not stripped.startswith("- "):
            if indent == base_indent:
                break
            idx += 1
            continue

        if indent != base_indent:
            if indent < base_indent:
                break
            idx += 1
            continue

        # Remove the "- " prefix
        after_dash = stripped[2:]

        # Check if the item is a mapping on the same line (e.g., ``- name: foo``)
        if ":" in after_dash and not after_dash.startswith('"') and not after_dash.startswith("'"):
            # Inline mapping — rewrite so we can parse normally
            inner_indent = indent + 2
            rewritten_lines: list[str] = [" " * inner_indent + after_dash]
            peek = idx + 1
            while peek < len(lines):
                pline = lines[peek]
                if pline.strip() == "" or pline.strip().startswith("#"):
                    peek += 1
                    continue
                pindent = _indent_level(pline)
                if pindent <= indent:
                    break
                rewritten_lines.append(pline)
                peek += 1
            item, _ = _parse_mapping(rewritten_lines, 0, inner_indent)
            result.append(item)
            idx = peek
        else:
            # Simple scalar item
            result.append(_parse_yaml_value(after_dash))
            idx += 1

    return result, idx


def _parse_mapping(lines: list[str], idx: int, base_indent: int) -> tuple[dict[str, Any], int]:
    """Parse a YAML mapping (dict)."""
    result: dict[str, Any] = {}

    while idx < len(lines):
        line = lines[idx]
        if line.strip() == "" or line.strip().startswith("#"):
            idx += 1
            continue

        indent = _indent_level(line)
        if indent < base_indent:
            break
        if indent > base_indent:
            idx += 1
            continue

        stripped = line.lstrip(" ")
        colon_idx = stripped.find(":")
        if colon_idx == -1:
            idx += 1
            continue

        key = stripped[:colon_idx].strip()
        key = _strip_yaml_quotes(key)
        rest = stripped[colon_idx + 1:].strip()

        if rest:
            result[key] = _parse_yaml_value(rest)
            idx += 1
        else:
            # Value is on the next line(s)
            idx += 1
            # Determine child indent
            child_idx = idx
            while child_idx < len(lines) and (
                lines[child_idx].strip() == "" or lines[child_idx].strip().startswith("#")
            ):
                child_idx += 1
            if child_idx < len(lines):
                child_indent = _indent_level(lines[child_idx])
                if child_indent > base_indent:
                    child_val, idx = _parse_block(lines, child_idx, child_indent)
                    result[key] = child_val
                else:
                    result[key] = None
            else:
                result[key] = None

    return result, idx


def load_yaml(path: Path) -> Any:
    """Load a YAML file, using PyYAML if available, else the minimal parser."""
    text = path.read_text(encoding="utf-8")
    yaml_mod = _try_import_yaml()
    if yaml_mod is not None:
        return yaml_mod.safe_load(text)
    return _minimal_yaml_load(text)


# ---------------------------------------------------------------------------
# Data model
# ---------------------------------------------------------------------------

@dataclass
class Rule:
    """A single documentation validation rule."""

    name: str
    applies_to: str
    severity: str  # "error", "warning", "info"
    # Exactly one of the following should be set:
    must_contain: str | None = None
    must_not_contain: str | None = None
    must_match: str | None = None
    required_sections: list[str] = field(default_factory=list)


@dataclass
class Violation:
    """A single rule violation."""

    file: str
    rule_name: str
    severity: str
    suggestion: str


@dataclass
class PassedCheck:
    """A rule that passed for a given file."""

    file: str
    rule_name: str
    severity: str


# ---------------------------------------------------------------------------
# Rule loading
# ---------------------------------------------------------------------------

def load_rules(path: Path) -> list[Rule]:  # noqa: C901
    """Load rules from a YAML file and return a list of ``Rule`` objects.

    Raises ``SystemExit`` with a user-friendly message on parse errors.
    """
    try:
        data = load_yaml(path)
    except Exception as exc:
        print(f"Error: failed to parse rules file {path}: {exc}", file=sys.stderr)
        sys.exit(1)

    if not isinstance(data, dict) or "rules" not in data:
        print(f"Error: rules file {path} must have a top-level 'rules' key.", file=sys.stderr)
        sys.exit(1)

    raw_rules = data["rules"]
    if not isinstance(raw_rules, list):
        print(f"Error: 'rules' must be a list in {path}.", file=sys.stderr)
        sys.exit(1)

    rules: list[Rule] = []
    for idx, entry in enumerate(raw_rules):
        if not isinstance(entry, dict):
            print(f"Warning: skipping non-mapping rule entry at index {idx}.", file=sys.stderr)
            continue

        name = str(entry.get("name", f"rule-{idx}"))
        applies_to = str(entry.get("applies_to", "**/*"))
        severity = str(entry.get("severity", "warning")).lower()
        if severity not in ("error", "warning", "info"):
            print(
                f"Warning: unknown severity '{severity}' in rule '{name}', defaulting to 'warning'.",
                file=sys.stderr,
            )
            severity = "warning"

        must_contain = entry.get("must_contain")
        must_not_contain = entry.get("must_not_contain")
        must_match = entry.get("must_match")
        required_sections = entry.get("required_sections")

        if must_contain is not None:
            must_contain = str(must_contain)
        if must_not_contain is not None:
            must_not_contain = str(must_not_contain)
        if must_match is not None:
            must_match = str(must_match)
        if required_sections is not None:
            if not isinstance(required_sections, list):
                required_sections = [str(required_sections)]
            else:
                required_sections = [str(s) for s in required_sections]
        else:
            required_sections = []

        rules.append(
            Rule(
                name=name,
                applies_to=applies_to,
                severity=severity,
                must_contain=must_contain,
                must_not_contain=must_not_contain,
                must_match=must_match,
                required_sections=required_sections,
            )
        )

    return rules


# ---------------------------------------------------------------------------
# File matching
# ---------------------------------------------------------------------------

def resolve_files(pattern: str, root: Path) -> list[Path]:
    """Resolve a glob *pattern* relative to *root* and return matching paths.

    Uses ``glob.glob`` with ``recursive=True`` for ``**`` support.
    """
    full_pattern = str(root / pattern)
    matches = sorted(glob.glob(full_pattern, recursive=True))
    return [Path(m) for m in matches if Path(m).is_file()]


# ---------------------------------------------------------------------------
# Rule evaluation
# ---------------------------------------------------------------------------

def _read_file(path: Path) -> str | None:
    """Read file contents, returning ``None`` on failure."""
    try:
        return path.read_text(encoding="utf-8", errors="replace")
    except OSError as exc:
        print(f"Warning: could not read {path}: {exc}", file=sys.stderr)
        return None


def _extract_headings(content: str) -> list[str]:
    """Extract Markdown headings (any level) from *content*."""
    headings: list[str] = []
    for line in content.splitlines():
        stripped = line.strip()
        match = re.match(r"^#{1,6}\s+(.+?)(?:\s+#+\s*)?$", stripped)
        if match:
            headings.append(match.group(1).strip())
    return headings


def evaluate_rule(  # noqa: C901
    rule: Rule,
    root: Path,
    include_paths: set[str] | None = None,
) -> tuple[list[Violation], list[PassedCheck]]:
    """Evaluate a single *rule* against matching files under *root*.

    Returns a tuple of (violations, passes).
    """
    violations: list[Violation] = []
    passes: list[PassedCheck] = []

    files = resolve_files(rule.applies_to, root)
    if include_paths is not None:
        files = [f for f in files if str(f.relative_to(root)).replace("\\", "/") in include_paths]
    if not files:
        return violations, passes

    for fpath in files:
        content = _read_file(fpath)
        if content is None:
            continue

        rel = str(fpath.relative_to(root))
        failed = False

        # --- must_contain ---
        if rule.must_contain is not None:
            try:
                found = re.search(rule.must_contain, content) is not None
            except re.error:
                found = rule.must_contain in content
            if not found:
                violations.append(
                    Violation(
                        file=rel,
                        rule_name=rule.name,
                        severity=rule.severity,
                        suggestion=f"File should contain: `{rule.must_contain}`",
                    )
                )
                failed = True

        # --- must_not_contain ---
        if rule.must_not_contain is not None:
            try:
                found = re.search(rule.must_not_contain, content) is not None
            except re.error:
                found = rule.must_not_contain in content
            if found:
                violations.append(
                    Violation(
                        file=rel,
                        rule_name=rule.name,
                        severity=rule.severity,
                        suggestion=f"File should NOT contain: `{rule.must_not_contain}`",
                    )
                )
                failed = True

        # --- must_match ---
        if rule.must_match is not None:
            matched = False
            for line in content.splitlines():
                try:
                    if re.search(rule.must_match, line):
                        matched = True
                        break
                except re.error:
                    if rule.must_match in line:
                        matched = True
                        break
            if not matched:
                violations.append(
                    Violation(
                        file=rel,
                        rule_name=rule.name,
                        severity=rule.severity,
                        suggestion=f"File should have a line matching: `{rule.must_match}`",
                    )
                )
                failed = True

        # --- required_sections ---
        if rule.required_sections:
            headings = _extract_headings(content)
            heading_texts_lower = [h.lower() for h in headings]
            for section in rule.required_sections:
                if section.lower() not in heading_texts_lower:
                    violations.append(
                        Violation(
                            file=rel,
                            rule_name=rule.name,
                            severity=rule.severity,
                            suggestion=f"Missing required section heading: `{section}`",
                        )
                    )
                    failed = True

        if not failed:
            passes.append(PassedCheck(file=rel, rule_name=rule.name, severity=rule.severity))

    return violations, passes


# ---------------------------------------------------------------------------
# Report generation
# ---------------------------------------------------------------------------

_SEVERITY_ICON = {
    "error": "X",
    "warning": "!",
    "info": "i",
}


def _severity_badge(severity: str) -> str:
    """Return a text badge for the given severity."""
    return f"**{severity.upper()}**"


def generate_report(
    violations: list[Violation],
    passes: list[PassedCheck],
    rules: list[Rule],
) -> str:
    """Generate a Markdown report from evaluation results."""
    total_checks = len(violations) + len(passes)
    error_count = sum(1 for v in violations if v.severity == "error")
    warning_count = sum(1 for v in violations if v.severity == "warning")
    info_count = sum(1 for v in violations if v.severity == "info")

    lines: list[str] = []

    lines.append("# Documentation Rules Report")
    lines.append("")
    now_utc = datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M:%S UTC")
    lines.append(f"*Generated: {now_utc}*")
    lines.append("")

    # Summary
    lines.append("## Summary")
    lines.append("")
    lines.append("| Metric | Count |")
    lines.append("|--------|-------|")
    lines.append(f"| Rules loaded | {len(rules)} |")
    lines.append(f"| Total file checks | {total_checks} |")
    lines.append(f"| Passed | {len(passes)} |")
    lines.append(f"| Failed | {len(violations)} |")
    lines.append(f"| Errors | {error_count} |")
    lines.append(f"| Warnings | {warning_count} |")
    lines.append(f"| Info | {info_count} |")
    lines.append("")

    if error_count > 0:
        lines.append(f"**Result: FAIL** ({error_count} error(s) found)")
    elif warning_count > 0:
        lines.append(f"**Result: PASS with warnings** ({warning_count} warning(s))")
    else:
        lines.append("**Result: PASS**")
    lines.append("")

    # Violations table
    if violations:
        lines.append("## Violations")
        lines.append("")
        lines.append("| Severity | File | Rule | Suggestion |")
        lines.append("|----------|------|------|------------|")
        for v in sorted(violations, key=lambda x: ({"error": 0, "warning": 1, "info": 2}[x.severity], x.file)):
            lines.append(
                f"| {_severity_badge(v.severity)} | `{v.file}` | {v.rule_name} | {v.suggestion} |"
            )
        lines.append("")

    # Passed table
    if passes:
        lines.append("## Passed Checks")
        lines.append("")
        lines.append("| File | Rule | Severity |")
        lines.append("|------|------|----------|")
        for p in sorted(passes, key=lambda x: (x.file, x.rule_name)):
            lines.append(f"| `{p.file}` | {p.rule_name} | {p.severity} |")
        lines.append("")

    return "\n".join(lines)


def generate_summary(violations: list[Violation], passes: list[PassedCheck]) -> str:
    """Generate a concise text summary suitable for ``GITHUB_STEP_SUMMARY``."""
    total = len(violations) + len(passes)
    error_count = sum(1 for v in violations if v.severity == "error")
    warning_count = sum(1 for v in violations if v.severity == "warning")
    info_count = sum(1 for v in violations if v.severity == "info")

    parts: list[str] = []
    parts.append(f"Doc rules: {total} checks, {len(passes)} passed, {len(violations)} failed")
    if error_count:
        parts.append(f"  Errors:   {error_count}")
    if warning_count:
        parts.append(f"  Warnings: {warning_count}")
    if info_count:
        parts.append(f"  Info:     {info_count}")

    if error_count > 0:
        parts.append("Status: FAIL")
    elif warning_count > 0:
        parts.append("Status: PASS (with warnings)")
    else:
        parts.append("Status: PASS")

    return "\n".join(parts)


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------

def build_parser() -> argparse.ArgumentParser:
    """Build and return the argument parser."""
    parser = argparse.ArgumentParser(
        description="Documentation validation rules engine for Meridian.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=(
            "Examples:\n"
            "  python3 rules-engine.py --rules build/rules/doc-rules.yaml\n"
            "  python3 rules-engine.py --rules build/rules/doc-rules.yaml --output report.md --summary\n"
        ),
    )
    parser.add_argument(
        "--rules",
        required=True,
        type=Path,
        help="Path to the YAML rules file.",
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=None,
        help="Path to write the Markdown report. If omitted, report is printed to stdout.",
    )
    parser.add_argument(
        "--root",
        type=Path,
        default=Path("."),
        help="Repository root directory (default: current directory).",
    )
    parser.add_argument(
        "--summary",
        action="store_true",
        default=False,
        help="Print a concise summary to stdout (for GITHUB_STEP_SUMMARY).",
    )
    parser.add_argument(
        "--paths-file",
        type=Path,
        default=None,
        help=(
            "Optional newline-delimited file containing repository-relative file paths "
            "to evaluate. When provided, only matching files are checked."
        ),
    )
    return parser


def main(argv: list[str] | None = None) -> int:  # noqa: C901
    """Entry point. Returns 0 on success, 1 if any error-level violations exist."""
    parser = build_parser()
    args = parser.parse_args(argv)

    root: Path = args.root.resolve()
    rules_path: Path = args.rules
    if not rules_path.is_absolute():
        rules_path = root / rules_path
    rules_path = rules_path.resolve()

    if not rules_path.is_file():
        print(f"Error: rules file not found: {rules_path}", file=sys.stderr)
        return 1

    if not root.is_dir():
        print(f"Error: root directory not found: {root}", file=sys.stderr)
        return 1

    include_paths: set[str] | None = None
    if args.paths_file is not None:
        paths_file: Path = args.paths_file
        if not paths_file.is_absolute():
            paths_file = root / paths_file
        paths_file = paths_file.resolve()
        if not paths_file.is_file():
            print(f"Error: paths file not found: {paths_file}", file=sys.stderr)
            return 1
        include_paths = {
            line.strip().replace("\\", "/")
            for line in paths_file.read_text(encoding="utf-8").splitlines()
            if line.strip()
        }

    # Load rules
    rules = load_rules(rules_path)
    if not rules:
        print("Warning: no rules loaded.", file=sys.stderr)

    # Evaluate
    all_violations: list[Violation] = []
    all_passes: list[PassedCheck] = []

    for rule in rules:
        violations, passes = evaluate_rule(rule, root, include_paths=include_paths)
        all_violations.extend(violations)
        all_passes.extend(passes)

    # Generate report
    report = generate_report(all_violations, all_passes, rules)

    if args.output:
        output_path: Path = args.output
        if not output_path.is_absolute():
            output_path = root / output_path
        try:
            output_path.parent.mkdir(parents=True, exist_ok=True)
            output_path.write_text(report, encoding="utf-8")
            print(f"Report written to {output_path}", file=sys.stderr)
        except OSError as exc:
            print(f"Error: could not write report to {output_path}: {exc}", file=sys.stderr)
            return 1
    else:
        print(report)

    # Summary for CI
    if args.summary:
        summary = generate_summary(all_violations, all_passes)
        print(summary)

    # Exit code
    has_errors = any(v.severity == "error" for v in all_violations)
    return 1 if has_errors else 0


if __name__ == "__main__":
    sys.exit(main())
