#!/usr/bin/env python3
"""No-new-raw-evidence ratchet for docs/testing/evidence/.

Committed run evidence should be a reviewable summary (README.md, small manifests, screenshots),
not raw run output. Raw logs, archives, and large payloads belong in CI artifacts or under
archive/ once they stop being read.

A tracked file under the evidence root is *raw* when either:

  1. its suffix is one of RAW_SUFFIXES (logs, archives, test-result and HAR captures), or
  2. it is larger than THRESHOLD_BYTES.

Every raw file that exists today is recorded in build/config/evidence-footprint-baseline.json
with its current size as a cap. The ratchet then fails when:

  * a raw file that is not in the baseline appears (new raw evidence), or
  * a baselined file grows past its cap.

Deleting or shrinking baselined files is always allowed; the run reports how much of the baseline
is reclaimable, and ``--tighten-baseline`` drops retired entries and lowers caps (never raises).
Existing packets are not moved by this script: some are checksum-pinned immutable evidence (see
IMMUTABLE_EVIDENCE_MANIFESTS in build/scripts/docs/validate-docs-structure.py).

Exit codes: 0 pass, 1 violation, 2 script error.

Usage:
    python3 build/scripts/ci/check-evidence-footprint.py
    python3 build/scripts/ci/check-evidence-footprint.py --tighten-baseline
    python3 build/scripts/ci/check-evidence-footprint.py --update-baseline   # needs review justification
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
from pathlib import Path

EVIDENCE_ROOT = "docs/testing/evidence"
BASELINE = "build/config/evidence-footprint-baseline.json"
THRESHOLD_BYTES = 256 * 1024
RAW_SUFFIXES = (".log", ".gz", ".tgz", ".zip", ".7z", ".tar", ".trx", ".har", ".binlog", ".etl", ".dmp")
BASELINE_COMMENT = (
    "No-new-raw-evidence ratchet baseline for docs/testing/evidence/. Each entry caps an existing raw "
    "evidence file (log/archive suffix, or larger than threshold_bytes) at its recorded size. Files may "
    "shrink or be deleted freely; a new raw file or a grown one fails CI. Keep a summary README.md per "
    "run and publish raw output as CI artifacts instead. Lock in deletions with "
    "`python3 build/scripts/ci/check-evidence-footprint.py --tighten-baseline`."
)


def repo_root() -> Path:
    return Path(__file__).resolve().parents[3]


def is_raw(rel_path: str, size: int, threshold: int) -> bool:
    return rel_path.lower().endswith(RAW_SUFFIXES) or size > threshold


def tracked_evidence(root: Path) -> list[str]:
    """Tracked files under the evidence root; falls back to a directory walk outside git."""
    try:
        result = subprocess.run(
            ["git", "-C", str(root), "ls-files", "-z", "--", EVIDENCE_ROOT],
            check=True, capture_output=True,
        )
        return sorted(p for p in result.stdout.decode("utf-8").split("\0") if p)
    except (OSError, subprocess.CalledProcessError):
        base = root / EVIDENCE_ROOT
        if not base.is_dir():
            return []
        return sorted(
            (Path(dirpath) / name).relative_to(root).as_posix()
            for dirpath, _, names in os.walk(base)
            for name in names
        )


def current_raw(root: Path, threshold: int) -> dict[str, int]:
    raw: dict[str, int] = {}
    for rel in tracked_evidence(root):
        path = root / rel
        try:
            # Count bytes without carriage returns so a CRLF checkout on Windows measures the same.
            data = path.read_bytes()
        except FileNotFoundError:
            continue  # tracked but deleted in the working tree
        size = len(data) - data.count(b"\r\n")
        if is_raw(rel, size, threshold):
            raw[rel] = size
    return raw


def load_baseline(root: Path) -> tuple[int, dict[str, int]]:
    path = root / BASELINE
    if not path.is_file():
        return THRESHOLD_BYTES, {}
    data = json.loads(path.read_text(encoding="utf-8"))
    return int(data.get("threshold_bytes", THRESHOLD_BYTES)), {k: int(v) for k, v in data["files"].items()}


def write_baseline(root: Path, threshold: int, files: dict[str, int]) -> None:
    payload = {"_comment": BASELINE_COMMENT, "threshold_bytes": threshold, "files": dict(sorted(files.items()))}
    target = root / BASELINE
    target.parent.mkdir(parents=True, exist_ok=True)
    temporary = target.with_suffix(".json.tmp")
    temporary.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    os.replace(temporary, target)


def evaluate(raw: dict[str, int], baseline: dict[str, int]) -> tuple[list[str], int]:
    """Return (violations, reclaimable bytes)."""
    violations: list[str] = []
    for rel, size in sorted(raw.items()):
        cap = baseline.get(rel)
        if cap is None:
            violations.append(f"new raw evidence: {rel} ({size:,} bytes)")
        elif size > cap:
            violations.append(f"baselined evidence grew: {rel} ({size:,} > cap {cap:,} bytes)")
    reclaimable = sum(cap - min(cap, raw.get(rel, 0)) for rel, cap in baseline.items())
    return violations, reclaimable


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--root", type=Path, default=repo_root())
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--update-baseline", action="store_true", help="Rewrite the baseline from the tree.")
    mode.add_argument("--tighten-baseline", action="store_true", help="Lower caps / retire entries only.")
    args = parser.parse_args(argv)
    root = args.root.resolve()

    try:
        threshold, baseline = load_baseline(root)
        raw = current_raw(root, threshold)
    except (OSError, ValueError, KeyError) as exc:
        print(f"Evidence footprint check failed: {exc}", file=sys.stderr)
        return 2

    if args.update_baseline:
        write_baseline(root, threshold, raw)
        print(f"Evidence baseline rewritten: {len(raw)} raw file(s), {sum(raw.values()):,} bytes.")
        return 0

    violations, reclaimable = evaluate(raw, baseline)
    if args.tighten_baseline:
        if violations:
            print("Refusing to tighten while the ratchet is failing.", file=sys.stderr)
            for line in violations:
                print(f"  {line}", file=sys.stderr)
            return 1
        tightened = {rel: min(cap, raw[rel]) for rel, cap in baseline.items() if rel in raw}
        write_baseline(root, threshold, tightened)
        print(f"Evidence baseline tightened: {len(baseline) - len(tightened)} entr(ies) retired, "
              f"{reclaimable:,} bytes locked in.")
        return 0

    print(f"Evidence footprint: {len(raw)} raw file(s), {sum(raw.values()):,} bytes "
          f"(baseline {len(baseline)} file(s), {sum(baseline.values()):,} bytes; {reclaimable:,} reclaimable).")
    if violations:
        print("Raw evidence ratchet failed. Commit a summary README.md and publish raw output as a CI "
              "artifact instead (or move retired packets under archive/).", file=sys.stderr)
        for line in violations:
            print(f"  {line}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
