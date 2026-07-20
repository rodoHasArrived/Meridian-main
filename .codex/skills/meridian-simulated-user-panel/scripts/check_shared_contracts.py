#!/usr/bin/env python3
"""Check normalized host-neutral simulated-user-panel contract parity."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

SKILL_DIR = Path(__file__).resolve().parents[1]
REPO_ROOT = SKILL_DIR.parents[2]
RELATIVE_FILES = [
    "references/review-contract.md",
    "references/personas.md",
    "references/rubric.md",
    "references/review-modes.md",
    "references/artifact-bundles.md",
    "assets/review-manifest.schema.json",
    "assets/review-result.schema.json",
    "assets/eval-result.schema.json",
    "assets/bundles/screen-review.manifest.json",
    "assets/bundles/workflow-walkthrough.manifest.json",
    "assets/bundles/roadmap-review.manifest.json",
    "assets/bundles/ship-readiness.manifest.json",
    "assets/bundles/cross-surface-review.manifest.json",
]
MIRROR_ROOTS = [
    REPO_ROOT / ".agents" / "skills" / "meridian-simulated-user-panel",
    REPO_ROOT / ".claude" / "skills" / "meridian-simulated-user-panel",
]


def normalize(path: Path) -> str:
    return "\n".join(line.rstrip() for line in path.read_text(encoding="utf-8").splitlines()).strip() + "\n"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--summary", action="store_true")
    parser.add_argument("--json", action="store_true")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    failures: list[str] = []
    checked = 0
    for relative in RELATIVE_FILES:
        canonical = SKILL_DIR / relative
        if not canonical.exists():
            failures.append(f"missing canonical file: {relative}")
            continue
        expected = normalize(canonical)
        for mirror_root in MIRROR_ROOTS:
            mirror = mirror_root / relative
            checked += 1
            if not mirror.exists():
                failures.append(f"missing mirror file: {mirror.relative_to(REPO_ROOT)}")
            elif normalize(mirror) != expected:
                failures.append(f"mirror drift: {mirror.relative_to(REPO_ROOT)}")

    payload = {"status": "pass" if not failures else "fail", "failures": failures, "summary": {"checked": checked, "failure_count": len(failures)}}
    if args.json:
        print(json.dumps(payload, indent=2))
    else:
        print(f"check_shared_contracts status={payload['status']} checked={checked} failures={len(failures)}")
        if args.summary:
            for failure in failures:
                print(f"- {failure}")
    return 0 if not failures else 1


if __name__ == "__main__":
    raise SystemExit(main())
