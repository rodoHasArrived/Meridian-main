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
    python3 build/scripts/ci/check-file-size.py --tighten-baseline [--buffer 50]

--tighten-baseline is the downward-only counterpart to --update-baseline (#2675). It lowers each
cap to the file's current size plus a working buffer, never raises one, retires an entry only once
the threshold itself provides at least that buffer of headroom, and records the retained headroom
in the baseline so the trend does not report deliberate slack as an unlocked reduction. It refuses
to run at all while the ratchet is failing or while any governed source is unreadable.
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


# Returns None when the size could not be determined, rather than a number that looks like a
# measurement. An earlier version returned 0 on any OSError, which put "unreadable" and "empty" in
# the same bucket and produced two distinct bugs from one line: a normal run reported an unreadable
# file's whole cap as reclaimed, and --update-baseline dropped its cap entirely. Callers must decide
# what an unknown means for them; there is no safe default here.
def _try_count_lines(path: Path) -> int | None:
    try:
        with path.open("rb") as handle:
            return sum(1 for _ in handle)
    except FileNotFoundError:
        # Genuinely gone: zero is the honest count, and for a baselined entry it is the whole point
        # of the trend. Distinguished here rather than by a stat() probe, because Path.exists() and
        # Path.is_file() re-raise any OSError outside (ENOENT, ENOTDIR, EBADF, ELOOP) - EACCES among
        # them - which would crash the caller on an untraversable parent.
        return 0
    except OSError:
        return None


def _iter_source_files(src_root: Path) -> list[Path]:
    files: list[Path] = []
    for dirpath, dirnames, filenames in os.walk(src_root, topdown=True, followlinks=False):
        dirnames[:] = [d for d in dirnames if d.lower() not in _SKIP_DIR_NAMES]
        for filename in filenames:
            if filename.endswith(SOURCE_SUFFIXES):
                files.append(Path(dirpath) / filename)
    return files


def _scan(root: Path, threshold: int) -> tuple[dict[str, int], list[str]]:
    """Oversized non-excluded files, plus every governed source whose size could not be read.

    An unreadable file is indistinguishable from a small one here: it produces no line count, so it
    is simply absent from the oversized set. That is harmless for a read-only check — the trend
    reports baselined entries it could not read — but it is not harmless for anything that writes
    the baseline from this result, because absent means "drop the cap".
    """
    src_root = root / "src"
    oversized: dict[str, int] = {}
    unreadable: list[str] = []
    for path in _iter_source_files(src_root):
        rel = path.relative_to(root).as_posix()
        if _is_excluded(rel):
            continue
        lines = _try_count_lines(path)
        if lines is None:
            unreadable.append(rel)
            continue
        if lines > threshold:
            oversized[rel] = lines
    return dict(sorted(oversized.items())), sorted(unreadable)


def _load_baseline(root: Path) -> dict[str, int]:
    path = _baseline_path(root)
    if not path.exists():
        return {}
    data = json.loads(path.read_text(encoding="utf-8"))
    return {str(k): int(v) for k, v in data.get("files", {}).items()}


def _load_headroom(root: Path) -> dict[str, int]:
    """Deliberate headroom per file, recorded by --tighten-baseline.

    Distinguishes "room left on purpose so the file can still be edited" from "reduction not yet
    locked in". Without the distinction, the slack a tightening deliberately retained reads as
    reclaimable, and the next ordinary run recommends the command that destroys it (#2675 defect 6).
    Absent for baselines never tightened, which is equivalent to zero everywhere.
    """
    path = _baseline_path(root)
    if not path.exists():
        return {}
    data = json.loads(path.read_text(encoding="utf-8"))
    return {str(k): int(v) for k, v in data.get("headroom", {}).items()}


def _write_baseline(
    root: Path, threshold: int, oversized: dict[str, int], headroom: dict[str, int] | None = None
) -> None:
    path = _baseline_path(root)
    path.parent.mkdir(parents=True, exist_ok=True)
    payload: dict[str, object] = {
        "_comment": (
            "No-new-god-file ratchet baseline. Each entry caps a currently "
            "oversized source file at its recorded line count. Files may shrink "
            "freely; growing one past its cap or adding a new file over the "
            "threshold fails CI. Lock in reductions with "
            "`python3 build/scripts/ci/check-file-size.py --tighten-baseline`, "
            "which only lowers caps; `--update-baseline` regenerates from the "
            "tree (and can raise caps), so it needs justification in review. "
            "The optional headroom map records lines deliberately left spare by "
            "the last tightening, so deliberate slack is not reported as an "
            "unlocked reduction."
        ),
        "threshold_lines": threshold,
        "files": oversized,
    }
    # Only entries for files still tracked; --update-baseline passes None and drops the map,
    # because re-pinning every cap at its exact current size leaves nothing deliberate about
    # whatever slack later appears.
    if headroom:
        payload["headroom"] = {rel: headroom[rel] for rel in sorted(headroom) if rel in oversized}
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

    Neither case may raise. This runs after the pass/fail verdict has been printed, so an exception
    here would replace a declared exit code with a traceback — turning a reporting nicety into a
    broken gate.
    """
    live: dict[str, int] = {}
    unreadable: list[str] = []
    for rel, cap in baseline.items():
        if rel in current:
            live[rel] = current[rel]
            continue

        lines = _try_count_lines(root / rel)
        if lines is None:
            live[rel] = cap
            unreadable.append(rel)
        else:
            live[rel] = lines

    return live, unreadable


def _report_trend(
    root: Path,
    baseline: dict[str, int],
    current: dict[str, int],
    headroom: dict[str, int] | None = None,
) -> None:
    """Print the burn-down numbers: what is tracked, and how much of it is reclaimable.

    Reclaimable means "reduction not yet locked in". Headroom a tightening deliberately retained is
    subtracted per file, because reporting it as reclaimable would recommend destroying it.
    """
    headroom = headroom or {}
    live_lines, unreadable = _live_lines(root, baseline, current)
    capped = sum(baseline.values())
    live = sum(live_lines.values())
    slack = sum(
        max(0, cap - live_lines[rel] - headroom.get(rel, 0)) for rel, cap in baseline.items()
    )
    retained = sum(
        min(headroom.get(rel, 0), max(0, cap - live_lines[rel]))
        for rel, cap in baseline.items()
    )

    print(
        f"Baseline trend: {len(baseline)} tracked file(s), {capped:,} capped line(s), "
        f"{live:,} current line(s), {slack:,} line(s) reclaimable."
    )
    if retained:
        print(
            f"({retained:,} further line(s) of cap are deliberate working headroom from the last "
            f"--tighten-baseline and are not counted as reclaimable.)"
        )
    if unreadable:
        print(
            f"NOTE: {len(unreadable)} baselined file(s) could not be read and are counted at their "
            f"cap, so the figures above understate rather than invent progress:"
        )
        for rel in unreadable:
            print(f"- {rel}")

    # Two kinds of entry are kept out of this list, for the same reason: their headroom is not a
    # measurement.
    #
    # Over-cap files have negative headroom, so an unbounded comparison admits a file hundreds of
    # lines past its cap, prints a negative "spare" count, claims an already-failing file "sits at
    # its cap", and sorts ahead of the near-cap files this warning surfaces.
    #
    # Unreadable files were substituted at their cap by _live_lines, which is the safe choice for
    # the aggregate totals but reads as exactly zero headroom here — so they would be announced as
    # pinned, and enough of them could push genuine near-cap files out of the ten shown. The
    # substitution stays; only the claim about it goes.
    #
    # Both are already reported by name elsewhere: over-cap files in the failure block with their
    # exact overage, unreadable ones in the NOTE above.
    unreadable_set = set(unreadable)
    tight = sorted(
        ((cap - live_lines[rel], rel, live_lines[rel], cap)
         for rel, cap in baseline.items()
         if rel not in unreadable_set and 0 <= cap - live_lines[rel] <= TIGHT_HEADROOM_LINES),
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
            "Those lines are not yet locked in: the caps still allow the files to grow back. "
            "Lock them in with --tighten-baseline, which lowers caps and never raises one."
        )


# Default working headroom --tighten-baseline leaves above each file's current size. Deliberately
# larger than TIGHT_HEADROOM_LINES so a freshly tightened baseline does not immediately announce
# every file as near its cap.
DEFAULT_TIGHTEN_BUFFER = 50


def _load_baseline_threshold(root: Path) -> int:
    path = _baseline_path(root)
    if not path.exists():
        return THRESHOLD_LINES
    data = json.loads(path.read_text(encoding="utf-8"))
    return int(data.get("threshold_lines", THRESHOLD_LINES))


def _tighten_baseline(
    root: Path,
    threshold: int,
    buffer: int,
    baseline: dict[str, int],
    current: dict[str, int],
) -> tuple[int, dict[str, int], dict[str, int], list[tuple[str, int]], list[str]]:
    """Compute the tightened baseline. Pure: reads sizes, writes nothing.

    Returns (exit_code, new_files, new_headroom, retired, unreadable_tracked). Only an exit code of
    0 carries meaningful maps.

    The rules, each the fix for a numbered defect in #2675:

    - A cap only moves down: new cap = min(old cap, lines + buffer).
    - An entry retires only when the threshold itself supplies the requested headroom
      (lines + buffer <= threshold). A file one line under the threshold keeps a cap rather than
      being handed the harder brand-new-god-file failure (defect 2).
    - A deleted file (line count genuinely zero because it is gone) retires; an unreadable one
      aborts the whole command, because a file that cannot be read is not a file with zero lines
      (defects 3 and 4).
    """
    new_files: dict[str, int] = {}
    new_headroom: dict[str, int] = {}
    retired: list[tuple[str, int]] = []
    unreadable_tracked: list[str] = []

    for rel, cap in baseline.items():
        if rel in current:
            lines: int | None = current[rel]
        else:
            lines = _try_count_lines(root / rel)
        if lines is None:
            unreadable_tracked.append(rel)
            continue

        if lines + buffer <= threshold:
            retired.append((rel, cap))
            continue

        new_cap = min(cap, lines + buffer)
        new_files[rel] = new_cap
        # Only positive headroom is recorded: a zero entry protects nothing and would bloat the
        # baseline with one line per pinned file.
        if new_cap > lines:
            new_headroom[rel] = new_cap - lines

    if unreadable_tracked:
        return 2, {}, {}, [], sorted(unreadable_tracked)
    return 0, dict(sorted(new_files.items())), new_headroom, sorted(retired), []


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="check-file-size")
    parser.add_argument("--threshold", type=int, default=None)
    parser.add_argument(
        "--update-baseline",
        action="store_true",
        help="Regenerate the baseline from the current tree instead of checking.",
    )
    parser.add_argument(
        "--tighten-baseline",
        action="store_true",
        help="Lower caps to current size plus --buffer; never raises a cap. "
             "Retires an entry only once the threshold itself supplies that headroom.",
    )
    parser.add_argument(
        "--buffer",
        type=int,
        default=None,
        help=f"Working headroom --tighten-baseline retains above each file's current size "
             f"(default {DEFAULT_TIGHTEN_BUFFER}). Only valid with --tighten-baseline.",
    )
    args = parser.parse_args(argv)

    # Contract slips from the withdrawn first attempt (#2675): options that are accepted and
    # ignored exit 0 while doing something other than what was asked, so both are hard errors.
    if args.buffer is not None and not args.tighten_baseline:
        print("ERROR: --buffer is only meaningful with --tighten-baseline.", file=sys.stderr)
        return 2
    if args.tighten_baseline and args.threshold is not None:
        print(
            "ERROR: --tighten-baseline uses the threshold recorded in the baseline; an explicit "
            "--threshold would retire entries the baseline still protects.",
            file=sys.stderr,
        )
        return 2
    if args.tighten_baseline and args.update_baseline:
        print("ERROR: --tighten-baseline and --update-baseline are mutually exclusive.",
              file=sys.stderr)
        return 2

    root = _repo_root()
    src_root = root / "src"
    if not src_root.exists():
        print(f"ERROR: source root not found: {src_root}", file=sys.stderr)
        return 2

    # Tightening reads its threshold from the baseline it is tightening; everything else takes
    # the flag or the module default.
    if args.tighten_baseline:
        threshold = _load_baseline_threshold(root)
    else:
        threshold = args.threshold if args.threshold is not None else THRESHOLD_LINES

    current, unreadable_sources = _scan(root, threshold)

    if args.tighten_baseline:
        baseline_path = _baseline_path(root)
        if not baseline_path.exists():
            print("ERROR: no baseline found. Run with --update-baseline first.", file=sys.stderr)
            return 2
        baseline = _load_baseline(root)
        buffer = args.buffer if args.buffer is not None else DEFAULT_TIGHTEN_BUFFER
        if buffer < 0:
            print("ERROR: --buffer must be non-negative.", file=sys.stderr)
            return 2

        # Fail closed on anything unreadable, tracked or not. An unreadable untracked source is
        # invisible to the scan, so a new god file could ride through the very command that
        # rewrites the protections (#2675 defect 4).
        if unreadable_sources:
            print(
                f"ERROR: refusing to tighten. {len(unreadable_sources)} governed source file(s) "
                f"could not be read:",
                file=sys.stderr,
            )
            for rel in unreadable_sources:
                print(f"- {rel}", file=sys.stderr)
            return 2

        # A mutating command must not exit 0 on a tree that fails the documented contract
        # (#2675 defect 5). Report the violations exactly as the ordinary check would, then stop.
        failing_new = sorted(rel for rel in current if rel not in baseline)
        failing_grown = sorted(rel for rel in current if rel in baseline and current[rel] > baseline[rel])
        if failing_new or failing_grown:
            print("ERROR: refusing to tighten while the ratchet is failing:", file=sys.stderr)
            for rel in failing_new:
                print(f"- NEW god file: {rel} has {current[rel]} lines (> {threshold}).",
                      file=sys.stderr)
            for rel in failing_grown:
                print(f"- GREW past cap: {rel} now {current[rel]} lines "
                      f"(baseline cap {baseline[rel]}).", file=sys.stderr)
            print("Fix the violations (or --update-baseline with justification), then tighten.",
                  file=sys.stderr)
            return 1

        code, new_files, new_headroom, retired, unreadable_tracked = _tighten_baseline(
            root, threshold, buffer, baseline, current
        )
        if code != 0:
            print(
                f"ERROR: refusing to tighten. {len(unreadable_tracked)} baselined file(s) could "
                f"not be read, and a file that cannot be read is not a file with zero lines:",
                file=sys.stderr,
            )
            for rel in unreadable_tracked:
                print(f"- {rel}", file=sys.stderr)
            return 2

        _write_baseline(root, threshold, new_files, new_headroom)

        # Progress accounting counts retired entries (#2675 defect 1). A retired file's future
        # effective cap is the threshold itself - exceeding it fails as a new god file - so the
        # locked-in reduction it contributes is cap minus threshold, not zero.
        locked_kept = sum(baseline[rel] - new_files[rel] for rel in new_files)
        locked_retired = sum(max(0, cap - threshold) for _, cap in retired)
        retained_actual = sum(new_headroom.values())

        print(
            f"Tightened baseline: {len(baseline)} tracked file(s) -> {len(new_files)} kept, "
            f"{len(retired)} retired."
        )
        print(
            f"Locked in {locked_kept + locked_retired:,} capped line(s) "
            f"({locked_retired:,} from retired entries); retained {retained_actual:,} line(s) of "
            f"working headroom (requested {buffer} per file)."
        )
        for rel, cap in retired:
            print(f"- retired: {rel} (cap was {cap}; the {threshold}-line threshold now protects it)")
        _report_trend(root, new_files, current, new_headroom)
        return 0

    if args.update_baseline:
        # Refuse to write from an incomplete scan. An unreadable source is absent from `current`,
        # and absent is how this file spells "no longer oversized" - so the write would silently
        # delete that file's cap, and the trend printed afterwards reloads the new baseline and can
        # no longer see the entry it just dropped. A transient permission or I/O error would
        # therefore retire a protection with an exit code of 0 and a clean-looking report.
        #
        # The read-only path deliberately does not fail this way: it reports what it could not read
        # and carries on, because a check that cannot see a file should not invent a verdict about
        # it either way. Only the mutating path has to be certain.
        if unreadable_sources:
            print(
                f"ERROR: refusing to rewrite the baseline. {len(unreadable_sources)} governed "
                f"source file(s) could not be read, and writing now would drop their caps:",
                file=sys.stderr,
            )
            for rel in unreadable_sources:
                print(f"- {rel}", file=sys.stderr)
            return 2

        _write_baseline(root, threshold, current)
        print(f"Wrote baseline with {len(current)} tracked file(s) "
              f"(threshold {threshold} lines).")
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
    headroom = _load_headroom(root)

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
            print(f"- NEW god file: {rel} has {lines} lines (> {threshold}). "
                  f"Split it into composed units, or if unavoidable add it to the "
                  f"baseline with justification.", file=sys.stderr)
        for rel, lines, cap in grown_files:
            print(f"- GREW past cap: {rel} now {lines} lines (baseline cap {cap}, "
                  f"exceeded by {lines - cap}). Reduce it, or update the baseline with "
                  f"justification.", file=sys.stderr)
        _report_trend(root, baseline, current, headroom)
        return 1

    print(f"File-size ratchet OK: {len(current)} tracked file(s), "
          f"no new or grown god files (threshold {threshold} lines).")
    _report_trend(root, baseline, current, headroom)
    if stale:
        print("Notice: baseline entries now under threshold (lock the reduction in "
              "with --tighten-baseline, which never raises a cap):")
        for rel in stale:
            print(f"- {rel}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
