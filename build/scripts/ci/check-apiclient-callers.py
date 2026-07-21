#!/usr/bin/env python3
"""Ratchet: no new callers of the legacy null-swallowing ApiClientService seam.

ApiClientService.GetAsync<T>/PostAsync<T> return null for every failure, so callers
cannot distinguish a backend outage from absent data (audit finding P7). The migration
end state moves every caller onto the ApiResponse<T> *WithResponseAsync seam and deletes
the legacy methods; until then this ratchet fails CI when a file adds a legacy call site
or a new file starts using the seam. Batch PRs shrink the baseline with --update-baseline.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
DEFAULT_BASELINE = Path(__file__).resolve().parent / "apiclient-caller-baseline.json"

# Generic legacy calls only: `.GetAsync<T>(` / `.PostAsync<T>(`. The *WithResponseAsync
# replacements and non-generic HttpClient.GetAsync calls do not match.
CALL_PATTERN = re.compile(r"\.(?:GetAsync|PostAsync)<")

# The seam's own definition file is not a caller.
EXCLUDED_FILES = {
    "src/Meridian.Ui.Services/Services/ApiClientService.cs",
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
        matches = CALL_PATTERN.findall(path.read_text(encoding="utf-8", errors="replace"))
        if matches:
            counts[rel] = len(matches)
    return counts


def main() -> int:
    parser = argparse.ArgumentParser(description="Enforce the ApiClientService caller ratchet.")
    parser.add_argument("--baseline", default=str(DEFAULT_BASELINE))
    parser.add_argument(
        "--update-baseline",
        action="store_true",
        help="Rewrite the baseline from the current call-site counts (use in migration batch PRs).",
    )
    args = parser.parse_args()

    baseline_path = Path(args.baseline)
    current = count_call_sites(REPO_ROOT)

    if args.update_baseline:
        payload = {
            "description": "Per-file counts of legacy ApiClientService GetAsync<T>/PostAsync<T> call sites. "
            "CI fails when any file exceeds its count or a new file appears; migration batches shrink this to empty.",
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
            violations.append(f"NEW legacy caller file: {rel} ({count} call site(s))")
        elif count > allowed:
            violations.append(f"{rel}: {count} legacy call site(s), baseline allows {allowed}")

    improved = {rel: baseline[rel] - current.get(rel, 0) for rel in baseline if current.get(rel, 0) < baseline[rel]}

    if violations:
        print("Legacy ApiClientService caller ratchet violations:", file=sys.stderr)
        for violation in violations:
            print(f"- {violation}", file=sys.stderr)
        print(
            "Use GetWithResponseAsync/PostWithResponseAsync (ApiResponse<T>) instead; "
            "see the P7 migration track. If a batch PR reduced counts, refresh with --update-baseline.",
            file=sys.stderr,
        )
        return 1

    total = sum(current.values())
    print(f"ApiClientService caller ratchet: {total} legacy call site(s) across {len(current)} file(s) (baseline satisfied).")
    if improved:
        print(f"{len(improved)} file(s) improved below baseline — run --update-baseline in the migration PR to lock gains.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
