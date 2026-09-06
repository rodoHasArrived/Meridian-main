#!/usr/bin/env python3
"""Fail when an endpoint handler can swallow a client disconnect (#2618).

`EndpointHelpers` re-throws `OperationCanceledException` before its generic handler so an
aborted request stays an abort. Hand-rolled handlers have to remember that themselves, and
forgetting it converts a client hang-up into a normal-looking error response: a logged 500,
a 400 blaming the caller, or — worse — a counter increment that puts a failure that never
happened into operator-visible state.

Only one shape is a defect:

  * a `catch (Exception ...)` whose `when` filter does not *provably* exclude
    cancellation. `when (ex is ArgumentException or FormatException)` and
    `when (ex is not OperationCanceledException)` do; an opaque predicate such as
    `when (ShouldHandle(ex))` does not, because it may well accept an
    OperationCanceledException, so it is reported rather than trusted;
  * with no earlier `catch (OperationCanceledException)` in the same catch-chain
    (whether that clause re-throws or answers 499, cancellation never reaches the
    generic handler). An earlier `catch (TaskCanceledException)` does *not* count:
    it derives from OperationCanceledException, so it cannot catch the base type
    that `ct.ThrowIfCancellationRequested()` throws; and
  * whose `try` body passes a cancellation token to an *awaited* call, so a client
    disconnect can reach the catch at all. The token has to be an argument of the
    awaited invocation itself — `await Run(); Register(ct);` passes no token to
    anything awaited and is not a defect.

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

# Opening delimiter of a C# raw string literal: three or more quotes, optionally interpolated.
RAW_STRING_OPEN = re.compile(r'\$*(?P<quotes>"{3,})')

# A catch clause that receives every exception type. Three spellings do that:
#   catch (Exception ex)          the common form
#   catch (System.Exception ex)   qualified — identical behaviour
#   catch { }                     a bare catch-all, which also catches cancellation
# The optional type group only matches `Exception` itself, so `catch (ArgumentException ex)`
# and `catch (OperationCanceledException)` fall through without matching. Requiring the
# opening brace is what keeps the bare form from matching a typed clause.
# `SystemException` is included because OperationCanceledException derives from it, so
# `catch (SystemException ex)` swallows a disconnect exactly like `catch (Exception ex)`.
# The filter sub-pattern allows two levels of nesting, so `when (ShouldHandle(ex, Ctx()))`
# is recognised rather than silently failing to match the clause at all.
_FILTER = r"(?:[^()]|\((?:[^()]|\([^()]*\))*\))*"
CATCH_ALL = re.compile(
    r"\bcatch"
    r"(?:\s*\(\s*(?:global::)?(?:System\.)?(?:System)?Exception\b[^()]*\))?"
    rf"(?:\s*when\s*\((?P<filter>{_FILTER})\))?"
    r"\s*\{"
)

# The clause that opens the block immediately preceding a catch, used to walk a
# try/catch/finally chain back to its `try`.
CHAIN_CLAUSE = re.compile(
    r"(try"
    r"|catch\s*\([^()]*(?:\([^()]*\)[^()]*)*\)(?:\s*when\s*\([^()]*(?:\([^()]*\)[^()]*)*\))?"
    r"|finally)\s*$"
)

# Token names are read out of the file rather than guessed from an allowlist: every
# `CancellationToken foo` declaration contributes `foo`, so a handler that spells its
# parameter `requestToken` or `stoppingToken` is recognised without the pattern having to
# anticipate it — and, just as importantly, without matching an `authToken` or `accessToken`
# that has nothing to do with cancellation.
TOKEN_DECLARATION = re.compile(r"\bCancellationToken\s+(?P<name>\w+)")
# A CancellationTokenSource, so that `thatSource.Token` is known to be a token while an
# unrelated `session.Token` — an auth or continuation token — is not.
TOKEN_SOURCE_DECLARATION = re.compile(
    r"\bCancellationTokenSource\s+(?P<name>\w+)"
    r"|\bvar\s+(?P<inferred>\w+)\s*=\s*(?:new\s+)?CancellationTokenSource\b"
)
# `var alias = context.RequestAborted;` — an inferred alias carries the request token onward.
TOKEN_ALIAS = re.compile(r"\bvar\s+(?P<name>\w+)\s*=\s*[\w.]*\b(?:RequestAborted|Token)\s*;")

# Token expressions that are not plain identifiers.
TOKEN_EXPRESSIONS = (r"[\w.]*\bRequestAborted\b",)


def token_argument_pattern(source: str) -> re.Pattern:
    """A matcher for "a cancellation token appears as an argument", specific to this file.

    Names are harvested rather than guessed, in both directions: an unusual parameter name is
    recognised, and a `.Token` property is only a cancellation token when it hangs off a
    declared CancellationTokenSource — otherwise `await auth.RefreshAsync(session.Token)`
    would be reported as a cancellation path and block valid code.
    """
    names = {m.group("name") for m in TOKEN_DECLARATION.finditer(source)}
    names |= {m.group("name") for m in TOKEN_ALIAS.finditer(source)}
    sources = {
        m.group("name") or m.group("inferred") for m in TOKEN_SOURCE_DECLARATION.finditer(source)
    }
    alternatives = [re.escape(n) for n in sorted(names)]
    alternatives += [rf"{re.escape(s)}\.Token" for s in sorted(sources)]
    alternatives += list(TOKEN_EXPRESSIONS)
    body = "|".join(alternatives)
    # Positional (`, ct)`) or named (`cancellationToken: ct`).
    return re.compile(rf"(?:^|[(,]\s*)(?:\w+\s*:\s*)?(?:{body})\s*[,)]")

# An earlier clause in the chain that catches cancellation. `catch (TaskCanceledException)` is
# deliberately not accepted: it derives from OperationCanceledException, so it cannot catch the
# base type that ct.ThrowIfCancellationRequested() throws, and treating it as a guard would
# wave through a handler that still swallows a disconnect.
CANCELLATION_GUARD = re.compile(
    r"\bcatch\s*\(\s*(?:global::)?(?:System\.)?OperationCanceledException\b[^()]*\)"
    rf"(?:\s*when\s*\((?P<filter>{_FILTER})\))?"
)
# A guard clause only guards if its own filter is true for an aborted request. An unfiltered
# clause always is; `when (ct.IsCancellationRequested)` is the repo's idiom and is; a negated
# or opaque filter is not, and then cancellation falls through to the generic handler.
GUARD_FILTER_HOLDS = re.compile(r"^\s*[\w.]*\bIsCancellationRequested\s*$")

# An explicit check is its own cancellation source: it throws straight into the generic catch
# even when no awaited call in the body takes a token. The hand-rolled equivalent
# (`if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);`) counts too.
EXPLICIT_CANCELLATION = re.compile(
    r"\bThrowIfCancellationRequested\s*\("
    r"|\bthrow\s+new\s+(?:global::)?(?:System\.)?(?:Operation|Task)CanceledException\b"
)


def throws_cancellation_inline(body: str) -> bool:
    """True when the try body itself throws cancellation, ignoring deferred callbacks.

    `service.Register(() => ct.ThrowIfCancellationRequested());` runs later, outside this
    catch, so crediting it would report a handler that cannot actually swallow a disconnect.
    A `=>` between the start of the statement and the throw marks it as deferred.
    """
    for match in EXPLICIT_CANCELLATION.finditer(body):
        statement_start = max(body.rfind(";", 0, match.start()), body.rfind("{", 0, match.start()))
        if "=>" not in body[statement_start + 1:match.start()]:
            return True
    return False


def chain_guards_cancellation(chain: str) -> bool:
    """True when some earlier clause in the chain actually handles an aborted request."""
    for match in CANCELLATION_GUARD.finditer(chain):
        filter_text = match.group("filter")
        if filter_text is None or GUARD_FILTER_HOLDS.match(filter_text):
            return True
    return False

# Three filter shapes provably keep a client disconnect out of the generic handler:
#   ex is not OperationCanceledException          — excludes the type outright
#   ex is ArgumentException or FormatException    — admits only unrelated types
#   !ct.IsCancellationRequested                   — declines to catch anything while cancelled
# The third is a distinct idiom from the re-throw and is equally safe: when the caller has
# hung up the filter is false, so the exception propagates untouched. Anything else — an
# opaque predicate, a call, a type list naming a cancellation or base type — is not trusted,
# because a predicate that happens to accept an OperationCanceledException still swallows it.
# Only excluding OperationCanceledException itself is sufficient. `ex is not
# TaskCanceledException` is not: TaskCanceledException is a *subclass*, so a base
# OperationCanceledException from ct.ThrowIfCancellationRequested() still satisfies the filter
# and reaches the generic handler.
FILTER_NEGATES_TYPE = re.compile(
    r"\bis\s+not\s+(?:global::)?(?:System\.)?OperationCanceledException\b")
FILTER_NEGATES_REQUEST = re.compile(r"^\s*!\s*[\w.]*\bIsCancellationRequested\s*$")
FILTER_TYPE_TEST = re.compile(r"^\s*\w+\s+is\s+(?P<types>[\w.]+(?:\s+or\s+[\w.]+)*)\s*$")
UNTRUSTED_FILTER_TYPES = {
    "Exception", "SystemException", "OperationCanceledException", "TaskCanceledException",
}


def _conjunct_excludes_cancellation(text: str) -> bool:
    if FILTER_NEGATES_TYPE.search(text) or FILTER_NEGATES_REQUEST.match(text):
        return True
    match = FILTER_TYPE_TEST.match(text)
    if match is None:
        return False
    return all(
        name.split(".")[-1] not in UNTRUSTED_FILTER_TYPES
        for name in re.split(r"\s+or\s+", match.group("types"))
    )


def filter_excludes_cancellation(filter_text: str) -> bool:
    """True only when the `when` clause provably cannot admit an OperationCanceledException.

    A disjunction is safe only if every branch is safe — one permissive branch is enough to
    let a cancellation through. A conjunction is safe as soon as one term excludes it.
    """
    return all(
        any(_conjunct_excludes_cancellation(conjunct) for conjunct in disjunct.split("&&"))
        for disjunct in filter_text.split("||")
    )


def awaited_calls(body: str) -> list[str]:
    """The text of each awaited statement, from `await` to the end of that statement.

    Bounding at the statement terminator is what ties a token to the awaited invocation:
    in `await Run(); Register(ct);` the awaited slice stops before `Register(ct)`, so the
    token is correctly not credited to the awaited call.
    """
    slices: list[str] = []
    for match in re.finditer(r"\bawait\b", body):
        depth, index = 0, match.end()
        while index < len(body):
            char = body[index]
            if char in "([{":
                depth += 1
            elif char in ")]}":
                if depth == 0:
                    break
                depth -= 1
            elif char == ";" and depth == 0:
                break
            index += 1
        slices.append(body[match.end():index])
    return slices


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
        elif (match := RAW_STRING_OPEN.match(text, i)) is not None:
            # A C# raw literal is delimited by a run of three or more quotes and closed by a
            # run of at least that many. Scanning it as ordinary strings desyncs on an
            # embedded quote, which can swallow the following source — including a real catch
            # chain — as string content, hiding a handler from the scan entirely.
            fence = match.group("quotes")
            end = text.find(fence, match.end())
            while end != -1 and text[end:end + len(fence) + 1] == fence + '"':
                end = text.find(fence, end + 1)  # a longer run is content, not the terminator
            end = len(text) if end == -1 else end + len(fence)
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
            # Skipping would scan zero files and report success, silently disabling the gate
            # exactly when its scope has drifted.
            raise FileNotFoundError(
                f"Scan directory '{scan_dir}' does not exist under {root}. "
                "Update --scan-dir (or DEFAULT_SCAN_DIRS) if the endpoint surface moved."
            )
        for path in sorted(base.rglob("*.cs")):
            if any(part in {"bin", "obj"} for part in path.parts):
                continue
            raw = path.read_text(encoding="utf-8")
            code = blank_literals(raw)
            token_argument = token_argument_pattern(code)
            for match in CATCH_ALL.finditer(code):
                filter_text = match.group("filter")
                if filter_text and filter_excludes_cancellation(filter_text):
                    continue  # the filter provably cannot admit a cancellation
                walked = walk_to_try(code, match.start())
                if walked is None:
                    continue
                try_index, chain = walked
                if chain_guards_cancellation(chain):
                    continue  # an earlier clause in the chain already handles it
                body_open = code.find("{", try_index)
                body = code[body_open:matching_close(code, body_open)]
                reachable = (
                    throws_cancellation_inline(body)
                    or any(token_argument.search(call) for call in awaited_calls(body))
                )
                if not reachable:
                    continue  # no cancellation can land here, so the catch cannot swallow one
                line = raw.count("\n", 0, match.start()) + 1
                violations.append((path.relative_to(root).as_posix(), line))
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
