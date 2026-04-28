#!/usr/bin/env python3
"""Validate documented make targets and PowerShell script paths."""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]

MAKE_TARGET_RE = re.compile(r"^([A-Za-z0-9][A-Za-z0-9_-]*):")
DOC_MAKE_LINE_RE = re.compile(r"^\s*(?:\$|PS>)?\s*make\s+([A-Za-z0-9][A-Za-z0-9_-]*)\b")
DOC_PWSH_FILE_RE = re.compile(r"\b(?:pwsh|powershell)\b[^\n`]*?\s+-File\s+([./A-Za-z0-9_-]+\.ps1)\b")
DOC_PWSH_DIRECT_RE = re.compile(r"\b(?:pwsh|powershell)\s+([./]*scripts/[A-Za-z0-9_./-]+\.ps1)\b")
FENCED_BLOCK_RE = re.compile(r"```[^\n]*\n(.*?)```", re.DOTALL)


def collect_make_targets() -> set[str]:
    targets: set[str] = set()
    makefiles = [REPO_ROOT / "Makefile", *(REPO_ROOT / "make").glob("*.mk")]
    for mk in makefiles:
        for line in mk.read_text(encoding="utf-8").splitlines():
            m = MAKE_TARGET_RE.match(line)
            if not m:
                continue
            target = m.group(1)
            if target == ".PHONY":
                continue
            targets.add(target)
    return targets


def normalize_script_path(raw: str) -> Path:
    cleaned = raw.strip().strip('"\'')
    cleaned = cleaned.removeprefix("./")
    return REPO_ROOT / cleaned


def validate_docs(docs: list[Path], make_targets: set[str]) -> tuple[list[str], list[str]]:
    missing_targets: list[str] = []
    missing_scripts: list[str] = []

    for doc in docs:
        text = doc.read_text(encoding="utf-8")

        for block in FENCED_BLOCK_RE.findall(text):
            for line in block.splitlines():
                make_match = DOC_MAKE_LINE_RE.search(line)
                if make_match:
                    target = make_match.group(1)
                    if target not in make_targets:
                        missing_targets.append(f"{doc}: make {target}")

                script_hits = DOC_PWSH_FILE_RE.findall(line) + DOC_PWSH_DIRECT_RE.findall(line)
                for script in script_hits:
                    script_path = normalize_script_path(script)
                    if not script_path.exists():
                        missing_scripts.append(f"{doc}: {script}")

    return missing_targets, missing_scripts


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--docs",
        nargs="+",
        default=[
            "README.md",
            "docs/development/desktop-workflow-automation.md",
            "docs/development/desktop-testing-guide.md",
            "docs/development/policies/desktop-support-policy.md",
            "docs/development/wpf-implementation-notes.md",
        ],
        help="Markdown files or directories to lint.",
    )
    args = parser.parse_args()

    docs: list[Path] = []
    for doc_input in args.docs:
        path = REPO_ROOT / doc_input
        if path.is_dir():
            docs.extend(sorted(path.rglob("*.md")))
        elif path.is_file():
            docs.append(path)

    make_targets = collect_make_targets()
    missing_targets, missing_scripts = validate_docs(docs, make_targets)

    if missing_targets or missing_scripts:
        print("Command snippet lint failed.")
        if missing_targets:
            print("\nUnknown make targets:")
            for item in missing_targets:
                print(f"  - {item}")
        if missing_scripts:
            print("\nMissing PowerShell scripts:")
            for item in missing_scripts:
                print(f"  - {item}")
        return 1

    print(f"Validated command snippets in {len(docs)} markdown files.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
