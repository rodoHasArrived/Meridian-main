#!/usr/bin/env python3
"""Ratchet: no new private copies of helpers that now have a shared owner.

Meridian.Contracts.Text.TextPrimitives owns NormalizeOptional, RequireText, and
FirstNonBlank. Before it existed the same one-liners were redeclared privately in
hundreds of files, and copy-paste had become the normal way to obtain one (#2614).

The cost of that is not the duplicated lines, it is that a copied helper is free to
drift. NormalizeOptional had already split into three behaviours under one name and
signature -- most copies trimmed to null, one also lower-cased, one also upper-cased --
so the same call spelled the same way meant different things in different files. Nothing
failed at the time it diverged, which is what this check is for: the count grew from 62
to 64 while the issue describing it sat open.

So this fails CI when a file adds a private declaration of a tracked helper, or when a
new file starts declaring one. Migration batches shrink the baseline with
--update-baseline; the end state is an empty baseline, at which point a copy anywhere
fails.

Tracked names include documented aliases: TrimOrNull is NormalizeOptional under another
name, and FirstNonEmpty is FirstNonBlank, which is part of why the counts grew -- nobody
could tell which to reach for.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
DEFAULT_BASELINE = Path(__file__).resolve().parent / "duplicate-helper-baseline.json"

# Helpers with a shared owner, plus the aliases that name the same function.
TRACKED_HELPERS = (
    "NormalizeOptional",
    "RequireText",
    "FirstNonBlank",
    "TrimOrNull",
    "FirstNonEmpty",
)

# A *declaration* of one of the tracked names, not a call. Matches any accessibility and
# any return type, so `private static string? NormalizeOptional(` and
# `internal static string RequireText(` both count. Exact name only: the near-name
# variants (NormalizeOptionalToken, NormalizeOptionalUpperInvariant, ...) are separate
# functions and are deliberately out of scope -- renaming one to say what it does is the
# fix this check should encourage, not something it should punish.
DECLARATION_PATTERN = re.compile(
    r"\b(?:private|internal|protected|public)\s+(?:static\s+)?(?:readonly\s+)?"
    r"[\w?<>\[\], .]+?\s+(" + "|".join(TRACKED_HELPERS) + r")\s*\("
)

# The canonical home is not a duplicate of itself.
EXCLUDED_FILES = {
    "src/Meridian.Contracts/Text/TextPrimitives.cs",
}

EXCLUDED_DIRECTORY_NAMES = {"bin", "node_modules", "obj"}


def count_declarations(repo_root: Path) -> dict[str, int]:
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
        matches = DECLARATION_PATTERN.findall(path.read_text(encoding="utf-8", errors="replace"))
        if matches:
            counts[rel] = len(matches)
    return counts


def main() -> int:
    parser = argparse.ArgumentParser(description="Enforce the consolidated-helper duplication ratchet.")
    parser.add_argument("--baseline", default=str(DEFAULT_BASELINE))
    parser.add_argument(
        "--update-baseline",
        action="store_true",
        help="Rewrite the baseline from current declaration counts (use in migration batch PRs).",
    )
    args = parser.parse_args()

    baseline_path = Path(args.baseline)
    current = count_declarations(REPO_ROOT)

    if args.update_baseline:
        payload = {
            "description": "Per-file counts of private declarations of helpers owned by "
            "Meridian.Contracts.Text.TextPrimitives. CI fails when a file exceeds its count or a "
            "new file appears; migration batches shrink this toward empty.",
            "tracked_helpers": list(TRACKED_HELPERS),
            "files": current,
            "total": sum(current.values()),
        }
        baseline_path.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")
        print(f"Baseline updated: {len(current)} file(s), {sum(current.values())} declaration(s).")
        return 0

    baseline = json.loads(baseline_path.read_text(encoding="utf-8"))["files"]

    violations: list[str] = []
    for rel, count in sorted(current.items()):
        allowed = baseline.get(rel)
        if allowed is None:
            violations.append(f"NEW private helper copy: {rel} ({count} declaration(s))")
        elif count > allowed:
            violations.append(f"{rel}: {count} declaration(s), baseline allows {allowed}")

    improved = {rel: baseline[rel] - current.get(rel, 0) for rel in baseline if current.get(rel, 0) < baseline[rel]}

    if violations:
        print("Consolidated-helper duplication ratchet violations:", file=sys.stderr)
        for violation in violations:
            print(f"- {violation}", file=sys.stderr)
        print(
            "Use Meridian.Contracts.Text.TextPrimitives instead "
            "(`using static Meridian.Contracts.Text.TextPrimitives;`). If the helper genuinely "
            "differs from the shared one, give it a name that says how -- a copy that silently "
            "behaves differently under the same name is the failure this check exists to prevent. "
            "If a batch PR reduced counts, refresh with --update-baseline.",
            file=sys.stderr,
        )
        return 1

    total = sum(current.values())
    print(
        f"Consolidated-helper ratchet: {total} private declaration(s) across {len(current)} file(s) "
        "(baseline satisfied)."
    )
    if improved:
        print(f"{len(improved)} file(s) improved below baseline — run --update-baseline in the migration PR to lock gains.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
