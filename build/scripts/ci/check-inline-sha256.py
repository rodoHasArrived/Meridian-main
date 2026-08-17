#!/usr/bin/env python3
"""Ratchet: no new inline SHA-256 hashing outside Sha256Digest.

Meridian.Contracts.Integrity.Sha256Digest owns digest computation and declares the
canonical encoding (lowercase hex, enforced by IsCanonical at governance gates). The
name-based helper audits (#2610/#2690) consolidated the 18 files that *named* a
ComputeSha256 helper, but an open-coded `SHA256.HashData(...)` has no name to audit,
which is how 293 inline call sites accumulated unnoticed — 33 of them producing
uppercase hex via bare Convert.ToHexString while at least eight validators reject
anything but lowercase (#2691).

So this fails CI when a file adds an inline `SHA256.HashData` call, or when a new file
starts making one. Migration batches shrink the baseline with --update-baseline; the end
state is an empty baseline, at which point inline hashing anywhere outside the primitive
fails.

Counting call sites (not statements) keeps the check cheap and deterministic; a file
that genuinely needs raw digest bytes (not hex) still trips the ratchet only when its
count grows, so existing byte-oriented sites are grandfathered until their migration
batch.
"""

from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
DEFAULT_BASELINE = Path(__file__).resolve().parent / "inline-sha256-baseline.json"

TRACKED_CALL = "SHA256.HashData"

# The canonical home is not an inline site of itself.
EXCLUDED_FILES = {
    "src/Meridian.Contracts/Integrity/Sha256Digest.cs",
}

EXCLUDED_DIRECTORY_NAMES = {"bin", "node_modules", "obj"}


def count_call_sites(repo_root: Path) -> dict[str, int]:
    counts: dict[str, int] = {}
    source_root = repo_root / "src"
    candidate_paths: list[Path] = []
    for current_root, directories, files in os.walk(source_root, topdown=True, followlinks=False):
        directories[:] = sorted(
            directory for directory in directories if directory.lower() not in EXCLUDED_DIRECTORY_NAMES
        )
        candidate_paths.extend(
            Path(current_root) / file_name
            for file_name in files
            if file_name.lower().endswith(".cs")
        )

    for path in sorted(candidate_paths):
        rel = path.relative_to(repo_root).as_posix()
        if rel in EXCLUDED_FILES:
            continue
        occurrences = path.read_text(encoding="utf-8", errors="replace").count(TRACKED_CALL)
        if occurrences:
            counts[rel] = occurrences
    return counts


def main() -> int:
    parser = argparse.ArgumentParser(description="Enforce the inline SHA-256 hashing ratchet.")
    parser.add_argument("--baseline", default=str(DEFAULT_BASELINE))
    parser.add_argument(
        "--update-baseline",
        action="store_true",
        help="Rewrite the baseline from current call-site counts (use in migration batch PRs).",
    )
    args = parser.parse_args()

    baseline_path = Path(args.baseline)
    current = count_call_sites(REPO_ROOT)

    if args.update_baseline:
        payload = {
            "description": "Per-file counts of inline SHA256.HashData call sites outside "
            "Meridian.Contracts.Integrity.Sha256Digest. CI fails when a file exceeds its count or a "
            "new file appears; migration batches shrink this toward empty.",
            "tracked_call": TRACKED_CALL,
            "files": current,
            "total": sum(current.values()),
        }
        baseline_path.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")
        print(f"Baseline updated: {len(current)} file(s), {sum(current.values())} call site(s).")
        return 0

    baseline = json.loads(baseline_path.read_text(encoding="utf-8"))["files"]

    violations: list[str] = []
    for rel, count in sorted(current.items()):
        allowed = baseline.get(rel)
        if allowed is None:
            violations.append(f"NEW inline SHA-256 site: {rel} ({count} call site(s))")
        elif count > allowed:
            violations.append(f"{rel}: {count} call site(s), baseline allows {allowed}")

    improved = {rel: baseline[rel] - current.get(rel, 0) for rel in baseline if current.get(rel, 0) < baseline[rel]}

    if violations:
        print("Inline SHA-256 hashing ratchet violations:", file=sys.stderr)
        for violation in violations:
            print(f"- {violation}", file=sys.stderr)
        print(
            "Use Meridian.Contracts.Integrity.Sha256Digest (Compute / ComputeUtf8) instead of "
            "open-coding SHA256.HashData. It produces the canonical lowercase encoding that the "
            "governance validators (Sha256Digest.IsCanonical) require; a bare Convert.ToHexString "
            "produces uppercase, which those validators reject. If a site genuinely needs raw "
            "digest bytes, add the capability to Sha256Digest rather than open-coding it. "
            "If a batch PR reduced counts, refresh with --update-baseline.",
            file=sys.stderr,
        )
        return 1

    total = sum(current.values())
    print(
        f"Inline SHA-256 ratchet: {total} call site(s) across {len(current)} file(s) "
        "(baseline satisfied)."
    )
    if improved:
        print(f"{len(improved)} file(s) improved below baseline — run --update-baseline in the migration PR to lock gains.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
