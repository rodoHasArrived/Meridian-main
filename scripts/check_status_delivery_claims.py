#!/usr/bin/env python3
"""Validate status-doc readiness claim language and evidence anchors."""

from __future__ import annotations

import argparse
import re
import string
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]

DEFAULT_DOCS = (
    "docs/status/production-status.md",
    "docs/status/provider-validation-matrix.md",
)

DENYLIST_PHRASES = (
    "production ready for live trading",
    "live-readiness complete",
    "fully ready for live trading",
    "ready for live trading",
)

APPROVED_SCOPING_PHRASES = (
    "paper-trading",
    "paper workflow",
    "local evidence",
    "local/paper evidence",
    "not readiness exits",
    "remain open",
    "in progress",
    "active wave 1 evidence gate",
)



def _is_negated_phrase(lowered_text: str, start_index: int) -> bool:
    context = lowered_text[max(0, start_index - 40):start_index]
    trimmed = context.rstrip(string.whitespace + string.punctuation)
    return any(
        trimmed.endswith(marker)
        for marker in (
            "not",
            "not yet",
            "never",
            "no longer",
            "isn't",
            "isnt",
            "aren't",
            "arent",
            "cannot",
            "can't",
            "cant",
        )
    )


def _has_unnegated_phrase(lowered_text: str, phrase: str) -> bool:
    start = 0
    while True:
        match_index = lowered_text.find(phrase, start)
        if match_index == -1:
            return False
        if not _is_negated_phrase(lowered_text, match_index):
            return True
        start = match_index + len(phrase)

PASS_PACKET_REFERENCE_RE = re.compile(
    r"(artifacts/provider-validation/_automation/\d{4}-\d{2}-\d{2}/[^\s`]*dk1-pilot-parity-packet\.json|artifacts/provider-validation/_automation/\d{4}-\d{2}-\d{2}/|artifact id\s*[:#-]?\s*`?[A-Za-z0-9_.-]+`?|signed\s+\d{4}-\d{2}-\d{2}\s+dk1\s+parity\s+packet)",
    re.IGNORECASE,
)


def _is_archive_migration_stub(lowered_text: str) -> bool:
    return "**status:** archive-migration-stub" in lowered_text


def validate_doc(path: Path) -> list[str]:
    errors: list[str] = []
    text = path.read_text(encoding="utf-8")
    lowered = text.lower()

    if _is_archive_migration_stub(lowered):
        return errors

    if not PASS_PACKET_REFERENCE_RE.search(text):
        errors.append(
            f"{path}: missing latest pass packet reference (expected dated artifacts/provider-validation/_automation/<yyyy-mm-dd>/ path, dk1 packet path, or explicit artifact id)."
        )

    for phrase in DENYLIST_PHRASES:
        if _has_unnegated_phrase(lowered, phrase):
            errors.append(f"{path}: prohibited live-readiness claim phrase found: '{phrase}'.")

    if not any(phrase in lowered for phrase in APPROVED_SCOPING_PHRASES):
        errors.append(
            f"{path}: missing approved local/paper scoping phrase; expected one of: {', '.join(APPROVED_SCOPING_PHRASES)}."
        )

    return errors


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--docs", nargs="*", default=list(DEFAULT_DOCS), help="Status docs to validate.")
    args = parser.parse_args()

    errors: list[str] = []
    for doc in args.docs:
        path = REPO_ROOT / doc
        if not path.exists():
            errors.append(f"{doc}: file not found.")
            continue
        errors.extend(validate_doc(path))

    if errors:
        print("Status-doc claim validation failed:", file=sys.stderr)
        for err in errors:
            print(f" - {err}", file=sys.stderr)
        return 1

    print("Status-doc claim validation passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
