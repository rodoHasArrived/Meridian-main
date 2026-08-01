#!/usr/bin/env python3
"""Keep every browser-workstation contract type declared exactly once behind the barrel.

The dashboard re-exports its contract modules through `src/types.ts` with `export * from`.
TypeScript resolves an ambiguous star export by exporting *neither* declaration, so two
modules declaring the same name do not produce a conflict at the barrel — the name simply
stops being exported, and every consumer that imported it from `@/types` fails somewhere
else entirely. The failure is loud but the cause is invisible, which is why duplicate DTO
declarations survived long enough to become a tracked defect.

This gate fails when:

- two barrel modules declare the same exported name;
- a module listed in the barrel does not exist;
- a contract module under `src/types/` is not reachable through the barrel and is not
  listed as a deliberately standalone module (so a new DTO file cannot be added outside
  the single-declaration contract without saying so).

Run with `--summary` for a one-line report.
"""

from __future__ import annotations

import argparse
import re
import sys
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

BARREL_EXPORT = re.compile(r'^\s*export\s+\*\s+from\s+"\./types/(?P<module>[^"]+)"\s*;?\s*$', re.MULTILINE)
# Leading whitespace is permitted: exports nested inside a `declare module` or namespace block
# are still exports. Requiring column zero silently skipped 11 declarations in workstation-3.ts
# alone, so duplicating any of them elsewhere would still have reported zero duplicates.
DECLARATION = re.compile(
    r"^[ \t]*export\s+(?:declare\s+)?(?:abstract\s+)?"
    r"(?:type|interface|enum|const\s+enum|const|let|var|function|class)\s+"
    r"(?P<name>[A-Za-z_$][\w$]*)",
    re.MULTILINE,
)


def read_barrel_modules(barrel_path: Path) -> list[str]:
    if not barrel_path.is_file():
        raise FileNotFoundError(f"type barrel not found: {barrel_path}")
    return BARREL_EXPORT.findall(barrel_path.read_text(encoding="utf-8"))


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
        out.append(ch)
        i += 1
    return "".join(out)


def declared_names(module_path: Path) -> list[str]:
    return DECLARATION.findall(strip_comments_and_strings(module_path.read_text(encoding="utf-8")))


def discover_contract_modules(types_dir: Path) -> list[str]:
    """Return the module stems under src/types/, excluding test files."""
    stems = []
    for path in sorted(types_dir.glob("*.ts")):
        if path.name.endswith(".test.ts") or path.name.endswith(".d.ts"):
            continue
        stems.append(path.stem)
    return stems


def evaluate(barrel_path: Path, types_dir: Path) -> tuple[list[str], dict[str, int]]:
    problems: list[str] = []
    modules = read_barrel_modules(barrel_path)

    declarations: dict[str, list[str]] = defaultdict(list)
    for module in modules:
        module_path = types_dir / f"{module}.ts"
        if not module_path.is_file():
            problems.append(f"src/types.ts re-exports './types/{module}', which does not exist")
            continue
        for name in declared_names(module_path):
            declarations[name].append(module)

    for name, owners in sorted(declarations.items()):
        if len(owners) > 1:
            problems.append(
                f"'{name}' is declared in {len(owners)} barrel modules ({', '.join(sorted(owners))}). "
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
        "duplicates": sum(1 for owners in declarations.values() if len(owners) > 1),
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
