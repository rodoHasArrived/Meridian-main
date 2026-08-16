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
each helper is canonicalised (comments stripped, parameters and locals renamed
positionally, literals folded, single-return blocks unwrapped) and flagged when it is
alpha-equivalent to a body TextPrimitives owns, whatever the copy is called. Adding the
generic names to the tracked list instead would not work: `Normalize` alone has 41 distinct
bodies across 55 declarations, so a name rule would drown 9 real clones in 41 false fires.

The canonicaliser targets *accidental* copies -- paste, rename, reformat, respell a type,
wrap in parentheses. It is a text scanner, so a deliberately evasive copy (an inserted
no-op statement, a reordered condition) can always slip it; chasing that tail buys
nothing, because every added canonicalisation is another rule a determined copier steps
around. The escalation path for semantic equivalence is a Roslyn-based checker, the same
boundary drawn for the endpoint-cancellation guard (#2705).
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

_NUMBER_LITERAL = re.compile(r"\b\d[\w.]*")
_IDENTIFIER = re.compile(r"[A-Za-z_@][\w]*")
_STRING_PREFIX = re.compile(r'[$@]*"')
_INTERESTING = re.compile(r'//|/\*|\'|[$@]+"|"')
_EXPLICIT_FOREACH = re.compile(r"\bforeach\s*\(\s*[\w?<>\[\], .]+?\s+([A-Za-z_]\w*)\s+in\b")
# The CLR type behind each C# keyword alias: respelling `string` as `String` or
# `System.String` is a style choice, not a different function.
_TYPE_ALIASES = {
    "String": "string", "Int32": "int", "Int64": "long", "Boolean": "bool",
    "Object": "object", "Decimal": "decimal", "Double": "double", "Single": "float",
    "Char": "char", "Byte": "byte", "SByte": "sbyte", "Int16": "short",
    "UInt16": "ushort", "UInt32": "uint", "UInt64": "ulong",
}
_TYPE_ALIAS_PATTERN = re.compile(
    r"\b(?:System\.)?(" + "|".join(_TYPE_ALIASES) + r")\b"
)
# The declaration header of a method at any accessibility (matching the name-based
# scan's scope): accessibility keyword(s), optional modifiers, a return type, then the
# name directly before the parameter list.
_METHOD_HEADER = re.compile(
    r"\b(?:private|internal|protected|public)\s+"
    r"(?:(?:static|readonly|async|sealed|new|virtual|override|internal|protected)\s+)*"
    r"[\w?<>\[\], .]+?\s+([A-Za-z_]\w*)\s*\("
)
_PARAMETER_MODIFIERS = ("params", "ref", "out", "in", "this", "scoped", "readonly")


def _mask_strings_and_comments(text: str) -> str:
    """One pass over C# source: comments become spaces, string literals become §str, char
    literals become §chr.

    Both jobs have to happen together, in source order, because each hides the other's
    markers: `//` inside a string is text, not a comment, and quotes inside a comment are
    text, not a string. Masking first also keeps every later stage honest at once — a
    method-shaped snippet inside a template string is not a declaration, and a `;` or
    brace inside a message cannot terminate an expression body early.

    Interpolated strings keep their `{...}` expressions (recursively masked): the text
    around them is message formatting, but the expressions are executed code, and a copy
    that runs different code is not a clone however similar its message.
    """
    out: list[str] = []
    index = 0
    length = len(text)
    while index < length:
        # Jump to the next construct start; everything in between is plain code and is
        # copied wholesale. The per-character walk this replaced dominated the check's
        # runtime once every method body in the tree went through it.
        interesting = _INTERESTING.search(text, index)
        if interesting is None:
            out.append(text[index:])
            break
        out.append(text[index : interesting.start()])
        index = interesting.start()
        two = text[index : index + 2]
        if two == "//":
            newline = text.find("\n", index)
            index = length if newline == -1 else newline
            out.append(" ")
            continue
        if two == "/*":
            closer = text.find("*/", index + 2)
            index = length if closer == -1 else closer + 2
            out.append(" ")
            continue
        if text[index] == "'":
            cursor = index + 1
            while cursor < length and text[cursor] != "'":
                cursor += 2 if text[cursor] == "\\" else 1
            out.append("§chr")
            index = cursor + 1
            continue
        prefix = _STRING_PREFIX.match(text, index)
        if prefix:
            dollars = prefix.group(0).count("$")
            interpolated = dollars > 0
            verbatim = "@" in prefix.group(0)
            out.append("§str")
            if text.startswith('"""', prefix.end() - 1):
                content_start = prefix.end() + 2
                closer = text.find('"""', content_start)
                index = length if closer == -1 else closer + 3
                if interpolated and closer != -1:
                    # Raw interpolated strings carry executed code too; preserve their
                    # expressions exactly as the ordinary $"..." branch below does. The
                    # interpolation delimiter is as many braces as the prefix has
                    # dollars; shorter brace runs are literal text.
                    delimiter = "{" * dollars
                    segment = text[content_start:closer]
                    cursor = 0
                    while cursor < len(segment):
                        if segment[cursor] != "{":
                            cursor += 1
                            continue
                        run = cursor
                        while run < len(segment) and segment[run] == "{":
                            run += 1
                        if run - cursor == dollars:
                            expr_end = segment.find("}" * dollars, run)
                            if expr_end == -1:
                                break
                            out.append(
                                "{" + _mask_strings_and_comments(segment[run:expr_end]) + "}"
                            )
                            out.append("§str")
                            cursor = expr_end + dollars
                        else:
                            cursor = run
                    continue
                continue
            cursor = prefix.end()
            while cursor < length:
                char = text[cursor]
                if verbatim and char == '"':
                    if text[cursor : cursor + 2] == '""':
                        cursor += 2
                        continue
                    cursor += 1
                    break
                if not verbatim and char == "\\":
                    cursor += 2
                    continue
                if not verbatim and char == '"':
                    cursor += 1
                    break
                if interpolated and char == "{":
                    if text[cursor : cursor + 2] == "{{":
                        cursor += 2
                        continue
                    depth = 0
                    closer = cursor
                    while closer < length:
                        if text[closer] == "{":
                            depth += 1
                        elif text[closer] == "}":
                            depth -= 1
                            if depth == 0:
                                break
                        closer += 1
                    out.append("{" + _mask_strings_and_comments(text[cursor + 1 : closer]) + "}")
                    out.append("§str")
                    cursor = closer + 1
                    continue
                cursor += 1
            index = cursor
            continue
        out.append(text[index])
        index += 1
    return "".join(out)


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
    stripped = _mask_strings_and_comments(text)
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


def _split_parameters(parameter_text: str) -> list[str]:
    """Top-level comma split, then per piece: attributes stripped, default cut.

    Attributes are removed by bracket matching before the default-value cut, because an
    attribute's *named argument* (`[Example(Name = "x")]`) contains an `=` that is not
    the parameter's default; cutting at the first `=` naively would truncate inside the
    attribute and misread the parameter entirely.
    """
    pieces: list[str] = []
    depth = 0
    current: list[str] = []
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

    cleaned: list[str] = []
    for piece in pieces:
        piece = piece.strip()
        while piece.startswith("["):
            closer = _matched_span(piece, 0, "[", "]")
            if closer is None:
                break
            piece = piece[closer:].lstrip()
        depth = 0
        for offset, char in enumerate(piece):
            if char in "([<":
                depth += 1
            elif char in ")]>":
                depth -= 1
            elif char == "=" and depth == 0:
                piece = piece[:offset]
                break
        cleaned.append(piece.strip())
    return cleaned


def _parameter_names(parameter_text: str) -> list[str]:
    """Declared parameter names, in order, ignoring attributes, types, and defaults."""
    names: list[str] = []
    for piece in _split_parameters(parameter_text):
        identifiers = _IDENTIFIER.findall(piece)
        if identifiers:
            names.append(identifiers[-1])
    return names


def _parameter_types(parameter_text: str) -> str:
    """Canonical comma-joined parameter types, for the clone key.

    A textually identical body over `dynamic` or a custom implicitly-convertible type
    can dispatch differently, so types are part of what makes two helpers the same
    function. Nullability annotations are erased -- `string` and `string?` differ only
    at compile time -- and calling-convention modifiers are dropped for the same reason.
    """
    types: list[str] = []
    for piece in _split_parameters(parameter_text):
        declaration = re.sub(r"[A-Za-z_@]\w*\s*$", "", piece).strip()
        changed = True
        while changed:
            changed = False
            for modifier in _PARAMETER_MODIFIERS:
                if declaration == modifier or declaration.startswith(modifier + " "):
                    declaration = declaration[len(modifier):].strip()
                    changed = True
        declaration = declaration.replace("?", "")
        declaration = re.sub(r"\s+", "", declaration)
        declaration = _TYPE_ALIAS_PATTERN.sub(
            lambda match: _TYPE_ALIASES[match.group(1)], declaration
        )
        if declaration:
            types.append(declaration)
    return ",".join(types)


def canonicalize_body(body: str, parameter_text: str) -> str:
    """Alpha-equivalent canonical form: same body, whatever the names and spacing.

    The body must already be masked by _mask_strings_and_comments -- _iter_methods
    yields masked text, and re-masking every body was the scan's dominant cost.
    Parameters and loop/`var` locals are renamed positionally, numeric literals are
    folded, an explicitly typed `foreach` is normalised to its `var` spelling,
    redundant outer parentheses are dropped, whitespace is normalised away except
    between word tokens, and a block consisting of a single `return` unwraps to its
    expression so an expression-bodied copy and its braced twin compare equal. Member
    and type names are deliberately left intact -- `IsNullOrEmpty` is a different
    function from `IsNullOrWhiteSpace`, and folding them would manufacture
    equivalences.
    """
    text = body.strip()

    unwrapped = re.match(r"^return\b(.*);$", text, re.DOTALL)
    if unwrapped and ";" not in unwrapped.group(1):
        text = unwrapped.group(1).strip()

    # Redundant grouping is formatting: `=> (expr);` is `=> expr;`.
    while text.startswith("(") and _matched_span(text, 0, "(", ")") == len(text):
        text = text[1:-1].strip()

    text = _EXPLICIT_FOREACH.sub(r"foreach (var \1 in", text)

    renames: dict[str, str] = {}
    for index, parameter in enumerate(_parameter_names(parameter_text)):
        renames[parameter] = f"§p{index}"
    for index, local in enumerate(re.findall(r"\bvar\s+([A-Za-z_]\w*)", text)):
        renames.setdefault(local, f"§l{index}")

    text = _NUMBER_LITERAL.sub("§num", text)
    text = _IDENTIFIER.sub(lambda match: renames.get(match.group(0), match.group(0)), text)

    text = re.sub(r"\s+", " ", text)
    return re.sub(r" ?([^\w§ ]) ?", r"\1", text).strip()


def owned_bodies(repo_root: Path) -> dict[tuple[str, str], str]:
    """(canonical parameter types, canonical body) -> owning TextPrimitives method name."""
    path = repo_root / TEXT_PRIMITIVES_RELATIVE
    if not path.exists():
        return {}
    text = path.read_text(encoding="utf-8", errors="replace")
    owners: dict[tuple[str, str], str] = {}
    for name, parameter_text, body in _iter_methods(text):
        owners[(_parameter_types(parameter_text), canonicalize_body(body, parameter_text))] = name
    return owners


def scan_body_clones(repo_root: Path) -> dict[str, list[tuple[str, str]]]:
    """Per-file [(declared name, owning TextPrimitives name)] for renamed body clones.

    Declarations whose name is already in TRACKED_HELPERS are the name ratchet's
    jurisdiction and are skipped here, so one copy is never double-counted.
    """
    owners = owned_bodies(repo_root)
    if not owners:
        return {}
    # The cheap signature gate: only a handful of parameter-type shapes can match an
    # owner, so almost every method skips the body canonicalisation entirely.
    owner_types = {types for types, _ in owners}

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
                types = _parameter_types(parameter_text)
                if types not in owner_types:
                    continue
                owner = owners.get((types, canonicalize_body(body, parameter_text)))
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
