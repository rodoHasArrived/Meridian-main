#!/usr/bin/env python3
# /// script
# requires-python = ">=3.10"
# ///
"""AI Documentation Maintenance — freshness, drift, and archive automation.

Keeps AI instruction files, skills, and project documentation current as the
codebase evolves. Detects stale content, validates cross-references, archives
deprecated docs, and generates sync reports.

Commands:
    freshness       Check staleness of all AI-related docs.
    drift           Detect when AI docs diverge from code reality.
    archive-stale   Move deprecated/stale docs to docs/archived/.
    validate-refs   Validate cross-references between AI docs.
    sync-report     Generate a sync report showing what needs updating.
    full            Run all checks and produce a combined report.

Exit codes:
    0   Clean — no critical or warning findings.
    1   Findings — at least one critical or warning issue detected.
    2   Error — script failed to run (bad arguments, missing files, etc.).

Examples:
    python3 build/scripts/docs/ai-docs-maintenance.py freshness
    python3 build/scripts/docs/ai-docs-maintenance.py drift --summary
    python3 build/scripts/docs/ai-docs-maintenance.py archive-stale --dry-run
    python3 build/scripts/docs/ai-docs-maintenance.py archive-stale --execute
    python3 build/scripts/docs/ai-docs-maintenance.py sync-report --output /tmp/sync.md
    python3 build/scripts/docs/ai-docs-maintenance.py full --json-output /tmp/report.json
"""

from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import subprocess
import sys
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from pathlib import Path

# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

REPO_ROOT = Path(__file__).resolve().parents[3]


def _build_path_lists(root: Path) -> tuple[list[Path], list[Path], list[Path]]:
    """Build the three path lists relative to the given repository root.

    Factored out so that ``--root`` overrides can recompute all lists after
    ``REPO_ROOT`` is updated, rather than keeping stale module-level values.
    """
    ai_doc_paths: list[Path] = [
        root / "CLAUDE.md",
        root / "docs" / "ai" / "README.md",
        root / "docs" / "ai" / "ai-known-errors.md",
        root / "docs" / "ai" / "navigation" / "README.md",
        root / "docs" / "ai" / "generated" / "repo-navigation.md",
        root / "docs" / "ai" / "claude" / "CLAUDE.providers.md",
        root / "docs" / "ai" / "claude" / "CLAUDE.storage.md",
        root / "docs" / "ai" / "claude" / "CLAUDE.fsharp.md",
        root / "docs" / "ai" / "claude" / "CLAUDE.testing.md",
        root / "docs" / "ai" / "claude" / "CLAUDE.actions.md",
        root / "docs" / "ai" / "claude" / "CLAUDE.repo-updater.md",
        root / "docs" / "ai" / "copilot" / "instructions.md",
        root / ".github" / "copilot-instructions.md",
        root / ".github" / "agents" / "code-review-agent.md",
        root / ".github" / "agents" / "documentation-agent.md",
        root / ".github" / "agents" / "repo-navigation-agent.md",
        root / ".claude" / "agents" / "meridian-navigation.md",
        root / ".codex" / "skills" / "meridian-repo-navigation" / "SKILL.md",
        root / ".claude" / "skills" / "meridian-code-review" / "SKILL.md",
    ]
    instruction_files: list[Path] = [
        root / ".github" / "instructions" / "csharp.instructions.md",
        root / ".github" / "instructions" / "wpf.instructions.md",
        root / ".github" / "instructions" / "dotnet-tests.instructions.md",
        root / ".github" / "instructions" / "docs.instructions.md",
    ]
    skill_resource_paths: list[Path] = [
        root / ".claude" / "skills" / "meridian-code-review" / "references" / "architecture.md",
        root / ".claude" / "skills" / "meridian-code-review" / "references" / "schemas.md",
        root / ".claude" / "skills" / "meridian-code-review" / "agents" / "grader.md",
        root / ".claude" / "skills" / "meridian-code-review" / "evals" / "evals.json",
    ]
    return ai_doc_paths, instruction_files, skill_resource_paths


AI_DOC_PATHS, INSTRUCTION_FILES, SKILL_RESOURCE_PATHS = _build_path_lists(REPO_ROOT)

# Staleness thresholds (days)
STALE_WARNING_DAYS = 60
STALE_CRITICAL_DAYS = 120
ARCHIVE_CANDIDATE_DAYS = 180

# Patterns for detecting timestamps in docs.
# Matches both italic footer format  →  *Last Updated: YYYY-MM-DD*
# and bold header format             →  **Last Updated:** YYYY-MM-DD
LAST_UPDATED_PATTERN = re.compile(
    r"\*{1,2}Last Updated:(?:\*{1,2})?\s*(\d{4}-\d{2}-\d{2})\*{0,2}", re.IGNORECASE
)
DATE_PATTERN = re.compile(r"(\d{4}-\d{2}-\d{2})")

# Provider detection patterns
PROVIDER_CLASS_PATTERN = re.compile(
    r'class\s+(\w+(?:MarketDataClient|DataSource|HistoricalDataProvider|SymbolSearchProvider))\b'
)
PROVIDER_TABLE_PATTERN = re.compile(r'\|\s*`?(\w+(?:Client|Source|Provider))`?\s*\|')

# Workflow file pattern
WORKFLOW_PATTERN = re.compile(r"\.github/workflows/(\w[\w-]*\.yml)")


# ---------------------------------------------------------------------------
# Data classes
# ---------------------------------------------------------------------------

@dataclass
class Finding:
    file: str
    category: str
    severity: str  # critical, warning, info
    message: str
    fix_hint: str = ""
    line: int = 0


@dataclass
class FreshnessEntry:
    file: str
    last_updated: str | None
    age_days: int
    status: str  # current, stale-warning, stale-critical, unknown


@dataclass
class DriftEntry:
    area: str
    expected: str
    actual: str
    severity: str
    fix_hint: str


@dataclass
class Report:
    timestamp: str = field(default_factory=lambda: datetime.now(timezone.utc).isoformat())
    command: str = ""
    freshness: list[FreshnessEntry] = field(default_factory=list)
    drift: list[DriftEntry] = field(default_factory=list)
    findings: list[Finding] = field(default_factory=list)
    archived: list[str] = field(default_factory=list)
    summary: dict[str, int] = field(default_factory=dict)


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def _relative(path: Path) -> str:
    """Return path relative to repo root."""
    try:
        return str(path.relative_to(REPO_ROOT))
    except ValueError:
        return str(path)


def _git_last_modified(path: Path) -> datetime | None:
    """Get the last git commit date for a file."""
    try:
        result = subprocess.run(
            ["git", "log", "-1", "--format=%aI", "--", str(path)],
            capture_output=True, text=True, cwd=str(REPO_ROOT), timeout=10,
        )
        if result.returncode == 0 and result.stdout.strip():
            return datetime.fromisoformat(result.stdout.strip())
    except (subprocess.SubprocessError, subprocess.TimeoutExpired, OSError):
        pass
    return None


def _extract_last_updated(path: Path) -> str | None:
    """Extract 'Last Updated' date from a markdown file."""
    try:
        content = path.read_text(encoding="utf-8")
        match = LAST_UPDATED_PATTERN.search(content)
        if match:
            return match.group(1)
    except OSError:
        pass
    return None


def _count_files(directory: str, extension: str) -> int:
    """Count files with given extension in directory, excluding bin/obj."""
    count = 0
    for root, dirs, files in os.walk(directory):
        dirs[:] = [d for d in dirs if d not in {"bin", "obj", "node_modules", ".git", "__pycache__"}]
        for f in files:
            if f.endswith(extension):
                count += 1
    return count


def _list_workflow_files() -> list[str]:
    """List all workflow YAML files."""
    wf_dir = REPO_ROOT / ".github" / "workflows"
    if not wf_dir.exists():
        return []
    return sorted(f.name for f in wf_dir.glob("*.yml"))


def _extract_providers_from_code() -> set[str]:
    """Find provider class names in the codebase."""
    providers: set[str] = set()
    adapters_dir = REPO_ROOT / "src" / "Meridian.Infrastructure" / "Adapters"
    if not adapters_dir.exists():
        return providers
    for cs_file in adapters_dir.rglob("*.cs"):
        try:
            content = cs_file.read_text(encoding="utf-8")
            for match in PROVIDER_CLASS_PATTERN.finditer(content):
                providers.add(match.group(1))
        except OSError:
            pass
    return providers


def _extract_providers_from_doc(path: Path) -> set[str]:
    """Find provider class names mentioned in a documentation file."""
    providers: set[str] = set()
    try:
        content = path.read_text(encoding="utf-8")
        for match in PROVIDER_TABLE_PATTERN.finditer(content):
            providers.add(match.group(1))
    except OSError:
        pass
    return providers


def _validate_markdown_links(path: Path) -> list[Finding]:
    """Check for broken internal links in a markdown file."""
    findings: list[Finding] = []
    try:
        content = path.read_text(encoding="utf-8")
    except OSError:
        return findings

    link_pattern = re.compile(r'\[([^\]]*)\]\(([^)]+)\)')
    # Strip inline code spans to avoid false positives from example links
    inline_code_pattern = re.compile(r'`[^`]*`')
    for i, line in enumerate(content.splitlines(), 1):
        # Remove inline code spans before checking for links so that example
        # links like `[text](path/file.md)` are not flagged as broken.
        clean_line = inline_code_pattern.sub('', line)
        for match in link_pattern.finditer(clean_line):
            target = match.group(2)
            # Skip external links, anchors, and special URLs
            if target.startswith(("http://", "https://", "#", "mailto:")):
                continue
            # Skip regex/glob patterns (contain regex metacharacters)
            if re.search(r'[*?+{}\[\]\\]|\.\*', target):
                continue
            # Remove anchor from path
            target_path_str = target.split("#")[0]
            if not target_path_str:
                continue
            target_path = (path.parent / target_path_str).resolve()
            if not target_path.exists():
                findings.append(Finding(
                    file=_relative(path),
                    category="broken-link",
                    severity="warning",
                    message=f"Broken link to '{target}' (resolved: {_relative(target_path)})",
                    fix_hint=f"Fix or remove the link on line {i}",
                    line=i,
                ))
    return findings


def _check_archive_candidates(docs_dir: Path) -> list[Finding]:  # noqa: C901
    """Find docs that should be moved to archived/ based on age and content."""
    findings: list[Finding] = []
    now = datetime.now(timezone.utc)
    archived_dir = docs_dir / "archived"

    # Patterns that suggest deprecated content
    deprecated_patterns = [
        re.compile(r'\b(deprecated|obsolete|removed|legacy)\b', re.IGNORECASE),
        re.compile(r'\b(UWP|uwp)\b'),  # UWP was removed
    ]

    for md_file in docs_dir.rglob("*.md"):
        # Skip files already in archived/
        if "archived" in md_file.parts:
            continue
        # Skip index files
        if md_file.name in ("README.md", "INDEX.md"):
            continue

        try:
            content = md_file.read_text(encoding="utf-8")
        except OSError:
            continue

        # Check for deprecated content markers
        deprecation_hits = 0
        for pattern in deprecated_patterns:
            if pattern.search(content):
                deprecation_hits += 1

        # Check git age
        git_date = _git_last_modified(md_file)
        if git_date:
            age_days = (now - git_date).days
        else:
            age_days = 0

        # Archive candidates: old + deprecated markers
        if age_days > ARCHIVE_CANDIDATE_DAYS and deprecation_hits >= 2:
            findings.append(Finding(
                file=_relative(md_file),
                category="archive-candidate",
                severity="info",
                message=f"Doc is {age_days} days old with {deprecation_hits} deprecation markers",
                fix_hint=f"Consider moving to {_relative(archived_dir)}/",
            ))

        # Check for stub files (< 3 meaningful lines)
        lines = [ln for ln in content.splitlines() if ln.strip() and not ln.startswith("#")]
        if len(lines) < 3 and age_days > 90:
            findings.append(Finding(
                file=_relative(md_file),
                category="stub-doc",
                severity="info",
                message=f"Stub document with {len(lines)} content lines, {age_days} days old",
                fix_hint="Either expand with content or archive",
            ))

    return findings


# ---------------------------------------------------------------------------
# Commands
# ---------------------------------------------------------------------------

def cmd_freshness(report: Report) -> None:
    """Check staleness of all AI-related documentation."""
    now = datetime.now(timezone.utc)
    all_paths = AI_DOC_PATHS + INSTRUCTION_FILES + SKILL_RESOURCE_PATHS

    for path in all_paths:
        rel = _relative(path)
        if not path.exists():
            report.freshness.append(FreshnessEntry(
                file=rel, last_updated=None, age_days=-1, status="missing",
            ))
            report.findings.append(Finding(
                file=rel, category="missing-file", severity="critical",
                message=f"AI resource file does not exist: {rel}",
                fix_hint="Create the file or remove references to it",
            ))
            continue

        # Try embedded timestamp first, then git
        last_updated_str = _extract_last_updated(path)
        if last_updated_str:
            try:
                last_dt = datetime.fromisoformat(last_updated_str).replace(tzinfo=timezone.utc)
            except ValueError:
                last_dt = None
        else:
            last_dt = _git_last_modified(path)
            if last_dt:
                last_updated_str = last_dt.strftime("%Y-%m-%d")

        if last_dt:
            age_days = (now - last_dt).days
            if age_days >= STALE_CRITICAL_DAYS:
                status = "stale-critical"
                report.findings.append(Finding(
                    file=rel, category="staleness", severity="critical",
                    message=f"AI doc is {age_days} days old (threshold: {STALE_CRITICAL_DAYS})",
                    fix_hint="Review and update the content, then update the 'Last Updated' timestamp",
                ))
            elif age_days >= STALE_WARNING_DAYS:
                status = "stale-warning"
                report.findings.append(Finding(
                    file=rel, category="staleness", severity="warning",
                    message=f"AI doc is {age_days} days old (threshold: {STALE_WARNING_DAYS})",
                    fix_hint="Review the content for accuracy",
                ))
            else:
                status = "current"
        else:
            age_days = -1
            status = "unknown"
            report.findings.append(Finding(
                file=rel, category="no-timestamp", severity="info",
                message="No 'Last Updated' timestamp found and no git history",
                fix_hint="Add '*Last Updated: YYYY-MM-DD*' footer or '**Last Updated:** YYYY-MM-DD' header",
            ))

        report.freshness.append(FreshnessEntry(
            file=rel, last_updated=last_updated_str, age_days=age_days, status=status,
        ))


def cmd_drift(report: Report) -> None:  # noqa: C901
    """Detect when AI documentation diverges from actual code."""
    # 1. Check provider documentation vs code
    code_providers = _extract_providers_from_code()
    claude_md = REPO_ROOT / "CLAUDE.md"
    if claude_md.exists():
        doc_providers = _extract_providers_from_doc(claude_md)
        undocumented = code_providers - doc_providers
        for p in sorted(undocumented):
            report.drift.append(DriftEntry(
                area="providers",
                expected=f"Provider '{p}' documented in CLAUDE.md",
                actual=f"Provider '{p}' exists in code but not in CLAUDE.md",
                severity="warning",
                fix_hint=f"Add '{p}' to the provider tables in CLAUDE.md",
            ))

    # 2. Check workflow count
    actual_workflows = _list_workflow_files()
    actions_md = REPO_ROOT / "docs" / "ai" / "claude" / "CLAUDE.actions.md"
    if actions_md.exists():
        content = actions_md.read_text(encoding="utf-8")
        count_match = re.search(r'uses (\d+) GitHub Actions workflows', content)
        if count_match:
            doc_count = int(count_match.group(1))
            actual_count = len(actual_workflows)
            if doc_count != actual_count:
                report.drift.append(DriftEntry(
                    area="workflows",
                    expected=f"{actual_count} workflows in .github/workflows/",
                    actual=f"CLAUDE.actions.md says {doc_count}",
                    severity="warning",
                    fix_hint=f"Update CLAUDE.actions.md workflow count to {actual_count}",
                ))

    # 3. Check file counts in CLAUDE.md
    if claude_md.exists():
        content = claude_md.read_text(encoding="utf-8")
        src_dir = str(REPO_ROOT / "src")
        tests_dir = str(REPO_ROOT / "tests")

        actual_cs = _count_files(src_dir, ".cs")
        actual_test = _count_files(tests_dir, ".cs")

        # Check C# count
        cs_match = re.search(r'C# Files\s*\|\s*(\d+)', content)
        if cs_match:
            doc_cs = int(cs_match.group(1))
            if abs(doc_cs - actual_cs) > 10:
                report.drift.append(DriftEntry(
                    area="file-counts",
                    expected=f"{actual_cs} C# source files",
                    actual=f"CLAUDE.md says {doc_cs}",
                    severity="info",
                    fix_hint=f"Update C# file count in CLAUDE.md to {actual_cs}",
                ))

        # Check test file count
        test_match = re.search(r'Test Files\s*\|\s*(\d+)', content)
        if test_match:
            doc_test = int(test_match.group(1))
            if abs(doc_test - actual_test) > 10:
                report.drift.append(DriftEntry(
                    area="file-counts",
                    expected=f"{actual_test} test files",
                    actual=f"CLAUDE.md says {doc_test}",
                    severity="info",
                    fix_hint=f"Update test file count in CLAUDE.md to {actual_test}",
                ))

    # 4. Check ADR count
    adr_dir = REPO_ROOT / "docs" / "adr"
    if adr_dir.exists() and claude_md.exists():
        actual_adrs = len([f for f in adr_dir.glob("*.md")
                          if f.name not in ("README.md", "_template.md")])
        content = claude_md.read_text(encoding="utf-8")
        adr_matches = re.findall(r'\| ADR-\d+', content)
        doc_adrs = len(adr_matches)
        if actual_adrs != doc_adrs:
            report.drift.append(DriftEntry(
                area="adrs",
                expected=f"{actual_adrs} ADRs in docs/adr/",
                actual=f"CLAUDE.md lists {doc_adrs} ADRs",
                severity="warning" if abs(actual_adrs - doc_adrs) > 1 else "info",
                fix_hint="Update the ADR table in CLAUDE.md",
            ))

    # 5. Check skill resource files exist
    for res_path in SKILL_RESOURCE_PATHS:
        if not res_path.exists():
            report.drift.append(DriftEntry(
                area="skills",
                expected=f"Skill resource exists: {_relative(res_path)}",
                actual="File is missing",
                severity="critical",
                fix_hint=f"Create {_relative(res_path)} or remove references to it",
            ))

    # 6. Check prompt files referenced in prompts README
    prompts_readme = REPO_ROOT / ".github" / "prompts" / "README.md"
    prompts_dir = REPO_ROOT / ".github" / "prompts"
    if prompts_readme.exists() and prompts_dir.exists():
        actual_prompts = {f.name for f in prompts_dir.glob("*.prompt.yml")}
        readme_content = prompts_readme.read_text(encoding="utf-8")
        # Match prompt filenames in backtick notation OR in markdown link notation
        referenced_prompts = set(re.findall(
            r'[`\[](\w[\w-]+\.prompt\.yml)[`\]\(]', readme_content
        ))
        missing_from_readme = actual_prompts - referenced_prompts
        for p in sorted(missing_from_readme):
            report.drift.append(DriftEntry(
                area="prompts",
                expected=f"Prompt '{p}' documented in prompts README",
                actual="Prompt exists on disk but not in README",
                severity="info",
                fix_hint=f"Add '{p}' to .github/prompts/README.md",
            ))


def cmd_validate_refs(report: Report) -> None:
    """Validate cross-references between AI documentation files."""
    all_paths = AI_DOC_PATHS + INSTRUCTION_FILES
    for path in all_paths:
        if path.exists():
            report.findings.extend(_validate_markdown_links(path))


def cmd_archive_stale(report: Report, dry_run: bool = True) -> None:
    """Find and optionally move deprecated docs to archived/."""
    docs_dir = REPO_ROOT / "docs"
    archived_dir = docs_dir / "archived"

    archive_findings = _check_archive_candidates(docs_dir)
    report.findings.extend(archive_findings)

    # Also check for docs already marked as deprecated in evaluations/plans
    for sub in ["evaluations", "plans"]:
        sub_dir = docs_dir / sub
        if not sub_dir.exists():
            continue
        for md_file in sub_dir.glob("*.md"):
            try:
                content = md_file.read_text(encoding="utf-8")
            except OSError:
                continue
            # Check for explicit "Status: Archived/Completed/Superseded"
            if re.search(r'Status:\s*(Archived|Completed|Superseded)', content, re.IGNORECASE):
                rel = _relative(md_file)
                if not dry_run:
                    archived_dir.mkdir(parents=True, exist_ok=True)
                    dest = archived_dir / md_file.name
                    if not dest.exists():
                        shutil.move(str(md_file), str(dest))
                        report.archived.append(f"{rel} -> {_relative(dest)}")
                else:
                    report.findings.append(Finding(
                        file=rel, category="archive-candidate", severity="info",
                        message="Document marked as Archived/Completed/Superseded",
                        fix_hint=f"Move to {_relative(archived_dir)}/ (use --execute to auto-move)",
                    ))


def cmd_sync_report(report: Report, output: Path | None = None) -> None:  # noqa: C901
    """Generate a comprehensive sync report as markdown."""
    # Run all sub-checks
    cmd_freshness(report)
    cmd_drift(report)
    cmd_validate_refs(report)
    cmd_archive_stale(report)

    # Build markdown
    lines: list[str] = [
        "# AI Documentation Sync Report",
        "",
        f"*Generated: {report.timestamp}*",
        "",
    ]

    # Summary
    critical = sum(1 for f in report.findings if f.severity == "critical")
    warning = sum(1 for f in report.findings if f.severity == "warning")
    info = sum(1 for f in report.findings if f.severity == "info")
    report.summary = {"critical": critical, "warning": warning, "info": info}

    lines.append(f"## Summary: {critical} critical, {warning} warnings, {info} info")
    lines.append("")

    # Freshness table
    if report.freshness:
        lines.append("## Document Freshness")
        lines.append("")
        lines.append("| File | Last Updated | Age (days) | Status |")
        lines.append("|------|-------------|-----------|--------|")
        for entry in sorted(report.freshness, key=lambda e: -(e.age_days or 0)):
            status_icon = {
                "current": "OK",
                "stale-warning": "WARNING",
                "stale-critical": "CRITICAL",
                "missing": "MISSING",
                "unknown": "?",
            }.get(entry.status, "?")
            lines.append(
                f"| `{entry.file}` | {entry.last_updated or 'N/A'} "
                f"| {entry.age_days if entry.age_days >= 0 else 'N/A'} | {status_icon} |"
            )
        lines.append("")

    # Drift entries
    if report.drift:
        lines.append("## Documentation Drift")
        lines.append("")
        for d in report.drift:
            lines.append(f"- **[{d.severity.upper()}] {d.area}**: {d.actual}")
            lines.append(f"  - Expected: {d.expected}")
            lines.append(f"  - Fix: {d.fix_hint}")
        lines.append("")

    # Findings by category
    if report.findings:
        lines.append("## Findings")
        lines.append("")
        by_cat: dict[str, list[Finding]] = {}
        for f in report.findings:
            by_cat.setdefault(f.category, []).append(f)
        for cat, cat_findings in sorted(by_cat.items()):
            lines.append(f"### {cat.replace('-', ' ').title()}")
            for f in cat_findings:
                lines.append(f"- **[{f.severity.upper()}]** `{f.file}`{f' (line {f.line})' if f.line else ''}: {f.message}")
                if f.fix_hint:
                    lines.append(f"  - Fix: {f.fix_hint}")
            lines.append("")

    md_content = "\n".join(lines)

    if output:
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(md_content, encoding="utf-8")
        print(f"Report written to {output}", file=sys.stderr)
    else:
        print(md_content)


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

def main() -> int:  # noqa: C901
    parser = argparse.ArgumentParser(
        description="AI Documentation Maintenance — freshness, drift, and archive automation",
        epilog=__doc__,
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    parser.add_argument(
        "command",
        choices=["freshness", "drift", "archive-stale", "validate-refs", "sync-report", "full"],
        help="Maintenance command to run",
    )
    parser.add_argument("--root", "-r", type=Path, help="Override repository root")
    parser.add_argument("--output", "-o", type=Path, help="Write markdown output to file")
    parser.add_argument("--json-output", "-j", type=Path, help="Write JSON output to file")
    mode_group = parser.add_mutually_exclusive_group()
    mode_group.add_argument(
        "--dry-run",
        action="store_true",
        help="Preview changes without modifying files (default when neither flag is given)",
    )
    mode_group.add_argument(
        "--execute",
        action="store_true",
        help="Actually move/archive files; without this flag the command runs in dry-run mode",
    )
    parser.add_argument("--summary", "-s", action="store_true", help="Print summary to stdout")

    args = parser.parse_args()

    if args.root:
        global REPO_ROOT, AI_DOC_PATHS, INSTRUCTION_FILES, SKILL_RESOURCE_PATHS
        REPO_ROOT = args.root.resolve()
        AI_DOC_PATHS, INSTRUCTION_FILES, SKILL_RESOURCE_PATHS = _build_path_lists(REPO_ROOT)

    report = Report(command=args.command)
    dry_run = not args.execute

    if args.command == "freshness":
        cmd_freshness(report)
    elif args.command == "drift":
        cmd_drift(report)
    elif args.command == "validate-refs":
        cmd_validate_refs(report)
    elif args.command == "archive-stale":
        cmd_archive_stale(report, dry_run=dry_run)
    elif args.command == "sync-report":
        cmd_sync_report(report, output=args.output)
    elif args.command == "full":
        cmd_freshness(report)
        cmd_drift(report)
        cmd_validate_refs(report)
        cmd_archive_stale(report, dry_run=dry_run)

    # Compute summary
    report.summary = {
        "critical": sum(1 for f in report.findings if f.severity == "critical"),
        "warning": sum(1 for f in report.findings if f.severity == "warning"),
        "info": sum(1 for f in report.findings if f.severity == "info"),
        "drift_items": len(report.drift),
        "stale_docs": sum(1 for f in report.freshness if f.status.startswith("stale")),
        "archived": len(report.archived),
    }

    # JSON output
    if args.json_output:
        json_data = {
            "timestamp": report.timestamp,
            "command": report.command,
            "summary": report.summary,
            "freshness": [asdict(e) for e in report.freshness],
            "drift": [asdict(e) for e in report.drift],
            "findings": [asdict(f) for f in report.findings],
            "archived": report.archived,
        }
        args.json_output.parent.mkdir(parents=True, exist_ok=True)
        args.json_output.write_text(json.dumps(json_data, indent=2), encoding="utf-8")
        print(f"JSON report written to {args.json_output}", file=sys.stderr)
    elif args.command not in ("sync-report",):
        # Print JSON to stdout for machine consumption
        json_data = {
            "timestamp": report.timestamp,
            "command": report.command,
            "summary": report.summary,
            "freshness": [asdict(e) for e in report.freshness] if report.freshness else [],
            "drift": [asdict(e) for e in report.drift] if report.drift else [],
            "findings": [asdict(f) for f in report.findings] if report.findings else [],
            "archived": report.archived,
        }
        print(json.dumps(json_data, indent=2))

    if args.summary:
        s = report.summary
        print(f"\nSummary: {s.get('critical', 0)} critical, {s.get('warning', 0)} warnings, "
              f"{s.get('info', 0)} info, {s.get('drift_items', 0)} drift items, "
              f"{s.get('stale_docs', 0)} stale docs", file=sys.stderr)

    # Exit code: 0 = clean, 1 = has critical or warning findings
    critical = report.summary.get("critical", 0)
    warning = report.summary.get("warning", 0)
    return 1 if (critical + warning) > 0 else 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as exc:
        print(f"Error: {exc}", file=sys.stderr)
        sys.exit(2)
