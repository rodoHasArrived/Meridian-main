#!/usr/bin/env python3
"""Render Make target help without requiring POSIX grep/awk."""

from __future__ import annotations

import re
import sys
from collections import OrderedDict
from pathlib import Path

TARGET_RE = re.compile(r"^([A-Za-z0-9][A-Za-z0-9_-]+):.*?##\s*(.+)$")

CATEGORIES: list[tuple[str, str]] = [
    ("General", r"^help$"),
    ("Installation", r"^(quickstart|install|install-docker|install-native|setup-config|check-deps|install-hooks|setup-dev|verify-setup)$"),
    ("Docker", r"^(docker|docker-build|docker-up|docker-down|docker-logs|docker-restart|docker-clean|docker-monitoring)$"),
    (
        "Development",
        r"^(bootstrap|verify-fast|verify-full|verify-release|build|build-quick|"
        r"build-web-workstation|run|run-backfill|run-selftest|lint|format|"
        r"format-check|watch|watch-build|clean|benchmark|bench-quick|"
        r"bench-filter|bench-velocity|test|test-unit|test-fsharp|"
        r"test-integration|test-scenario|test-all|test-coverage)$",
    ),
    (
        "Documentation",
        r"^(docs|verify-docs|docs-health|docs-stale-mark|docs-stale-update|"
        r"docs-hashes-refresh|docs-source-readmes-sync|"
        r"docs-source-readmes-tree-check|gen-context|verify-adrs|"
        r"verify-contracts|verify-tooling-metadata|gen-interfaces|"
        r"gen-structure|gen-providers|gen-workflows|gen-workflow-manifest|"
        r"update-claude-md|generate-icons|generate-diagrams|docs-lint|"
        r"docs-all|check-workflow-docs-parity|check-status-delivery-claims)$",
    ),
    (
        "Desktop",
        r"^(verify-desktop|desktop-build|desktop-test|desktop-test-dev|"
        r"desktop-test-position-blotter-route|desktop-test-operator-inbox-route)$",
    ),
    ("Publishing", r"^(publish|publish-linux|publish-windows|publish-macos)$"),
    ("Pre-PR & Quality", r"^(pre-pr|pre-pr-full)$"),
    ("AI Required Quality Gates", r"^(ai-verify|ai-arch-check)$"),
    (
        "AI Advisory Tooling",
        r"^(ai-audit|ai-audit-code|ai-audit-docs|ai-audit-tests|"
        r"ai-audit-ai-docs|ai-report|ai-arch-check-summary|"
        r"ai-arch-check-json|ai-codex-skills-check|ai-docs-freshness|"
        r"ai-docs-drift|ai-docs-sync-report|ai-docs-map|"
        r"ai-docs-map-check|ai-plan-checklists-check)$",
    ),
    ("AI Maintenance & Reporting", r"^(ai-maintenance-light|ai-maintenance-full|ai-docs-archive|ai-docs-archive-execute)$"),
    (
        "Skills",
        r"^(skill-list|skill-resources|skill-scripts|skill-chains|"
        r"skill-resource|skill-run|skill-chain|skill-run-chain|"
        r"skill-validate|skill-run-eval|skill-benchmark|skill-discover)$",
    ),
    (
        "Diagnostics",
        r"^(doctor|doctor-ci|doctor-quick|doctor-fix|diagnose|diagnose-build|"
        r"collect-debug|collect-debug-minimal|build-profile|build-binlog|"
        r"validate-data|analyze-errors|build-graph|fingerprint|env-capture|"
        r"env-diff|impact|bisect|metrics|history|health|status|"
        r"app-metrics|version)$",
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

    rendered: set[str] = set()
    for heading, pattern in CATEGORIES:
        matcher = re.compile(pattern)
        entries = [
            (target, text)
            for target, text in targets.items()
            if target not in rendered and matcher.search(target)
        ]
        if not entries:
            continue

        width = 28 if heading.startswith("AI") or heading.startswith("Pre-PR") else 18
        print()
        print(f"{heading}:")
        for target, text in entries:
            print(f"  {target:<{width}} {text}")
            rendered.add(target)

    other_entries = [(target, text) for target, text in targets.items() if target not in rendered]
    if other_entries:
        print()
        print("Other:")
        for target, text in other_entries:
            print(f"  {target:<18} {text}")

    print()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
