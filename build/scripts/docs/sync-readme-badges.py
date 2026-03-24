#!/usr/bin/env python3
"""
README Badge Synchronization Script

Updates README.md badges with current project metrics and status.
Fetches data from various sources (workflows, tests, coverage, versions)
and updates badge URLs and values in the README.

Features:
- Workflow status badges
- Test coverage badges
- Version badges
- License badge
- Documentation badges
- Dependency status badges

Usage:
    python3 sync-readme-badges.py --readme README.md
    python3 sync-readme-badges.py --dry-run
    python3 sync-readme-badges.py --summary
"""

from __future__ import annotations

import argparse
import re
import sys
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Optional
import subprocess


# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

# Badge patterns to match in README
BADGE_PATTERNS = {
    'build': re.compile(
        r'!\[Build Status\]\(https://github\.com/[^/]+/[^/]+/workflows/[^/]+/badge\.svg\)'
    ),
    'tests': re.compile(
        r'!\[Tests\]\(https://img\.shields\.io/badge/tests-\d+-[a-z]+\)'
    ),
    'coverage': re.compile(
        r'!\[Coverage\]\(https://img\.shields\.io/badge/coverage-\d+%25-[a-z]+\)'
    ),
    'version': re.compile(
        r'!\[Version\]\(https://img\.shields\.io/badge/version-[\d\.]+-[a-z]+\)'
    ),
}

# Badge color thresholds
COLOR_THRESHOLDS = {
    'coverage': [
        (90, 'brightgreen'),
        (75, 'green'),
        (60, 'yellow'),
        (40, 'orange'),
        (0, 'red'),
    ],
    'tests': [
        (1000, 'brightgreen'),
        (500, 'green'),
        (100, 'yellow'),
        (0, 'orange'),
    ],
}


# ---------------------------------------------------------------------------
# Data Models
# ---------------------------------------------------------------------------

@dataclass
class BadgeInfo:
    """Information for a badge."""
    name: str
    label: str
    value: str
    color: str
    url: str = ""

    def generate_markdown(self) -> str:
        """Generate markdown badge syntax."""
        if self.url:
            return f"![{self.name}]({self.url})"
        else:
            # Use shields.io
            encoded_value = self.value.replace('%', '%25').replace(' ', '%20')
            return (
                f"![{self.name}]"
                f"(https://img.shields.io/badge/{self.label}-{encoded_value}-{self.color})"
            )


@dataclass
class BadgeUpdate:
    """Represents a badge update operation."""
    badge: BadgeInfo
    old_markdown: str
    new_markdown: str
    line_number: int


@dataclass
class SyncResults:
    """Results of badge synchronization."""
    updates: list[BadgeUpdate] = field(default_factory=list)
    errors: list[str] = field(default_factory=list)
    generated_at: str = ""

    @property
    def success_count(self) -> int:
        return len(self.updates)

    @property
    def had_errors(self) -> bool:
        return len(self.errors) > 0


# ---------------------------------------------------------------------------
# Helper Functions
# ---------------------------------------------------------------------------

def _get_color_for_value(category: str, value: float) -> str:
    """Determine badge color based on value."""
    if category not in COLOR_THRESHOLDS:
        return 'blue'

    for threshold, color in COLOR_THRESHOLDS[category]:
        if value >= threshold:
            return color

    return 'red'


def _run_command(cmd: list[str], cwd: Path) -> tuple[int, str, str]:
    """Run a command and return exit code, stdout, stderr."""
    try:
        result = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            cwd=str(cwd),
            timeout=30
        )
        return result.returncode, result.stdout, result.stderr
    except subprocess.TimeoutExpired:
        return 1, "", "Command timed out"
    except Exception as e:
        return 1, "", str(e)


def _extract_version_from_props(root: Path) -> Optional[str]:
    """Extract version from Directory.Build.props."""
    props_file = root / "Directory.Build.props"
    if not props_file.exists():
        return None

    try:
        content = props_file.read_text(encoding='utf-8')
        match = re.search(r'<Version>([\d\.]+)</Version>', content)
        if match:
            return match.group(1)
    except Exception:
        pass

    return None


def _count_tests(root: Path) -> int:
    """Count total test files in the repository."""
    tests_dir = root / "tests"
    if not tests_dir.exists():
        return 0

    count = 0
    for test_file in tests_dir.rglob("*Tests.cs"):
        count += 1
    for test_file in tests_dir.rglob("*Tests.fs"):
        count += 1

    return count


def _get_test_coverage(root: Path) -> Optional[float]:
    """Get test coverage percentage from coverage reports."""
    # Look for coverage reports
    coverage_files = [
        root / "coverage" / "Summary.xml",
        root / "TestResults" / "coverage.cobertura.xml",
    ]

    for coverage_file in coverage_files:
        if coverage_file.exists():
            try:
                content = coverage_file.read_text(encoding='utf-8')
                # Try to extract line coverage
                match = re.search(r'line-rate="([\d\.]+)"', content)
                if match:
                    return float(match.group(1)) * 100

                # Try percentage format
                match = re.search(r'coverage>\s*([\d\.]+)%', content)
                if match:
                    return float(match.group(1))
            except Exception:
                pass

    return None


# ---------------------------------------------------------------------------
# Badge Generation
# ---------------------------------------------------------------------------

def generate_badges(root: Path) -> list[BadgeInfo]:
    """Generate current badge information."""
    badges = []

    # Version badge
    version = _extract_version_from_props(root)
    if version:
        badges.append(BadgeInfo(
            name="Version",
            label="version",
            value=version,
            color="blue"
        ))

    # Tests badge
    test_count = _count_tests(root)
    if test_count > 0:
        badges.append(BadgeInfo(
            name="Tests",
            label="tests",
            value=str(test_count),
            color=_get_color_for_value('tests', test_count)
        ))

    # Coverage badge
    coverage = _get_test_coverage(root)
    if coverage is not None:
        badges.append(BadgeInfo(
            name="Coverage",
            label="coverage",
            value=f"{coverage:.0f}%",
            color=_get_color_for_value('coverage', coverage)
        ))

    # Build status badge (placeholder - would need GitHub API)
    badges.append(BadgeInfo(
        name="Build Status",
        label="build",
        value="passing",
        color="brightgreen",
        url="https://github.com/rodoHasArrived/Meridian/workflows/test-matrix/badge.svg"
    ))

    # License badge
    badges.append(BadgeInfo(
        name="License",
        label="license",
        value="MIT",
        color="blue"
    ))

    # .NET version badge
    badges.append(BadgeInfo(
        name=".NET",
        label=".NET",
        value="9.0",
        color="512BD4"
    ))

    return badges


# ---------------------------------------------------------------------------
# README Update
# ---------------------------------------------------------------------------

def update_readme(readme_path: Path, badges: list[BadgeInfo], dry_run: bool = False) -> SyncResults:  # noqa: C901
    """Update badges in README.md."""
    results = SyncResults(
        generated_at=datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M:%S UTC")
    )

    if not readme_path.exists():
        results.errors.append(f"README not found: {readme_path}")
        return results

    try:
        content = readme_path.read_text(encoding='utf-8')
        lines = content.splitlines(keepends=True)
    except Exception as e:
        results.errors.append(f"Failed to read README: {e}")
        return results

    # Build badge lookup by name
    badge_map = {b.name: b for b in badges}

    # Update badges in content
    for line_num, line in enumerate(lines):
        for badge_name, pattern in BADGE_PATTERNS.items():
            if pattern.search(line):
                # Find matching badge
                for name, badge in badge_map.items():
                    if name.lower() == badge_name or badge_name in name.lower():
                        new_markdown = badge.generate_markdown()
                        old_markdown = pattern.search(line).group(0)

                        if old_markdown != new_markdown:
                            results.updates.append(BadgeUpdate(
                                badge=badge,
                                old_markdown=old_markdown,
                                new_markdown=new_markdown,
                                line_number=line_num + 1
                            ))

                            if not dry_run:
                                lines[line_num] = line.replace(old_markdown, new_markdown)

                        break

    # Write updated content
    if not dry_run and results.updates:
        try:
            readme_path.write_text(''.join(lines), encoding='utf-8')
        except Exception as e:
            results.errors.append(f"Failed to write README: {e}")

    return results


# ---------------------------------------------------------------------------
# Report Generation
# ---------------------------------------------------------------------------

def generate_markdown_report(results: SyncResults) -> str:
    """Generate Markdown report of sync results."""
    lines = []

    lines.append("# README Badge Sync Report")
    lines.append("")
    lines.append(f"> Generated: {results.generated_at}")
    lines.append("")

    lines.append("## Summary")
    lines.append("")
    lines.append(f"- **Badges Updated**: {results.success_count}")
    lines.append(f"- **Errors**: {len(results.errors)}")
    lines.append("")

    if results.updates:
        lines.append("## Updated Badges")
        lines.append("")
        lines.append("| Badge | Line | Old | New |")
        lines.append("|-------|------|-----|-----|")

        for update in results.updates:
            lines.append(
                f"| {update.badge.name} | {update.line_number} | "
                f"`{update.old_markdown[:40]}...` | `{update.new_markdown[:40]}...` |"
            )
        lines.append("")

    if results.errors:
        lines.append("## Errors")
        lines.append("")
        for error in results.errors:
            lines.append(f"- {error}")
        lines.append("")

    return "\n".join(lines)


def generate_summary(results: SyncResults) -> str:
    """Generate concise summary."""
    status = "✅" if not results.had_errors else "⚠️"
    return (
        f"### README Badge Sync {status}\n\n"
        f"- Updated: {results.success_count} badges\n"
        f"- Errors: {len(results.errors)}\n"
    )


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

def main(argv: Optional[list[str]] = None) -> int:
    """Entry point."""
    parser = argparse.ArgumentParser(
        description='Synchronize README badges with current project metrics'
    )
    parser.add_argument(
        '--root', '-r',
        type=Path,
        default=Path('.'),
        help='Repository root directory (default: current directory)'
    )
    parser.add_argument(
        '--readme',
        type=Path,
        default=Path('README.md'),
        help='README file to update (default: README.md)'
    )
    parser.add_argument(
        '--dry-run',
        action='store_true',
        help='Show what would be updated without making changes'
    )
    parser.add_argument(
        '--output', '-o',
        type=Path,
        help='Output file for report'
    )
    parser.add_argument(
        '--summary', '-s',
        action='store_true',
        help='Print summary to stdout'
    )

    args = parser.parse_args(argv)

    root = args.root.resolve()
    if not root.is_dir():
        print(f"Error: root directory does not exist: {root}", file=sys.stderr)
        return 1

    readme_path = root / args.readme if not args.readme.is_absolute() else args.readme

    try:
        print("Generating badge information...", file=sys.stderr)
        badges = generate_badges(root)

        print(f"Generated {len(badges)} badges", file=sys.stderr)

        if args.dry_run:
            print("DRY RUN: No changes will be made", file=sys.stderr)

        print("Updating README...", file=sys.stderr)
        results = update_readme(readme_path, badges, dry_run=args.dry_run)

    except Exception as exc:
        print(f"Error during sync: {exc}", file=sys.stderr)
        return 1

    # Write report
    if args.output:
        report = generate_markdown_report(results)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(report, encoding='utf-8')
        print(f"Report written to {args.output}")

    # Print summary
    if args.summary:
        print(generate_summary(results))
    elif not args.output:
        print(generate_summary(results))

    if results.had_errors:
        return 1

    return 0


if __name__ == '__main__':
    sys.exit(main())
