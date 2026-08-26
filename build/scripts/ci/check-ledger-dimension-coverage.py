#!/usr/bin/env python3
"""Guard: every declared ledger dimension reaches every surface that enumerates dimensions.

ACCT-CHECKLIST-03 asks for durable dimensional persistence and query coverage across journal
lines, report filters, close checks, and export mappings. `LedgerLineDimensionSet` is the canonical
set, and `Meridian.Ledger` already drives its own consumers off one registry
(`LedgerLineDimensionSetFields`) so a new dimension reaches report packs and close scope keys for
free. That registry is internal to `Meridian.Ledger`, so every surface outside that assembly
re-lists the fields by hand -- and each hand-written list is a place a dimension can be dropped
without anything failing.

The failure mode is silent and wrong rather than loud:

* dropped from `LedgerDimensionSetDto`, the dimension never crosses the API boundary at all;
* dropped from the JSONB containment predicate, `PostgresLedgerJournalStore.QueryAsync` ignores it,
  so a dimension-scoped journal query returns rows outside the requested scope -- no exception;
* dropped from the financial-record explorer, operators cannot filter or even see a dimension the
  ledger is faithfully persisting.

This gate re-derives the canonical dimension list from `LedgerLineDimensionSet` itself and fails
when any enumerating surface omits one. It checks presence, not behavior: a surface that names a
dimension is assumed to handle it, and the C# tests cover what the value does.
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]

CANONICAL_SOURCE = Path("src/Meridian.Ledger/LedgerLineDimensionSet.cs")

# Each surface: the file, the member that enumerates dimensions, and how a dimension appears there.
# "properties" surfaces name the C# member; "json-keys" surfaces name the camelCase wire key.
SURFACES = [
    (
        Path("src/Meridian.Contracts/Ledger/AccountingConfigurationDtos.cs"),
        "LedgerDimensionSetDto",
        "properties",
        "the API contract mirror -- a dimension missing here never crosses the API boundary",
    ),
    (
        Path("src/Meridian.Storage/Ledger/PostgresLedgerJournalStore.Serialization.cs"),
        "BuildLineDimensionContainmentJson",
        "json-keys",
        "the JSONB containment predicate -- a dimension missing here is ignored by "
        "dimension-scoped journal queries, which then return rows outside the requested scope",
    ),
    (
        Path("src/Meridian.Storage/Ledger/PostgresLedgerJournalStore.Serialization.cs"),
        "CanonicalizeLineDimensions",
        "properties",
        "canonicalization rebuilds the record field by field -- a dimension missing here is "
        "stripped before the line is ever persisted",
    ),
    (
        Path("src/Meridian.Ui.Shared/Services/FinancialRecordExplorerReadService.cs"),
        "AddDimensionFilters",
        "properties",
        "the explorer's report filters -- a dimension missing here cannot be filtered on",
    ),
    (
        Path("src/Meridian.Ui.Shared/Services/FinancialRecordExplorerReadService.cs"),
        "BuildDimensionFields",
        "properties",
        "the explorer's record detail -- a dimension missing here is invisible to operators",
    ),
]


def camel_case(name: str) -> str:
    return name[0].lower() + name[1:] if name else name


def canonical_dimensions(text: str) -> list[str]:
    """Dimension member names declared on LedgerLineDimensionSet, in declaration order."""
    positional = re.findall(
        r"^\s{4}(?:string\?|Guid\?|IReadOnlyDictionary<string, string>\?)\s+(\w+)\s*=", text, re.M
    )
    declared = re.findall(
        r"public\s+(?:Guid\?|IReadOnlyDictionary<string, string>)\s+(\w+)\s*\{\s*get;\s*init;", text
    )
    ordered: list[str] = []
    for name in [*positional, *declared]:
        if name not in ordered:
            ordered.append(name)
    return ordered


def member_body(text: str, member: str) -> str | None:
    """The brace-delimited body of a type or method declaration, found by name."""
    # The *declaration*, not a call site: an earlier version matched the first mention and brace-
    # matched from an unrelated block, which reported every dimension as missing everywhere.
    match = re.search(
        r"^[ \t]*public\s+(?:sealed\s+)?record\s+" + re.escape(member) + r"\b"
        r"|^[ \t]*(?:private|internal|public|protected)[^\n(]*?\b" + re.escape(member) + r"\s*\(",
        text,
        re.MULTILINE,
    )
    if not match:
        return None
    opening = text.find("{", match.start())
    if opening < 0:
        return None
    # A positional record's members live in its parameter list, which precedes the brace.
    prefix = text[match.start() : opening]
    depth = 0
    for index in range(opening, len(text)):
        if text[index] == "{":
            depth += 1
        elif text[index] == "}":
            depth -= 1
            if depth == 0:
                return prefix + text[opening : index + 1]
    return prefix + text[opening:]


def find_gaps(repo_root: Path) -> list[tuple[str, str, list[str], str]]:
    """[(file, member, missing dimensions, why it matters)] for each surface that drops one."""
    canonical_path = repo_root / CANONICAL_SOURCE
    dimensions = canonical_dimensions(canonical_path.read_text(encoding="utf-8"))
    if len(dimensions) < 5:
        raise SystemExit(
            f"{CANONICAL_SOURCE.as_posix()} yielded only {len(dimensions)} dimensions; the guard "
            "cannot have parsed it correctly, so it is failing rather than passing vacuously."
        )

    gaps: list[tuple[str, str, list[str], str]] = []
    for relative, member, style, why in SURFACES:
        text = (repo_root / relative).read_text(encoding="utf-8")
        body = member_body(text, member)
        if body is None:
            raise SystemExit(
                f"{relative.as_posix()} no longer declares '{member}'. This guard checks it for "
                "dimension coverage; point the guard at its replacement rather than dropping the "
                "surface, so coverage is never silently unchecked."
            )
        missing = [
            dimension
            for dimension in dimensions
            if not re.search(
                r"\b" + re.escape(camel_case(dimension) if style == "json-keys" else dimension) + r"\b",
                body,
            )
        ]
        if missing:
            gaps.append((relative.as_posix(), member, missing, why))
    return gaps


def main() -> int:
    parser = argparse.ArgumentParser(description="Enforce ledger dimension coverage across surfaces.")
    parser.add_argument("--repo-root", default=str(REPO_ROOT))
    args = parser.parse_args()

    gaps = find_gaps(Path(args.repo_root))
    if gaps:
        print("Ledger dimension coverage guard FAILED.", file=sys.stderr)
        print(
            "These surfaces enumerate ledger dimensions by hand and have fallen behind "
            "LedgerLineDimensionSet (ACCT-CHECKLIST-03):",
            file=sys.stderr,
        )
        for path, member, missing, why in gaps:
            print(f"\n  {path} -> {member}", file=sys.stderr)
            print(f"    missing: {', '.join(missing)}", file=sys.stderr)
            print(f"    {why}", file=sys.stderr)
        print(
            "\nAdd the dimension to each surface above. Meridian.Ledger's own consumers need no "
            "edit -- they enumerate through LedgerLineDimensionSetFields.",
            file=sys.stderr,
        )
        return 1

    print("Ledger dimension coverage guard: every declared dimension reaches every enumerating surface.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
