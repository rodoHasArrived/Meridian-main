#!/usr/bin/env python3
"""Validate cross-host routing semantics references stay aligned."""

from __future__ import annotations

import argparse
import sys
from pathlib import Path


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--summary", action="store_true", help="Print compact summary output.")
    return parser.parse_args()


def read(path: str) -> str:
    return Path(path).read_text(encoding="utf-8")


def main() -> int:
    _ = parse_args()
    checks = [
        (
            "docs/ai/codex/README.md",
            [
                "prompt-route-rules.json",
                "prompt-route-linter.py",
                "handoff-packet-generator.py",
            ],
        ),
        (
            "docs/ai/copilot/instructions.md",
            [
                "agent-handoff-checklist.md",
                "parallel-task-manifest-template.md",
                "work-modes.md",
            ],
        ),
        (
            "docs/ai/assistant-workflow-contract.md",
            [
                "agent-handoff-checklist.md",
                "parallel-task-manifest-template.md",
                "work-modes.md",
            ],
        ),
        (
            "docs/ai/README.md",
            [
                "model-routing-policy.json",
                "agent-handoff-checklist.md",
                "parallel-task-manifest-template.md",
            ],
        ),
    ]

    failures: list[str] = []
    for path, tokens in checks:
        text = read(path)
        for token in tokens:
            if token not in text:
                failures.append(f"{path} missing required routing semantics reference: {token}")

    if failures:
        print("[check-ai-routing-parity] FAILED:", file=sys.stderr)
        for failure in failures:
            print(f" - {failure}", file=sys.stderr)
        return 1

    print("[check-ai-routing-parity] OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
