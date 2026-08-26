#!/usr/bin/env python3
"""Guard: no accounting path substitutes an empty ledger book for a missing one.

ACCT-CHECKLIST-01 asks for proof that every accounting workflow is ledger-book-native end to end.
Tracing it produced a clear, consistent answer: the tree treats an all-zeros ledger book as
*invalid* and refuses it. AccountingPostingCommandValidator throws ("Accounting posting command
ledger book id is required"), TradeFillLedgerPostingTarget throws, StatementRunWorkflowService
throws, DailyMarkToMarketService and the wash-sale query reject it, ShadowBookValuationService
fails with a reason, and OperationsContinuityWorkflowService states the one deliberate exception --
fund-level workflows omit the book entirely rather than passing an empty one.

Exactly one site disagreed, coercing a nullable book to Guid.Empty while building an evidence
route, which stamped an identifier with a scope every one of those readers would have rejected.

So the invariant worth keeping is narrow and checkable: a *missing* ledger book must be refused or
deliberately omitted, never stood in for. This fails CI when source coerces a nullable ledger-book
id into an empty or default Guid.

Deliberately NOT flagged: comparisons against Guid.Empty (`== Guid.Empty`, `is null or Guid.Empty`).
Those are the correct posture -- they are how the rest of the tree rejects an unscoped book -- and a
guard that confused rejection with substitution would push authors away from the very check it
exists to encourage.
"""

from __future__ import annotations

import argparse
import os
import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
SOURCE_ROOT = REPO_ROOT / "src"

# `<something>LedgerBookId ?? Guid.Empty` / `?? default` / `?? new Guid()`, and the quieter
# GetValueOrDefault() spelling. Anchored on the identifier so an unrelated `?? Guid.Empty` on some
# other id is not this guard's business.
COERCION = re.compile(
    r"(?P<expr>[A-Za-z0-9_.\[\]]*LedgerBookId)\s*"
    r"(?:"
    r"\?\?\s*(?:Guid\.Empty|default(?:\(Guid\))?|new\s+Guid\s*\(\s*\))"
    r"|\.GetValueOrDefault\s*\(\s*\)"
    r")"
)

EXCLUDED_DIRECTORY_NAMES = {"bin", "node_modules", "obj"}


def _iter_sources(source_root: Path) -> list[Path]:
    paths: list[Path] = []
    for current_root, directories, files in os.walk(source_root, topdown=True, followlinks=False):
        directories[:] = sorted(d for d in directories if d.lower() not in EXCLUDED_DIRECTORY_NAMES)
        paths.extend(Path(current_root) / f for f in files if f.lower().endswith(".cs"))
    return sorted(paths)


def find_scope_coercions(source_root: Path) -> list[tuple[str, int, str]]:
    """[(repo-relative path, line, matched expression)] for each substituted ledger book."""
    repo_root = source_root.parent
    findings: list[tuple[str, int, str]] = []
    for path in _iter_sources(source_root):
        text = path.read_text(encoding="utf-8", errors="replace")
        if "LedgerBookId" not in text:
            continue
        for line_number, line in enumerate(text.split("\n"), start=1):
            stripped = line.lstrip()
            if stripped.startswith("//") or stripped.startswith("///"):
                continue
            match = COERCION.search(line)
            if match:
                findings.append((path.relative_to(repo_root).as_posix(), line_number, match.group(0).strip()))
    return findings


def main() -> int:
    parser = argparse.ArgumentParser(description="Enforce ledger-book-native accounting scope.")
    parser.add_argument("--source-root", default=str(SOURCE_ROOT))
    args = parser.parse_args()

    findings = find_scope_coercions(Path(args.source_root))
    if findings:
        print("Ledger-book scope guard FAILED.", file=sys.stderr)
        print(
            "These sites substitute an empty ledger book for a missing one, producing a scope the "
            "rest of the tree rejects as invalid (ACCT-CHECKLIST-01):",
            file=sys.stderr,
        )
        for rel, line, expr in findings:
            print(f"  {rel}:{line}  {expr}", file=sys.stderr)
        print(
            "\nRefuse the missing book instead, the way AccountingPostingCommandValidator and "
            "TradeFillLedgerPostingTarget do -- or, for a genuinely fund-level workflow, omit the "
            "book entirely as OperationsContinuityWorkflowService does. Comparing against "
            "Guid.Empty to reject an unscoped book is fine and is not what this checks.",
            file=sys.stderr,
        )
        return 1

    print("Ledger-book scope guard: no accounting path substitutes an empty ledger book.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
