#!/usr/bin/env python3
"""Contract compatibility gate for workstation/strategy/ledger API surfaces."""

from __future__ import annotations

import argparse
import re
import subprocess
import sys
from pathlib import Path

TRACKED_PREFIXES = (
    "src/Meridian.Contracts/Workstation/",
    "src/Meridian.Strategies/Services/",
    "src/Meridian.Ledger/",
)
TRACKED_EXACT = {
    "src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs",
}

BREAKING_REMOVAL_PATTERNS = (
    re.compile(r"^-\s*public\s+(?:sealed\s+|static\s+|partial\s+|readonly\s+|abstract\s+)*"
               r"(?:record|class|interface|enum|struct|delegate)\b"),
    re.compile(r"^-\s*public\s+[^=;]+\([^)]*\)\s*(?:=>|\{|;)"),
    re.compile(r"^-\s*group\.Map(?:Get|Post|Put|Delete|Patch)\("),
    re.compile(r"^-\s*\.WithName\("),
    re.compile(r"^-\s*\.Produces<"),
)


def run_git(args: list[str]) -> str:
    result = subprocess.run(["git", *args], check=True, capture_output=True, text=True)
    return result.stdout


def is_tracked(path: str) -> bool:
    return path in TRACKED_EXACT or path.startswith(TRACKED_PREFIXES)


def load_pr_body(path: str | None) -> str:
    if not path:
        return ""
    file_path = Path(path)
    if not file_path.exists():
        return ""
    return file_path.read_text(encoding="utf-8")


def migration_note_in_pr_body(pr_body: str) -> bool:
    if not pr_body.strip():
        return False

    if re.search(r"\[x\]\s+.*contract migration notes added", pr_body, re.IGNORECASE):
        return True

    return bool(re.search(r"migration notes", pr_body, re.IGNORECASE))


def migration_note_in_matrix(matrix_doc: Path) -> bool:
    if not matrix_doc.exists():
        return False

    content = matrix_doc.read_text(encoding="utf-8")
    if "## Migration Notes" not in content:
        return False

    return bool(re.search(r"-\s+\d{4}-\d{2}-\d{2}:", content))


def main() -> int:
    parser = argparse.ArgumentParser(description="Enforce contract compatibility migration-note requirements.")
    parser.add_argument("--base", required=True, help="Base git ref for comparison.")
    parser.add_argument("--head", required=True, help="Head git ref for comparison.")
    parser.add_argument(
        "--matrix-doc",
        default="docs/status/contract-compatibility-matrix.md",
        help="Compatibility matrix documentation path.",
    )
    parser.add_argument("--pr-body-file", default=None, help="Optional pull request body text file.")
    args = parser.parse_args()

    diff_range = f"{args.base}...{args.head}"
    changed_files_raw = run_git(["diff", "--name-only", diff_range])
    changed_files = [line.strip() for line in changed_files_raw.splitlines() if line.strip()]

    tracked_changed_files = [path for path in changed_files if is_tracked(path)]
    if not tracked_changed_files:
        print("No tracked contract surfaces changed; compatibility gate passed.")
        return 0

    print("Tracked surfaces changed:")
    for path in tracked_changed_files:
        print(f"  - {path}")

    patch = run_git(["diff", "--unified=0", diff_range, "--", *tracked_changed_files])
    is_breaking = any(pattern.search(line) for line in patch.splitlines() for pattern in BREAKING_REMOVAL_PATTERNS)

    if not is_breaking:
        print("Tracked surfaces changed, but no breaking-removal heuristic matched; compatibility gate passed.")
        return 0

    print("Potential breaking contract change detected by removal heuristics.")

    matrix_doc = Path(args.matrix_doc)
    matrix_updated = args.matrix_doc in changed_files
    has_matrix_migration_note = migration_note_in_matrix(matrix_doc)
    pr_body = load_pr_body(args.pr_body_file)
    has_pr_migration_note = migration_note_in_pr_body(pr_body)

    failures: list[str] = []
    if not matrix_updated:
        failures.append(f"- Update `{args.matrix_doc}` with compatibility and migration notes for this break.")
    if not has_matrix_migration_note:
        failures.append(f"- Add an entry under `## Migration Notes` in `{args.matrix_doc}` using `- YYYY-MM-DD:` format.")
    if args.pr_body_file and not has_pr_migration_note:
        failures.append("- Include migration notes in the PR body and check the contract migration-notes checklist item.")

    if failures:
        print("Contract compatibility gate failed:")
        for failure in failures:
            print(failure)
        return 1

    print("Breaking change has migration documentation and notes; compatibility gate passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
