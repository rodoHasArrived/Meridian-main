#!/usr/bin/env python3
"""Preview or resolve known generated conflicts during a merge of main.

This only seeds conflicted outputs from the incoming main commit. Regeneration,
validation, and review of the complete merged tree are still required.
"""

from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path


# Whole-file generator outputs only. Source READMEs, HELP.md, schema snapshots,
# diagrams, and the reviewed source-hash manifest require separate review.
# Do not replace this list with a wildcard over docs/status or docs/generated.
GENERATED_FILES = frozenset({
    "docs/generated/repository-structure.md",
    "docs/generated/workflows-overview.md",
    "docs/status/doc-health-dashboard.json",
    "docs/status/doc-health-dashboard.md",
    "docs/status/docs-automation-summary.json",
    "docs/status/docs-automation-summary.md",
    "docs/status/todo-scan-results.json",
    "docs/status/TODO.md",
    "docs/status/coverage-report.md",
    "docs/status/api-docs-report.md",
    "docs/status/workflow-drift-report.md",
    "docs/roadmap/generated/MANIFEST.json",
    "docs/roadmap/generated/ROADMAP_SUMMARY.md",
    "docs/roadmap/generated/roadmap-register.md",
    "docs/status/ROADMAP_SUMMARY.md",
    "docs/source/generated/MANIFEST.json",
    "docs/source/generated/source-module-index.md",
    "docs/source/generated/source-roadmap-traceability.md",
    "docs/source/generated/source-todo-checklist.md",
})
WORKSTATION_ROOT = "src/Meridian.Ui/wwwroot/workstation/"


def git(root: Path, *args: str, data: bytes | None = None) -> bytes:
    result = subprocess.run(
        ["git", "--literal-pathspecs", *args], cwd=root, input=data,
        capture_output=True, check=True,
    )
    return result.stdout


def generated(path: str) -> bool:
    return path.startswith(WORKSTATION_ROOT) or path in GENERATED_FILES


def unmerged_paths(root: Path) -> list[str]:
    entries = git(root, "ls-files", "--unmerged", "-z").split(b"\0")
    return sorted({entry.split(b"\t", 1)[1].decode("utf-8") for entry in entries if entry})


def recover(root: Path, main_ref: str, apply: bool) -> int:
    root = Path(git(root, "rev-parse", "--show-toplevel").decode("utf-8").strip())
    merge_path = Path(git(root, "rev-parse", "--git-path", "MERGE_HEAD").decode("utf-8").strip())
    if not merge_path.is_absolute():
        merge_path = root / merge_path
    if not merge_path.is_file():
        raise ValueError("No merge is in progress. Start from a clean branch and merge origin/main first.")
    parents = merge_path.read_text(encoding="utf-8").splitlines()
    if len(parents) != 1:
        raise ValueError("Only a merge with one incoming parent is supported.")
    main_sha = git(root, "rev-parse", "--verify", "--end-of-options", f"{main_ref}^{{commit}}").decode().strip()
    if parents[0] != main_sha:
        raise ValueError(f"MERGE_HEAD does not match {main_ref}; refusing to select the wrong merge parent.")

    conflicts = unmerged_paths(root)
    candidates = [path for path in conflicts if generated(path)]
    manual = [path for path in conflicts if not generated(path)]
    print(f"Incoming main: {main_sha}")
    for path in candidates:
        print(f"GENERATED  {path}")
    for path in manual:
        print(f"REVIEW     {path}")
    print(f"{len(candidates)} generated conflict(s); {len(manual)} conflict(s) require review.")

    if apply and candidates:
        # Use the pinned merge parent, not ours/theirs (which reverse during a
        # rebase). NUL-delimited literal paths handle spaces and wildcard names.
        # restore cannot resolve an unmerged path absent from the source tree.
        # Explicitly remove obsolete names, including rename/rename conflicts.
        incoming_paths = set(git(root, "ls-tree", "-r", "--name-only", "-z", main_sha).split(b"\0"))
        present = [path for path in candidates if path.encode("utf-8") in incoming_paths]
        deleted = [path for path in candidates if path.encode("utf-8") not in incoming_paths]
        for paths, command in (
            (present, ("restore", f"--source={main_sha}", "--staged", "--worktree")),
            (deleted, ("rm", "--force")),
        ):
            if paths:
                pathspec = b"".join(path.encode("utf-8") + b"\0" for path in paths)
                git(root, *command, "--pathspec-from-file=-", "--pathspec-file-nul", data=pathspec)
        remaining_generated = [path for path in unmerged_paths(root) if generated(path)]
        if remaining_generated:
            raise ValueError("Generated conflicts remain: " + ", ".join(remaining_generated))
        print("Selected incoming output. Resolve the remaining conflicts, then regenerate and validate before committing.")
    elif not apply:
        print("Preview only. Pass --apply to stage the listed generated resolutions.")
    return 1 if manual else 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, default=Path.cwd(), help="Checkout to inspect (default: current directory).")
    parser.add_argument("--main-ref", default="origin/main", help="Main ref that must match MERGE_HEAD.")
    parser.add_argument("--apply", action="store_true", help="Resolve only listed generated conflicts; default is preview.")
    args = parser.parse_args()
    try:
        return recover(args.repo, args.main_ref, args.apply)
    except (ValueError, OSError, subprocess.CalledProcessError) as exc:
        detail = exc.stderr.decode("utf-8", errors="replace").strip() if isinstance(exc, subprocess.CalledProcessError) else str(exc)
        print(f"Generated conflict recovery failed: {detail}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
