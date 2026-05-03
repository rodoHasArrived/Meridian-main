#!/usr/bin/env python3
"""
Documentation Health Dashboard Generator

Analyzes the repository's Markdown documentation and produces a health
dashboard report with actionable metrics.  The output is a Markdown file
suitable for inclusion in generated docs, an optional JSON file for
programmatic consumption, and an optional one-line summary for
``GITHUB_STEP_SUMMARY``.

Metrics computed:
  - Total documentation file count (``*.md``)
  - Total documentation line count
  - Orphaned docs (Markdown files not linked from any other Markdown file)
  - Files with no headings
  - Stale files (last git commit older than 90 days, falls back to mtime)
  - TODO / FIXME count inside documentation
  - Average documentation file size (in lines)
  - Overall health score (0-100)

Usage:
    python3 generate-health-dashboard.py --output docs/generated/health-dashboard.md
    python3 generate-health-dashboard.py --json-output health.json --summary
    python3 generate-health-dashboard.py --root /path/to/repo --output report.md --json-output report.json
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import List, Optional, Sequence

from dashboard_rendering import (
    current_utc_timestamp,
    load_canonical_json,
    render_markdown_from_json,
    write_canonical_json,
)


# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

EXCLUDE_DIRS: frozenset[str] = frozenset(
    {".git", ".github", "node_modules", "bin", "obj", "__pycache__", ".vs"}
)

STALE_THRESHOLD_DAYS: int = 90

# Patterns that count as "TODO" markers inside documentation files.
_TODO_PATTERN: re.Pattern[str] = re.compile(
    r"\b(TODO|FIXME|HACK|XXX)\b", re.IGNORECASE
)

# Pattern used to extract Markdown links to other files.
# Matches [text](path) where path ends with .md (optional fragment / query).
_MD_LINK_PATTERN: re.Pattern[str] = re.compile(
    r"\[(?:[^\]]*)\]\(([^)]+\.md(?:#[^)]*)?)\)"
)

# Headings: lines starting with one or more '#'.
_HEADING_PATTERN: re.Pattern[str] = re.compile(r"^\s*#{1,6}\s+\S")


# ---------------------------------------------------------------------------
# Data models
# ---------------------------------------------------------------------------


@dataclass
class FileInfo:
    """Lightweight metadata for a single Markdown file."""

    path: str
    line_count: int
    has_heading: bool
    todo_count: int
    last_modified_utc: str  # ISO-8601
    stale: bool


@dataclass
class HealthMetrics:
    """Aggregated documentation health metrics."""

    total_files: int = 0
    total_lines: int = 0
    orphaned_count: int = 0
    no_heading_count: int = 0
    stale_count: int = 0
    todo_count: int = 0
    average_lines: float = 0.0
    health_score: int = 0

    # Detail lists
    orphaned_files: List[str] = field(default_factory=list)
    no_heading_files: List[str] = field(default_factory=list)
    stale_files: List[str] = field(default_factory=list)
    all_files: List[FileInfo] = field(default_factory=list)

    # Metadata
    scan_time: str = ""
    root_dir: str = ""

    def to_dict(self) -> dict:
        """Serialise to a plain dictionary (JSON-friendly)."""
        data = asdict(self)
        # ``all_files`` already serialises via ``asdict``.
        return data


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------


def _should_skip(path: Path) -> bool:
    """Return *True* when *path* resides under an excluded directory."""
    for part in path.parts:
        if part in EXCLUDE_DIRS:
            return True
    return False


def _read_text_safe(path: Path) -> Optional[str]:
    """Read a file as UTF-8, returning *None* on failure."""
    try:
        return path.read_text(encoding="utf-8", errors="replace")
    except (OSError, PermissionError) as exc:
        print(f"Warning: could not read {path}: {exc}", file=sys.stderr)
        return None


def _git_last_commit_date(path: Path, root: Path) -> Optional[datetime]:
    """Return the last git commit date for *path*, or *None*."""
    try:
        result = subprocess.run(
            ["git", "log", "-1", "--format=%aI", "--", str(path)],
            capture_output=True,
            text=True,
            cwd=str(root),
            timeout=10,
        )
        if result.returncode == 0 and result.stdout.strip():
            value = result.stdout.strip()
            if value.endswith("Z"):
                value = value[:-1] + "+00:00"
            return datetime.fromisoformat(value)
    except (FileNotFoundError, subprocess.TimeoutExpired, OSError):
        pass
    return None


def _file_mtime_utc(path: Path) -> datetime:
    """Return the file modification time in UTC."""
    ts = path.stat().st_mtime
    return datetime.fromtimestamp(ts, tz=timezone.utc)


def _last_modified(path: Path, root: Path) -> datetime:
    """Best-effort last-modified date: git commit date or file mtime."""
    git_date = _git_last_commit_date(path, root)
    if git_date is not None:
        # Ensure timezone-aware
        if git_date.tzinfo is None:
            git_date = git_date.replace(tzinfo=timezone.utc)
        return git_date
    return _file_mtime_utc(path)


def _repo_relative_path(path: Path, root: Path) -> str:
    """Return a stable repository-relative path for generated reports."""
    return path.relative_to(root).as_posix()


def _display_path(path: str) -> str:
    """Normalize stored path strings before rendering Markdown."""
    return path.replace("\\", "/")


# ---------------------------------------------------------------------------
# Link extraction & orphan detection
# ---------------------------------------------------------------------------


def _normalise_link_target(link: str, source_dir: Path, root: Path) -> Optional[str]:
    """Resolve a Markdown link target to a repo-relative path string.

    Returns *None* when the target is an absolute URL or cannot be resolved.
    """
    # Strip fragment / query parts.
    link = link.split("#")[0].split("?")[0].strip()
    if not link:
        return None
    # Skip absolute URLs.
    if link.startswith(("http://", "https://", "mailto:")):
        return None
    resolved = (source_dir / link).resolve()
    try:
        return resolved.relative_to(root.resolve()).as_posix()
    except ValueError:
        return None


def _collect_links(
    md_files: Sequence[Path], root: Path
) -> dict[str, set[str]]:
    """Build a mapping ``{source_rel_path: {target_rel_paths ...}}``."""
    links: dict[str, set[str]] = {}
    for md_path in md_files:
        content = _read_text_safe(md_path)
        if content is None:
            continue
        try:
            source_rel = _repo_relative_path(md_path, root)
        except ValueError:
            continue
        targets: set[str] = set()
        for match in _MD_LINK_PATTERN.finditer(content):
            target = _normalise_link_target(match.group(1), md_path.parent, root)
            if target is not None:
                targets.add(target)
        links[source_rel] = targets
    return links


def _find_orphans(
    md_files: Sequence[Path], root: Path
) -> list[str]:
    """Return repo-relative paths of Markdown files not linked by any other file."""
    link_map = _collect_links(md_files, root)

    # Set of all files that are the *target* of at least one link.
    linked_targets: set[str] = set()
    for targets in link_map.values():
        linked_targets.update(targets)

    all_rel: set[str] = set()
    for md_path in md_files:
        try:
            all_rel.add(_repo_relative_path(md_path, root))
        except ValueError:
            pass

    # A file is orphaned if no other file links to it.
    # Exception: top-level entry-point files are not considered orphans.
    entry_points = {"README.md", "CLAUDE.md", "LICENSE", "CHANGELOG.md"}
    orphans: list[str] = []
    for rel_path in sorted(all_rel):
        if Path(rel_path).name in entry_points:
            continue
        if rel_path not in linked_targets:
            orphans.append(rel_path)
    return orphans


# ---------------------------------------------------------------------------
# Core analysis
# ---------------------------------------------------------------------------


def _analyse_file(path: Path, root: Path, now: datetime) -> FileInfo:
    """Analyse a single Markdown file and return its metadata."""
    content = _read_text_safe(path) or ""
    lines = content.splitlines()
    line_count = len(lines)
    has_heading = any(_HEADING_PATTERN.match(line) for line in lines)
    todo_count = sum(1 for line in lines for _ in _TODO_PATTERN.finditer(line))

    last_mod = _last_modified(path, root)
    stale = (now - last_mod).days > STALE_THRESHOLD_DAYS

    try:
        rel = _repo_relative_path(path, root)
    except ValueError:
        rel = _display_path(str(path))

    return FileInfo(
        path=rel,
        line_count=line_count,
        has_heading=has_heading,
        todo_count=todo_count,
        last_modified_utc=last_mod.isoformat(),
        stale=stale,
    )


def compute_health_score(metrics: HealthMetrics) -> int:
    """Compute an overall health score in the range [0, 100].

    The score is a weighted composite:
      - 30 pts: low orphan ratio
      - 25 pts: all files have headings
      - 20 pts: low staleness ratio
      - 15 pts: low TODO density
      - 10 pts: sufficient average size (> 20 lines)
    """
    if metrics.total_files == 0:
        return 0

    # Orphan ratio (0 = perfect).
    orphan_ratio = metrics.orphaned_count / metrics.total_files
    orphan_score = max(0.0, 1.0 - orphan_ratio) * 30

    # Heading coverage.
    heading_ratio = 1.0 - (metrics.no_heading_count / metrics.total_files)
    heading_score = heading_ratio * 25

    # Staleness ratio.
    stale_ratio = metrics.stale_count / metrics.total_files
    stale_score = max(0.0, 1.0 - stale_ratio) * 20

    # TODO density (per 1000 lines; < 5 is ideal).
    if metrics.total_lines > 0:
        todo_density = (metrics.todo_count / metrics.total_lines) * 1000
        todo_score = max(0.0, 1.0 - (todo_density / 10)) * 15
    else:
        todo_score = 15.0

    # Average size bonus.
    if metrics.average_lines >= 20:
        size_score = 10.0
    elif metrics.average_lines > 0:
        size_score = (metrics.average_lines / 20) * 10
    else:
        size_score = 0.0

    raw = orphan_score + heading_score + stale_score + todo_score + size_score
    return max(0, min(100, round(raw)))


def analyse(root: Path) -> HealthMetrics:
    """Run full documentation health analysis rooted at *root*."""
    root = root.resolve()
    now = datetime.now(tz=timezone.utc)

    # Discover all Markdown files, respecting exclusions.
    md_files: list[Path] = []
    for md_path in root.rglob("*.md"):
        if _should_skip(md_path.relative_to(root)):
            continue
        if md_path.is_file():
            md_files.append(md_path)

    # Per-file analysis.
    file_infos: list[FileInfo] = []
    total_lines = 0
    todo_count = 0
    no_heading_files: list[str] = []
    stale_files: list[str] = []

    for md_path in md_files:
        info = _analyse_file(md_path, root, now)
        file_infos.append(info)
        total_lines += info.line_count
        todo_count += info.todo_count
        if not info.has_heading:
            no_heading_files.append(info.path)
        if info.stale:
            stale_files.append(info.path)

    # Orphan detection.
    orphaned = _find_orphans(md_files, root)

    total_files = len(file_infos)
    avg_lines = round(total_lines / total_files, 1) if total_files > 0 else 0.0

    metrics = HealthMetrics(
        total_files=total_files,
        total_lines=total_lines,
        orphaned_count=len(orphaned),
        no_heading_count=len(no_heading_files),
        stale_count=len(stale_files),
        todo_count=todo_count,
        average_lines=avg_lines,
        orphaned_files=orphaned,
        no_heading_files=no_heading_files,
        stale_files=stale_files,
        all_files=file_infos,
        scan_time=current_utc_timestamp(),
        root_dir=str(root),
    )
    metrics.health_score = compute_health_score(metrics)
    return metrics


# ---------------------------------------------------------------------------
# Output formatting
# ---------------------------------------------------------------------------


def _ascii_bar(value: int, width: int = 30) -> str:
    """Return an ASCII progress bar for a 0-100 value."""
    filled = round(value * width / 100)
    empty = width - filled
    return f"[{'#' * filled}{'-' * empty}] {value}/100"


def _score_label(score: int) -> str:
    """Human-readable label for the health score."""
    if score >= 90:
        return "Excellent"
    if score >= 75:
        return "Good"
    if score >= 50:
        return "Fair"
    if score >= 25:
        return "Needs Attention"
    return "Critical"


def _render_markdown_body_from_payload(payload: dict) -> str:  # noqa: C901
    metrics = HealthMetrics(
        **{k: v for k, v in payload.items() if k in {"total_files","total_lines","orphaned_count","no_heading_count","stale_count","todo_count","average_lines","health_score","orphaned_files","no_heading_files","stale_files","scan_time","root_dir"}},
        all_files=[FileInfo(**item) for item in payload.get("all_files", [])],
    )
    lines: list[str] = []

    lines.append("# Documentation Health Dashboard")
    lines.append("")
    lines.append("> Auto-generated documentation health report. Do not edit manually.")
    lines.append(f"> Last updated: {metrics.scan_time}")
    lines.append("")

    # Overall score
    lines.append("## Overall Health Score")
    lines.append("")
    lines.append("```text")
    lines.append(f"  {_ascii_bar(metrics.health_score)}")
    lines.append(f"  Rating: {_score_label(metrics.health_score)}")
    lines.append("```")
    lines.append("")

    # Summary table
    lines.append("## Summary")
    lines.append("")
    lines.append("| Metric | Value |")
    lines.append("| -------- | ------- |")
    lines.append(f"| Total documentation files | {metrics.total_files} |")
    lines.append(f"| Total lines | {metrics.total_lines:,} |")
    lines.append(f"| Average file size (lines) | {metrics.average_lines} |")
    lines.append(f"| Orphaned files | {metrics.orphaned_count} |")
    lines.append(f"| Files without headings | {metrics.no_heading_count} |")
    lines.append(
        f"| Stale files (>{STALE_THRESHOLD_DAYS} days) | {metrics.stale_count} |"
    )
    lines.append(f"| TODO/FIXME markers | {metrics.todo_count} |")
    lines.append(f"| **Health score** | **{metrics.health_score}/100** |")
    lines.append("")

    # Score breakdown
    lines.append("### Score Breakdown")
    lines.append("")
    lines.append("| Component | Weight | Description |")
    lines.append("| ----------- | -------- | ------------- |")
    lines.append("| Orphan ratio | 30 pts | Fewer orphaned files is better |")
    lines.append("| Heading coverage | 25 pts | All files should have at least one heading |")
    lines.append("| Freshness | 20 pts | Files updated within the last 90 days |")
    lines.append("| TODO density | 15 pts | Lower density of TODO/FIXME markers |")
    lines.append("| Average size | 10 pts | Files averaging at least 20 lines |")
    lines.append("")

    # Top priorities
    lines.append("## Top Priorities for Improvement")
    lines.append("")

    has_priorities = False

    if metrics.no_heading_files:
        has_priorities = True
        lines.append("### Files Without Headings")
        lines.append("")
        lines.append(
            "These files lack a Markdown heading, making them harder to navigate:"
        )
        lines.append("")
        for f in sorted(_display_path(f) for f in metrics.no_heading_files)[:15]:
            lines.append(f"- `{f}`")
        if len(metrics.no_heading_files) > 15:
            remaining = len(metrics.no_heading_files) - 15
            lines.append(f"- ... and {remaining} more")
        lines.append("")

    if metrics.orphaned_files:
        has_priorities = True
        lines.append("### Orphaned Documentation")
        lines.append("")
        lines.append(
            "These files are not linked from any other Markdown file in the repository:"
        )
        lines.append("")
        for f in sorted(_display_path(f) for f in metrics.orphaned_files)[:20]:
            lines.append(f"- `{f}`")
        if len(metrics.orphaned_files) > 20:
            remaining = len(metrics.orphaned_files) - 20
            lines.append(f"- ... and {remaining} more")
        lines.append("")

    if metrics.stale_files:
        has_priorities = True
        lines.append("### Stale Documentation")
        lines.append("")
        lines.append(
            f"These files have not been updated in over {STALE_THRESHOLD_DAYS} days:"
        )
        lines.append("")
        for f in sorted(_display_path(f) for f in metrics.stale_files)[:20]:
            lines.append(f"- `{f}`")
        if len(metrics.stale_files) > 20:
            remaining = len(metrics.stale_files) - 20
            lines.append(f"- ... and {remaining} more")
        lines.append("")

    if not has_priorities:
        lines.append("No immediate issues found. Documentation is in good shape!")
        lines.append("")

    # Trend placeholder
    lines.append("## Trend")
    lines.append("")
    lines.append(
        "<!-- Trend data will be appended by CI when historical snapshots are available. -->"
    )
    lines.append("")
    lines.append("| Date | Score | Files | Orphans | Stale |")
    lines.append("| ------ | ------- | ------- | --------- | ------- |")
    lines.append(
        f"| {metrics.scan_time[:10]} | {metrics.health_score} "
        f"| {metrics.total_files} | {metrics.orphaned_count} | {metrics.stale_count} |"
    )
    lines.append("")

    # Footer
    lines.append("---")
    lines.append("")
    lines.append("_This file is auto-generated. Do not edit manually._")
    lines.append("")

    return "\n".join(lines)


def generate_markdown_from_json_payload(payload: dict) -> str:
    return render_markdown_from_json(
        json_payload=payload,
        render_body=_render_markdown_body_from_payload,
        data_sources=["repo markdown (*.md)", "git commit metadata"],
    )


def generate_summary(metrics: HealthMetrics) -> str:
    """Return a compact single-paragraph summary for ``GITHUB_STEP_SUMMARY``."""
    return (
        f"Documentation Health: **{metrics.health_score}/100** "
        f"({_score_label(metrics.health_score)}) | "
        f"{metrics.total_files} files, "
        f"{metrics.orphaned_count} orphaned, "
        f"{metrics.no_heading_count} missing headings, "
        f"{metrics.stale_count} stale, "
        f"{metrics.todo_count} TODOs"
    )


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------


def _build_parser() -> argparse.ArgumentParser:
    """Construct the argument parser."""
    parser = argparse.ArgumentParser(
        description=(
            "Generate a documentation health dashboard with metrics and "
            "actionable improvement priorities."
        ),
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=(
            "Examples:\n"
            "  %(prog)s --output docs/generated/health-dashboard.md\n"
            "  %(prog)s --json-output health.json --summary\n"
            "  %(prog)s --root /path/to/repo --output report.md --json-output report.json\n"
        ),
    )
    parser.add_argument(
        "--output",
        "-o",
        type=Path,
        default=None,
        help="Path for the Markdown report output.",
    )
    parser.add_argument(
        "--json-output",
        "-j",
        type=Path,
        default=None,
        help="Path for the JSON metrics output.",
    )
    parser.add_argument(
        "--root",
        "-r",
        type=Path,
        default=Path("."),
        help="Repository root directory (default: current directory).",
    )
    parser.add_argument(
        "--summary",
        "-s",
        action="store_true",
        default=False,
        help="Print a one-line summary to stdout (useful for GITHUB_STEP_SUMMARY).",
    )
    return parser


def main(argv: Optional[Sequence[str]] = None) -> int:  # noqa: C901
    """Entry point for the documentation health dashboard generator."""
    parser = _build_parser()
    args = parser.parse_args(argv)

    root: Path = args.root.resolve()
    if not root.is_dir():
        print(f"Error: root directory does not exist: {root}", file=sys.stderr)
        return 1

    # Run analysis.
    try:
        metrics = analyse(root)
    except Exception as exc:
        print(f"Error during analysis: {exc}", file=sys.stderr)
        return 1

    if args.output is not None and args.json_output is None:
        print("Error: --output requires --json-output so markdown is rendered from canonical JSON.", file=sys.stderr)
        return 1

    if not any(root.rglob("*.md")):
        print("Error: missing required markdown evidence input (*.md files).", file=sys.stderr)
        return 1

    payload = metrics.to_dict()

    if args.json_output is not None:
        try:
            write_canonical_json(payload, args.json_output)
            print(f"JSON metrics written to {args.json_output}")
        except OSError as exc:
            print(f"Error writing JSON output: {exc}", file=sys.stderr)
            return 1

    if args.output is not None:
        try:
            canonical = load_canonical_json(args.json_output)
            args.output.parent.mkdir(parents=True, exist_ok=True)
            args.output.write_text(generate_markdown_from_json_payload(canonical), encoding="utf-8")
            print(f"Markdown report written to {args.output}")
        except OSError as exc:
            print(f"Error writing Markdown report: {exc}", file=sys.stderr)
            return 1

    # Print summary.
    if args.summary:
        print(generate_summary(metrics))

    # If no output flags at all, print summary as a courtesy.
    if args.output is None and args.json_output is None and not args.summary:
        print(generate_summary(metrics))

    return 0


if __name__ == "__main__":
    sys.exit(main())
