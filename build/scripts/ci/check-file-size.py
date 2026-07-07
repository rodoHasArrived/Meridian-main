#!/usr/bin/env python3
"""No-new-god-file ratchet guard for Meridian source files.

Enforces two rules against a checked-in baseline so oversized files can only
shrink, never grow, and no brand-new oversized file can be introduced:

  1. Any source file whose line count exceeds THRESHOLD_LINES and is NOT in the
     baseline is a CRITICAL failure (a new "god file").
  2. Any baselined file whose current line count exceeds its recorded cap is a
     CRITICAL failure (an existing god file grew).

The baseline (build/config/file-size-baseline.json) records the current line
count of every already-oversized file as its frozen cap. Legitimately shrinking
a file is always allowed; growing one past its cap requires explicitly updating
the baseline (`--update-baseline`), which surfaces in the PR diff as tracked debt.

This mirrors the RatchetPlan discipline used by check-warning-suppressions.py and
codifies ADR-017 (modular operational monolith) / ADR-018 (module conventions):
capability logic belongs in composed, single-responsibility units, not god files.

Exit codes:
    0  No new or grown god files
    1  One or more violations
    2  Script error

Usage:
    python3 build/scripts/ci/check-file-size.py
    python3 build/scripts/ci/check-file-size.py --update-baseline
    python3 build/scripts/ci/check-file-size.py --threshold 2000
"""

from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path

# Files at or below this many lines are never flagged. Chosen so the current
# baseline captures the genuine monoliths (40 files) rather than ordinary code.
THRESHOLD_LINES = 2000

# Source extensions the ratchet governs. Generated/vendored trees are pruned.
SOURCE_SUFFIXES = (".cs", ".fs", ".ts", ".tsx")

# Lowercase names; traversal compares case-insensitively so folders like
# Bin/Obj/TestResults are skipped regardless of casing on disk.
_SKIP_DIR_NAMES = {
    ".git",
    ".vite",
    "artifacts",
    "bin",
    "coverage",
    "dist",
    "node_modules",
    "obj",
    "publish",
    "testresults",
    "wwwroot",
}

# Test and generated sources are excluded from the ratchet: large test files and
# machine-generated barrels are tracked debt of a different kind and would only
# add noise here. The refactor targets hand-authored production monoliths.
def _is_excluded(rel_path: str) -> bool:
    lowered = rel_path.lower()
    if ".test." in lowered or ".spec." in lowered or lowered.endswith(".g.cs"):
        return True
    if "/generated/" in lowered or lowered.endswith(".generated.ts"):
        return True
    return False


def _repo_root() -> Path:
    return Path(__file__).resolve().parents[3]


def _baseline_path(root: Path) -> Path:
    return root / "build" / "config" / "file-size-baseline.json"


def _count_lines(path: Path) -> int:
    try:
        with path.open("rb") as handle:
            return sum(1 for _ in handle)
    except OSError:
        return 0


def _iter_source_files(src_root: Path) -> list[Path]:
    files: list[Path] = []
    for dirpath, dirnames, filenames in os.walk(src_root, topdown=True, followlinks=False):
        dirnames[:] = [d for d in dirnames if d.lower() not in _SKIP_DIR_NAMES]
        for filename in filenames:
            if filename.endswith(SOURCE_SUFFIXES):
                files.append(Path(dirpath) / filename)
    return files


def _scan(root: Path, threshold: int) -> dict[str, int]:
    """Return {relative_path: line_count} for every oversized, non-excluded file."""
    src_root = root / "src"
    oversized: dict[str, int] = {}
    for path in _iter_source_files(src_root):
        rel = path.relative_to(root).as_posix()
        if _is_excluded(rel):
            continue
        lines = _count_lines(path)
        if lines > threshold:
            oversized[rel] = lines
    return dict(sorted(oversized.items()))


def _load_baseline(root: Path) -> dict[str, int]:
    path = _baseline_path(root)
    if not path.exists():
        return {}
    data = json.loads(path.read_text(encoding="utf-8"))
    return {str(k): int(v) for k, v in data.get("files", {}).items()}


def _write_baseline(root: Path, threshold: int, oversized: dict[str, int]) -> None:
    path = _baseline_path(root)
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "_comment": (
            "No-new-god-file ratchet baseline. Each entry caps a currently "
            "oversized source file at its recorded line count. Files may shrink "
            "freely; growing one past its cap or adding a new file over the "
            "threshold fails CI. Regenerate with "
            "`python3 build/scripts/ci/check-file-size.py --update-baseline` "
            "and justify the change in review."
        ),
        "threshold_lines": threshold,
        "files": oversized,
    }
    path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="check-file-size")
    parser.add_argument("--threshold", type=int, default=THRESHOLD_LINES)
    parser.add_argument(
        "--update-baseline",
        action="store_true",
        help="Regenerate the baseline from the current tree instead of checking.",
    )
    args = parser.parse_args(argv)

    root = _repo_root()
    src_root = root / "src"
    if not src_root.exists():
        print(f"ERROR: source root not found: {src_root}", file=sys.stderr)
        return 2

    current = _scan(root, args.threshold)

    if args.update_baseline:
        _write_baseline(root, args.threshold, current)
        print(f"Wrote baseline with {len(current)} tracked file(s) "
              f"(threshold {args.threshold} lines).")
        return 0

    baseline_path = _baseline_path(root)
    if not baseline_path.exists():
        print("ERROR: no baseline found. Run with --update-baseline first.", file=sys.stderr)
        return 2
    baseline = _load_baseline(root)

    new_god_files: list[tuple[str, int]] = []
    grown_files: list[tuple[str, int, int]] = []
    for rel, lines in current.items():
        if rel not in baseline:
            new_god_files.append((rel, lines))
        elif lines > baseline[rel]:
            grown_files.append((rel, lines, baseline[rel]))

    stale = sorted(set(baseline) - set(current))

    if new_god_files or grown_files:
        print("File-size ratchet FAILED:", file=sys.stderr)
        for rel, lines in new_god_files:
            print(f"- NEW god file: {rel} has {lines} lines (> {args.threshold}). "
                  f"Split it into composed units, or if unavoidable add it to the "
                  f"baseline with justification.", file=sys.stderr)
        for rel, lines, cap in grown_files:
            print(f"- GREW past cap: {rel} now {lines} lines (baseline cap {cap}). "
                  f"Reduce it, or update the baseline with justification.", file=sys.stderr)
        return 1

    print(f"File-size ratchet OK: {len(current)} tracked file(s), "
          f"no new or grown god files (threshold {args.threshold} lines).")
    if stale:
        print("Notice: baseline entries now under threshold (tighten the ratchet "
              "by rerunning --update-baseline):")
        for rel in stale:
            print(f"- {rel}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
