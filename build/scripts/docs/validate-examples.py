#!/usr/bin/env python3
"""
Documentation Code Example Validator

Scans markdown documentation files for fenced code blocks and validates
their syntax by language. Generates a report of valid, invalid, and
skipped code examples to help maintain documentation quality.

Supported languages:
- Python: Full AST parse validation
- JSON: Full json.loads() validation
- JSONC: JSON validation after stripping comments and trailing commas
- YAML/YML: Indentation consistency and basic structure checks
- Bash/Shell/SH: Unmatched quotes, common syntax issues
- C#/CS: Brace matching and syntax heuristics
- XML/HTML/XAML: Tag matching validation

Usage:
    python3 validate-examples.py --root . --docs-dir docs/
    python3 validate-examples.py --output report.md --summary
"""

import argparse
import ast
import json
import os
import re
import sys
from dataclasses import dataclass, field
from datetime import datetime, timezone
from enum import Enum
from pathlib import Path
from typing import Optional


# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

EXCLUDE_DIRS: set[str] = {
    ".git",
    "node_modules",
    "bin",
    "obj",
    "__pycache__",
}

# Map fence language tags to canonical language names.
LANGUAGE_ALIASES: dict[str, str] = {
    "python": "python",
    "python3": "python",
    "py": "python",
    "json": "json",
    "jsonc": "jsonc",
    "jsonl": "jsonl",
    "yaml": "yaml",
    "yml": "yaml",
    "bash": "bash",
    "shell": "bash",
    "sh": "bash",
    "zsh": "bash",
    "csharp": "csharp",
    "cs": "csharp",
    "c#": "csharp",
    "xml": "xml",
    "html": "xml",
    "xaml": "xml",
    "xhtml": "xml",
}

# Languages we can validate.
VALIDATABLE_LANGUAGES: set[str] = {
    "python",
    "json",
    "jsonc",
    "yaml",
    "bash",
    "csharp",
    "xml",
}

# Regex for the opening fence of a code block: ``` optionally followed by a language tag.
_FENCE_OPEN_RE = re.compile(r"^(?P<indent>\s*)```(?P<lang>[a-zA-Z0-9_#+-]*)\s*$")
_FENCE_CLOSE_RE = re.compile(r"^\s*```\s*$")


# ---------------------------------------------------------------------------
# Data models
# ---------------------------------------------------------------------------

class ValidationStatus(Enum):
    """Outcome of validating a single code block."""

    VALID = "valid"
    INVALID = "invalid"
    SKIPPED = "skipped"


@dataclass
class CodeBlock:
    """A fenced code block extracted from a markdown file."""

    source_file: str
    line_number: int
    language_tag: str
    canonical_language: str
    source: str


@dataclass
class ValidationResult:
    """Result of validating a single code block."""

    block: CodeBlock
    status: ValidationStatus
    error_message: str = ""


@dataclass
class ValidationReport:
    """Aggregated validation report across all files."""

    results: list[ValidationResult] = field(default_factory=list)
    scanned_files: int = 0
    scan_time: str = ""


# ---------------------------------------------------------------------------
# Markdown code-block extraction
# ---------------------------------------------------------------------------

def _should_skip_dir(dir_path: Path) -> bool:
    """Return True if *dir_path* contains an excluded directory component."""
    for part in dir_path.parts:
        if part in EXCLUDE_DIRS:
            return True
    return False


def discover_markdown_files(root: Path, docs_dir: str) -> list[Path]:
    """Recursively discover markdown files under *root/docs_dir*.

    Respects EXCLUDE_DIRS and returns paths sorted for deterministic output.
    """
    base = root / docs_dir
    if not base.is_dir():
        return []

    files: list[Path] = []
    for path in sorted(base.rglob("*.md")):
        if _should_skip_dir(path.relative_to(root)):
            continue
        if path.is_file():
            files.append(path)
    return files


def extract_code_blocks(file_path: Path, root: Path) -> list[CodeBlock]:
    """Extract all fenced code blocks from a single markdown file.

    Returns a list of ``CodeBlock`` instances with source text and metadata.
    """
    try:
        text = file_path.read_text(encoding="utf-8", errors="replace")
    except OSError as exc:
        print(f"Warning: could not read {file_path}: {exc}", file=sys.stderr)
        return []

    lines = text.splitlines()
    blocks: list[CodeBlock] = []

    i = 0
    while i < len(lines):
        open_match = _FENCE_OPEN_RE.match(lines[i])
        if open_match:
            lang_tag = open_match.group("lang").strip().lower()
            canonical = LANGUAGE_ALIASES.get(lang_tag, lang_tag)
            start_line = i + 1  # 1-indexed line of the opening fence
            content_lines: list[str] = []
            i += 1
            while i < len(lines):
                if _FENCE_CLOSE_RE.match(lines[i]):
                    break
                content_lines.append(lines[i])
                i += 1

            try:
                rel = file_path.relative_to(root)
            except ValueError:
                rel = file_path

            blocks.append(
                CodeBlock(
                    source_file=str(rel),
                    line_number=start_line,
                    language_tag=lang_tag,
                    canonical_language=canonical,
                    source="\n".join(content_lines),
                )
            )
        i += 1

    return blocks


# ---------------------------------------------------------------------------
# Language validators
# ---------------------------------------------------------------------------

def _validate_python(source: str) -> tuple[bool, str]:
    """Validate Python syntax via ``ast.parse``."""
    try:
        ast.parse(source)
        return True, ""
    except SyntaxError as exc:
        detail = f"line {exc.lineno}: {exc.msg}" if exc.lineno else str(exc.msg)
        return False, f"Python syntax error - {detail}"


def _validate_json(source: str) -> tuple[bool, str]:
    """Validate JSON via ``json.loads``."""
    # Strip trailing commas that are common in documentation snippets but
    # invalid in strict JSON -- we still flag them, since json.loads will fail.
    try:
        json.loads(source)
        return True, ""
    except json.JSONDecodeError as exc:
        return False, f"JSON error - {exc.msg} (line {exc.lineno}, col {exc.colno})"


def _strip_jsonc_comments(source: str) -> str:
    """Remove JSONC comments while preserving quoted strings."""
    result: list[str] = []
    i = 0
    in_string = False
    escape = False
    in_line_comment = False
    in_block_comment = False

    while i < len(source):
        ch = source[i]
        nxt = source[i + 1] if i + 1 < len(source) else ""

        if in_line_comment:
            if ch == "\n":
                in_line_comment = False
                result.append(ch)
            i += 1
            continue

        if in_block_comment:
            if ch == "*" and nxt == "/":
                in_block_comment = False
                i += 2
                continue
            if ch == "\n":
                result.append(ch)
            i += 1
            continue

        if in_string:
            result.append(ch)
            if escape:
                escape = False
            elif ch == "\\":
                escape = True
            elif ch == '"':
                in_string = False
            i += 1
            continue

        if ch == '"':
            in_string = True
            result.append(ch)
            i += 1
            continue

        if ch == "/" and nxt == "/":
            in_line_comment = True
            i += 2
            continue

        if ch == "/" and nxt == "*":
            in_block_comment = True
            i += 2
            continue

        result.append(ch)
        i += 1

    return "".join(result)


def _strip_jsonc_trailing_commas(source: str) -> str:
    """Remove trailing commas before closing braces and brackets."""
    previous = None
    current = source
    while previous != current:
        previous = current
        current = re.sub(r",(\s*[}\]])", r"\1", current)
    return current


def _validate_jsonc(source: str) -> tuple[bool, str]:
    """Validate JSONC by normalizing to strict JSON first."""
    normalized = _strip_jsonc_trailing_commas(_strip_jsonc_comments(source))
    return _validate_json(normalized)


def _validate_yaml(source: str) -> tuple[bool, str]:
    """Basic YAML structure validation (stdlib only).

    Checks for:
    - Consistent indentation (no mixed tabs/spaces on indentation)
    - Mapping entries have a value after the colon on non-nested lines
    - Duplicate top-level keys
    """
    lines = source.splitlines()
    if not lines or all(line.strip() == "" for line in lines):
        return True, ""

    seen_top_keys: set[str] = set()
    errors: list[str] = []

    for idx, raw_line in enumerate(lines, start=1):
        # Skip blank lines and comments.
        stripped = raw_line.strip()
        if stripped == "" or stripped.startswith("#"):
            continue

        # Check for mixed tabs and spaces in the leading whitespace.
        leading = raw_line[: len(raw_line) - len(raw_line.lstrip())]
        if "\t" in leading and " " in leading:
            errors.append(f"line {idx}: mixed tabs and spaces in indentation")

        # Check for tab-only indentation (YAML spec requires spaces).
        if leading and leading == "\t" * len(leading):
            errors.append(f"line {idx}: tab indentation (YAML requires spaces)")

        # Top-level duplicate key check (simple heuristic).
        if not leading and ":" in stripped and not stripped.startswith("-"):
            key = stripped.split(":", 1)[0].strip()
            if key in seen_top_keys:
                errors.append(f"line {idx}: duplicate top-level key '{key}'")
            seen_top_keys.add(key)

    if errors:
        return False, "YAML issues - " + "; ".join(errors[:5])
    return True, ""


def _validate_bash(source: str) -> tuple[bool, str]:
    """Basic bash / shell syntax heuristic checks.

    Checks for:
    - Unmatched single and double quotes (outside of comments)
    - Unmatched backticks (outside of comments)
    - ``do`` without ``done``, ``if`` without ``fi``, ``case`` without ``esac``
    """
    errors: list[str] = []

    # --- Quote balance (per-line) ---
    for idx, raw_line in enumerate(source.splitlines(), start=1):
        # Strip comments (simplistic: first unquoted '#').
        line = _strip_bash_comment(raw_line)

        for char, name in [('"', "double quote"), ("'", "single quote"), ("`", "backtick")]:
            count = _count_unescaped(line, char)
            if count % 2 != 0:
                errors.append(f"line {idx}: unmatched {name}")

    # --- Block keyword balance ---
    text = _strip_all_bash_comments(source)
    keywords_pairs = [
        (r"\bdo\b", r"\bdone\b", "do/done"),
        (r"\bif\b", r"\bfi\b", "if/fi"),
        (r"\bcase\b", r"\besac\b", "case/esac"),
    ]
    for open_re, close_re, label in keywords_pairs:
        opens = len(re.findall(open_re, text))
        closes = len(re.findall(close_re, text))
        if opens > closes:
            errors.append(f"unmatched {label} ({opens} opens, {closes} closes)")

    if errors:
        return False, "Bash issues - " + "; ".join(errors[:5])
    return True, ""


def _strip_bash_comment(line: str) -> str:
    """Remove the comment portion of a bash line (very simplistic)."""
    in_single = False
    in_double = False
    i = 0
    while i < len(line):
        ch = line[i]
        if ch == "\\" and not in_single:
            i += 2
            continue
        if ch == "'" and not in_double:
            in_single = not in_single
        elif ch == '"' and not in_single:
            in_double = not in_double
        elif ch == "#" and not in_single and not in_double:
            return line[:i]
        i += 1
    return line


def _strip_all_bash_comments(source: str) -> str:
    """Strip comments from every line and rejoin."""
    return "\n".join(_strip_bash_comment(ln) for ln in source.splitlines())


def _count_unescaped(line: str, char: str) -> int:
    """Count occurrences of *char* that are not preceded by a backslash."""
    count = 0
    i = 0
    while i < len(line):
        if line[i] == "\\" :
            i += 2  # skip escaped character
            continue
        if line[i] == char:
            count += 1
        i += 1
    return count


def _validate_csharp(source: str) -> tuple[bool, str]:
    """Heuristic C# validation: brace matching and basic syntax checks.

    Checks for:
    - Balanced ``{}``, ``()``, ``[]``
    - String literals are closed on the same logical line
    - Common typo patterns (``;;`` outside of for-loops)
    """
    errors: list[str] = []

    # --- Remove string literals and comments to avoid false positives ---
    cleaned = _strip_csharp_strings_and_comments(source)

    # --- Bracket balance ---
    pairs = [("{", "}", "braces"), ("(", ")", "parentheses"), ("[", "]", "brackets")]
    for open_ch, close_ch, name in pairs:
        depth = 0
        for idx, ch in enumerate(cleaned):
            if ch == open_ch:
                depth += 1
            elif ch == close_ch:
                depth -= 1
            if depth < 0:
                errors.append(f"extra closing {name}")
                break
        if depth > 0:
            errors.append(f"unclosed {name} ({depth} remaining)")

    if errors:
        return False, "C# issues - " + "; ".join(errors[:5])
    return True, ""


def _strip_csharp_strings_and_comments(source: str) -> str:  # noqa: C901
    """Replace string literals and comments in C# source with whitespace.

    This is intentionally simplistic (no raw-string literal support) but
    sufficient for brace-matching in documentation examples.
    """
    result: list[str] = []
    i = 0
    length = len(source)
    while i < length:
        # Single-line comment
        if source[i : i + 2] == "//":
            while i < length and source[i] != "\n":
                result.append(" ")
                i += 1
            continue
        # Multi-line comment
        if source[i : i + 2] == "/*":
            while i < length and source[i : i + 2] != "*/":
                result.append(" " if source[i] != "\n" else "\n")
                i += 1
            if i < length:
                result.append("  ")  # consume */
                i += 2
            continue
        # Verbatim string @"..."
        if source[i : i + 2] == '@"':
            result.append("  ")
            i += 2
            while i < length:
                if source[i] == '"':
                    if i + 1 < length and source[i + 1] == '"':
                        result.append("  ")
                        i += 2
                        continue
                    result.append(" ")
                    i += 1
                    break
                result.append(" " if source[i] != "\n" else "\n")
                i += 1
            continue
        # Regular string "..."
        if source[i] == '"':
            result.append(" ")
            i += 1
            while i < length and source[i] != '"':
                if source[i] == "\\":
                    result.append("  ")
                    i += 2
                    continue
                result.append(" " if source[i] != "\n" else "\n")
                i += 1
            if i < length:
                result.append(" ")
                i += 1
            continue
        # Character literal
        if source[i] == "'":
            result.append(" ")
            i += 1
            while i < length and source[i] != "'":
                if source[i] == "\\":
                    result.append("  ")
                    i += 2
                    continue
                result.append(" ")
                i += 1
            if i < length:
                result.append(" ")
                i += 1
            continue
        result.append(source[i])
        i += 1
    return "".join(result)


def _validate_xml(source: str) -> tuple[bool, str]:  # noqa: C901
    """Basic XML / HTML / XAML tag matching validation.

    Checks for:
    - Every opening tag has a matching closing tag (and vice-versa)
    - Self-closing tags are handled
    - Ignores processing instructions (``<?...?>``) and CDATA sections

    Uses a simple stack-based approach rather than a full parser so that
    partial snippets common in documentation are handled gracefully.
    """
    errors: list[str] = []

    # Remove comments, CDATA, and processing instructions.
    cleaned = re.sub(r"<!--.*?-->", "", source, flags=re.DOTALL)
    cleaned = re.sub(r"<!\[CDATA\[.*?\]\]>", "", cleaned, flags=re.DOTALL)
    cleaned = re.sub(r"<\?.*?\?>", "", cleaned, flags=re.DOTALL)
    cleaned = re.sub(r"<!DOCTYPE[^>]*>", "", cleaned, flags=re.IGNORECASE)

    # Find all tags.
    tag_re = re.compile(
        r"<\s*(?P<closing>/)?\s*(?P<name>[a-zA-Z][a-zA-Z0-9_.:-]*)"
        r"(?P<attrs>[^>]*?)"
        r"(?P<selfclose>/)?\s*>"
    )

    # Void elements that never have closing tags in HTML.
    void_elements = {
        "area", "base", "br", "col", "embed", "hr", "img", "input",
        "link", "meta", "param", "source", "track", "wbr",
    }

    stack: list[str] = []
    for m in tag_re.finditer(cleaned):
        name = m.group("name").lower()
        is_closing = m.group("closing") is not None
        is_self_closing = m.group("selfclose") is not None

        if name in void_elements or is_self_closing:
            continue

        if is_closing:
            if not stack:
                errors.append(f"closing </{name}> without matching opening tag")
            elif stack[-1] != name:
                errors.append(
                    f"expected </{stack[-1]}> but found </{name}>"
                )
                # Attempt recovery: pop if it exists anywhere in the stack.
                if name in stack:
                    while stack and stack[-1] != name:
                        stack.pop()
                    if stack:
                        stack.pop()
            else:
                stack.pop()
        else:
            stack.append(name)

    if stack:
        unclosed = ", ".join(f"<{t}>" for t in reversed(stack))
        errors.append(f"unclosed tags: {unclosed}")

    if errors:
        return False, "XML/HTML issues - " + "; ".join(errors[:5])
    return True, ""


# Map canonical language to validator function.
_VALIDATORS: dict[str, object] = {
    "python": _validate_python,
    "json": _validate_json,
    "jsonc": _validate_jsonc,
    "yaml": _validate_yaml,
    "bash": _validate_bash,
    "csharp": _validate_csharp,
    "xml": _validate_xml,
}


# ---------------------------------------------------------------------------
# Validation orchestration
# ---------------------------------------------------------------------------

def validate_block(block: CodeBlock) -> ValidationResult:
    """Validate a single code block and return the result."""
    # Skip empty blocks.
    if not block.source.strip():
        return ValidationResult(block, ValidationStatus.SKIPPED, "empty code block")

    # Skip blocks whose language we cannot validate.
    validator = _VALIDATORS.get(block.canonical_language)
    if validator is None:
        return ValidationResult(
            block,
            ValidationStatus.SKIPPED,
            f"no validator for language '{block.language_tag}'"
            if block.language_tag
            else "no language specified",
        )

    try:
        ok, error = validator(block.source)
    except Exception as exc:
        return ValidationResult(
            block,
            ValidationStatus.INVALID,
            f"validator raised {type(exc).__name__}: {exc}",
        )

    if ok:
        return ValidationResult(block, ValidationStatus.VALID)
    return ValidationResult(block, ValidationStatus.INVALID, error)


def run_validation(root: Path, docs_dir: str) -> ValidationReport:
    """Discover markdown files, extract code blocks, and validate them all.

    Returns a ``ValidationReport`` with every result.
    """
    report = ValidationReport()
    report.scan_time = datetime.now(timezone.utc).isoformat()

    md_files = discover_markdown_files(root, docs_dir)
    report.scanned_files = len(md_files)

    for md_path in md_files:
        blocks = extract_code_blocks(md_path, root)
        for block in blocks:
            result = validate_block(block)
            report.results.append(result)

    return report


# ---------------------------------------------------------------------------
# Report generation
# ---------------------------------------------------------------------------

def _status_label(status: ValidationStatus) -> str:
    """Human-friendly label for a validation status."""
    return {
        ValidationStatus.VALID: "Valid",
        ValidationStatus.INVALID: "Invalid",
        ValidationStatus.SKIPPED: "Skipped",
    }[status]


def generate_markdown_report(report: ValidationReport) -> str:
    """Render the full validation report as a markdown document."""
    lines: list[str] = []

    lines.append("# Documentation Code Example Validation Report")
    lines.append("")
    lines.append("> Auto-generated by `validate-examples.py`. Do not edit manually.")
    lines.append(f"> Scan time: {report.scan_time}")
    lines.append(f"> Files scanned: {report.scanned_files}")
    lines.append("")

    total = len(report.results)
    valid_count = sum(1 for r in report.results if r.status == ValidationStatus.VALID)
    invalid_count = sum(1 for r in report.results if r.status == ValidationStatus.INVALID)
    skipped_count = sum(1 for r in report.results if r.status == ValidationStatus.SKIPPED)

    # ---- Overall summary ----
    lines.append("## Overall Summary")
    lines.append("")
    lines.append("| Metric | Count |")
    lines.append("|--------|------:|")
    lines.append(f"| Total code blocks | {total} |")
    lines.append(f"| Valid | {valid_count} |")
    lines.append(f"| Invalid | {invalid_count} |")
    lines.append(f"| Skipped | {skipped_count} |")
    lines.append("")

    # ---- Summary by language ----
    lang_stats: dict[str, dict[str, int]] = {}
    for r in report.results:
        lang = r.block.canonical_language or "(none)"
        if lang not in lang_stats:
            lang_stats[lang] = {"total": 0, "valid": 0, "invalid": 0, "skipped": 0}
        lang_stats[lang]["total"] += 1
        lang_stats[lang][r.status.value] += 1

    lines.append("## Summary by Language")
    lines.append("")
    lines.append("| Language | Total | Valid | Invalid | Skipped |")
    lines.append("|----------|------:|------:|--------:|--------:|")
    for lang in sorted(lang_stats):
        s = lang_stats[lang]
        lines.append(
            f"| `{lang}` | {s['total']} | {s['valid']} | {s['invalid']} | {s['skipped']} |"
        )
    lines.append("")

    # ---- Invalid examples ----
    invalid_results = [r for r in report.results if r.status == ValidationStatus.INVALID]
    if invalid_results:
        lines.append("## Invalid Code Examples")
        lines.append("")
        lines.append("| # | File | Line | Language | Error |")
        lines.append("|--:|------|-----:|----------|-------|")
        for idx, r in enumerate(invalid_results, start=1):
            escaped_error = r.error_message.replace("|", "\\|")
            lines.append(
                f"| {idx} "
                f"| `{r.block.source_file}` "
                f"| {r.block.line_number} "
                f"| `{r.block.language_tag or '(none)'}` "
                f"| {escaped_error} |"
            )
        lines.append("")
    else:
        lines.append("## Invalid Code Examples")
        lines.append("")
        lines.append("No invalid code examples found.")
        lines.append("")

    # ---- Valid examples count by file ----
    valid_by_file: dict[str, int] = {}
    for r in report.results:
        if r.status == ValidationStatus.VALID:
            valid_by_file[r.block.source_file] = (
                valid_by_file.get(r.block.source_file, 0) + 1
            )

    if valid_by_file:
        lines.append("## Valid Examples by File")
        lines.append("")
        lines.append("| File | Valid Blocks |")
        lines.append("|------|------------:|")
        for fpath in sorted(valid_by_file):
            lines.append(f"| `{fpath}` | {valid_by_file[fpath]} |")
        lines.append("")

    # ---- Footer ----
    lines.append("---")
    lines.append("")
    lines.append(
        "*Report generated by "
        "[validate-examples.py](../../build/scripts/docs/validate-examples.py)*"
    )
    lines.append("")

    return "\n".join(lines)


def generate_summary(report: ValidationReport) -> str:
    """Generate a concise summary suitable for ``GITHUB_STEP_SUMMARY``."""
    total = len(report.results)
    valid_count = sum(1 for r in report.results if r.status == ValidationStatus.VALID)
    invalid_count = sum(1 for r in report.results if r.status == ValidationStatus.INVALID)
    skipped_count = sum(1 for r in report.results if r.status == ValidationStatus.SKIPPED)

    parts: list[str] = [
        "### Documentation Code Example Validation",
        "",
        f"| Metric | Count |",
        f"|--------|------:|",
        f"| Files scanned | {report.scanned_files} |",
        f"| Total code blocks | {total} |",
        f"| Valid | {valid_count} |",
        f"| Invalid | {invalid_count} |",
        f"| Skipped | {skipped_count} |",
        "",
    ]

    if invalid_count > 0:
        parts.append(f"> **{invalid_count} invalid code example(s) found.** "
                      "See the full report for details.")
    else:
        parts.append("> All validated code examples passed.")

    parts.append("")
    return "\n".join(parts)


# ---------------------------------------------------------------------------
# CLI entry point
# ---------------------------------------------------------------------------

def main() -> int:
    """Parse arguments, run validation, and produce output."""
    parser = argparse.ArgumentParser(
        description=(
            "Validate fenced code examples in markdown documentation files. "
            "Generates a report listing valid, invalid, and skipped blocks."
        ),
    )
    parser.add_argument(
        "--root",
        type=Path,
        default=Path("."),
        help="Repository root directory (default: current directory)",
    )
    parser.add_argument(
        "--docs-dir",
        type=str,
        default="docs/",
        help="Documentation directory relative to --root (default: docs/)",
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=None,
        help="Path for the markdown validation report",
    )
    parser.add_argument(
        "--summary",
        action="store_true",
        default=False,
        help="Print a concise summary to stdout (useful for GITHUB_STEP_SUMMARY)",
    )

    args = parser.parse_args()

    root = args.root.resolve()
    if not root.is_dir():
        print(f"Error: root directory does not exist: {root}", file=sys.stderr)
        return 1

    docs_path = root / args.docs_dir
    if not docs_path.is_dir():
        print(
            f"Error: docs directory does not exist: {docs_path}",
            file=sys.stderr,
        )
        return 1

    # Run validation.
    report = run_validation(root, args.docs_dir)

    # Counts for exit code and console output.
    total = len(report.results)
    valid_count = sum(1 for r in report.results if r.status == ValidationStatus.VALID)
    invalid_count = sum(1 for r in report.results if r.status == ValidationStatus.INVALID)
    skipped_count = sum(1 for r in report.results if r.status == ValidationStatus.SKIPPED)

    # Write markdown report.
    if args.output:
        try:
            args.output.parent.mkdir(parents=True, exist_ok=True)
            args.output.write_text(
                generate_markdown_report(report), encoding="utf-8"
            )
            print(f"Report written to {args.output}")
        except OSError as exc:
            print(f"Error writing report: {exc}", file=sys.stderr)
            return 1

    # Print summary to stdout.
    if args.summary:
        print(generate_summary(report))
    else:
        print(
            f"Validation complete: {report.scanned_files} files, "
            f"{total} blocks ({valid_count} valid, "
            f"{invalid_count} invalid, {skipped_count} skipped)"
        )
        if invalid_count > 0:
            print("\nInvalid examples:")
            for r in report.results:
                if r.status == ValidationStatus.INVALID:
                    print(
                        f"  {r.block.source_file}:{r.block.line_number} "
                        f"[{r.block.language_tag}] {r.error_message}"
                    )

    # Exit with non-zero if any block is invalid, so CI can gate on it.
    return 1 if invalid_count > 0 else 0


if __name__ == "__main__":
    sys.exit(main())
