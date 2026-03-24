#!/usr/bin/env python3
"""
Changelog Generator from Git Commit History

Generates a structured changelog in Markdown format by parsing git commit
messages that follow the Conventional Commits specification.

Supported commit prefixes:
    feat:     -> Features
    fix:      -> Bug Fixes
    docs:     -> Documentation
    refactor: -> Refactoring
    perf:     -> Performance
    test:     -> Tests
    build:    -> Build & CI
    chore:    -> Build & CI
    ci:       -> Build & CI

Commits containing "BREAKING CHANGE" in the subject or body are elevated
to a dedicated Breaking Changes section at the top of the output.

Non-conventional commits are categorized under "Other".

Usage:
    python3 generate-changelog.py --output docs/status/CHANGELOG.md
    python3 generate-changelog.py --recent 100 --since 2025-01-01
    python3 generate-changelog.py --summary
"""

import argparse
import os
import re
import subprocess
import sys
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Optional


# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

# Maps conventional-commit prefixes to human-readable category names.
CATEGORY_MAP: dict[str, str] = {
    "feat": "Features",
    "fix": "Bug Fixes",
    "docs": "Documentation",
    "refactor": "Refactoring",
    "perf": "Performance",
    "test": "Tests",
    "build": "Build & CI",
    "chore": "Build & CI",
    "ci": "Build & CI",
}

# Display order for categories in the generated markdown.
CATEGORY_ORDER: list[str] = [
    "Breaking Changes",
    "Features",
    "Bug Fixes",
    "Performance",
    "Refactoring",
    "Documentation",
    "Tests",
    "Build & CI",
    "Other",
]

# Regex that matches a conventional-commit subject line.
# Group 1: type, Group 2: optional scope (without parens), Group 3: message.
CONVENTIONAL_RE = re.compile(
    r"^(?P<type>[a-z]+)"          # type prefix (e.g. "feat")
    r"(?:\((?P<scope>[^)]*)\))?"  # optional scope in parentheses
    r"!?"                         # optional '!' for breaking
    r":\s*"                       # colon + whitespace
    r"(?P<message>.+)$",          # commit message body
    re.IGNORECASE,
)

# Regex for PR / issue references such as (#123) or #456 in prose.
PR_ISSUE_RE = re.compile(r"#(\d+)")

# Sentinel used as the field separator in git log formatting.
_LOG_SEP = "---CHANGELOG_SEP---"


# ---------------------------------------------------------------------------
# Data classes
# ---------------------------------------------------------------------------

@dataclass
class CommitInfo:
    """Parsed representation of a single git commit."""

    hash: str
    short_hash: str
    subject: str
    body: str
    author: str
    date: str
    category: str = "Other"
    scope: Optional[str] = None
    message: str = ""
    pr_refs: list[str] = field(default_factory=list)
    issue_refs: list[str] = field(default_factory=list)
    is_breaking: bool = False


# ---------------------------------------------------------------------------
# Git helpers
# ---------------------------------------------------------------------------

def _run_git(args: list[str], cwd: str) -> subprocess.CompletedProcess[str]:
    """Run a git command and return the CompletedProcess result.

    Args:
        args: Arguments to pass after ``git``.
        cwd:  Working directory (repository root).

    Returns:
        The completed process with captured stdout/stderr.

    Raises:
        SystemExit: If ``git`` is not found on the system ``PATH``.
    """
    try:
        return subprocess.run(
            ["git"] + args,
            capture_output=True,
            text=True,
            cwd=cwd,
        )
    except FileNotFoundError:
        print("Error: git is not installed or not on PATH.", file=sys.stderr)
        sys.exit(1)


def verify_git_repo(root: str) -> None:
    """Verify that *root* is inside a git repository.

    Raises:
        SystemExit: If *root* is not a git working tree.
    """
    result = _run_git(["rev-parse", "--is-inside-work-tree"], cwd=root)
    if result.returncode != 0:
        print(
            f"Error: '{root}' is not a git repository.",
            file=sys.stderr,
        )
        sys.exit(1)


def get_remote_url(root: str) -> Optional[str]:
    """Return the ``origin`` remote URL, or ``None`` if unavailable."""
    result = _run_git(["remote", "get-url", "origin"], cwd=root)
    if result.returncode == 0:
        return result.stdout.strip()
    return None


def parse_github_base_url(remote_url: Optional[str]) -> Optional[str]:
    """Derive a GitHub web base URL from a remote URL.

    Supports both HTTPS and SSH remote formats.

    Returns:
        A string like ``https://github.com/owner/repo`` or ``None``.
    """
    if remote_url is None:
        return None

    # HTTPS: https://github.com/owner/repo.git
    match = re.match(r"https?://github\.com/([^/]+/[^/]+?)(?:\.git)?$", remote_url)
    if match:
        return f"https://github.com/{match.group(1)}"

    # SSH: git@github.com:owner/repo.git
    match = re.match(r"git@github\.com:([^/]+/[^/]+?)(?:\.git)?$", remote_url)
    if match:
        return f"https://github.com/{match.group(1)}"

    return None


def fetch_commits(
    root: str,
    recent: int,
    since: Optional[str],
) -> list[dict[str, str]]:
    """Retrieve raw commit data from git log.

    Args:
        root:   Repository root directory.
        recent: Maximum number of commits to retrieve.
        since:  Optional ISO date string to limit history.

    Returns:
        A list of dicts with keys ``hash``, ``short_hash``, ``subject``,
        ``body``, ``author``, and ``date``.
    """
    # Use a record-end sentinel so that multi-line commit bodies do not
    # break the field splitting.  The field separator (_LOG_SEP) delimits
    # fields within a single commit, while record_sep marks the boundary
    # between commits.
    record_sep = "---RECORD_END---"
    fmt = _LOG_SEP.join(["%H", "%h", "%s", "%b", "%an", "%ai"]) + record_sep

    cmd: list[str] = [
        "log",
        f"--pretty=format:{fmt}",
        f"-n{recent}",
    ]
    if since:
        cmd.append(f"--since={since}")

    result = _run_git(cmd, cwd=root)
    if result.returncode != 0:
        stderr = result.stderr.strip()
        print(f"Error running git log: {stderr}", file=sys.stderr)
        sys.exit(1)

    raw = result.stdout.strip()
    if not raw:
        return []

    commits: list[dict[str, str]] = []
    for record in raw.split(record_sep):
        record = record.strip()
        if not record:
            continue
        parts = record.split(_LOG_SEP, 5)
        if len(parts) < 6:
            continue

        commits.append({
            "hash": parts[0].strip(),
            "short_hash": parts[1].strip(),
            "subject": parts[2].strip(),
            "body": parts[3].strip(),
            "author": parts[4].strip(),
            "date": parts[5].strip(),
        })

    return commits


# ---------------------------------------------------------------------------
# Parsing helpers
# ---------------------------------------------------------------------------

def extract_references(text: str) -> list[str]:
    """Return a deduplicated list of ``#NNN`` references found in *text*."""
    return sorted(set(PR_ISSUE_RE.findall(text)))


def classify_commit(raw: dict[str, str]) -> CommitInfo:
    """Parse a raw commit dict into a structured ``CommitInfo``.

    Applies conventional-commit parsing, reference extraction, and
    breaking-change detection.
    """
    subject = raw["subject"]
    body = raw.get("body", "")
    full_text = f"{subject}\n{body}"

    # Detect breaking change markers.
    is_breaking = (
        "BREAKING CHANGE" in full_text
        or "BREAKING-CHANGE" in full_text
        or subject.startswith("feat!:")
        or subject.startswith("fix!:")
        or bool(re.match(r"^[a-z]+(?:\([^)]*\))?!:", subject, re.IGNORECASE))
    )

    # Attempt conventional-commit parse.
    match = CONVENTIONAL_RE.match(subject)
    if match:
        ctype = match.group("type").lower()
        scope = match.group("scope")
        message = match.group("message").strip()
        category = CATEGORY_MAP.get(ctype, "Other")
    else:
        scope = None
        message = subject
        category = "Other"

    if is_breaking:
        category = "Breaking Changes"

    refs = extract_references(full_text)

    return CommitInfo(
        hash=raw["hash"],
        short_hash=raw["short_hash"],
        subject=subject,
        body=body,
        author=raw["author"],
        date=raw["date"],
        category=category,
        scope=scope,
        message=message,
        pr_refs=refs,
        issue_refs=refs,
        is_breaking=is_breaking,
    )


# ---------------------------------------------------------------------------
# Grouping
# ---------------------------------------------------------------------------

def group_by_category(
    commits: list[CommitInfo],
) -> dict[str, list[CommitInfo]]:
    """Group commits by their resolved category.

    Returns:
        An ordered dict following ``CATEGORY_ORDER`` (categories with no
        commits are omitted).
    """
    buckets: dict[str, list[CommitInfo]] = {}
    for commit in commits:
        buckets.setdefault(commit.category, []).append(commit)

    ordered: dict[str, list[CommitInfo]] = {}
    for cat in CATEGORY_ORDER:
        if cat in buckets:
            ordered[cat] = buckets.pop(cat)
    # Append any remaining categories not listed in CATEGORY_ORDER.
    for cat in sorted(buckets):
        ordered[cat] = buckets[cat]

    return ordered


# ---------------------------------------------------------------------------
# Markdown generation
# ---------------------------------------------------------------------------

def _format_ref_links(
    refs: list[str],
    github_base: Optional[str],
) -> str:
    """Format PR/issue references as markdown links when possible."""
    if not refs:
        return ""

    parts: list[str] = []
    for ref in refs:
        if github_base:
            # Link to the *issue* URL; GitHub redirects to PR if applicable.
            parts.append(f"[#{ref}]({github_base}/issues/{ref})")
        else:
            parts.append(f"#{ref}")

    return " " + ", ".join(parts)


def generate_markdown(
    grouped: dict[str, list[CommitInfo]],
    total: int,
    github_base: Optional[str],
) -> str:
    """Render the full changelog as a Markdown string.

    Args:
        grouped:     Commits grouped by category.
        total:       Total number of commits processed.
        github_base: Optional GitHub base URL for linking.

    Returns:
        A complete Markdown document.
    """
    now_utc = datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M:%S UTC")
    lines: list[str] = []

    lines.append("# Changelog")
    lines.append("")
    lines.append("> Auto-generated from git commit history using conventional commits.")
    lines.append(f"> Generated: {now_utc}")
    lines.append("")

    # Summary statistics table.
    category_counts = {cat: len(items) for cat, items in grouped.items()}
    lines.append("## Summary")
    lines.append("")
    lines.append("| Category | Count |")
    lines.append("|----------|-------|")
    for cat in CATEGORY_ORDER:
        if cat in category_counts:
            lines.append(f"| {cat} | {category_counts[cat]} |")
    # Any extra categories.
    for cat, count in category_counts.items():
        if cat not in CATEGORY_ORDER:
            lines.append(f"| {cat} | {count} |")
    lines.append(f"| **Total** | **{total}** |")
    lines.append("")

    # Breaking changes get a prominent callout.
    if "Breaking Changes" in grouped:
        lines.append("> **Warning**")
        lines.append("> This changelog includes breaking changes. Review them carefully before upgrading.")
        lines.append("")

    # Category sections.
    for category, commits in grouped.items():
        lines.append(f"## {category}")
        lines.append("")
        for commit in commits:
            scope_prefix = f"**{commit.scope}:** " if commit.scope else ""
            ref_links = _format_ref_links(commit.pr_refs, github_base)
            short_hash = commit.short_hash

            if github_base:
                hash_link = f"[`{short_hash}`]({github_base}/commit/{commit.hash})"
            else:
                hash_link = f"`{short_hash}`"

            lines.append(
                f"- {scope_prefix}{commit.message}{ref_links} ({hash_link})"
            )
        lines.append("")

    # Footer.
    lines.append("---")
    lines.append("")
    lines.append(f"*{total} commits processed.*")
    lines.append("")

    return "\n".join(lines)


def generate_summary(grouped: dict[str, list[CommitInfo]], total: int) -> str:
    """Produce a short plain-text summary suitable for ``GITHUB_STEP_SUMMARY``.

    Args:
        grouped: Commits grouped by category.
        total:   Total number of commits processed.

    Returns:
        A concise multi-line summary string.
    """
    parts: list[str] = [
        f"Changelog generated: {total} commits processed.",
        "",
    ]
    for cat in CATEGORY_ORDER:
        if cat in grouped:
            parts.append(f"  - {cat}: {len(grouped[cat])}")
    for cat in sorted(grouped):
        if cat not in CATEGORY_ORDER:
            parts.append(f"  - {cat}: {len(grouped[cat])}")
    return "\n".join(parts)


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------

def parse_args() -> argparse.Namespace:
    """Parse command-line arguments."""
    parser = argparse.ArgumentParser(
        description=(
            "Generate a changelog from git commit history using the "
            "Conventional Commits format."
        ),
    )
    parser.add_argument(
        "--output", "-o",
        type=str,
        default=None,
        help="Path to write the generated Markdown changelog.",
    )
    parser.add_argument(
        "--root",
        type=str,
        default=".",
        help="Repository root directory (default: current directory).",
    )
    parser.add_argument(
        "--recent",
        type=int,
        default=50,
        help="Number of recent commits to include (default: 50).",
    )
    parser.add_argument(
        "--since",
        type=str,
        default=None,
        help="Only include commits after this date (ISO format, e.g. 2025-01-01).",
    )
    parser.add_argument(
        "--summary",
        action="store_true",
        help="Print a short summary to stdout (for GITHUB_STEP_SUMMARY).",
    )
    return parser.parse_args()


def main() -> int:
    """Entry point for the changelog generator.

    Returns:
        Exit code: 0 on success, 1 on error.
    """
    args = parse_args()
    root = os.path.abspath(args.root)

    # --- Validate environment -------------------------------------------
    verify_git_repo(root)

    # --- Gather commit data ---------------------------------------------
    raw_commits = fetch_commits(root, recent=args.recent, since=args.since)

    if not raw_commits:
        msg = "No commits found"
        if args.since:
            msg += f" since {args.since}"
        msg += "."
        print(msg, file=sys.stderr)
        # Still write an empty changelog if an output path was given.
        if args.output:
            output_path = Path(args.output)
            output_path.parent.mkdir(parents=True, exist_ok=True)
            now_utc = datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M:%S UTC")
            output_path.write_text(
                f"# Changelog\n\n> Generated: {now_utc}\n\nNo commits found.\n",
                encoding="utf-8",
            )
            print(f"Wrote empty changelog to {output_path}")
        return 0

    # --- Classify and group ---------------------------------------------
    commits = [classify_commit(raw) for raw in raw_commits]
    grouped = group_by_category(commits)
    total = len(commits)

    # --- Resolve GitHub base URL for links ------------------------------
    remote_url = get_remote_url(root)
    github_base = parse_github_base_url(remote_url)

    # --- Generate markdown ----------------------------------------------
    markdown = generate_markdown(grouped, total, github_base)

    # --- Write output ---------------------------------------------------
    if args.output:
        output_path = Path(args.output)
        output_path.parent.mkdir(parents=True, exist_ok=True)
        output_path.write_text(markdown, encoding="utf-8")
        print(f"Changelog written to {output_path}")

    # --- Summary mode ---------------------------------------------------
    if args.summary:
        summary = generate_summary(grouped, total)
        print(summary)
    elif not args.output:
        # If no output file and no --summary, print the markdown to stdout.
        print(markdown)

    return 0


if __name__ == "__main__":
    sys.exit(main())
