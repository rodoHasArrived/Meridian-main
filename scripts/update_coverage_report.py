#!/usr/bin/env python3
"""Write a one-line coverage summary into docs/status/coverage-report.md.

Reads Cobertura XML coverage files produced by the dotnet test coverage run
and appends a dated summary line to the status doc so it always reflects the
most recent nightly run.
"""

from __future__ import annotations

import argparse
import sys
from datetime import datetime, timezone
from pathlib import Path

try:
    import xml.etree.ElementTree as ET
except ImportError as exc:  # pragma: no cover
    raise ImportError("xml.etree.ElementTree is required but could not be imported") from exc

REPO_ROOT = Path(__file__).resolve().parents[1]
COVERAGE_REPORT = REPO_ROOT / "docs" / "status" / "coverage-report.md"

COVERAGE_SECTION_MARKER = "<!-- auto-sync:coverage -->"


def _find_coverage_files(results_dir: Path) -> list[Path]:
    return list(results_dir.rglob("coverage.cobertura.xml"))


def _collect_line_hits(xml_path: Path, hits: dict[tuple[str, int], bool]) -> bool:
    """Merge one Cobertura file's per-line results into ``hits``; False when unreadable."""
    try:
        filename = ""
        for event, element in ET.iterparse(xml_path, events=("start", "end")):
            if event == "start":
                if element.tag == "class":
                    filename = element.attrib.get("filename", "")
                continue
            if element.tag == "line" and filename:
                key = (filename, int(element.attrib["number"]))
                hits[key] = hits.get(key, False) or int(element.attrib.get("hits", "0")) > 0
            elif element.tag == "class":
                # Coverage files for the whole solution are large; release parsed classes.
                element.clear()
    except (ET.ParseError, OSError, KeyError, ValueError):
        return False
    return True


def _aggregate_coverage(files: list[Path]) -> tuple[float, int] | None:
    """Line rate over the union of every instrumented line, and that line count.

    Each test project writes its own Cobertura file and most of them instrument the same
    production assemblies. Averaging the per-file rates weighted a 300-line assembly like a
    300,000-line one and counted a line as uncovered once for every project that did not
    reach it. A line is covered here when any project executed it.
    """
    hits: dict[tuple[str, int], bool] = {}
    readable = [path for path in files if _collect_line_hits(path, hits)]
    if not readable or not hits:
        return None
    return sum(hits.values()) / len(hits), len(hits)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--results-dir",
        type=Path,
        default=Path("artifacts/test-results/full"),
        help="Directory containing coverage XML files.",
    )
    args = parser.parse_args()

    results_dir = REPO_ROOT / args.results_dir if not args.results_dir.is_absolute() else args.results_dir

    coverage_files = _find_coverage_files(results_dir)
    aggregate = _aggregate_coverage(coverage_files)
    today = datetime.now(tz=timezone.utc).strftime("%Y-%m-%d")

    if aggregate is not None:
        rate, line_count = aggregate
        pct = f"{rate * 100:.1f}%"
        summary_line = (
            f"**Last run:** {today} — line coverage: **{pct}** of {line_count:,} instrumented lines "
            f"({len(coverage_files)} project(s))"
        )
    else:
        summary_line = f"**Last run:** {today} — no coverage data found in `{results_dir}`"

    if not COVERAGE_REPORT.exists():
        print(f"Coverage report not found at {COVERAGE_REPORT}; skipping update.", file=sys.stderr)
        return 0

    text = COVERAGE_REPORT.read_text(encoding="utf-8")

    if COVERAGE_SECTION_MARKER in text:
        # Replace the line immediately after the marker.
        lines = text.splitlines(keepends=True)
        out: list[str] = []
        skip_next = False
        for line in lines:
            if skip_next:
                # Replace the previous summary line with the updated one.
                out.append(summary_line + "\n")
                skip_next = False
                continue
            out.append(line)
            if line.strip() == COVERAGE_SECTION_MARKER:
                skip_next = True
        COVERAGE_REPORT.write_text("".join(out), encoding="utf-8")
    else:
        # Append section at end of file.
        COVERAGE_REPORT.write_text(
            text.rstrip() + f"\n\n{COVERAGE_SECTION_MARKER}\n{summary_line}\n",
            encoding="utf-8",
        )

    print(f"Coverage report updated: {summary_line}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
