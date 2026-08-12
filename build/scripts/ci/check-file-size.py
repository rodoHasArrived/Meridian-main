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

Containment is only half the job: the baseline records where the debt is, but nothing drives it
down. Every run therefore also reports the trend — tracked files, capped lines, and how much of the
baseline is reclaimable — so progress is a visible number rather than a per-file pass/fail. See
docs/development/god-file-burn-down-plan.md for the burn-down targets those numbers feed.

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


# Files this close to their cap are called out before a contributor trips over them. The ratchet
# offers no headroom by construction — a freshly written baseline pins every file at its exact
# current size — so without this warning the first signal is a failed CI run on a one-line change.
TIGHT_HEADROOM_LINES = 25


def _live_lines(
    root: Path, baseline: dict[str, int], current: dict[str, int]
) -> tuple[dict[str, int], list[str]]:
    """Current line count for every baselined file, plus the ones whose size could not be read.

    A baselined file that has shrunk below the threshold is absent from `current`, so its real size
    has to be read from disk. Treating it as zero would erase its lines from the totals and
    overstate the reclaimable figure at exactly the moment a decomposition is about to retire it.

    A file that is *present but unreadable* — a permission problem, a transient I/O error — is a
    different case with the same shape, and the dangerous one: read as zero it is indistinguishable
    from a file successfully deleted, so a failed read would be reported as the largest possible
    reduction. Deleted files count as zero because their lines really are gone; unreadable ones are
    held at their cap, contributing no reclaimable slack, and named in the output so nobody reads
    the total as complete.
    """
    live: dict[str, int] = {}
    unreadable: list[str] = []
    for rel, cap in baseline.items():
        if rel in current:
            live[rel] = current[rel]
            continue

        path = root / rel
        if not path.exists():
            live[rel] = 0
            continue

        try:
            with path.open("rb") as handle:
                live[rel] = sum(1 for _ in handle)
        except OSError:
            live[rel] = cap
            unreadable.append(rel)

    return live, unreadable


def _report_trend(root: Path, baseline: dict[str, int], current: dict[str, int]) -> None:
    """Print the burn-down numbers: what is tracked, and how much of it is reclaimable."""
    live_lines, unreadable = _live_lines(root, baseline, current)
    capped = sum(baseline.values())
    live = sum(live_lines.values())
    slack = sum(max(0, cap - live_lines[rel]) for rel, cap in baseline.items())

    print(
        f"Baseline trend: {len(baseline)} tracked file(s), {capped:,} capped line(s), "
        f"{live:,} current line(s), {slack:,} line(s) reclaimable."
    )
    if unreadable:
        print(
            f"NOTE: {len(unreadable)} baselined file(s) could not be read and are counted at their "
            f"cap, so the figures above understate rather than invent progress:"
        )
        for rel in unreadable:
            print(f"- {rel}")

    # Over-cap files are excluded. Their headroom is negative, so an unbounded comparison admits a
    # file hundreds of lines past its cap, prints a negative "spare" count, claims an already-
    # failing file "sits at its cap", and sorts ahead of the near-cap files this warning surfaces.
    # They are reported by name and exact overage in the failure block instead.
    tight = sorted(
        ((cap - live_lines[rel], rel, live_lines[rel], cap)
         for rel, cap in baseline.items()
         if 0 <= cap - live_lines[rel] <= TIGHT_HEADROOM_LINES),
        key=lambda item: (item[0], item[1]),
    )
    if tight:
        pinned = sum(1 for headroom, *_ in tight if headroom == 0)
        detail = (
            f" {pinned} of them sit at their cap, where a single added line fails this check."
            if pinned
            else ""
        )
        print(
            f"TIGHT: {len(tight)} of {len(baseline)} tracked file(s) are within "
            f"{TIGHT_HEADROOM_LINES} line(s) of their cap.{detail} Decompose before extending."
        )
        for headroom, rel, lines, cap in tight[:10]:
            print(f"- {rel}: {lines}/{cap} ({headroom} line(s) spare)")
        if len(tight) > 10:
            print(f"- ... and {len(tight) - 10} more")
    if slack:
        print(
            "Those lines are not yet locked in: the caps still allow the file to grow back."
        )


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
        # Report against the baseline just written. This is the command run right after a
        # decomposition lands, so it is the one moment an operator most wants the trend - and
        # every file it just re-pinned shows up as TIGHT, which is the cost of the update being
        # made visible at the point of making it.
        _report_trend(root, _load_baseline(root), current)
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
            print(f"- GREW past cap: {rel} now {lines} lines (baseline cap {cap}, "
                  f"exceeded by {lines - cap}). Reduce it, or update the baseline with "
                  f"justification.", file=sys.stderr)
        _report_trend(root, baseline, current)
        return 1

    print(f"File-size ratchet OK: {len(current)} tracked file(s), "
          f"no new or grown god files (threshold {args.threshold} lines).")
    _report_trend(root, baseline, current)
    if stale:
        print("Notice: baseline entries now under threshold (tighten the ratchet "
              "by rerunning --update-baseline):")
        for rel in stale:
            print(f"- {rel}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
