#!/usr/bin/env python3
"""Fail-closed inventory of every skipped test under an owned lane.

A skipped test reports the same green as a passing one. Left unregistered, skips accumulate
silently: a test quarantined "temporarily" during a refactor keeps the suite green for years
while the behaviour it covered goes unverified, and an environment-gated test that should run
in CI looks identical to one that legitimately cannot.

This gate makes every skip an explicit, owned, expiring decision:

- every ``Skip = "..."`` in a test project must have a matching register entry naming an
  owner, a category, tracking, and a review-by date;
- register entries whose ``review_by`` has passed fail the gate, so a quarantine cannot
  outlive its review without somebody re-deciding;
- register entries that match no skip in the source fail, so the register cannot rot after
  a test is re-enabled or deleted;
- the register stores the exact skip reason, so editing the reason forces re-review.

Categories:

- ``environment-gated`` — the test cannot run in this environment (no Docker, no Windows
  DPAPI). It is expected to run in the lane that does provide the dependency.
- ``quarantined`` — the test is disabled pending a product or design decision. This is a
  coverage gap and must carry a review date.

Run with ``--summary`` for a compact report, ``--json-output`` to attach machine-readable
skip evidence to a CI run.
"""

from __future__ import annotations

import argparse
import datetime as _dt
import json
import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
DEFAULT_REGISTER = REPO_ROOT / "build" / "config" / "testing" / "test-skip-register.json"
DEFAULT_TESTS_DIR = REPO_ROOT / "tests"

VALID_CATEGORIES = {"environment-gated", "quarantined"}
REQUIRED_FIELDS = ("path", "test", "reason", "owner", "category", "tracking", "review_by")

# Every `Skip = <expression>` in a test project, in any of the forms xUnit accepts:
#     [Fact(Skip = "plain literal")]
#     Skip = "first part " + "second part";        (compile-time concatenation)
#     Skip = $"...{DisableDockerVariable}=true.";  (interpolated)
#     Skip = reason;                               (variable or method result)
# Matching only plain literals silently ignored the interpolated and variable forms, so
# environment-gated skips built from a variable were never inventoried and the gate reported
# every skip owned while unowned conditional skips ran in CI.
SKIP_KEYWORD = re.compile(r"\bSkip\s*=\s*")
IDENTIFIER_CHAR = re.compile(r"[A-Za-z0-9_]")

# `Skip` is not a reserved word, so an ordinary DTO assignment such as
# `new SearchOptions { Skip = 10 }` reads identically to an xUnit skip. Treating those as skips
# made the required workflow lane demand a register entry for a pagination offset, so adding a
# test for any type with a `Skip` property could block CI with no test disabled anywhere.
# xUnit's Skip is a string, which separates the two: an expression that yields a string is a
# candidate, a numeric/boolean/null literal never is.
NON_STRING_LITERAL = re.compile(r"^\s*(?:[-+]?[0-9][\w.]*|true|false|null|None)\s*$")
BARE_IDENTIFIER = re.compile(r"^\s*[A-Za-z_][\w.]*\s*$")
# `Skip = reason;` inside a class deriving from FactAttribute/TheoryAttribute is a real skip with
# no statically-known string. Accepting the bare-identifier form only in files that declare such a
# type keeps DirectLendingDatabaseFactAttribute inventoried without re-admitting DTO assignments.
XUNIT_ATTRIBUTE_BASE = re.compile(
    r"(?::\s*|inherit\s+)(?:Xunit\.)?(?:Fact|Theory)(?:Attribute)?\b"
)
STRING_LITERAL = re.compile(r'"((?:[^"\\]|\\.)*)"')
PURE_LITERAL_EXPRESSION = re.compile(
    r"""^ \s* " (?: [^"\\] | \\. )* "
        (?: \s* \+ \s* " (?: [^"\\] | \\. )* " )* \s* $
    """,
    re.VERBOSE,
)
WHITESPACE_RUN = re.compile(r"\s+")


class GateFailure(Exception):
    """Raised for gate-level failures that should exit 1 with a clear message."""


class SkipSite:
    def __init__(self, path: str, line: int, reason: str, test: str) -> None:
        self.path = path
        self.line = line
        self.reason = reason
        self.test = test

    @property
    def key(self) -> tuple[str, str, str]:
        # Keyed by the test as well as the file and reason. On (path, reason) alone, deleting one
        # skipped test and giving a different test in the same file the same reason left the key
        # unchanged: no stale entry, no unregistered skip, and the owner and review date silently
        # transferred to coverage nobody had reviewed.
        return (self.path, self.test, self.reason)

    def as_dict(self) -> dict[str, object]:
        return {"path": self.path, "test": self.test, "line": self.line, "reason": self.reason}


def join_literals(literal_text: str) -> str:
    """Concatenate a C# string-literal expression into the single string it produces."""
    parts = STRING_LITERAL.findall(literal_text)
    joined = "".join(parts)
    # Unescape the sequences that actually appear in skip reasons; leaving them encoded would
    # make the register text differ from what the test runner reports.
    return joined.replace('\\"', '"').replace("\\\\", "\\").replace("\\n", "\n").replace("\\t", "\t")


def _is_char_literal(text: str, index: int) -> bool:
    """Return whether the apostrophe at *index* opens an F# character literal.

    A char literal is 'c', '\\n', or '\\u0041' — always a closing apostrophe within a few
    characters. A type variable ('T, 'TResult) or a primed identifier (value') has none.
    """
    rest = text[index + 1 : index + 12]
    if rest.startswith("\\"):
        return "'" in rest[1:]
    return len(rest) >= 2 and rest[1] == "'"


def find_skip_positions(text: str, fsharp: bool = False) -> list[tuple[int, bool]]:
    """Return offsets just past each `Skip =` that appears in real code.

    Broadening discovery to every textual occurrence would inventory documentation such as
    `// Example: Skip = "reason";` and fixture strings used by parser tests as though they were
    real skipped tests, failing the gate until somebody added a bogus register entry. This walks
    the source instead, stepping over comments and string literals so only assignments in code
    are reported.

    *fsharp* enables F#'s nesting `(* ... *)` block comments. The scan covers `**/*.fs`, so
    without this a documentation block containing an example `Skip = "reason"` was inventoried
    as a real skipped test and failed the required workflow lane.
    """
    positions: list[tuple[int, bool]] = []
    bracket_depth = 0
    i = 0
    length = len(text)
    while i < length:
        ch = text[i]
        if ch == "[":
            bracket_depth += 1
            i += 1
            continue
        if ch == "]":
            bracket_depth = max(0, bracket_depth - 1)
            i += 1
            continue
        if fsharp and ch == "(" and i + 1 < length and text[i + 1] == "*":
            # `(*)` is F#'s multiplication operator used as a function, not a comment opener.
            if i + 2 < length and text[i + 2] == ")":
                i += 3
                continue
            nesting = 1
            i += 2
            while i < length and nesting > 0:
                if text.startswith("(*", i):
                    nesting += 1
                    i += 2
                    continue
                if text.startswith("*)", i):
                    nesting -= 1
                    i += 2
                    continue
                i += 1
            continue
        if ch == '"':
            # A C# raw string literal opens with three or more quotes and closes with the same
            # count. Treating its quotes as ordinary boundaries let the scan resume inside the
            # literal, so a fixture holding quoted JSON such as {"source":"Skip = reason"} was
            # inventoried as a real skipped test and failed the gate.
            fence = 0
            while i + fence < length and text[i + fence] == '"':
                fence += 1
            if fence >= 3:
                closing = '"' * fence
                end = text.find(closing, i + fence)
                i = length if end == -1 else end + fence
                continue

            verbatim = i > 0 and text[i - 1] == "@"
            i += 1
            while i < length:
                if verbatim:
                    if text[i] == '"':
                        if i + 1 < length and text[i + 1] == '"':
                            i += 2
                            continue
                        break
                else:
                    if text[i] == "\\":
                        i += 2
                        continue
                    if text[i] == '"':
                        break
                i += 1
            i += 1
            continue
        if ch == "'":
            # In F# an apostrophe also introduces a type variable ('T) and may end an
            # identifier (value'), neither of which closes. Consuming to the next apostrophe
            # there swallowed everything up to the following one — including a real
            # `Skip = ...` — and the gate then reported complete coverage over an
            # unregistered skipped test. Only a genuine short char literal is consumed.
            if fsharp and not _is_char_literal(text, i):
                i += 1
                continue
            # Character literal; a lone apostrophe inside one must not open a string.
            i += 1
            while i < length and text[i] != "'":
                i += 2 if text[i] == "\\" else 1
            i += 1
            continue
        if ch == "/" and i + 1 < length and text[i + 1] == "/":
            while i < length and text[i] != "\n":
                i += 1
            continue
        if ch == "/" and i + 1 < length and text[i + 1] == "*":
            i += 2
            while i + 1 < length and not (text[i] == "*" and text[i + 1] == "/"):
                i += 1
            i += 2
            continue
        if ch == "S" and text.startswith("Skip", i):
            before_is_identifier = i > 0 and IDENTIFIER_CHAR.match(text[i - 1]) is not None
            match = SKIP_KEYWORD.match(text, i)
            if match and not before_is_identifier:
                positions.append((match.end(), bracket_depth > 0))
                i = match.end()
                continue
        i += 1
    return positions


def read_expression(text: str, start: int) -> str | None:
    """Return the source of the assignment expression beginning at *start*.

    Consumes to the terminating `;` for a statement assignment, or to the closing `)` of the
    enclosing attribute for `[Fact(Skip = "...")]`, tracking string literals so a `;` or `)`
    inside a reason does not end the scan early.
    """
    depth = 0
    i = start
    length = len(text)
    while i < length:
        ch = text[i]
        if ch == '"':
            # A C# raw string literal opens with three or more quotes and closes with the same
            # count. Treating its quotes as ordinary boundaries let the scan resume inside the
            # literal, so a fixture holding quoted JSON such as {"source":"Skip = reason"} was
            # inventoried as a real skipped test and failed the gate.
            fence = 0
            while i + fence < length and text[i + fence] == '"':
                fence += 1
            if fence >= 3:
                closing = '"' * fence
                end = text.find(closing, i + fence)
                i = length if end == -1 else end + fence
                continue

            verbatim = i > 0 and text[i - 1] == "@"
            i += 1
            while i < length:
                if verbatim:
                    if text[i] == '"':
                        if i + 1 < length and text[i + 1] == '"':
                            i += 2
                            continue
                        break
                else:
                    if text[i] == "\\":
                        i += 2
                        continue
                    if text[i] == '"':
                        break
                i += 1
            i += 1
            continue
        if ch in "([{":
            depth += 1
        elif ch in ")]}":
            if depth == 0:
                return text[start:i]
            depth -= 1
        elif ch == ";" and depth == 0:
            return text[start:i]
        elif ch == "," and depth == 0:
            # Another named argument in the same attribute ends this expression.
            return text[start:i]
        i += 1
    return None


def normalise_expression(expression: str) -> str:
    """Collapse an expression's source to a stable single-line form."""
    return WHITESPACE_RUN.sub(" ", expression).strip()


def is_skip_expression(expression: str, declares_test_attribute: bool, in_attribute: bool) -> bool:
    """Return whether *expression* is a value xUnit would accept as a skip reason.

    `Skip` is an ordinary property name, so a DTO initializer such as
    `new SearchOptions { Skip = 10 }` is textually indistinguishable from a disabled test.
    Three signals separate them:

    - xUnit's Skip is a string, so a numeric, boolean, or null value never qualifies;
    - a `Skip =` inside an attribute application is a named argument to a test attribute.
      `[Fact(Skip = SkipReasons.Quarantine)]` and `[Fact(Skip = nameof(Method))]` are real skips
      even though neither is a literal, and neither appears in a file that *declares* a custom
      attribute — restricting computed expressions to such files missed both entirely;
    - outside an attribute, a computed expression counts only where a FactAttribute or
      TheoryAttribute subclass is declared, which is the shape DirectLendingDatabaseFactAttribute
      uses when it assigns `Skip = reason` to itself.
    """
    candidate = expression.strip()
    if not candidate or NON_STRING_LITERAL.match(candidate):
        return False
    if '"' in candidate:
        return True
    if in_attribute:
        return True
    # Outside an attribute this is ordinary code unless the file declares a test attribute.
    return declares_test_attribute


# The decorated member follows an attribute; the enclosing type is what a statement assignment
# belongs to. Both are matched loosely — this is an identity for keying, not a parse.
MEMBER_AFTER = re.compile(
    r"\b(?:public|private|protected|internal|static|async|sealed|override|virtual|partial|extern|unsafe|new)\s+"
    r"[^\s(){};]+\s+(?P<name>[A-Za-z_]\w*)\s*(?:<[^>()]*>)?\s*\(",
)
FSHARP_MEMBER_AFTER = re.compile(r"\blet\s+``?(?P<name>[^`\n=]+?)``?\s*(?:\(|:|=)")
TYPE_BEFORE = re.compile(r"\b(?:class|record|struct|type)\s+(?P<name>[A-Za-z_]\w*)")


def resolve_test_identity(text: str, position: int, fsharp: bool) -> str:
    """Return the test or type the skip at *position* belongs to.

    An attribute decorates the member that follows it; a `Skip = reason` statement belongs to the
    type that encloses it. Looking forward first and falling back to the enclosing type covers
    both without needing a real parser.
    """
    window = text[position : position + 600]
    pattern = FSHARP_MEMBER_AFTER if fsharp else MEMBER_AFTER
    match = pattern.search(window)
    if match:
        return match.group("name").strip()

    preceding = [m.group("name") for m in TYPE_BEFORE.finditer(text, 0, position)]
    return preceding[-1] if preceding else "<unknown>"


def discover_skips(tests_dir: Path, repo_root: Path) -> list[SkipSite]:
    """Return every skip declaration in the .NET/F# test projects, in stable order."""
    sites: list[SkipSite] = []
    for pattern in ("**/*.cs", "**/*.fs"):
        for path in sorted(tests_dir.glob(pattern)):
            posix = path.as_posix()
            if "/obj/" in posix or "/bin/" in posix:
                continue
            try:
                text = path.read_text(encoding="utf-8")
            except (OSError, UnicodeDecodeError):
                continue
            if "Skip" not in text:
                continue
            relative = path.relative_to(repo_root).as_posix()
            declares_test_attribute = XUNIT_ATTRIBUTE_BASE.search(text) is not None
            for position, in_attribute in find_skip_positions(text, fsharp=path.suffix == ".fs"):
                expression = read_expression(text, position)
                if expression is None or not expression.strip():
                    continue
                if not is_skip_expression(expression, declares_test_attribute, in_attribute):
                    continue
                line = text.count("\n", 0, position) + 1
                # A pure literal registers under the exact text the runner reports. Anything
                # interpolated or computed has no statically-known reason, so it registers
                # under its normalised source instead — still exact, still forcing re-review
                # when it changes, and no longer invisible.
                reason = (
                    join_literals(expression)
                    if PURE_LITERAL_EXPRESSION.match(expression)
                    else normalise_expression(expression)
                )
                sites.append(
                    SkipSite(
                        relative,
                        line,
                        reason,
                        resolve_test_identity(text, position, path.suffix == ".fs"),
                    )
                )
    return sites


def load_register(register_path: Path) -> list[dict]:
    if not register_path.is_file():
        raise GateFailure(f"skip register not found: {register_path}")
    try:
        payload = json.loads(register_path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        raise GateFailure(f"skip register is not valid JSON: {exc}") from exc

    entries = payload.get("skips") if isinstance(payload, dict) else payload
    if not isinstance(entries, list):
        raise GateFailure("skip register must contain a 'skips' array")
    return entries


def parse_review_date(value: str, entry_id: str) -> _dt.date:
    try:
        return _dt.date.fromisoformat(value)
    except (TypeError, ValueError) as exc:
        raise GateFailure(f"{entry_id}: review_by '{value}' is not an ISO date (YYYY-MM-DD)") from exc


def evaluate(
    sites: list[SkipSite],
    entries: list[dict],
    today: _dt.date,
) -> list[str]:
    """Return one problem line per failure; an empty list means the inventory is sound."""
    problems: list[str] = []

    seen_keys: dict[tuple[str, str], SkipSite] = {}
    for site in sites:
        if site.key in seen_keys:
            first = seen_keys[site.key]
            problems.append(
                f"{site.path}:{site.line}: duplicate skip reason also at line {first.line}; "
                "identical reasons in one file cannot be registered independently — make each reason specific"
            )
            continue
        seen_keys[site.key] = site

    registered: dict[tuple[str, str], dict] = {}
    for index, entry in enumerate(entries):
        entry_id = f"skip register entry #{index + 1}"
        if not isinstance(entry, dict):
            problems.append(f"{entry_id}: entry must be an object")
            continue

        missing = [field for field in REQUIRED_FIELDS if not str(entry.get(field, "")).strip()]
        if missing:
            problems.append(f"{entry_id}: missing required field(s): {', '.join(missing)}")
            continue

        category = entry["category"]
        if category not in VALID_CATEGORIES:
            problems.append(
                f"{entry_id} ({entry['path']}): category '{category}' is not one of "
                f"{', '.join(sorted(VALID_CATEGORIES))}"
            )
            continue

        key = (entry["path"], entry["test"], entry["reason"])
        if key in registered:
            problems.append(f"{entry_id} ({entry['path']}): duplicate register entry for the same skip")
            continue
        registered[key] = entry

        try:
            review_by = parse_review_date(entry["review_by"], f"{entry_id} ({entry['path']})")
        except GateFailure as exc:
            problems.append(str(exc))
            continue

        if review_by < today:
            problems.append(
                f"{entry['path']}: skip owned by {entry['owner']} was due for review on {review_by.isoformat()}. "
                "Re-enable the test, or re-review and extend review_by."
            )

    for key, site in seen_keys.items():
        if key not in registered:
            problems.append(
                f"{site.path}:{site.line}: skip is not in the register. "
                "Add an entry naming owner, category, tracking, and review_by."
            )

    source_keys = set(seen_keys)
    for key, entry in registered.items():
        if key not in source_keys:
            path, test, reason = key
            excerpt = reason if len(reason) <= 60 else reason[:57] + "..."
            problems.append(
                f"{path}: register entry for '{test}' matches no skip in the source "
                f"(reason: \"{excerpt}\"). Remove the stale entry, or update it if the skip "
                "reason or the test it covers changed."
            )

    return problems


def build_evidence(sites: list[SkipSite], entries: list[dict]) -> dict:
    by_key = {(entry.get("path"), entry.get("test"), entry.get("reason")): entry for entry in entries if isinstance(entry, dict)}
    categorised: dict[str, int] = {category: 0 for category in sorted(VALID_CATEGORIES)}
    records = []
    for site in sites:
        entry = by_key.get(site.key, {})
        category = entry.get("category", "unregistered")
        categorised[category] = categorised.get(category, 0) + 1
        record = site.as_dict()
        record.update(
            {
                "owner": entry.get("owner"),
                "category": category,
                "tracking": entry.get("tracking"),
                "review_by": entry.get("review_by"),
            }
        )
        records.append(record)

    return {"skip_count": len(sites), "by_category": categorised, "skips": records}


def parse_arguments(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--tests-dir", type=Path, default=DEFAULT_TESTS_DIR, help="Root of the test projects.")
    parser.add_argument("--register", type=Path, default=DEFAULT_REGISTER, help="Path to the skip register JSON.")
    parser.add_argument("--repo-root", type=Path, default=REPO_ROOT, help="Repository root for relative paths.")
    parser.add_argument("--json-output", type=Path, help="Write machine-readable skip evidence to this path.")
    parser.add_argument("--summary", action="store_true", help="Print a one-line summary.")
    parser.add_argument("--today", help="Override today's date (YYYY-MM-DD) for review-date checks.")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_arguments(argv)
    today = _dt.date.fromisoformat(args.today) if args.today else _dt.date.today()

    try:
        entries = load_register(args.register.resolve())
    except GateFailure as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1

    sites = discover_skips(args.tests_dir.resolve(), args.repo_root.resolve())
    problems = evaluate(sites, entries, today)

    if args.json_output:
        evidence = build_evidence(sites, entries)
        args.json_output.parent.mkdir(parents=True, exist_ok=True)
        args.json_output.write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")

    if args.summary:
        print(f"test-skip-register: {len(sites)} skip(s), {len(entries)} register entry(ies), {len(problems)} problem(s)")
    else:
        print(f"Found {len(sites)} skipped test declaration(s) across the test projects.")
        print(f"Register holds {len(entries)} entry(ies).")

    if problems:
        print("", file=sys.stderr)
        print(f"Test skip register validation failed with {len(problems)} problem(s):", file=sys.stderr)
        for problem in problems:
            print(f"  {problem}", file=sys.stderr)
        return 1

    print("Every skipped test is registered, owned, and within its review window.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
