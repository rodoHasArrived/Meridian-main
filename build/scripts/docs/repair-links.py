#!/usr/bin/env python3
"""
Broken Internal Link Detector and Repairer for Markdown Documentation.

Scans all markdown files in the documentation tree for internal links
(relative paths), verifies that linked files and anchor targets exist,
and optionally auto-fixes broken paths when the target file has been
moved elsewhere in the repository.

External (HTTP/HTTPS) links are intentionally ignored -- those are
validated by the dedicated markdown-link-check GitHub Action.

Features:
- Detects broken relative file links and anchor references
- Locates moved files anywhere in the repository for auto-fix suggestions
- Generates a markdown report with summary, broken-link table, and fix table
- Prints a GITHUB_STEP_SUMMARY-compatible summary block on request

Usage:
    python3 repair-links.py
    python3 repair-links.py --root /path/to/repo --docs-dir docs/
    python3 repair-links.py --auto-fix --output report.md
    python3 repair-links.py --summary
"""

from __future__ import annotations

import argparse
import os
import re
import sys
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Optional


# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

#: Directories to skip when scanning and indexing.
EXCLUDE_DIRS: frozenset[str] = frozenset({
    ".git",
    "node_modules",
    "bin",
    "obj",
    "__pycache__",
    ".vs",
})

#: Regex that captures markdown links: [text](target)
#: Group 1 = link text, Group 2 = link target (path + optional anchor).
#: Ignores image links (![alt](src)) by requiring the opening bracket is NOT
#: preceded by '!'.
_LINK_PATTERN: re.Pattern[str] = re.compile(
    r"(?<!!)\[([^\]]*)\]\(([^)]+)\)"
)

#: Anchors generated from markdown headings.
#: GitHub-flavoured heading-to-anchor rules:
#: lowercase, strip non-alphanumerics except hyphens/spaces, spaces to hyphens,
#: collapse consecutive hyphens.
_HEADING_PATTERN: re.Pattern[str] = re.compile(r"^#{1,6}\s+(.+)$", re.MULTILINE)

#: Characters stripped from heading text when generating an anchor slug.
_SLUG_STRIP: re.Pattern[str] = re.compile(r"[^\w\s-]", re.UNICODE)

#: Collapse multiple hyphens.
_SLUG_COLLAPSE: re.Pattern[str] = re.compile(r"-{2,}")


# ---------------------------------------------------------------------------
# Data classes
# ---------------------------------------------------------------------------

@dataclass(frozen=True)
class LinkLocation:
    """Source location where a markdown link was found."""

    file: Path
    line: int
    column: int
    link_text: str
    raw_target: str


@dataclass
class BrokenLink:
    """A link that could not be resolved."""

    location: LinkLocation
    reason: str
    suggestion: str = ""


@dataclass
class FixedLink:
    """A link that was auto-repaired."""

    location: LinkLocation
    old_target: str
    new_target: str


@dataclass
class ScanResult:
    """Aggregated results of the link scan."""

    total_links_checked: int = 0
    broken_links: list[BrokenLink] = field(default_factory=list)
    fixed_links: list[FixedLink] = field(default_factory=list)
    files_scanned: int = 0
    scan_time: str = ""


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def _heading_to_anchor(heading_text: str) -> str:
    """Convert a markdown heading string to a GitHub-flavoured anchor slug.

    Rules applied (matching GitHub rendering):
    1. Strip leading/trailing whitespace.
    2. Convert to lowercase.
    3. Remove characters that are not alphanumeric, spaces, or hyphens.
    4. Replace spaces with hyphens.
    5. Collapse consecutive hyphens.

    Args:
        heading_text: Raw heading text (without the leading ``#`` characters).

    Returns:
        The anchor slug, e.g. ``"my-heading"``.
    """
    text = heading_text.strip().lower()
    text = _SLUG_STRIP.sub("", text)
    text = text.replace(" ", "-")
    text = _SLUG_COLLAPSE.sub("-", text)
    return text.strip("-")


def _extract_anchors(content: str) -> set[str]:
    """Return the set of anchor slugs defined by headings in *content*.

    Also considers HTML ``id`` attributes of the form ``id="slug"`` and
    ``<a name="slug">`` patterns that are sometimes used in markdown files.

    Args:
        content: Full text of a markdown file.

    Returns:
        A set of lowercase anchor slugs.
    """
    anchors: set[str] = set()

    # Headings
    for match in _HEADING_PATTERN.finditer(content):
        anchors.add(_heading_to_anchor(match.group(1)))

    # Explicit HTML anchors: <a name="foo"> or id="foo"
    for match in re.finditer(r'(?:name|id)\s*=\s*"([^"]+)"', content):
        anchors.add(match.group(1).lower())

    return anchors


def _is_external_or_special(target: str) -> bool:
    """Return ``True`` if *target* is an external URL or special link.

    We skip ``http://``, ``https://``, ``mailto:``, ``tel:``, ``#``-only
    (same-page anchors resolved separately), and template placeholders.

    Args:
        target: The raw link target string.

    Returns:
        ``True`` when the link should be ignored by internal-link checking.
    """
    stripped = target.strip()
    if not stripped:
        return True
    # External protocols
    if re.match(r"^https?://", stripped, re.IGNORECASE):
        return True
    if stripped.startswith(("mailto:", "tel:", "ftp://", "ftps://")):
        return True
    # Same-page anchor (starts with #, no path component)
    if stripped.startswith("#"):
        return True
    # Template / variable placeholders (e.g. {{url}})
    if stripped.startswith(("{", "$", "%")):
        return True
    return False


def _should_skip_dir(name: str) -> bool:
    """Return ``True`` when a directory name is in the exclusion set.

    Args:
        name: A single directory component name.

    Returns:
        ``True`` if excluded.
    """
    return name in EXCLUDE_DIRS


def _build_file_index(root: Path) -> dict[str, list[Path]]:
    """Build an index mapping each filename to its path(s) under *root*.

    This index is used to locate files that may have been moved to a
    different directory within the repository.

    Args:
        root: The repository root directory.

    Returns:
        A dict from lowercase filename to a list of ``Path`` objects.
    """
    index: dict[str, list[Path]] = {}
    for dirpath, dirnames, filenames in os.walk(root):
        # Prune excluded directories in-place so os.walk skips them.
        dirnames[:] = [d for d in dirnames if not _should_skip_dir(d)]
        for fname in filenames:
            key = fname.lower()
            full = Path(dirpath) / fname
            index.setdefault(key, []).append(full)
    return index


def _find_moved_file(
    filename: str,
    file_index: dict[str, list[Path]],
    source_file: Path,
) -> Optional[Path]:
    """Try to locate *filename* elsewhere in the repository.

    If the file is found in exactly one alternative location, that path is
    returned.  When there are multiple candidates, the one closest to
    *source_file* (fewest ``../`` hops) is preferred.

    Args:
        filename: The basename of the file to search for.
        file_index: Precomputed file-location index.
        source_file: The markdown file that contains the broken link.

    Returns:
        A ``Path`` to the best match, or ``None`` if no match is found.
    """
    key = filename.lower()
    candidates = file_index.get(key)
    if not candidates:
        return None

    # Filter out the source file itself.
    candidates = [c for c in candidates if c.resolve() != source_file.resolve()]
    if not candidates:
        return None

    if len(candidates) == 1:
        return candidates[0]

    # Pick the candidate with the shortest relative path from source_file.
    def _rel_len(candidate: Path) -> int:
        try:
            rel = os.path.relpath(candidate, source_file.parent)
            return len(Path(rel).parts)
        except ValueError:
            return 9999

    candidates.sort(key=_rel_len)
    return candidates[0]


def _make_relative(target: Path, source_dir: Path) -> str:
    """Compute a clean POSIX-style relative path from *source_dir* to *target*.

    Args:
        target: The absolute path of the target file.
        source_dir: The directory containing the source markdown file.

    Returns:
        A relative POSIX path string (e.g. ``"../other/file.md"``).
    """
    try:
        rel = os.path.relpath(target, source_dir)
    except ValueError:
        # Different drive on Windows -- fall back to absolute.
        return str(target)
    return Path(rel).as_posix()


# ---------------------------------------------------------------------------
# Core scanning logic
# ---------------------------------------------------------------------------

def _collect_markdown_files(docs_root: Path) -> list[Path]:
    """Recursively collect all ``.md`` files under *docs_root*.

    Respects :data:`EXCLUDE_DIRS`.

    Args:
        docs_root: Root directory to search.

    Returns:
        Sorted list of markdown file paths.
    """
    md_files: list[Path] = []
    for dirpath, dirnames, filenames in os.walk(docs_root):
        dirnames[:] = [d for d in dirnames if not _should_skip_dir(d)]
        for fname in filenames:
            if fname.lower().endswith(".md"):
                md_files.append(Path(dirpath) / fname)
    md_files.sort()
    return md_files


def _scan_file(  # noqa: C901
    md_file: Path,
    repo_root: Path,
    file_index: dict[str, list[Path]],
    auto_fix: bool,
    result: ScanResult,
) -> list[tuple[int, str, str]]:
    """Scan a single markdown file for broken internal links.

    Args:
        md_file: Path to the markdown file to scan.
        repo_root: Repository root for resolving paths.
        file_index: Precomputed filename index for moved-file detection.
        auto_fix: If ``True``, attempt to repair broken links in-place.
        result: Mutable :class:`ScanResult` to accumulate into.

    Returns:
        A list of ``(line_number, old_target, new_target)`` tuples for lines
        that were rewritten when *auto_fix* is ``True``.  Empty otherwise.
    """
    try:
        content = md_file.read_text(encoding="utf-8", errors="replace")
    except OSError as exc:
        print(f"Warning: could not read {md_file}: {exc}", file=sys.stderr)
        return []

    lines = content.splitlines(keepends=True)
    replacements: list[tuple[int, str, str]] = []  # (line_idx, old, new)

    for line_idx, line in enumerate(lines):
        for match in _LINK_PATTERN.finditer(line):
            link_text = match.group(1)
            raw_target = match.group(2).strip()

            # Skip external and special links.
            if _is_external_or_special(raw_target):
                continue

            result.total_links_checked += 1

            # Split target into file path and optional anchor.
            if "#" in raw_target:
                path_part, anchor_part = raw_target.split("#", 1)
                anchor_part = anchor_part.lower()
            else:
                path_part = raw_target
                anchor_part = ""

            # Normalise the path part -- it may be empty when the link is
            # ``[text](#anchor)`` which we already skipped above via
            # ``_is_external_or_special``.
            path_part = path_part.strip()

            location = LinkLocation(
                file=md_file,
                line=line_idx + 1,
                column=match.start() + 1,
                link_text=link_text,
                raw_target=raw_target,
            )

            if path_part:
                # Resolve relative to the source file's directory.
                resolved = (md_file.parent / path_part).resolve()

                if not resolved.exists():
                    # Target file does not exist -- try to locate it.
                    basename = Path(path_part).name
                    moved = _find_moved_file(basename, file_index, md_file)

                    suggestion = ""
                    if moved is not None:
                        new_rel = _make_relative(moved, md_file.parent)
                        new_target = (
                            f"{new_rel}#{anchor_part}" if anchor_part else new_rel
                        )
                        suggestion = new_target

                        if auto_fix:
                            replacements.append((line_idx, raw_target, new_target))
                            result.fixed_links.append(
                                FixedLink(
                                    location=location,
                                    old_target=raw_target,
                                    new_target=new_target,
                                )
                            )
                            # Don't also record as broken -- it's been fixed.
                            continue

                    result.broken_links.append(
                        BrokenLink(
                            location=location,
                            reason="File not found",
                            suggestion=suggestion,
                        )
                    )
                    continue

                # File exists -- validate anchor if present.
                if anchor_part and resolved.is_file():
                    try:
                        target_content = resolved.read_text(
                            encoding="utf-8", errors="replace"
                        )
                    except OSError:
                        target_content = ""

                    anchors = _extract_anchors(target_content)
                    if anchor_part not in anchors:
                        result.broken_links.append(
                            BrokenLink(
                                location=location,
                                reason=f"Anchor '#{anchor_part}' not found in target file",
                            )
                        )
            # If path_part is empty but we got here, the target was
            # something like "#anchor" which is already handled.

    return replacements


def _apply_replacements(
    md_file: Path,
    replacements: list[tuple[int, str, str]],
) -> None:
    """Write back auto-fixed link targets into *md_file*.

    Each entry in *replacements* is ``(line_index, old_target, new_target)``.
    Only the first occurrence of *old_target* on the specified line is
    replaced to avoid unintended edits.

    Args:
        md_file: The markdown file to rewrite.
        replacements: Collected replacement triples from :func:`_scan_file`.
    """
    try:
        content = md_file.read_text(encoding="utf-8", errors="replace")
    except OSError as exc:
        print(f"Warning: could not re-read {md_file} for patching: {exc}", file=sys.stderr)
        return

    lines = content.splitlines(keepends=True)

    for line_idx, old_target, new_target in replacements:
        if 0 <= line_idx < len(lines):
            # Replace only inside the parenthesised portion of the markdown link.
            lines[line_idx] = lines[line_idx].replace(
                f"]({old_target})", f"]({new_target})", 1
            )

    try:
        md_file.write_text("".join(lines), encoding="utf-8")
    except OSError as exc:
        print(f"Warning: could not write {md_file}: {exc}", file=sys.stderr)


# ---------------------------------------------------------------------------
# Report generation
# ---------------------------------------------------------------------------

def _relative_display(path: Path, root: Path) -> str:
    """Return a short display path relative to *root*.

    Args:
        path: Absolute path to display.
        root: Repository root for computing relative representation.

    Returns:
        A POSIX-style relative path string.
    """
    try:
        return path.resolve().relative_to(root.resolve()).as_posix()
    except ValueError:
        return str(path)


def generate_report(result: ScanResult, repo_root: Path) -> str:
    """Produce a markdown report from *result*.

    The report contains:
    - A summary table (links checked, broken, fixed).
    - A table of broken links (file, line, target, suggestion).
    - A table of auto-fixed links (file, line, old target, new target).

    Args:
        result: The aggregated scan result.
        repo_root: Repository root for relative-path display.

    Returns:
        A complete markdown document as a string.
    """
    lines: list[str] = []

    lines.append("# Internal Link Check Report")
    lines.append("")
    lines.append("> Auto-generated by `repair-links.py`")
    lines.append(f"> Scanned at: {result.scan_time}")
    lines.append("")

    # -- Summary --
    lines.append("## Summary")
    lines.append("")
    lines.append("| Metric | Count |")
    lines.append("|--------|------:|")
    lines.append(f"| Files scanned | {result.files_scanned} |")
    lines.append(f"| Internal links checked | {result.total_links_checked} |")
    lines.append(f"| Broken links | {len(result.broken_links)} |")
    lines.append(f"| Auto-fixed links | {len(result.fixed_links)} |")
    lines.append("")

    # -- Broken links --
    if result.broken_links:
        lines.append("## Broken Links")
        lines.append("")
        lines.append("| File | Line | Target | Reason | Suggestion |")
        lines.append("|------|-----:|--------|--------|------------|")
        for bl in result.broken_links:
            display = _relative_display(bl.location.file, repo_root)
            target_escaped = bl.location.raw_target.replace("|", "\\|")
            reason_escaped = bl.reason.replace("|", "\\|")
            suggestion_escaped = (
                bl.suggestion.replace("|", "\\|") if bl.suggestion else "-"
            )
            lines.append(
                f"| `{display}` | {bl.location.line} "
                f"| `{target_escaped}` | {reason_escaped} "
                f"| {suggestion_escaped} |"
            )
        lines.append("")
    else:
        lines.append("## Broken Links")
        lines.append("")
        lines.append("No broken internal links found.")
        lines.append("")

    # -- Fixed links --
    if result.fixed_links:
        lines.append("## Auto-Fixed Links")
        lines.append("")
        lines.append("| File | Line | Old Target | New Target |")
        lines.append("|------|-----:|------------|------------|")
        for fl in result.fixed_links:
            display = _relative_display(fl.location.file, repo_root)
            old_escaped = fl.old_target.replace("|", "\\|")
            new_escaped = fl.new_target.replace("|", "\\|")
            lines.append(
                f"| `{display}` | {fl.location.line} "
                f"| `{old_escaped}` | `{new_escaped}` |"
            )
        lines.append("")
    elif not result.broken_links:
        lines.append("## Auto-Fixed Links")
        lines.append("")
        lines.append("No links were auto-fixed.")
        lines.append("")

    lines.append("---")
    lines.append("")
    lines.append("*This report is auto-generated. Do not edit manually.*")
    lines.append("")

    return "\n".join(lines)


def generate_summary(result: ScanResult) -> str:
    """Produce a concise summary suitable for ``GITHUB_STEP_SUMMARY``.

    Args:
        result: The aggregated scan result.

    Returns:
        A short markdown snippet.
    """
    status = "PASS" if not result.broken_links else "FAIL"

    parts: list[str] = [
        f"### Internal Link Check: {status}",
        "",
        f"- **Files scanned:** {result.files_scanned}",
        f"- **Links checked:** {result.total_links_checked}",
        f"- **Broken:** {len(result.broken_links)}",
        f"- **Auto-fixed:** {len(result.fixed_links)}",
        "",
    ]

    if result.broken_links:
        parts.append("<details><summary>Broken links</summary>")
        parts.append("")
        for bl in result.broken_links:
            parts.append(
                f"- `{bl.location.file}:{bl.location.line}` "
                f"-> `{bl.location.raw_target}` ({bl.reason})"
            )
        parts.append("")
        parts.append("</details>")
        parts.append("")

    return "\n".join(parts)


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------

def _parse_args(argv: Optional[list[str]] = None) -> argparse.Namespace:
    """Parse command-line arguments.

    Args:
        argv: Argument list (defaults to ``sys.argv[1:]``).

    Returns:
        Parsed :class:`argparse.Namespace`.
    """
    parser = argparse.ArgumentParser(
        description=(
            "Detect and optionally repair broken internal links in "
            "markdown documentation."
        ),
    )
    parser.add_argument(
        "--root",
        type=Path,
        default=Path("."),
        help="Repository root directory (default: current directory).",
    )
    parser.add_argument(
        "--docs-dir",
        type=str,
        default="docs/",
        help=(
            "Relative path (from --root) to the documentation directory to "
            "scan (default: docs/)."
        ),
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=None,
        help="Path for the markdown report.  If omitted, the report is not written.",
    )
    parser.add_argument(
        "--auto-fix",
        action="store_true",
        default=False,
        help=(
            "Attempt to auto-fix broken relative links by searching for "
            "moved files elsewhere in the repository."
        ),
    )
    parser.add_argument(
        "--summary",
        action="store_true",
        default=False,
        help="Print a GITHUB_STEP_SUMMARY-compatible summary to stdout.",
    )

    return parser.parse_args(argv)


def main(argv: Optional[list[str]] = None) -> int:
    """Entry point for the link-repair script.

    Args:
        argv: Optional argument list (for testing); defaults to
            ``sys.argv[1:]``.

    Returns:
        ``0`` on success (no broken links remaining), ``1`` when broken
        links are still present after scanning.
    """
    args = _parse_args(argv)

    repo_root: Path = args.root.resolve()
    if not repo_root.is_dir():
        print(f"Error: repository root '{repo_root}' is not a directory.", file=sys.stderr)
        return 1

    docs_root: Path = (repo_root / args.docs_dir).resolve()
    if not docs_root.is_dir():
        print(
            f"Error: docs directory '{docs_root}' does not exist.  "
            f"Check --root and --docs-dir values.",
            file=sys.stderr,
        )
        return 1

    print(f"Scanning markdown files under {docs_root} ...")

    # Build a repository-wide file index for moved-file detection.
    file_index = _build_file_index(repo_root)

    # Collect markdown files.
    md_files = _collect_markdown_files(docs_root)
    if not md_files:
        print("No markdown files found.  Nothing to do.")
        return 0

    result = ScanResult(
        scan_time=datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M:%S UTC"),
    )
    result.files_scanned = len(md_files)

    # Scan every file.
    for md_file in md_files:
        replacements = _scan_file(
            md_file,
            repo_root=repo_root,
            file_index=file_index,
            auto_fix=args.auto_fix,
            result=result,
        )
        if replacements:
            _apply_replacements(md_file, replacements)

    # Generate and write the full report if requested.
    report = generate_report(result, repo_root)

    if args.output is not None:
        output_path: Path = args.output
        try:
            output_path.parent.mkdir(parents=True, exist_ok=True)
            output_path.write_text(report, encoding="utf-8")
            print(f"Report written to {output_path}")
        except OSError as exc:
            print(f"Error: could not write report to {output_path}: {exc}", file=sys.stderr)
            return 1

    # Print summary.
    if args.summary:
        print()
        print(generate_summary(result))

    # Always print a brief line to stdout.
    broken_count = len(result.broken_links)
    fixed_count = len(result.fixed_links)
    print(
        f"Link check complete: {result.total_links_checked} links checked, "
        f"{broken_count} broken, {fixed_count} auto-fixed "
        f"({result.files_scanned} files scanned)."
    )

    # Exit with failure if any broken links remain.
    return 1 if broken_count > 0 else 0


if __name__ == "__main__":
    sys.exit(main())
