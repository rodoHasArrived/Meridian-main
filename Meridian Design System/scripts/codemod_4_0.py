#!/usr/bin/env python3
"""codemod_4_0.py — mechanical consumer migration to Meridian 4.0.

Rewrites the changes that ARE mechanical and reports the ones that are not.
Dependency-free (Python 3.9+ stdlib), no network. Dry-run by default; pass
--write to apply. Companion prose: docs/UPGRADING.md § "→ 4.0".

Automatic rewrites
  1. Component renames (word-boundary, .js/.jsx/.ts/.tsx/.html):
       DenseDataTable | ExpandableDataTable | GroupedDataTable | FilteredDataTable → DataGrid
     Duplicate names inside one `const { … } = window.MeridianDesignSystem_…`
     destructure are deduped after renaming.
  2. Template path remaps (merged 3.x folders → 4.0 survivor + view param) —
     see TEMPLATE_REMAP below; applies to hrefs, window.location writes, docs.

Report-only flags (need a human)
  - Glyph* imports          → core/icons.js is deleted; inline the mark or use a Badge.
  - CONDITIONS              → read DataCondition.CONDITIONS off the bundle instead.
  - aggregateRows=          → went with GroupedDataTable; declare per-column `total`
                              in DataGrid columns (group headers subtotal automatically).
  - expandedContent= / renderExpanded= → DataGrid `expand` prop.
  - filters= on a table     → DataGrid has no filter UI; compose a Toolbar above it.

Usage
  python scripts/codemod_4_0.py <path> [<path> …] [--write]
"""
import argparse, pathlib, re, sys

RENAMES = {n: "DataGrid" for n in
           ("DenseDataTable", "ExpandableDataTable", "GroupedDataTable", "FilteredDataTable")}

TEMPLATE_REMAP = {
    "reconciliation-workstation/ReconciliationWorkstation.dc.html": "reconciliation/Reconciliation.dc.html",
    "reconciliation-exception/ReconciliationException.dc.html": "reconciliation/Reconciliation.dc.html?view=exception",
    "close-tie-out/CloseTieOut.dc.html": "close-management/CloseManagement.dc.html?view=tie-out",
    "strategy-onboarding/StrategyOnboarding.dc.html": "strategy-formation/StrategyFormation.dc.html",
    "strategy-builder/StrategyBuilder.dc.html": "strategy-formation/StrategyFormation.dc.html?view=builder",
    "backtest-builder/BacktestBuilder.dc.html": "strategy-runs/StrategyRuns.dc.html?view=backtest",
    "backtest-compare/BacktestCompare.dc.html": "strategy-runs/StrategyRuns.dc.html?view=compare",
    "covered-call-lab/CoveredCallLab.dc.html": "strategy-runs/StrategyRuns.dc.html?view=lab",
    "amx-governance/AmxGovernance.dc.html": "strategy-governance/StrategyGovernance.dc.html",
    "journaling-workstation/JournalingWorkstation.dc.html": "accounting-workstation/AccountingWorkstation.dc.html?view=gl&compose=1",
}

FLAGS = [
    (re.compile(r"\bGlyph(Check|Dash|X|ChevronDown|ChevronRight|Warn|Info)\b"),
     "core/icons.js is deleted — inline the mark (Checkbox-style) or use a Badge/SeverityBadge."),
    (re.compile(r"\bCONDITIONS\b"),
     "CONDITIONS is bundle-internal — read DataCondition.CONDITIONS instead."),
    (re.compile(r"\baggregateRows\s*[=:]"),
     "aggregateRows went with GroupedDataTable — declare per-column `total` in DataGrid columns."),
    (re.compile(r"\b(expandedContent|renderExpanded)\s*[=:]"),
     "expansion is DataGrid's `expand` prop now."),
    (re.compile(r"\bFilteredDataTable\b|\bfilters\s*=\s*\{"),
     "DataGrid ships no filter UI — compose a Toolbar (Input/Select) above the grid."),
]

EXTS = {".js", ".jsx", ".ts", ".tsx", ".html", ".md"}
DESTRUCTURE = re.compile(r"(const\s*\{)([^}]*)(\}\s*=\s*window\.MeridianDesignSystem_\w+)")
SKIP_DIRS = {"node_modules", ".git", "_ds"}


def dedupe_destructures(text):
    def fix(m):
        names, seen, out = m.group(2).split(","), set(), []
        for raw in names:
            key = raw.strip()
            if not key or key in seen:
                continue
            seen.add(key)
            out.append(raw if raw.strip() else raw)
        return m.group(1) + ", ".join(n.strip() for n in out) + m.group(3)
    return DESTRUCTURE.sub(fix, text)


def process(path, write):
    text = path.read_text(encoding="utf-8")
    original, notes = text, []
    for old, new in RENAMES.items():
        text, n = re.subn(r"\b%s\b" % old, new, text)
        if n:
            notes.append("  renamed %s → %s (%d)" % (old, new, n))
    if notes:
        text = dedupe_destructures(text)
    for old, new in TEMPLATE_REMAP.items():
        if old in text:
            text = text.replace(old, new)
            notes.append("  remapped template path %s → %s" % (old.split("/")[0], new))
    for lineno, line in enumerate(original.splitlines(), 1):
        for rx, msg in FLAGS:
            if rx.search(line):
                notes.append("  FLAG L%d: %s" % (lineno, msg))
                break
    if notes:
        print(path)
        print("\n".join(dict.fromkeys(notes)))  # de-dupe, keep order
    if text != original and write:
        path.write_text(text, encoding="utf-8")
        print("  written.")
    return text != original


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("paths", nargs="+")
    ap.add_argument("--write", action="store_true", help="apply changes (default: dry run)")
    args = ap.parse_args()
    changed = 0
    for root in args.paths:
        p = pathlib.Path(root)
        files = [p] if p.is_file() else [
            f for f in p.rglob("*")
            if f.suffix in EXTS and not any(part in SKIP_DIRS for part in f.parts)]
        for f in files:
            changed += process(f, args.write)
    print("\n%d file(s) %s." % (changed, "rewritten" if args.write else "would change (dry run — pass --write)"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
