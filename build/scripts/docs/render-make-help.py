#!/usr/bin/env python3
"""Render Make target help without requiring POSIX grep/awk."""

from __future__ import annotations

import re
import sys
from collections import OrderedDict
from pathlib import Path

TARGET_RE = re.compile(r"^([A-Za-z0-9][A-Za-z0-9_-]+):.*?##\s*(.+)$")

CATEGORIES: list[tuple[str, str]] = [
    ("Installation", r"install|setup"),
    ("Docker", r"docker"),
    ("Development", r"run|build|test|clean|bench|lint|watch|setup-dev|format"),
    (
        "Documentation",
        r"docs|verify-adr|verify-contract|verify-tooling-metadata|gen-context|"
        r"gen-interface|gen-structure|gen-provider|gen-workflow|update-claude|"
        r"generate-icons|generate-diagrams",
    ),
    ("Publishing", r"publish"),
    ("Pre-PR & Quality", r"^pre-pr|^pre-pr-full"),
    ("AI Required Quality Gates", r"^ai-verify|^ai-arch-check$"),
    (
        "AI Advisory Tooling",
        r"^ai-audit|^ai-report|^ai-codex-skills-check|^ai-docs-freshness|"
        r"^ai-docs-drift|^ai-docs-sync-report|^ai-docs-map|"
        r"^ai-plan-checklists-check|^ai-arch-check-summary|^ai-arch-check-json",
    ),
    ("AI Maintenance & Reporting", r"^ai-maintenance|^ai-docs-archive"),
    ("Skills", r"skill-"),
    (
        "Diagnostics",
        r"doctor|diagnose|verify-setup|collect-debug|build-profile|build-binlog|"
        r"build-graph|fingerprint|env-|impact|bisect|metrics|history|"
        r"validate-data|analyze-errors",
    ),
]


def collect_targets(paths: list[Path]) -> OrderedDict[str, str]:
    targets: OrderedDict[str, str] = OrderedDict()
    for path in paths:
        if not path.exists():
            continue
        for line in path.read_text(encoding="utf-8").splitlines():
            match = TARGET_RE.match(line)
            if match:
                targets.setdefault(match.group(1), match.group(2).strip())
    return targets


def main() -> int:
    paths = [Path(arg) for arg in sys.argv[1:]]
    targets = collect_targets(paths)

    print()
    print("+-----------------------------------------------------------------------+")
    print("|                      Meridian - Make Commands                         |")
    print("+-----------------------------------------------------------------------+")

    for heading, pattern in CATEGORIES:
        matcher = re.compile(pattern)
        entries = [(target, text) for target, text in targets.items() if matcher.search(target)]
        if not entries:
            continue

        width = 28 if heading.startswith("AI") or heading.startswith("Pre-PR") else 18
        print()
        print(f"{heading}:")
        for target, text in entries:
            print(f"  {target:<{width}} {text}")

    print()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
