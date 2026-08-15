#!/usr/bin/env python3
"""Fail when an endpoint handler can swallow a client disconnect (#2618).

`EndpointHelpers` re-throws `OperationCanceledException` before its generic handler so an
aborted request stays an abort. Hand-rolled handlers have to remember that themselves, and
forgetting it converts a client hang-up into a normal-looking error response: a logged 500,
a 400 blaming the caller, or — worse — a counter increment that puts a failure that never
happened into operator-visible state.

Only one shape is a defect:

  * a *bare* `catch (Exception ...)` — a `when` filter such as
    `when (ex is ArgumentException or FormatException)` or
    `when (ex is not OperationCanceledException)` already lets cancellation past;
  * with no earlier `catch (OperationCanceledException)` in the same catch-chain
    (whether that clause re-throws or answers 499, cancellation never reaches the
    generic handler); and
  * whose `try` body actually passes a cancellation token to an awaited call, so a
    client disconnect can reach it at all.

A synchronous handler with a bare catch is untidy but cannot swallow a disconnect, so it is
not reported here — see the issue for the separate consolidation lane.

Enforcement runs through tests/scripts/test_check_endpoint_cancellation.py rather than a
scripts/ci.sh step: run-script-tests.py already gates every tests/scripts suite inside
verify_workflows(), which quality-gate aggregates, so the guard is live without editing a
governance file. Run it directly for a quick local check:

    python3 build/scripts/ci/check-endpoint-cancellation.py
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
DEFAULT_SCAN_DIRS = ("src/Meridian.Ui.Shared/Endpoints",)

# A `catch (Exception ...)` clause plus its optional `when (...)` filter. The inner
# alternation lets a filter contain one level of nested parentheses, which covers the
# `when (ex is A or B)` and `when (Foo(ex))` shapes used in this repo.
CATCH_EXCEPTION = re.compile(
    r"catch\s*\(\s*Exception\b[^()]*\)"
    r"(?:\s*when\s*\((?P<filter>[^()]*(?:\([^()]*\)[^()]*)*)\))?"
)

# The clause that opens the block immediately preceding a catch, used to walk a
# try/catch/finally chain back to its `try`.
CHAIN_CLAUSE = re.compile(
    r"(try"
    r"|catch\s*\([^()]*(?:\([^()]*\)[^()]*)*\)(?:\s*when\s*\([^()]*(?:\([^()]*\)[^()]*)*\))?"
    r"|finally)\s*$"
)

# A cancellation token passed as an argument, rather than merely named somewhere.
TOKEN_ARGUMENT = re.compile(
    r"(?:^|[(,]\s*)"
    r"(?:ct|cancellationToken|token|linkedCts\.Token|cts\.Token"
    r"|(?:\w+\.)?RequestAborted)"
    r"\s*[,)]"
)

CANCELLATION_TYPES = ("OperationCanceledException", "TaskCanceledException")


def blank_literals(text: str) -> str:
    """Blank out comments and string literals, preserving length and line breaks.

    Brace matching and keyword searches both run over the result, so a brace inside a
    string or a `catch` written in a comment cannot skew the parse.
    """
    out: list[str] = []
    i, n = 0, len(text)
    while i < n:
        if text.startswith("//", i):
            end = text.find("\n", i)
            end = n if end < 0 else end
            out.append(" " * (end - i))
            i = end
        elif text.startswith("/*", i):
            end = text.find("*/", i)
            end = n if end < 0 else end + 2
            out.append(re.sub(r"[^\n]", " ", text[i:end]))
            i = end
        elif text.startswith('@"', i) or text.startswith('$@"', i) or text.startswith('@$"', i):
            j = text.index('"', i) + 1
            while j < n:
                if text[j] == '"':
                    if j + 1 < n and text[j + 1] == '"':
                        j += 2
                        continue
                    j += 1
                    break
                j += 1
            out.append(re.sub(r"[^\n]", " ", text[i:j]))
            i = j
        elif text[i] == '"':
            j = i + 1
            while j < n and text[j] != '"':
                j += 2 if text[j] == "\\" else 1
            j = min(j + 1, n)
            out.append(" " * (j - i))
            i = j
        elif text[i] == "'":
            j = i + 1
            while j < n and text[j] != "'":
                j += 2 if text[j] == "\\" else 1
            j = min(j + 1, n)
            out.append(" " * (j - i))
            i = j
        else:
            out.append(text[i])
            i += 1
    return "".join(out)


def matching_open(text: str, close_index: int) -> int | None:
    """Index of the `{` matching the `}` at close_index."""
    depth = 0
    for i in range(close_index, -1, -1):
        if text[i] == "}":
            depth += 1
        elif text[i] == "{":
            depth -= 1
            if depth == 0:
                return i
    return None


def matching_close(text: str, open_index: int) -> int:
    depth = 0
    for i in range(open_index, len(text)):
        if text[i] == "{":
            depth += 1
        elif text[i] == "}":
            depth -= 1
            if depth == 0:
                return i
    return len(text)


def walk_to_try(text: str, catch_index: int) -> tuple[int, str] | None:
    """Walk a catch back through its chain to the opening `try`.

    Returns (index of `try`, the chain text) or None when the shape is unrecognised.
    """
    position = catch_index
    chain: list[str] = []
    for _ in range(64):
        k = position - 1
        while k >= 0 and text[k].isspace():
            k -= 1
        if k < 0 or text[k] != "}":
            return None
        opening = matching_open(text, k)
        if opening is None:
            return None
        head = text[:opening].rstrip()
        match = CHAIN_CLAUSE.search(head)
        if match is None:
            return None
        clause = match.group(0)
        chain.append(clause)
        start = head.rfind(clause)
        if clause.lstrip().startswith("try"):
            return start, " | ".join(chain)
        position = start
    return None


def find_violations(root: Path, scan_dirs: tuple[str, ...] = DEFAULT_SCAN_DIRS) -> list[tuple[str, int]]:
    """Bare catches that a cancelled request can reach, as (repo-relative path, line)."""
    violations: list[tuple[str, int]] = []
    for scan_dir in scan_dirs:
        base = root / scan_dir
        if not base.is_dir():
            continue
        for path in sorted(base.rglob("*.cs")):
            if any(part in {"bin", "obj"} for part in path.parts):
                continue
            raw = path.read_text(encoding="utf-8")
            code = blank_literals(raw)
            for match in CATCH_EXCEPTION.finditer(code):
                if match.group("filter"):
                    continue  # a filter already lets cancellation past
                walked = walk_to_try(code, match.start())
                if walked is None:
                    continue
                try_index, chain = walked
                if any(name in chain for name in CANCELLATION_TYPES):
                    continue  # an earlier clause in the chain already handles it
                body_open = code.find("{", try_index)
                body = code[body_open:matching_close(code, body_open)]
                if "await" not in body or not TOKEN_ARGUMENT.search(body):
                    continue  # no token reaches an awaited call, so no disconnect can land here
                line = raw.count("\n", 0, match.start()) + 1
                violations.append((str(path.relative_to(root)), line))
    return violations


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--root", default=str(REPO_ROOT))
    parser.add_argument(
        "--scan-dir",
        action="append",
        dest="scan_dirs",
        help="Repo-relative directory to scan; repeatable. Defaults to the endpoint surface.",
    )
    args = parser.parse_args()

    scan_dirs = tuple(args.scan_dirs) if args.scan_dirs else DEFAULT_SCAN_DIRS
    violations = find_violations(Path(args.root), scan_dirs)

    if not violations:
        print(f"Endpoint cancellation guard: no unguarded catches in {', '.join(scan_dirs)}.")
        return 0

    print("Endpoint handlers that can swallow a client disconnect:", file=sys.stderr)
    for rel, line in violations:
        print(f"  {rel}:{line}", file=sys.stderr)
    print(
        "\nAdd `catch (OperationCanceledException) when (<token>.IsCancellationRequested) { throw; }`\n"
        "before the generic handler, or route the endpoint through EndpointHelpers.",
        file=sys.stderr,
    )
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
