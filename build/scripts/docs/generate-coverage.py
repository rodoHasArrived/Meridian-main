#!/usr/bin/env python3
"""
Documentation Coverage Report Generator

Analyzes the Meridian codebase to determine how well
source-level constructs (public types, API endpoints, configuration
keys, provider implementations, and ADR references) are reflected in
the project documentation.

Produces a Markdown report with per-category coverage tables,
a list of undocumented items, and actionable recommendations.

Usage:
    python3 generate-coverage.py
    python3 generate-coverage.py --root /path/to/repo --output report.md
    python3 generate-coverage.py --summary  # print summary for GITHUB_STEP_SUMMARY
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Dict, List, Optional, Set, Tuple


# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

EXCLUDE_DIRS: Set[str] = {
    ".git",
    "node_modules",
    "bin",
    "obj",
    "__pycache__",
    ".vs",
}

CS_FILE_EXTENSIONS: Tuple[str, ...] = (".cs",)

DOC_FILE_EXTENSIONS: Tuple[str, ...] = (".md",)

# Regex: public (static )?(sealed )?(partial )?(class|interface|record|enum) Name
PUBLIC_TYPE_RE = re.compile(
    r"^\s*(?:\[.*?\]\s*)*"                          # optional attributes
    r"public\s+"
    r"(?:static\s+)?"
    r"(?:sealed\s+)?"
    r"(?:partial\s+)?"
    r"(?:abstract\s+)?"
    r"(class|interface|record|enum)\s+"
    r"([A-Z]\w*)",                                   # type name
    re.MULTILINE,
)

# Route-style endpoint patterns
ROUTE_ATTRIBUTE_RE = re.compile(
    r'\[\s*(?:Http(?:Get|Post|Put|Delete|Patch)|Route)\s*\(\s*"([^"]+)"\s*\)',
)
MAP_ENDPOINT_RE = re.compile(
    r'\.(?:MapGet|MapPost|MapPut|MapDelete|MapPatch)\s*\(\s*"([^"]+)"',
)

# ADR reference in source: [ImplementsAdr("ADR-001", ...)]
ADR_REF_RE = re.compile(r'ImplementsAdr\s*\(\s*"(ADR-\d+)"')

# ADR file naming: 001-provider-abstraction.md -> ADR-001
ADR_FILE_RE = re.compile(r"^(\d{3})-.*\.md$")


# ---------------------------------------------------------------------------
# Data classes
# ---------------------------------------------------------------------------

@dataclass
class SourceItem:
    """A source-level construct that may or may not be documented."""

    name: str
    file_path: str
    line: int = 0
    documented: bool = False


@dataclass
class CategoryResult:
    """Coverage result for a single category."""

    category: str
    total: int = 0
    documented: int = 0
    items: List[SourceItem] = field(default_factory=list)

    @property
    def undocumented_items(self) -> List[SourceItem]:
        return [item for item in self.items if not item.documented]

    @property
    def coverage_pct(self) -> float:
        if self.total == 0:
            return 100.0
        return (self.documented / self.total) * 100.0


@dataclass
class CoverageReport:
    """Full documentation coverage report."""

    categories: List[CategoryResult] = field(default_factory=list)
    generated_at: str = ""

    @property
    def overall_total(self) -> int:
        return sum(c.total for c in self.categories)

    @property
    def overall_documented(self) -> int:
        return sum(c.documented for c in self.categories)

    @property
    def overall_pct(self) -> float:
        if self.overall_total == 0:
            return 100.0
        return (self.overall_documented / self.overall_total) * 100.0


# ---------------------------------------------------------------------------
# Utility helpers
# ---------------------------------------------------------------------------

def _should_skip(path: Path) -> bool:
    """Return True if *path* is inside an excluded directory."""
    for part in path.parts:
        if part in EXCLUDE_DIRS:
            return True
    return False


def _collect_files(root: Path, extensions: Tuple[str, ...]) -> List[Path]:
    """Recursively collect files matching *extensions*, honouring exclusions."""
    results: List[Path] = []
    for ext in extensions:
        for p in root.rglob(f"*{ext}"):
            if not _should_skip(p):
                results.append(p)
    return results


def _read_text_safe(path: Path) -> str:
    """Read file text, returning empty string on failure."""
    try:
        return path.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return ""


def _rel(path: Path, root: Path) -> str:
    """Return a portable relative path string."""
    try:
        return str(path.relative_to(root))
    except ValueError:
        return str(path)


# ---------------------------------------------------------------------------
# Analysis: Public types
# ---------------------------------------------------------------------------

def _scan_public_types(root: Path) -> List[SourceItem]:
    """Scan C# source files for public class / interface / record / enum."""
    src_dir = root / "src"
    if not src_dir.is_dir():
        return []

    items: List[SourceItem] = []
    seen: Set[str] = set()

    for cs_file in _collect_files(src_dir, CS_FILE_EXTENSIONS):
        text = _read_text_safe(cs_file)
        for match in PUBLIC_TYPE_RE.finditer(text):
            type_name = match.group(2)
            if type_name in seen:
                continue
            seen.add(type_name)
            line_num = text[:match.start()].count("\n") + 1
            items.append(
                SourceItem(
                    name=type_name,
                    file_path=_rel(cs_file, root),
                    line=line_num,
                )
            )
    return items


def _check_type_documentation(
    items: List[SourceItem],
    doc_contents: Dict[str, str],
) -> CategoryResult:
    """Mark items as documented if any doc file mentions their name."""
    for item in items:
        for _doc_path, content in doc_contents.items():
            if item.name in content:
                item.documented = True
                break

    documented = sum(1 for i in items if i.documented)
    return CategoryResult(
        category="Public Classes / Interfaces",
        total=len(items),
        documented=documented,
        items=items,
    )


# ---------------------------------------------------------------------------
# Analysis: API endpoints
# ---------------------------------------------------------------------------

def _scan_endpoints(root: Path) -> List[SourceItem]:
    """Scan source for HTTP API route definitions."""
    src_dir = root / "src"
    if not src_dir.is_dir():
        return []

    items: List[SourceItem] = []
    seen: Set[str] = set()

    for cs_file in _collect_files(src_dir, CS_FILE_EXTENSIONS):
        text = _read_text_safe(cs_file)

        for pattern in (ROUTE_ATTRIBUTE_RE, MAP_ENDPOINT_RE):
            for match in pattern.finditer(text):
                route = match.group(1)
                if route in seen:
                    continue
                seen.add(route)
                line_num = text[:match.start()].count("\n") + 1
                items.append(
                    SourceItem(
                        name=route,
                        file_path=_rel(cs_file, root),
                        line=line_num,
                    )
                )
    return items


def _check_endpoint_documentation(
    items: List[SourceItem],
    root: Path,
) -> CategoryResult:
    """Check endpoints against docs/reference/api-reference.md and CLAUDE.md."""
    api_ref = root / "docs" / "reference" / "api-reference.md"
    claude_md = root / "CLAUDE.md"

    combined_text = ""
    for doc_path in (api_ref, claude_md):
        combined_text += _read_text_safe(doc_path) + "\n"

    for item in items:
        # Normalise route for matching (strip leading /)
        route = item.name.lstrip("/")
        # Check if the route (or its non-parameterised prefix) appears in docs
        if route in combined_text or f"/{route}" in combined_text:
            item.documented = True
        else:
            # Try matching parameterised routes: /api/backfill/schedules/{id}
            # Strip parameter segments and try the base path
            base = re.sub(r"/\{[^}]+\}", "", item.name)
            if base and (base in combined_text or base.lstrip("/") in combined_text):
                item.documented = True

    documented = sum(1 for i in items if i.documented)
    return CategoryResult(
        category="API Endpoints",
        total=len(items),
        documented=documented,
        items=items,
    )


# ---------------------------------------------------------------------------
# Analysis: Configuration keys
# ---------------------------------------------------------------------------

def _flatten_json_keys(obj: object, prefix: str = "") -> List[str]:
    """Recursively extract dotted key paths from a parsed JSON object."""
    keys: List[str] = []
    if isinstance(obj, dict):
        for k, v in obj.items():
            full = f"{prefix}.{k}" if prefix else k
            keys.append(full)
            keys.extend(_flatten_json_keys(v, full))
    elif isinstance(obj, list):
        for idx, v in enumerate(obj):
            keys.extend(_flatten_json_keys(v, f"{prefix}[{idx}]"))
    return keys


def _strip_json_comments(text: str) -> str:
    """Remove single-line // comments from JSON-with-comments text."""
    lines: List[str] = []
    in_string = False
    for line in text.split("\n"):
        cleaned: List[str] = []
        i = 0
        while i < len(line):
            ch = line[i]
            if ch == '"' and (i == 0 or line[i - 1] != "\\"):
                in_string = not in_string
                cleaned.append(ch)
            elif not in_string and ch == "/" and i + 1 < len(line) and line[i + 1] == "/":
                break  # rest of line is comment
            else:
                cleaned.append(ch)
            i += 1
        lines.append("".join(cleaned))
        # Reset in_string at end of each line (single-line strings only)
        in_string = False
    return "\n".join(lines)


def _scan_config_keys(root: Path) -> List[SourceItem]:
    """Extract top-level and second-level config keys from appsettings.sample.json."""
    sample = root / "config" / "appsettings.sample.json"
    if not sample.is_file():
        return []

    raw = _read_text_safe(sample)
    stripped = _strip_json_comments(raw)
    try:
        data = json.loads(stripped)
    except json.JSONDecodeError:
        return []

    # We care about top-level keys and one level of nesting.
    items: List[SourceItem] = []
    if not isinstance(data, dict):
        return items

    for key, value in data.items():
        items.append(
            SourceItem(name=key, file_path=_rel(sample, root))
        )
        if isinstance(value, dict):
            for sub_key in value:
                items.append(
                    SourceItem(
                        name=f"{key}.{sub_key}",
                        file_path=_rel(sample, root),
                    )
                )
    return items


def _check_config_documentation(
    items: List[SourceItem],
    root: Path,
) -> CategoryResult:
    """Check config keys against configuration-schema.md and CLAUDE.md."""
    schema_doc = root / "docs" / "generated" / "configuration-schema.md"
    claude_md = root / "CLAUDE.md"

    combined = ""
    for doc_path in (schema_doc, claude_md):
        combined += _read_text_safe(doc_path) + "\n"

    for item in items:
        # Match the key name or the dotted path
        key_leaf = item.name.split(".")[-1]
        if key_leaf in combined or item.name in combined:
            item.documented = True

    documented = sum(1 for i in items if i.documented)
    return CategoryResult(
        category="Configuration Options",
        total=len(items),
        documented=documented,
        items=items,
    )


# ---------------------------------------------------------------------------
# Analysis: Provider implementations
# ---------------------------------------------------------------------------

def _scan_providers(root: Path) -> List[SourceItem]:
    """Identify provider implementation directories under Infrastructure/Providers."""
    providers_root = (
        root / "src" / "Meridian.Infrastructure" / "Providers"
    )
    if not providers_root.is_dir():
        return []

    items: List[SourceItem] = []
    # Scan subcategories: Streaming, Historical, Backfill, SymbolSearch
    for sub in sorted(providers_root.iterdir()):
        if not sub.is_dir() or sub.name.startswith("."):
            continue
        for provider_dir in sorted(sub.iterdir()):
            if not provider_dir.is_dir() or provider_dir.name.startswith("."):
                continue
            # Skip utility/framework dirs that are not actual provider names
            if provider_dir.name.lower() in {
                "core", "queue", "ratelimiting", "symbolresolution",
                "gapalysis", "gapanalysis",
            }:
                continue
            items.append(
                SourceItem(
                    name=f"{sub.name}/{provider_dir.name}",
                    file_path=_rel(provider_dir, root),
                )
            )
    return items


def _check_provider_documentation(
    items: List[SourceItem],
    root: Path,
) -> CategoryResult:
    """Check if providers are mentioned in docs/providers/ or CLAUDE.md."""
    provider_docs_dir = root / "docs" / "providers"
    claude_md = root / "CLAUDE.md"

    combined = _read_text_safe(claude_md)
    if provider_docs_dir.is_dir():
        for md_file in provider_docs_dir.glob("*.md"):
            combined += "\n" + _read_text_safe(md_file)

    for item in items:
        # Extract short provider name (e.g. "Alpaca" from "Streaming/Alpaca")
        provider_name = item.name.split("/")[-1]
        if provider_name.lower() in combined.lower():
            item.documented = True

    documented = sum(1 for i in items if i.documented)
    return CategoryResult(
        category="Provider Implementations",
        total=len(items),
        documented=documented,
        items=items,
    )


# ---------------------------------------------------------------------------
# Analysis: ADR implementations
# ---------------------------------------------------------------------------

def _scan_adr_references(root: Path) -> List[SourceItem]:
    """Find ADR identifiers referenced in source via [ImplementsAdr]."""
    src_dir = root / "src"
    if not src_dir.is_dir():
        return []

    seen: Set[str] = set()
    items: List[SourceItem] = []

    for cs_file in _collect_files(src_dir, CS_FILE_EXTENSIONS):
        text = _read_text_safe(cs_file)
        for match in ADR_REF_RE.finditer(text):
            adr_id = match.group(1)  # e.g. "ADR-001"
            if adr_id in seen:
                continue
            seen.add(adr_id)
            line_num = text[:match.start()].count("\n") + 1
            items.append(
                SourceItem(
                    name=adr_id,
                    file_path=_rel(cs_file, root),
                    line=line_num,
                )
            )
    return items


def _check_adr_documentation(
    items: List[SourceItem],
    root: Path,
) -> CategoryResult:
    """Check that each referenced ADR has a matching file in docs/adr/."""
    adr_dir = root / "docs" / "adr"
    existing_adrs: Set[str] = set()

    if adr_dir.is_dir():
        for f in adr_dir.iterdir():
            m = ADR_FILE_RE.match(f.name)
            if m:
                num = int(m.group(1))
                existing_adrs.add(f"ADR-{num:03d}")

    for item in items:
        # Normalise to 3-digit form: ADR-1 -> ADR-001
        num_match = re.search(r"ADR-0*(\d+)", item.name)
        if num_match:
            normalised = f"ADR-{int(num_match.group(1)):03d}"
            item.documented = normalised in existing_adrs

    documented = sum(1 for i in items if i.documented)
    return CategoryResult(
        category="ADR Implementations",
        total=len(items),
        documented=documented,
        items=items,
    )


# ---------------------------------------------------------------------------
# Documentation content loader
# ---------------------------------------------------------------------------

def _load_doc_contents(root: Path) -> Dict[str, str]:
    """Load all Markdown documentation content keyed by relative path."""
    docs_dir = root / "docs"
    contents: Dict[str, str] = {}
    if docs_dir.is_dir():
        for md_file in _collect_files(docs_dir, DOC_FILE_EXTENSIONS):
            contents[_rel(md_file, root)] = _read_text_safe(md_file)
    # Also include CLAUDE.md at repo root
    claude_md = root / "CLAUDE.md"
    if claude_md.is_file():
        contents[_rel(claude_md, root)] = _read_text_safe(claude_md)
    # And README.md
    readme = root / "README.md"
    if readme.is_file():
        contents[_rel(readme, root)] = _read_text_safe(readme)
    return contents


# ---------------------------------------------------------------------------
# Report generation
# ---------------------------------------------------------------------------

def _coverage_bar(pct: float, width: int = 20) -> str:
    """Render a text-based progress bar for Markdown."""
    filled = round(pct / 100 * width)
    empty = width - filled
    return f"{'=' * filled}{'-' * empty}"


def _grade(pct: float) -> str:
    """Letter grade for a coverage percentage."""
    if pct >= 90:
        return "A"
    if pct >= 75:
        return "B"
    if pct >= 60:
        return "C"
    if pct >= 40:
        return "D"
    return "F"


def _recommendations(report: CoverageReport) -> List[str]:  # noqa: C901
    """Generate human-readable improvement recommendations."""
    recs: List[str] = []

    for cat in report.categories:
        undoc = cat.undocumented_items
        if not undoc:
            continue
        count = len(undoc)

        if cat.category == "Public Classes / Interfaces":
            if count > 50:
                recs.append(
                    f"**{cat.category}**: {count} undocumented types. "
                    "Consider generating API docs with DocFX (`docfx docfx.json`) "
                    "to cover the long tail of public types automatically."
                )
            elif count > 0:
                recs.append(
                    f"**{cat.category}**: {count} undocumented type(s). "
                    "Add entries to `docs/reference/api-reference.md` or relevant "
                    "architecture docs for the most important ones."
                )

        elif cat.category == "API Endpoints":
            if count > 0:
                recs.append(
                    f"**{cat.category}**: {count} endpoint(s) missing from "
                    "`docs/reference/api-reference.md`. Run the endpoint audit "
                    "and update the API reference table."
                )

        elif cat.category == "Configuration Options":
            if count > 0:
                recs.append(
                    f"**{cat.category}**: {count} config key(s) not found in "
                    "`docs/generated/configuration-schema.md`. Re-run the "
                    "configuration schema generator to synchronise."
                )

        elif cat.category == "Provider Implementations":
            if count > 0:
                names = ", ".join(i.name.split("/")[-1] for i in undoc[:5])
                suffix = f" (and {count - 5} more)" if count > 5 else ""
                recs.append(
                    f"**{cat.category}**: {count} provider(s) lack dedicated "
                    f"documentation: {names}{suffix}. Add setup guides under "
                    "`docs/providers/`."
                )

        elif cat.category == "ADR Implementations":
            if count > 0:
                ids = ", ".join(i.name for i in undoc)
                recs.append(
                    f"**{cat.category}**: Referenced ADR(s) {ids} have no "
                    "corresponding file in `docs/adr/`. Create the missing "
                    "ADR document(s) using `docs/adr/_template.md`."
                )

    if not recs:
        recs.append("All categories are fully documented. Great job!")

    return recs


def generate_markdown(report: CoverageReport) -> str:
    """Render the full Markdown coverage report."""
    lines: List[str] = []

    lines.append("# Documentation Coverage Report")
    lines.append("")
    lines.append("> Auto-generated by `build/scripts/docs/generate-coverage.py`")
    lines.append(f"> Generated: {report.generated_at}")
    lines.append("")

    # --- Overall ---
    lines.append("## Overall Coverage")
    lines.append("")
    grade = _grade(report.overall_pct)
    lines.append(
        f"**{report.overall_documented} / {report.overall_total}** items documented "
        f"(**{report.overall_pct:.1f}%**) &mdash; Grade: **{grade}**"
    )
    lines.append("")
    lines.append("```")
    lines.append(f"[{_coverage_bar(report.overall_pct)}] {report.overall_pct:.1f}%")
    lines.append("```")
    lines.append("")

    # --- Per-category table ---
    lines.append("## Coverage by Category")
    lines.append("")
    lines.append("| Category | Documented | Total | Coverage | Grade |")
    lines.append("|----------|-----------|-------|----------|-------|")
    for cat in report.categories:
        lines.append(
            f"| {cat.category} | {cat.documented} | {cat.total} "
            f"| {cat.coverage_pct:.1f}% | {_grade(cat.coverage_pct)} |"
        )
    lines.append("")

    # --- Undocumented items ---
    has_undocumented = any(cat.undocumented_items for cat in report.categories)
    if has_undocumented:
        lines.append("## Undocumented Items")
        lines.append("")

        for cat in report.categories:
            undoc = cat.undocumented_items
            if not undoc:
                continue
            lines.append(f"### {cat.category} ({len(undoc)} undocumented)")
            lines.append("")
            lines.append("| Item | Location |")
            lines.append("|------|----------|")
            # Show up to 50 items per category to keep the report manageable
            display = undoc[:50]
            for item in display:
                loc = item.file_path
                if item.line:
                    loc += f":{item.line}"
                lines.append(f"| `{item.name}` | `{loc}` |")
            if len(undoc) > 50:
                lines.append(
                    f"| ... and {len(undoc) - 50} more | |"
                )
            lines.append("")

    # --- Recommendations ---
    lines.append("## Recommendations")
    lines.append("")
    for idx, rec in enumerate(_recommendations(report), 1):
        lines.append(f"{idx}. {rec}")
    lines.append("")

    # --- Footer ---
    lines.append("---")
    lines.append("")
    lines.append(
        "*This report was generated automatically. "
        "Do not edit manually.*"
    )
    lines.append("")

    return "\n".join(lines)


def generate_summary(report: CoverageReport) -> str:
    """Generate a concise summary suitable for GITHUB_STEP_SUMMARY."""
    lines: List[str] = []
    grade = _grade(report.overall_pct)

    lines.append("### Documentation Coverage")
    lines.append("")
    lines.append(
        f"**{report.overall_pct:.1f}%** overall "
        f"({report.overall_documented}/{report.overall_total}) "
        f"&mdash; Grade: **{grade}**"
    )
    lines.append("")
    lines.append("| Category | Coverage |")
    lines.append("|----------|----------|")
    for cat in report.categories:
        lines.append(f"| {cat.category} | {cat.coverage_pct:.1f}% ({cat.documented}/{cat.total}) |")
    lines.append("")

    undoc_total = sum(len(c.undocumented_items) for c in report.categories)
    if undoc_total:
        lines.append(f"**{undoc_total}** item(s) lack documentation coverage.")
    else:
        lines.append("All items are documented.")
    lines.append("")

    return "\n".join(lines)


# ---------------------------------------------------------------------------
# Main orchestration
# ---------------------------------------------------------------------------

def build_report(root: Path) -> CoverageReport:
    """Run all analyses and assemble the coverage report."""
    report = CoverageReport(
        generated_at=datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M:%S UTC"),
    )

    # Load documentation content once for type-mention scanning.
    doc_contents = _load_doc_contents(root)

    # 1. Public types
    type_items = _scan_public_types(root)
    type_result = _check_type_documentation(type_items, doc_contents)
    report.categories.append(type_result)

    # 2. API endpoints
    endpoint_items = _scan_endpoints(root)
    endpoint_result = _check_endpoint_documentation(endpoint_items, root)
    report.categories.append(endpoint_result)

    # 3. Configuration options
    config_items = _scan_config_keys(root)
    config_result = _check_config_documentation(config_items, root)
    report.categories.append(config_result)

    # 4. Provider implementations
    provider_items = _scan_providers(root)
    provider_result = _check_provider_documentation(provider_items, root)
    report.categories.append(provider_result)

    # 5. ADR implementations
    adr_items = _scan_adr_references(root)
    adr_result = _check_adr_documentation(adr_items, root)
    report.categories.append(adr_result)

    return report


def main(argv: Optional[List[str]] = None) -> int:
    """Entry point for the documentation coverage generator."""
    parser = argparse.ArgumentParser(
        description="Generate documentation coverage reports by comparing "
        "what is documented versus what exists in source code.",
    )
    parser.add_argument(
        "--root",
        type=Path,
        default=Path("."),
        help="Repository root directory (default: current directory).",
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=None,
        help="Path to write the Markdown coverage report. "
        "Defaults to docs/generated/documentation-coverage.md.",
    )
    parser.add_argument(
        "--summary",
        action="store_true",
        default=False,
        help="Print a concise summary to stdout (suitable for GITHUB_STEP_SUMMARY).",
    )

    args = parser.parse_args(argv)

    root: Path = args.root.resolve()
    if not root.is_dir():
        print(f"Error: root directory does not exist: {root}", file=sys.stderr)
        return 1

    output: Path = (
        args.output
        if args.output is not None
        else root / "docs" / "generated" / "documentation-coverage.md"
    )

    try:
        report = build_report(root)
    except Exception as exc:
        print(f"Error: failed to build coverage report: {exc}", file=sys.stderr)
        return 1

    # Write Markdown report
    md = generate_markdown(report)
    try:
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(md, encoding="utf-8")
        print(f"Coverage report written to {output}")
    except OSError as exc:
        print(f"Error: could not write report: {exc}", file=sys.stderr)
        return 1

    # Print summary
    if args.summary:
        print("")
        print(generate_summary(report))
    else:
        # Always print a one-liner even without --summary
        grade = _grade(report.overall_pct)
        print(
            f"Documentation coverage: {report.overall_pct:.1f}% "
            f"({report.overall_documented}/{report.overall_total}) "
            f"- Grade: {grade}"
        )

    return 0


if __name__ == "__main__":
    sys.exit(main())
