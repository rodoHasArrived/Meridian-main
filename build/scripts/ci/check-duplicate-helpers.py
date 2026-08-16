#!/usr/bin/env python3
"""Ratchet: no new private copies of helpers that now have a shared owner.

Meridian.Contracts.Text.TextPrimitives owns NormalizeOptional, RequireText, and
FirstNonBlank. Before it existed the same one-liners were redeclared privately in
hundreds of files, and copy-paste had become the normal way to obtain one (#2614).

The cost of that is not the duplicated lines, it is that a copied helper is free to
drift. NormalizeOptional had already split into three behaviours under one name and
signature -- most copies trimmed to null, one also lower-cased, one also upper-cased --
so the same call spelled the same way meant different things in different files. Nothing
failed at the time it diverged, which is what this check is for: the count grew from 62
to 64 while the issue describing it sat open.

So this fails CI when a file adds a private declaration of a tracked helper, or when a
new file starts declaring one. Migration batches shrink the baseline with
--update-baseline; the end state is an empty baseline, at which point a copy anywhere
fails.

Tracked names include documented aliases: TrimOrNull is NormalizeOptional under another
name, and FirstNonEmpty is FirstNonBlank, which is part of why the counts grew -- nobody
could tell which to reach for.

Names are only half the check. A copy that is *renamed* walks straight past a name list,
and renaming is the natural move when pasting a helper into a class that wants a shorter
word -- nine `Normalize` declarations were exact NormalizeOptional bodies while this check
reported the baseline satisfied (#2702). So declarations are also compared by *body*:
each private/internal helper is canonicalised (comments stripped, parameters and locals
renamed positionally, literals folded, single-return blocks unwrapped) and flagged when it
is alpha-equivalent to a body TextPrimitives owns, whatever the copy is called. Adding the
generic names to the tracked list instead would not work: `Normalize` alone has 41 distinct
bodies across 55 declarations, so a name rule would drown 9 real clones in 41 false fires.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
DEFAULT_BASELINE = Path(__file__).resolve().parent / "duplicate-helper-baseline.json"

# Helpers with a shared owner, plus the aliases that name the same function.
TRACKED_HELPERS = (
    "NormalizeOptional",
    "RequireText",
    "FirstNonBlank",
    "TrimOrNull",
    "FirstNonEmpty",
)

# A *declaration* of one of the tracked names, not a call. Matches any accessibility and
# any return type, so `private static string? NormalizeOptional(` and
# `internal static string RequireText(` both count. Exact name only: the near-name
# variants (NormalizeOptionalToken, NormalizeOptionalUpperInvariant, ...) are separate
# functions and are deliberately out of scope -- renaming one to say what it does is the
# fix this check should encourage, not something it should punish.
DECLARATION_PATTERN = re.compile(
    r"\b(?:private|internal|protected|public)\s+(?:static\s+)?(?:readonly\s+)?"
    r"[\w?<>\[\], .]+?\s+(" + "|".join(TRACKED_HELPERS) + r")\s*\("
)

# The canonical home is not a duplicate of itself.
EXCLUDED_FILES = {
    "src/Meridian.Contracts/Text/TextPrimitives.cs",
}

EXCLUDED_DIRECTORY_NAMES = {"bin", "node_modules", "obj"}

TEXT_PRIMITIVES_RELATIVE = "src/Meridian.Contracts/Text/TextPrimitives.cs"

_LINE_COMMENT = re.compile(r"//[^\n]*")
_BLOCK_COMMENT = re.compile(r"/\*.*?\*/", re.DOTALL)
_STRING_LITERAL = re.compile(r'\$?@?"(?:[^"\\]|\\.)*"')
_CHAR_LITERAL = re.compile(r"'(?:[^'\\]|\\.)'")
_NUMBER_LITERAL = re.compile(r"\b\d[\w.]*")
_IDENTIFIER = re.compile(r"[A-Za-z_@][\w]*")
# The declaration header of any private/internal method: accessibility, optional
# modifiers, a return type, then the name directly before the parameter list.
_METHOD_HEADER = re.compile(
    r"\b(?:private|internal)\s+(?:(?:static|readonly|async|sealed|new)\s+)*"
    r"[\w?<>\[\], .]+?\s+([A-Za-z_]\w*)\s*\("
)


def _strip_comments(text: str) -> str:
    return _LINE_COMMENT.sub(" ", _BLOCK_COMMENT.sub(" ", text))


def _matched_span(text: str, start: int, open_char: str, close_char: str) -> int | None:
    """Index just past the closer matching text[start] (which must be open_char)."""
    depth = 0
    for index in range(start, len(text)):
        char = text[index]
        if char == open_char:
            depth += 1
        elif char == close_char:
            depth -= 1
            if depth == 0:
                return index + 1
    return None


def _iter_methods(text: str):
    """Yield (name, parameter_text, body_text) for private/internal method declarations.

    Bodies are either the expression of an expression-bodied member (text between `=>`
    and the statement-level `;`) or the full braced block. Anything the scanner cannot
    bracket cleanly is skipped: this is a ratchet, and a miss is a quieter failure than
    a false fire on mangled input.
    """
    stripped = _strip_comments(text)
    for header in _METHOD_HEADER.finditer(stripped):
        name = header.group(1)
        params_open = header.end() - 1
        params_close = _matched_span(stripped, params_open, "(", ")")
        if params_close is None:
            continue
        parameter_text = stripped[params_open + 1 : params_close - 1]
        rest = stripped[params_close:]
        arrow = re.match(r"\s*=>", rest)
        if arrow:
            depth = 0
            for offset in range(arrow.end(), len(rest)):
                char = rest[offset]
                if char in "([{":
                    depth += 1
                elif char in ")]}":
                    depth -= 1
                elif char == ";" and depth == 0:
                    yield name, parameter_text, rest[arrow.end() : offset]
                    break
            continue
        brace = re.match(r"\s*(?:where\s[^{]*)?\{", rest)
        if brace:
            block_open = brace.end() - 1
            block_close = _matched_span(rest, block_open, "{", "}")
            if block_close is not None:
                yield name, parameter_text, rest[block_open + 1 : block_close - 1]


def _parameter_names(parameter_text: str) -> list[str]:
    """Declared parameter names, in order, ignoring attributes, types, and defaults."""
    names: list[str] = []
    depth = 0
    current: list[str] = []
    pieces: list[str] = []
    for char in parameter_text:
        if char in "([<":
            depth += 1
        elif char in ")]>":
            depth -= 1
        if char == "," and depth == 0:
            pieces.append("".join(current))
            current = []
        else:
            current.append(char)
    pieces.append("".join(current))

    for piece in pieces:
        declaration = piece.split("=", 1)[0]
        identifiers = _IDENTIFIER.findall(declaration)
        if identifiers:
            names.append(identifiers[-1])
    return names


def canonicalize_body(body: str, parameter_text: str) -> str:
    """Alpha-equivalent canonical form: same body, whatever the names and spacing.

    Parameters and `var`/`foreach var` locals are renamed positionally, string, char,
    and numeric literals are folded, whitespace is normalised away except between
    word tokens, and a block consisting of a single `return` unwraps to its expression
    so an expression-bodied copy and its braced twin compare equal. Member and type
    names are deliberately left intact -- `IsNullOrEmpty` is a different function from
    `IsNullOrWhiteSpace`, and folding them would manufacture equivalences.
    """
    text = _strip_comments(body).strip()

    unwrapped = re.match(r"^return\b(.*);$", text, re.DOTALL)
    if unwrapped and ";" not in unwrapped.group(1):
        text = unwrapped.group(1).strip()

    renames: dict[str, str] = {}
    for index, parameter in enumerate(_parameter_names(parameter_text)):
        renames[parameter] = f"§p{index}"
    for index, local in enumerate(re.findall(r"\bvar\s+([A-Za-z_]\w*)", text)):
        renames.setdefault(local, f"§l{index}")

    text = _STRING_LITERAL.sub("§str", text)
    text = _CHAR_LITERAL.sub("§chr", text)
    text = _NUMBER_LITERAL.sub("§num", text)
    text = _IDENTIFIER.sub(lambda match: renames.get(match.group(0), match.group(0)), text)

    text = re.sub(r"\s+", " ", text)
    return re.sub(r" ?([^\w§ ]) ?", r"\1", text).strip()


def owned_bodies(repo_root: Path) -> dict[str, str]:
    """Canonical body -> owning method name, for every method TextPrimitives declares."""
    path = repo_root / TEXT_PRIMITIVES_RELATIVE
    if not path.exists():
        return {}
    text = path.read_text(encoding="utf-8", errors="replace")
    owners: dict[str, str] = {}
    # The owner's methods are public, so the private/internal header regex does not see
    # them; scan with the accessibility widened instead of duplicating the machinery.
    widened = text.replace("public static", "internal static")
    for name, parameter_text, body in _iter_methods(widened):
        owners[canonicalize_body(body, parameter_text)] = name
    return owners


def scan_body_clones(repo_root: Path) -> dict[str, list[tuple[str, str]]]:
    """Per-file [(declared name, owning TextPrimitives name)] for renamed body clones.

    Declarations whose name is already in TRACKED_HELPERS are the name ratchet's
    jurisdiction and are skipped here, so one copy is never double-counted.
    """
    owners = owned_bodies(repo_root)
    if not owners:
        return {}

    clones: dict[str, list[tuple[str, str]]] = {}
    source_root = repo_root / "src"
    for current_root, directories, files in os.walk(source_root, topdown=True, followlinks=False):
        directories[:] = sorted(
            directory for directory in directories if directory.lower() not in EXCLUDED_DIRECTORY_NAMES
        )
        for file_name in sorted(files):
            if not file_name.lower().endswith(".cs"):
                continue
            path = Path(current_root) / file_name
            rel = path.relative_to(repo_root).as_posix()
            if rel in EXCLUDED_FILES:
                continue
            text = path.read_text(encoding="utf-8", errors="replace")
            for name, parameter_text, body in _iter_methods(text):
                if name in TRACKED_HELPERS:
                    continue
                owner = owners.get(canonicalize_body(body, parameter_text))
                if owner is not None:
                    clones.setdefault(rel, []).append((name, owner))
    return {rel: sorted(entries) for rel, entries in sorted(clones.items())}


def count_declarations(repo_root: Path) -> dict[str, int]:
    counts: dict[str, int] = {}
    source_root = repo_root / "src"
    candidate_paths: list[Path] = []
    for current_root, directories, files in os.walk(source_root, topdown=True, followlinks=False):
        directories[:] = sorted(
            directory for directory in directories if directory.lower() not in EXCLUDED_DIRECTORY_NAMES
        )
        candidate_paths.extend(
            Path(current_root) / file_name
            for file_name in files
            if file_name.lower().endswith(".cs")
        )

    for path in sorted(candidate_paths):
        rel = path.relative_to(repo_root).as_posix()
        if rel in EXCLUDED_FILES:
            continue
        matches = DECLARATION_PATTERN.findall(path.read_text(encoding="utf-8", errors="replace"))
        if matches:
            counts[rel] = len(matches)
    return counts


def main() -> int:
    parser = argparse.ArgumentParser(description="Enforce the consolidated-helper duplication ratchet.")
    parser.add_argument("--baseline", default=str(DEFAULT_BASELINE))
    parser.add_argument(
        "--update-baseline",
        action="store_true",
        help="Rewrite the baseline from current declaration counts (use in migration batch PRs).",
    )
    args = parser.parse_args()

    baseline_path = Path(args.baseline)
    current = count_declarations(REPO_ROOT)
    clones = scan_body_clones(REPO_ROOT)
    clone_counts = {rel: len(entries) for rel, entries in clones.items()}

    if args.update_baseline:
        payload = {
            "description": "Per-file counts of private declarations of helpers owned by "
            "Meridian.Contracts.Text.TextPrimitives. CI fails when a file exceeds its count or a "
            "new file appears; migration batches shrink this toward empty. body_clones counts "
            "declarations under any other name whose canonical body is one TextPrimitives owns.",
            "tracked_helpers": list(TRACKED_HELPERS),
            "files": current,
            "body_clones": clone_counts,
            "total": sum(current.values()) + sum(clone_counts.values()),
        }
        baseline_path.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")
        print(
            f"Baseline updated: {len(current)} file(s), {sum(current.values())} declaration(s), "
            f"{sum(clone_counts.values())} renamed body clone(s)."
        )
        return 0

    payload = json.loads(baseline_path.read_text(encoding="utf-8"))
    baseline = payload["files"]
    clone_baseline = payload.get("body_clones", {})

    violations: list[str] = []
    for rel, count in sorted(current.items()):
        allowed = baseline.get(rel)
        if allowed is None:
            violations.append(f"NEW private helper copy: {rel} ({count} declaration(s))")
        elif count > allowed:
            violations.append(f"{rel}: {count} declaration(s), baseline allows {allowed}")

    for rel, entries in clones.items():
        allowed = clone_baseline.get(rel, 0)
        if len(entries) > allowed:
            described = ", ".join(f"{name} is TextPrimitives.{owner}" for name, owner in entries)
            violations.append(
                f"RENAMED helper clone: {rel} ({described}; baseline allows {allowed})"
            )

    improved = {rel: baseline[rel] - current.get(rel, 0) for rel in baseline if current.get(rel, 0) < baseline[rel]}
    improved.update(
        {rel: clone_baseline[rel] - clone_counts.get(rel, 0)
         for rel in clone_baseline if clone_counts.get(rel, 0) < clone_baseline[rel]}
    )

    if violations:
        print("Consolidated-helper duplication ratchet violations:", file=sys.stderr)
        for violation in violations:
            print(f"- {violation}", file=sys.stderr)
        print(
            "Use Meridian.Contracts.Text.TextPrimitives instead "
            "(`using static Meridian.Contracts.Text.TextPrimitives;`). If the helper genuinely "
            "differs from the shared one, give it a name that says how -- a copy that silently "
            "behaves differently under the same name is the failure this check exists to prevent. "
            "If a batch PR reduced counts, refresh with --update-baseline.",
            file=sys.stderr,
        )
        return 1

    total = sum(current.values())
    print(
        f"Consolidated-helper ratchet: {total} private declaration(s) across {len(current)} file(s), "
        f"{sum(clone_counts.values())} baselined renamed body clone(s) (baseline satisfied)."
    )
    if improved:
        print(f"{len(improved)} file(s) improved below baseline — run --update-baseline in the migration PR to lock gains.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
