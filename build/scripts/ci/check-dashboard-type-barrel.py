#!/usr/bin/env python3
"""Keep every browser-workstation contract type declared exactly once behind the barrel.

The dashboard re-exports its contract modules through `src/types.ts` with `export * from`.
TypeScript resolves an ambiguous star export by exporting *neither* declaration, so two
modules declaring the same name do not produce a conflict at the barrel — the name simply
stops being exported, and every consumer that imported it from `@/types` fails somewhere
else entirely. The failure is loud but the cause is invisible, which is why duplicate DTO
declarations survived long enough to become a tracked defect.

This gate fails when:

- two barrel modules publish the same exported name from *different bindings* (one module
  declaring `Shared` and another re-exporting that same `Shared` is not a collision —
  TypeScript keeps the name importable — so names are attributed to the binding they
  resolve to rather than to the module that published them);
- a module listed in the barrel does not exist;
- a contract module under `src/types/` is not reachable through the barrel and is not
  listed as a deliberately standalone module (so a new DTO file cannot be added outside
  the single-declaration contract without saying so).

Name collection covers direct declarations, named re-exports, and `export * as Ns from`
(which publishes the single name `Ns`). It cannot follow a *bare* `export * from "../contracts"`
chain: the names that publishes depend on the target and everything it re-exports in turn,
which wants the TypeScript AST rather than another regex pass. Left as a silent gap that would
be the gate's worst failure mode — a name republished that way contributes no owner, so a real
collision with a sibling's declaration removes the name from `@/types` while the gate reports
zero duplicates. So a bare star re-export inside a barrel module is rejected outright instead,
with the explicit form to use. No barrel module uses one today, so this bounds the gap rather
than restricting anything currently written.

Run with `--summary` for a one-line report.
"""

from __future__ import annotations

import argparse
import posixpath
import re
import sys
from bisect import bisect_right
from collections import defaultdict
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
DASHBOARD_ROOT = REPO_ROOT / "src" / "Meridian.Ui" / "dashboard"
BARREL_PATH = DASHBOARD_ROOT / "src" / "types.ts"
TYPES_DIR = DASHBOARD_ROOT / "src" / "types"

# Modules under src/types/ that are deliberately imported by path rather than re-exported
# through the barrel, typically because they are feature-local rather than shared contracts.
STANDALONE_MODULES = {
    "portfolio-cash-ladder.types",
    "covered-call.types",
    "data-operations-assurance",
    "provider-setup",
}

# A trailing `// note` is allowed: without it the whole entry failed to match, so the module
# dropped out of the barrel list and then tripped the orphan-module check instead — a
# confusing failure for a line TypeScript reads as an ordinary export.
BARREL_EXPORT = re.compile(
    r"^\s*export\s+(?:type\s+)?\*\s+from\s+(?P<q>[\"'])\./types/(?P<module>[^\"']+)(?P=q)\s*;?\s*(?://.*)?$",
    re.MULTILINE,
)
# Leading whitespace is permitted because indentation alone does not mean nesting: 11 declarations
# in workstation-3.ts sit at module scope carrying stray indentation from a removed wrapper, and
# requiring column zero skipped every one of them, so duplicating any would still have reported
# zero duplicates. Nesting is decided by brace depth instead (see declared_names) — only a
# depth-zero declaration is what `export *` re-exports, so `export namespace N { export interface
# Row {} }` contributes `N` and never `Row`.
# `(?<![\w$.])` instead of a line anchor: several declarations may share a line, and the anchor
# let this collector see only the first. The lookbehind still refuses a match inside an identifier
# ('reexport') or after a dot ('ns.export'), which a bare \b would not.
DECLARATION = re.compile(
    r"(?<![\w$.])export\s+(?:declare\s+)?(?:abstract\s+)?(?:async\s+)?"
    r"(?:type|interface|enum|const\s+enum|const|let|var|function\*?|class|namespace|module)\s+"
    r"(?P<name>[A-Za-z_$][\w$]*)",
    re.MULTILINE,
)
# `export { LedgerRowDto } from "../contracts"` and `export { A, B as C }` publish names from the
# module just as a declaration does, so they collide under the barrel's `export *` in exactly the
# same way. Matching only declarations meant a re-exported duplicate silently vanished from
# '@/types' while the gate reported zero duplicates.
NAMED_REEXPORT = re.compile(
    r"(?<![\w$.])export\s+type\s*\{(?P<names>[^}]*)\}|(?<![\w$.])export\s*\{(?P<plain>[^}]*)\}",
    re.MULTILINE,
)
# `import type { Shared } from "./origin"` followed by a local `export type { Shared }` still
# publishes origin's binding, so the export has to be resolved through the import rather than
# attributed to this module — otherwise a sibling re-exporting the same original binding looked
# like a second binding and the gate blocked a barrel TypeScript keeps unambiguous.
NAMED_IMPORT = re.compile(r"(?<![\w$.])import\s+(?:type\s+)?\{(?P<names>[^}]*)\}", re.MULTILINE)
# Inside the braces: `Name`, `Name as Alias`, `type Name`, `default as Name`. The published name
# is the alias when present, otherwise the name itself.
REEXPORT_SPECIFIER = re.compile(r"(?:type\s+)?(?P<name>[A-Za-z_$][\w$]*)(?:\s+as\s+(?P<alias>[A-Za-z_$][\w$]*))?")
# `export type * from "..."` and `export type * as Ns from "..."` (TypeScript 5.0) publish the
# same way as their value forms and must be recognised identically — matching only the untyped
# spelling left the type-only one collecting nothing, which is the silent under-report this
# rejection exists to prevent.
# `export * as Ns from "..."` publishes exactly one name, so it collides like a declaration.
# A *bare* `export * from "..."` republishes an unknown set, which this gate cannot resolve
# without the TypeScript AST — it is rejected outright rather than under-reported. See
# star_reexports() for why that is a failure and not a silent skip.
# Matched against the *stripped* text, so a star export written inside a comment or a string
# is not a finding. strip_comments_and_strings blanks a literal including its quotes, so these
# cannot require the quotes — the specifier is recovered from the raw text by offset, which the
# blanking preserves exactly.
NAMESPACE_REEXPORT = re.compile(
    r"(?<![\w$.])export(?:\s+type)?\s*\*\s*as\s+(?P<name>[A-Za-z_$][\w$]*)\s+from\b",
    re.MULTILINE,
)
BARE_STAR_REEXPORT = re.compile(r"(?<![\w$.])export(?:\s+type)?\s*\*\s*from\b", re.MULTILINE)
# `export const { Shared } = value` publishes every binding in the pattern, which can nest,
# rename, default, and rest. Enumerating those correctly is parser work; leaving them uncollected
# means a real collision with a sibling's declaration reports as zero duplicates. Rejected
# outright instead, exactly as a bare star re-export is.
DESTRUCTURED_EXPORT = re.compile(
    r"(?<![\w$.])export\s+(?:declare\s+)?(?:const|let|var)\s*[{\[]",
    re.MULTILINE,
)
MODULE_SPECIFIER = re.compile(r"[\"'](?P<module>[^\"']+)[\"']")
# A `from` clause immediately after a re-export's closing brace or star.
FROM_CLAUSE = re.compile(r"\s*from\b")


def read_barrel_modules(barrel_path: Path) -> list[str]:
    """Return the modules the barrel re-exports, ignoring commented-out entries.

    BARREL_EXPORT has to run over raw text because stripping blanks a string literal along with
    its quotes, and the module path lives inside those quotes. So a commented-out
    `export * from "./types/retired"` was collected as a live entry, and if that module had been
    deleted the gate reported it missing and blocked CI over a statement TypeScript ignores.
    Comment-stripping preserves offsets exactly, so a match whose span comes back blank there was
    inside a comment.
    """
    if not barrel_path.is_file():
        raise FileNotFoundError(f"type barrel not found: {barrel_path}")
    raw = barrel_path.read_text(encoding="utf-8")
    stripped = strip_comments_and_strings(raw)
    return [
        match.group("module")
        for match in BARREL_EXPORT.finditer(raw)
        if "export" in stripped[match.start() : match.end()]
    ]


REGEX_PRECEDING_KEYWORD = re.compile(
    r"\b(?:return|typeof|instanceof|in|of|new|delete|void|throw|case|do|else|yield|await)$"
)


def _starts_regex_literal(emitted: list[str]) -> bool:
    """Return whether a `/` at this point opens a regex literal rather than dividing.

    JavaScript needs the parser state to answer this exactly; the standard heuristic is that a
    regex can only begin where a value can, i.e. after an operator, an opening bracket, or a
    statement boundary. Contract modules are types and constants, so the heuristic is ample —
    and it is only used to blank the literal out, never to interpret it.
    """
    prefix = "".join(emitted).rstrip()
    if not prefix:
        return True  # start of file
    # `return /\{/.test(x)` ends in a keyword, not punctuation. Treating that `/` as division
    # left the escaped brace in the text, and declared_names infers nesting from brace depth, so
    # the depth stayed positive and every later module-scope export dropped out of the comparison.
    if REGEX_PRECEDING_KEYWORD.search(prefix):
        return True
    # `/` after `/` or `*` is a comment opener, handled by the caller before this runs.
    # `)` is genuinely ambiguous: `(a + b) / c` divides, but `if (value) /\{/.test(value)` opens
    # a regex. Admitting it is safe only because _regex_end requires the literal to terminate on
    # the same line — and getting it wrong in this direction is the costly one. Classifying that
    # opener as division let the regex's `{` reach the brace counter, raising the depth for the
    # rest of the file so every later declaration looked nested and dropped out of the
    # comparison, and the gate then reported zero duplicates whatever was duplicated.
    return prefix[-1] in "(),=:[!&|?{};+-*%~^<>"


def _regex_end(text: str, start: int) -> int | None:
    """Return the index just past a regex literal opening at *start*, or None.

    A JavaScript regex literal cannot contain an unescaped newline, so a candidate that reaches
    end of line was never a regex. Checking before blanking means a misread `/` costs nothing;
    the previous shape blanked as it scanned and only then discovered the newline.
    """
    i = start + 1
    length = len(text)
    in_class = False
    while i < length:
        ch = text[i]
        if ch == "\\":
            i += 2
            continue
        if ch == "\n":
            return None
        # A quote before the closing slash means this was almost certainly code, not a regex:
        # `(total) / "x/y".length` would otherwise terminate on the slash *inside* the string,
        # blank through it, and leave the string's closing quote to open a new one — swallowing
        # any declaration later on the line. Regexes matching a quote character exist but are
        # vanishingly rare in contract modules, and rejecting them only falls back to reading
        # the slash as division, which is what this position meant before regexes were admitted
        # here at all.
        if ch in "\"'`":
            return None
        if ch == "[":
            in_class = True
        elif ch == "]":
            in_class = False
        elif ch == "/" and not in_class:
            return i + 1
        i += 1
    return None


def strip_comments_and_strings(text: str) -> str:
    """Blank out comments and string/template contents, preserving line structure.

    Matching raw text counts a declaration written inside a block comment or a template
    literal as a live export. Unlike the metric gate — where that produces a false pass —
    here it produces a false *failure*: the phantom name collides with the real declaration
    in another module and the gate blocks a valid change over a duplicate TypeScript never
    emitted. Newlines are preserved so any future line reporting stays accurate.
    """
    out: list[str] = []
    i = 0
    length = len(text)
    while i < length:
        ch = text[i]
        if ch in "\"'`":
            quote = ch
            out.append(" ")
            i += 1
            while i < length:
                if text[i] == "\\":
                    out.append("  ")
                    i += 2
                    continue
                if text[i] == "\n":
                    # Only template literals span lines; keep the newline either way so line
                    # numbers do not shift.
                    out.append("\n")
                    i += 1
                    if quote != "`":
                        break
                    continue
                if text[i] == quote:
                    out.append(" ")
                    i += 1
                    break
                out.append(" ")
                i += 1
            continue
        if ch == "/" and i + 1 < length and text[i + 1] == "/":
            while i < length and text[i] != "\n":
                out.append(" ")
                i += 1
            continue
        if ch == "/" and i + 1 < length and text[i + 1] == "*":
            out.append("  ")
            i += 2
            while i + 1 < length and not (text[i] == "*" and text[i + 1] == "/"):
                out.append("\n" if text[i] == "\n" else " ")
                i += 1
            out.append("  ")
            i += 2
            continue
        if ch == "/" and _starts_regex_literal(out):
            # A regex literal can hold an unmatched brace (/\{/), and declared_names infers
            # nesting from brace depth. One such literal at module scope would raise the depth
            # for the rest of the file, dropping every later export from the comparison — the
            # gate would then report zero duplicates no matter what was duplicated.
            regex_end = _regex_end(text, i)
            if regex_end is not None:
                out.append(" " * (regex_end - i))
                i = regex_end
                continue
            # Not a regex after all — fall through and emit the slash as an ordinary character.
        out.append(ch)
        i += 1
    return "".join(out)


def declared_names(module_path: Path, module: str = "") -> list[tuple[str, str]]:
    """Return `(published_name, origin)` for every name `export *` re-exports from *module_path*.

    Only module-scope declarations qualify. A declaration inside `export namespace N { ... }`
    or an ambient `declare module "x" { ... }` block is reachable as `N.Row`, never as a bare
    `Row`, so counting it would let a legitimate top-level `Row` elsewhere read as an ambiguous
    star export and block CI over a collision TypeScript never sees.

    `origin` identifies the *binding* a name resolves to, not the module that published it. Two
    barrel modules publishing the same name are only ambiguous to TypeScript when the names
    resolve to different bindings: one module declaring `Shared` and another writing
    `export { Shared } from "./first"` republishes a single binding, so `Shared` stays importable
    from `@/types` and reporting it as a duplicate would block CI over a collision that does not
    exist. Attributing a re-export to its target rather than to the re-exporting module makes
    those two agree on one origin, while two independent declarations keep distinct origins and
    are still reported.
    """
    raw = module_path.read_text(encoding="utf-8")
    text = strip_comments_and_strings(raw)

    def resolve(spec: str) -> str:
        """Resolve a module specifier against this module's directory."""
        if not spec.startswith("."):
            return spec  # bare package specifier; distinct from any local module
        return posixpath.normpath(posixpath.join(posixpath.dirname(module), spec))

    def specifier_after(end: int, from_consumed: bool = False) -> str | None:
        """Return the module specifier of the `from "..."` clause at *end*, if any.

        NAMESPACE_REEXPORT matches through `from`, so requiring the keyword again here found
        nothing and every `export * as Ns from "./origin"` fell back to its own module as the
        origin. Two barrel modules re-exporting one target then looked like two bindings and
        were reported as a collision, blocking CI for a barrel TypeScript accepts.
        """
        search_from = end
        if not from_consumed:
            clause = FROM_CLAUSE.match(text, end)
            if clause is None:
                return None
            # Start after the `from` keyword. Searching from the closing brace picked up a
            # quoted string inside an intervening comment —
            # `export { Shared } /* "../origins/common" */ from "../origins/a"` resolved to the
            # comment, so two modules sharing that comment were given one origin and a real
            # collision was reported as zero duplicates. FROM_CLAUSE runs on stripped text, so a
            # `from` written inside a comment is not mistaken for the real one.
            search_from = clause.end()
        found = MODULE_SPECIFIER.search(raw, search_from, search_from + 200)
        return found.group("module") if found else None

    # Brace depth at the start of each line. Comments and string bodies are already blanked, so
    # a brace surviving here is real syntax.
    depth_at_line_start: list[int] = []
    depth = 0
    for line in text.split("\n"):
        depth_at_line_start.append(depth)
        depth += line.count("{") - line.count("}")

    line_starts = [0]
    for line in text.split("\n")[:-1]:
        line_starts.append(line_starts[-1] + len(line) + 1)

    def at_module_scope(offset: int) -> bool:
        # Depth at the exact offset, not at the start of its line. Several declarations can share
        # a line ('export interface A {} export interface Shared {}'), and a line-granular depth
        # would give every one of them the first's scope — counting `Row` in a single-line
        # `export namespace N { export interface Row {} }` as though it were top level.
        line = bisect_right(line_starts, offset) - 1
        prefix = text[line_starts[line] : offset]
        return depth_at_line_start[line] + prefix.count("{") - prefix.count("}") == 0

    # local name -> the binding it was imported from, for resolving `export { X }` with no
    # `from` clause. `import { A as B }` binds local B to the target's A.
    imported: dict[str, str] = {}
    for match in NAMED_IMPORT.finditer(text):
        if not at_module_scope(match.start()):
            continue
        target = specifier_after(match.end())
        if target is None:
            continue
        source = resolve(target)
        for specifier in REEXPORT_SPECIFIER.finditer(match.group("names") or ""):
            local = specifier.group("alias") or specifier.group("name")
            imported[local] = f"{source}:{specifier.group('name')}"

    names: list[tuple[str, str]] = []
    for match in DECLARATION.finditer(text):
        if at_module_scope(match.start()):
            names.append((match.group("name"), f"{module}:{match.group('name')}"))

    for match in NAMED_REEXPORT.finditer(text):
        if not at_module_scope(match.start()):
            continue
        body = match.group("names") if match.group("names") is not None else match.group("plain")
        # `export { A } from "./first"` resolves to first's binding; a local `export { A }`
        # resolves to this module's own `A`.
        target = specifier_after(match.end())
        source = resolve(target) if target is not None else module
        for specifier in REEXPORT_SPECIFIER.finditer(body or ""):
            published = specifier.group("alias") or specifier.group("name")
            # `export { default as X }` publishes X; a bare `default` publishes nothing here.
            if published == "default":
                continue
            local = specifier.group("name")
            # A local clause re-exporting an imported name resolves to where it was imported
            # from, not to this module.
            origin = (
                imported[local]
                if target is None and local in imported
                else f"{source}:{local}"
            )
            names.append((published, origin))

    for match in NAMESPACE_REEXPORT.finditer(text):
        if not at_module_scope(match.start()):
            continue
        # The namespace object of a module is canonical, so two modules re-exporting the same
        # target under the same name publish one binding, not two.
        target = specifier_after(match.end(), from_consumed=True)
        source = resolve(target) if target is not None else module
        names.append((match.group("name"), f"{source}:*"))

    return names


def star_reexports(module_path: Path) -> list[str]:
    """Return the module specifiers a contract module republishes with a bare `export *`.

    The names such a re-export publishes cannot be determined without resolving the target
    and everything it in turn re-exports, which wants the TypeScript AST rather than this
    lexer. Rather than let those names contribute no owner — which would report zero
    duplicates for a collision that does remove the name from '@/types' — the gate treats a
    bare star re-export inside a barrel module as a failure and says why. No barrel module
    uses one today, so this is a precondition that keeps the reported count honest, not a
    restriction on anything currently written.
    """
    raw = module_path.read_text(encoding="utf-8")
    text = strip_comments_and_strings(raw)

    targets = []
    for match in BARE_STAR_REEXPORT.finditer(text):
        # The statement runs to the first ';' or newline after the match; the specifier is the
        # quoted string inside it, read from raw text because stripping blanked the quotes.
        end = min(pos for pos in (text.find(";", match.end()), text.find("\n", match.end()), len(text)) if pos != -1)
        specifier = MODULE_SPECIFIER.search(raw, match.end(), end)
        targets.append(specifier.group("module") if specifier else "<unresolved>")
    return targets


def discover_contract_modules(types_dir: Path) -> list[str]:
    """Return the module stems under src/types/, excluding test files."""
    stems = []
    # Recursive: a contract at src/types/accounting/ledger.ts was invisible to a flat glob, so it
    # could be absent from both the barrel and STANDALONE_MODULES while the orphan-module check
    # reported success. Barrel entries and module_path already carry slash-separated paths, so
    # nothing else here assumes a flat directory.
    for path in sorted(types_dir.rglob("*.ts")):
        if path.name.endswith(".test.ts") or path.name.endswith(".d.ts"):
            continue
        stems.append(path.relative_to(types_dir).with_suffix("").as_posix())
    return stems


def evaluate(barrel_path: Path, types_dir: Path) -> tuple[list[str], dict[str, int]]:
    problems: list[str] = []
    modules = read_barrel_modules(barrel_path)

    # published name -> resolved binding origin -> the barrel modules publishing it.
    declarations: dict[str, dict[str, list[str]]] = defaultdict(dict)
    for module in modules:
        module_path = types_dir / f"{module}.ts"
        if not module_path.is_file():
            problems.append(f"src/types.ts re-exports './types/{module}', which does not exist")
            continue
        for _ in DESTRUCTURED_EXPORT.finditer(
            strip_comments_and_strings(module_path.read_text(encoding="utf-8"))
        ):
            problems.append(
                f"src/types/{module}.ts uses a destructured export "
                "('export const {{ ... }} = ...'). This gate cannot enumerate the names that "
                "publishes, so a collision between them and a sibling module's declaration would "
                "be reported as zero duplicates. Declare the bindings individually."
            )
        for target in star_reexports(module_path):
            problems.append(
                f"src/types/{module}.ts republishes '{target}' with a bare 'export *'. "
                "This gate cannot resolve which names that publishes, so a collision between "
                "them and a sibling module's declaration would be reported as zero duplicates. "
                "Re-export the names explicitly ('export { A, B } from ...') so they are visible."
            )
        stripped = strip_comments_and_strings(module_path.read_text(encoding="utf-8"))
        if stripped.count("{") != stripped.count("}"):
            # Brace depth decides which declarations are at module scope, so an unbalanced view
            # means this lexer misread something and every later declaration in the file may have
            # silently dropped out of the comparison. Reporting zero duplicates from that view is
            # the one outcome worse than failing.
            problems.append(
                f"src/types/{module}.ts did not parse to balanced braces. The scanner cannot tell "
                "which declarations are at module scope in this file, so its result is not "
                "trustworthy; simplify the construct or report this as a gate bug."
            )
        for name, origin in declared_names(module_path, module):
            # Declaration merging and function overloads legitimately repeat a name inside one
            # module, and `export *` still publishes a single unambiguous symbol. Appending each
            # occurrence made three overload signatures read as "3 barrel modules" and blocked CI
            # over a collision that does not exist. Only distinct *bindings* can collide.
            owners = declarations[name].setdefault(origin, [])
            if module not in owners:
                owners.append(module)

    for name, by_origin in sorted(declarations.items()):
        if len(by_origin) > 1:
            modules_involved = sorted({m for owners in by_origin.values() for m in owners})
            problems.append(
                f"'{name}' is declared in {len(modules_involved)} barrel modules "
                f"({', '.join(modules_involved)}). "
                "An ambiguous star export removes the name from '@/types' entirely — "
                "keep one declaration and re-export it."
            )

    barrelled = set(modules)
    for stem in discover_contract_modules(types_dir):
        if stem in barrelled or stem in STANDALONE_MODULES:
            continue
        problems.append(
            f"src/types/{stem}.ts is neither re-exported by src/types.ts nor listed as standalone; "
            "add it to the barrel or to STANDALONE_MODULES with a reason."
        )

    counts = {
        "modules": len(modules),
        "exported_names": len(declarations),
        "duplicates": sum(1 for by_origin in declarations.values() if len(by_origin) > 1),
    }
    return problems, counts


def parse_arguments(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--barrel", type=Path, default=BARREL_PATH, help="Path to the dashboard type barrel.")
    parser.add_argument("--types-dir", type=Path, default=TYPES_DIR, help="Directory holding the contract modules.")
    parser.add_argument("--summary", action="store_true", help="Print a one-line summary.")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_arguments(argv)

    try:
        problems, counts = evaluate(args.barrel.resolve(), args.types_dir.resolve())
    except FileNotFoundError as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1

    if args.summary:
        print(
            f"dashboard-type-barrel: {counts['modules']} modules, "
            f"{counts['exported_names']} exported names, {counts['duplicates']} duplicate(s)"
        )
    else:
        print(f"Barrel re-exports {counts['modules']} contract modules declaring {counts['exported_names']} names.")

    if problems:
        print("", file=sys.stderr)
        print(f"Dashboard type barrel validation failed with {len(problems)} problem(s):", file=sys.stderr)
        for problem in problems:
            print(f"  {problem}", file=sys.stderr)
        return 1

    print("Every contract type is declared exactly once behind the barrel.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
