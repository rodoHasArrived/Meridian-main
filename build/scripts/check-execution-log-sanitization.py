#!/usr/bin/env python3
"""Enforce the execution module's log-sanitization invariant.

Caller-supplied order text — the client order id, the symbol, the strategy id, and
identifiers echoed back on gateway reports — must pass through
``LogSanitizer.Sanitize`` before reaching a logger. ``OrderRequest.ClientOrderId``
and ``Symbol`` are submitted values that nothing upstream is required to constrain
(the Security Master gate is optional), so a line break in either would render as an
extra line in a text sink and let a submitter forge execution log entries.

``LogSanitizer.Sanitize`` neutralizes line endings through ``String.Replace``, which
CodeQL models as a barrier, so ``cs/log-forging`` recognizes a sanitized call site and no
query filter is needed. This check covers what the query does not: it fails on a *missing*
sanitizer call in this module rather than on a reachable one, so a new raw log site is
caught in review even where the query is not run. Twice during PR #2554 an ad-hoc grep was
declared clean and was not, both times because the pattern list below was too narrow —
keep it here, in review, rather than in anyone's head.

Usage:
    python3 build/scripts/check-execution-log-sanitization.py [--list]

Exits non-zero if any unsanitized call site is found.
"""

from __future__ import annotations

import argparse
import pathlib
import re
import sys

ROOTS = ("src/Meridian.Execution", "src/Meridian.Execution.Sdk", "src/Meridian.Risk")

# Argument expressions that carry caller-supplied text. Extend this list when a new
# caller-derived field starts being logged; a missing entry is how this check goes
# quietly blind.
CALLER_SUPPLIED = (
    r"\w*[Rr]equest\.ClientOrderId",
    r"\w*[Rr]equest\.Symbol",
    r"\w*[Rr]equest\.StrategyId",
    r"\w*[Rr]eport\.ClientOrderId",
    r"\w*[Rr]eport\.OrderId",
    r"\w*[Rr]eport\.Symbol",
    r"\w*[Ss]tate\.OrderId",
    r"\w*[Ss]tate\.Symbol",
    r"(?<![\w.])orderId\b",
    r"(?<![\w.])clientOrderId\b",
    r"(?<![\w.])symbol\b",
)

LOG_CALL = re.compile(r"_logger\.Log\w+\((?:[^()]|\([^()]*\))*\)", re.S)


def findings(repo: pathlib.Path) -> list[tuple[str, int, str]]:
    found: list[tuple[str, int, str]] = []
    for root in ROOTS:
        for path in sorted((repo / root).rglob("*.cs")):
            text = path.read_text(encoding="utf-8")
            for call in LOG_CALL.finditer(text):
                block = call.group(0)
                line = text[: call.start()].count("\n") + 1
                for pattern in CALLER_SUPPLIED:
                    # Bare argument positions only: preceded by ( or , and followed by , or ).
                    for arg in re.finditer(r"(?<=[,(])\s*(" + pattern + r")\s*[,)]", block):
                        expression = arg.group(1)
                        if f"Sanitize({expression})" not in block:
                            rel = path.relative_to(repo).as_posix()
                            found.append((rel, line, expression))
    return found


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--list", action="store_true", help="list every checked pattern and exit")
    args = parser.parse_args()

    repo = pathlib.Path(__file__).resolve().parents[2]

    if args.list:
        print("Roots:")
        for root in ROOTS:
            print(f"  {root}")
        print("Caller-supplied argument patterns:")
        for pattern in CALLER_SUPPLIED:
            print(f"  {pattern}")
        return 0

    found = findings(repo)
    if not found:
        print("Execution log sanitization: OK — no caller-supplied value reaches a logger unsanitized.")
        return 0

    print("Execution log sanitization FAILED. Wrap each value in LogSanitizer.Sanitize(...):")
    for rel, line, expression in found:
        print(f"  {rel}:{line}  {expression}")
    print(
        "\nSee src/Meridian.Execution/README.md for the invariant this enforces."
    )
    return 1


if __name__ == "__main__":
    sys.exit(main())
