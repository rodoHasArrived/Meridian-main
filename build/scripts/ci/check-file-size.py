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
    python3 build/scripts/ci/check-file-size.py --tighten-baseline
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


def _baseline_threshold(root: Path) -> int | None:
    """The threshold the committed baseline was generated against, if it records one."""
    path = _baseline_path(root)
    if not path.exists():
        return None
    data = json.loads(path.read_text(encoding="utf-8"))
    recorded = data.get("threshold_lines")
    return int(recorded) if recorded is not None else None


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
    root: Path,
    baseline: dict[str, int],
    current: dict[str, int],
    *,
    strict: bool = False,
) -> dict[str, int]:
    """Current line count for every baselined file, including ones the scan omits.

    A baselined file that has shrunk below the threshold is absent from `current`, so its real size
    has to be read from disk. Treating it as zero would erase its lines from the totals and
    overstate the reclaimable figure at exactly the moment a decomposition is about to retire it.

    `strict` raises instead of falling back to zero for a file that exists but cannot be read. The
    reporting path can tolerate a bad count; the tightening path cannot, because a file that looks
    empty gets its cap written away.
    """
    lines: dict[str, int] = {}
    for rel in baseline:
        if rel in current:
            lines[rel] = current[rel]
            continue
        path = root / rel
        if strict and path.exists():
            # Deliberately unguarded: an unreadable tracked file must abort a mutating run.
            with path.open("rb") as handle:
                lines[rel] = sum(1 for _ in handle)
        else:
            lines[rel] = _count_lines(path)
    return lines


def _report_trend(root: Path, baseline: dict[str, int], current: dict[str, int]) -> None:
    """Print the burn-down numbers: what is tracked, and how much of it is reclaimable."""
    live_lines = _live_lines(root, baseline, current)
    capped = sum(baseline.values())
    live = sum(live_lines.values())
    slack = sum(max(0, cap - live_lines[rel]) for rel, cap in baseline.items())

    print(
        f"Baseline trend: {len(baseline)} tracked file(s), {capped:,} capped line(s), "
        f"{live:,} current line(s), {slack:,} line(s) reclaimable."
    )

    tight = sorted(
        ((cap - live_lines[rel], rel, live_lines[rel], cap)
         for rel, cap in baseline.items()
         if cap - live_lines[rel] <= TIGHT_HEADROOM_LINES),
        key=lambda item: (item[0], item[1]),
    )
    if tight:
        pinned = sum(1 for headroom, *_ in tight if headroom <= 0)
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
            "Run --tighten-baseline to lock in the reclaimable lines so they cannot be given back."
        )


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="check-file-size")
    parser.add_argument("--threshold", type=int, default=THRESHOLD_LINES)
    parser.add_argument(
        "--update-baseline",
        action="store_true",
        help="Regenerate the baseline from the current tree instead of checking.",
    )
    parser.add_argument(
        "--tighten-baseline",
        action="store_true",
        help=(
            "Lower caps toward current line counts. Unlike --update-baseline this never raises a "
            "cap or adds a file, so reclaimed lines cannot be silently given back."
        ),
    )
    parser.add_argument(
        "--buffer",
        type=int,
        default=0,
        help=(
            "With --tighten-baseline, leave this many lines of headroom above the current count "
            "so the file stays editable. The cap is still never raised above its existing value."
        ),
    )
    args = parser.parse_args(argv)

    if args.update_baseline and args.tighten_baseline:
        print("ERROR: choose either --update-baseline or --tighten-baseline.", file=sys.stderr)
        return 2

    if args.buffer and not args.tighten_baseline:
        print(
            "ERROR: --buffer applies only to --tighten-baseline. Passing it elsewhere (notably "
            "with --update-baseline) would silently pin caps to the current count, the opposite "
            "of the requested headroom.",
            file=sys.stderr,
        )
        return 2

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

    if args.tighten_baseline:
        # Raising the threshold would make _scan omit files that are still oversized by the
        # baseline's own standard, and the retirement filter below would then delete their caps —
        # a downward-only operation silently dropping protections. Require the same threshold.
        recorded_threshold = _baseline_threshold(root)
        if recorded_threshold is not None and recorded_threshold != args.threshold:
            print(
                f"ERROR: --tighten-baseline requires the baseline's own threshold "
                f"({recorded_threshold}), got {args.threshold}. Tightening under a different "
                f"threshold would retire files the baseline still protects.",
                file=sys.stderr,
            )
            return 2

        if args.buffer < 0:
            print("ERROR: --buffer cannot be negative.", file=sys.stderr)
            return 2

        # Tightening writes a baseline and exits 0. Doing that while the tree still violates the
        # ratchet would hand automation a success for a tree that fails the documented contract,
        # and would bake the violation in as the new normal.
        if new_god_files or grown_files:
            print(
                "ERROR: refusing to tighten while the ratchet is failing. Resolve these first:",
                file=sys.stderr,
            )
            for rel, lines in new_god_files:
                print(f"- NEW god file: {rel} has {lines} lines (> {args.threshold}).",
                      file=sys.stderr)
            for rel, lines, cap in grown_files:
                print(f"- GREW past cap: {rel} now {lines} lines (baseline cap {cap}).",
                      file=sys.stderr)
            return 2

        try:
            live_lines = _live_lines(root, baseline, current, strict=True)
        except OSError as error:
            print(
                f"ERROR: cannot read a tracked file, refusing to tighten: {error}. "
                f"An unreadable file would be counted as empty and have its cap written away.",
                file=sys.stderr,
            )
            return 2
        tightened: dict[str, int] = {}
        retired: list[str] = []
        reclaimed = 0
        for rel, cap in sorted(baseline.items()):
            lines = live_lines[rel]
            # Retiring an entry removes its cap, so the file's only remaining protection is the
            # threshold itself. Hold the entry until the threshold supplies the requested headroom,
            # otherwise a file dropped to one line under it would fail as a brand-new god file on
            # the next added line rather than having the buffer it was promised.
            if lines + args.buffer <= args.threshold:
                retired.append(rel)
                reclaimed += max(0, cap - lines)
                continue
            # min() keeps this strictly downward-only: the buffer can leave headroom above the
            # current count, but never above the cap the baseline already recorded.
            tightened[rel] = min(cap, lines + args.buffer)
            reclaimed += cap - tightened[rel]

        _write_baseline(root, args.threshold, tightened)

        headroom = ""
        if args.buffer and tightened:
            smallest = min(cap - live_lines[rel] for rel, cap in tightened.items())
            headroom = (
                f", {args.buffer} line(s) of headroom kept"
                if smallest >= args.buffer
                else f", as little as {smallest} line(s) of headroom retained "
                     f"(requested {args.buffer}; existing caps limited the rest)"
            )
        print(
            f"Tightened baseline: {reclaimed:,} line(s) reclaimed, "
            f"{len(retired)} file(s) retired, {len(tightened)} still tracked{headroom}."
        )
        for rel in retired:
            print(f"- retired (now under threshold): {rel}")
        return 0

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
        print("Notice: baseline entries now under threshold (retire them with "
              "--tighten-baseline):")
        for rel in stale:
            print(f"- {rel}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
