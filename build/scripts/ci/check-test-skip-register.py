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
REQUIRED_FIELDS = ("path", "reason", "owner", "category", "tracking", "review_by")

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
    def __init__(self, path: str, line: int, reason: str) -> None:
        self.path = path
        self.line = line
        self.reason = reason

    @property
    def key(self) -> tuple[str, str]:
        return (self.path, self.reason)

    def as_dict(self) -> dict[str, object]:
        return {"path": self.path, "line": self.line, "reason": self.reason}


def join_literals(literal_text: str) -> str:
    """Concatenate a C# string-literal expression into the single string it produces."""
    parts = STRING_LITERAL.findall(literal_text)
    joined = "".join(parts)
    # Unescape the sequences that actually appear in skip reasons; leaving them encoded would
    # make the register text differ from what the test runner reports.
    return joined.replace('\\"', '"').replace("\\\\", "\\").replace("\\n", "\n").replace("\\t", "\t")


def find_skip_positions(text: str) -> list[int]:
    """Return offsets just past each `Skip =` that appears in real code.

    Broadening discovery to every textual occurrence would inventory documentation such as
    `// Example: Skip = "reason";` and fixture strings used by parser tests as though they were
    real skipped tests, failing the gate until somebody added a bogus register entry. This walks
    the source instead, stepping over comments and string literals so only assignments in code
    are reported.
    """
    positions: list[int] = []
    i = 0
    length = len(text)
    while i < length:
        ch = text[i]
        if ch == '"':
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
                positions.append(match.end())
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
            for position in find_skip_positions(text):
                expression = read_expression(text, position)
                if expression is None or not expression.strip():
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
                sites.append(SkipSite(relative, line, reason))
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

        key = (entry["path"], entry["reason"])
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
            path, reason = key
            excerpt = reason if len(reason) <= 60 else reason[:57] + "..."
            problems.append(
                f"{path}: register entry matches no skip in the source (reason: \"{excerpt}\"). "
                "Remove the stale entry, or update it if the skip reason changed."
            )

    return problems


def build_evidence(sites: list[SkipSite], entries: list[dict]) -> dict:
    by_key = {(entry.get("path"), entry.get("reason")): entry for entry in entries if isinstance(entry, dict)}
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
