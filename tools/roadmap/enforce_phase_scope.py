#!/usr/bin/env python3
"""Phase scope gate for roadmap/source-doc PRs.

Accepts phase declarations from workflow dispatch input, PR labels, or PR body markers,
then validates changed files against configured glob allowlists.
"""

from __future__ import annotations

import argparse
import fnmatch
import json
import os
import re
import subprocess
import sys
from dataclasses import dataclass
from pathlib import PurePosixPath
from typing import Iterable

PHASE_RULES: dict[str, list[str]] = {
    "PR0": [
        "docs/roadmap/**",
        "docs/roadmap-governance/**",
        "tools/roadmap/**",
        ".github/workflows/roadmap-source-docs.yml",
    ],
    "PR1": [
        "docs/**",
        "tools/roadmap/**",
        ".github/workflows/roadmap-source-docs.yml",
    ],
    "PR2": ["PR1", "schemas/roadmap/**"],
    "PR3": ["PR2", "scripts/roadmap/**", "src/Meridian.Roadmap/**"],
    "PR4": ["PR3", "tests/**"],
    "PR5": ["PR4", ".github/actions/**"],
    "PR6": ["PR5", "build/**"],
    "PR7": ["PR6", "src/**"],
    "PR8": ["PR7", ".github/workflows/**"],
    "PR9": ["**"],
}

PHASE_LABEL = re.compile(r"\bphase:(PR[0-9])\b", re.IGNORECASE)
PHASE_BODY = re.compile(r"<!--\s*phase\s*:\s*(PR[0-9])\s*-->", re.IGNORECASE)


@dataclass(frozen=True)
class PhaseResolution:
    phase: str
    source: str


def run(cmd: list[str]) -> str:
    return subprocess.check_output(cmd, text=True).strip()


def get_changed_files(base_ref: str, head_ref: str) -> list[str]:
    output = run(["git", "diff", "--name-only", f"{base_ref}...{head_ref}"])
    return [line.strip() for line in output.splitlines() if line.strip()]


def expand_patterns(phase: str) -> list[str]:
    expanded: list[str] = []
    seen: set[str] = set()

    def walk(token: str) -> None:
        key = token.upper()
        if key in seen:
            return
        seen.add(key)
        for item in PHASE_RULES[key]:
            if item.upper() in PHASE_RULES:
                walk(item.upper())
            else:
                expanded.append(item)

    walk(phase.upper())
    return expanded


def matches(path: str, patterns: Iterable[str]) -> bool:
    posix = PurePosixPath(path).as_posix()
    return any(fnmatch.fnmatch(posix, pattern) for pattern in patterns)


def resolve_phase(args: argparse.Namespace) -> PhaseResolution:
    if args.phase:
        return PhaseResolution(args.phase.upper(), "--phase")

    if args.dispatch_phase:
        return PhaseResolution(args.dispatch_phase.upper(), "dispatch")

    if args.labels:
        m = PHASE_LABEL.search(args.labels)
        if m:
            return PhaseResolution(m.group(1).upper(), "labels")

    if args.pr_body:
        m = PHASE_BODY.search(args.pr_body)
        if m:
            return PhaseResolution(m.group(1).upper(), "pr_body")

    raise ValueError(
        "No phase declaration found. Provide --phase/--dispatch-phase, "
        "a 'phase:PRx' label, or a PR body marker like '<!-- phase:PR2 -->'."
    )


def parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(description="Enforce roadmap PR phase scope.")
    p.add_argument("--phase", help="Explicit phase (PR0..PR9).")
    p.add_argument("--dispatch-phase", help="Phase from workflow_dispatch input.")
    p.add_argument("--labels", help="Comma-separated PR labels.")
    p.add_argument("--pr-body", help="PR body markdown.")
    p.add_argument("--base-ref", default=os.getenv("GITHUB_BASE_REF", "origin/main"))
    p.add_argument("--head-ref", default=os.getenv("GITHUB_SHA", "HEAD"))
    p.add_argument("--changed-file", action="append", default=[])
    p.add_argument("--json", action="store_true", help="Emit JSON details to stdout.")
    return p


def main() -> int:
    args = parser().parse_args()

    try:
        resolved = resolve_phase(args)
    except ValueError as ex:
        print(f"::error::{ex}")
        return 2

    if resolved.phase not in PHASE_RULES:
        print(f"::error::Unsupported phase '{resolved.phase}'. Expected PR0..PR9.")
        return 2

    files = args.changed_file if args.changed_file else get_changed_files(args.base_ref, args.head_ref)
    patterns = expand_patterns(resolved.phase)
    violations = [f for f in files if not matches(f, patterns)]

    if args.json:
        print(json.dumps({"phase": resolved.phase, "source": resolved.source, "changed": files, "patterns": patterns, "violations": violations}, indent=2))

    if violations:
        print(f"::error::Phase scope gate failed for {resolved.phase} (source: {resolved.source}).")
        print("--- allowed-patterns")
        for ptn in patterns:
            print(f"+++ {ptn}")
        print("--- violating-files")
        for path in violations:
            print(f"- {path}")
        print("\nHint: widen phase marker only when roadmap governance allows it.")
        return 1

    print(f"Phase scope gate passed for {resolved.phase} (source: {resolved.source}).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
