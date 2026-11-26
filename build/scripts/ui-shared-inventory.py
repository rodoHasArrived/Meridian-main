#!/usr/bin/env python3
"""Dependency inventory for the Ui.Shared decomposition plan.

Classifies every C# file under src/Meridian.Ui.Shared/Services and Evidence into the
clusters proposed in docs/engineering/plans/ui-shared-decomposition-plan.md, then reports:

- size per cluster,
- cross-cluster type references (which moves must happen together or in order),
- references from movable code back into the adapter layer (Endpoints/, DtoExtensions, ...),
- internal types referenced across a cluster boundary,
- external assemblies that grant InternalsVisibleTo("Meridian.Ui.Shared").

It is a heuristic text scan (declared type names matched as whole words), good enough to
plan PR batches; the compiler remains the authority. Usage:

    python3 build/scripts/ui-shared-inventory.py [--output PATH]
"""

from __future__ import annotations

import argparse
import re
from collections import Counter, defaultdict
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
ROOT = REPO / "src" / "Meridian.Ui.Shared"

# Ordered: first matching prefix wins. Mirrors the plan's cluster table.
CLUSTERS: list[tuple[str, tuple[str, ...]]] = [
    ("Reporting", ("ReportPack", "Reporting", "Report", "Statement", "CapitalCall")),
    ("Accounting", ("Accounting", "Ledger", "PostedLedger", "TrialBalance", "Fund", "Reconcil",
        "ManualJournal", "AutomatedJournal", "DailyValuation", "FinancialRecord")),
    ("SecurityMaster", ("Security",)),
    (
        "Operations",
        (
            "Provider", "Brokerage", "Alpaca", "Backfill", "LeanAutoExport", "LeanSymbol",
            "Trading", "Risk", "Strategy", "Execution", "Portfolio", "Bond", "Backtest",
            "CoveredCall", "AggregatePortfolio",
        ),
    ),
]

ADAPTER_DIRS = ("Endpoints", "Serialization", "Streaming", "Contracts")
ADAPTER_ROOT_FILES = ("DtoExtensions.cs", "HtmlTemplateGenerator")

DECL = re.compile(
    r"^\s*(?P<mods>(?:public|internal|private|protected|sealed|static|abstract|partial|readonly|file|\s)+)?"
    r"(?:class|record(?:\s+struct)?|struct|interface|enum)\s+(?P<name>[A-Z][A-Za-z0-9_]*)",
    re.MULTILINE,
)
COMMENT = re.compile(r"//[^\n]*|/\*.*?\*/", re.DOTALL)


def cluster_of(path: Path) -> str:
    rel = path.relative_to(ROOT)
    top = rel.parts[0]
    if top == "Evidence":
        return "Evidence"
    if top in ADAPTER_DIRS or rel.name.startswith(ADAPTER_ROOT_FILES):
        return "Adapter"
    if top != "Services":
        return "Other"  # Workflows/, Extensibility/, remaining root files
    name = rel.name
    for cluster, prefixes in CLUSTERS:
        if name.startswith(prefixes):
            return cluster
    return "Workstation"  # shell/read-model services with no domain prefix


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--output", type=Path, help="write markdown here instead of stdout")
    args = parser.parse_args()

    files = sorted(p for p in ROOT.rglob("*.cs") if "/bin/" not in p.as_posix() and "/obj/" not in p.as_posix())
    text: dict[Path, str] = {p: COMMENT.sub("", p.read_text(encoding="utf-8", errors="ignore")) for p in files}
    cluster = {p: cluster_of(p) for p in files}

    owner: dict[str, str] = {}
    internal: set[str] = set()
    for p, t in text.items():
        for m in DECL.finditer(t):
            name = m.group("name")
            owner.setdefault(name, cluster[p])
            mods = m.group("mods") or ""
            if "public" not in mods:
                internal.add(name)

    sizes: Counter[str] = Counter()
    lines: Counter[str] = Counter()
    edges: Counter[tuple[str, str]] = Counter()
    back_refs: dict[str, set[str]] = defaultdict(set)
    internal_cross: dict[str, set[str]] = defaultdict(set)
    word = re.compile(r"\b[A-Z][A-Za-z0-9_]*\b")

    for p, t in text.items():
        src = cluster[p]
        sizes[src] += 1
        lines[src] += t.count("\n")
        used = {w for w in word.findall(t) if w in owner}
        for w in used:
            dst = owner[w]
            if dst == src:
                continue
            edges[(src, dst)] += 1
            if dst == "Adapter" and src not in ("Adapter", "Other"):
                back_refs[str(p.relative_to(REPO))].add(w)
            if w in internal and src not in ("Adapter",):
                internal_cross[w].add(src)

    ivt = sorted(
        str(p.relative_to(REPO))
        for p in (REPO / "src").rglob("*.cs")
        if "/obj/" not in p.as_posix()
        and 'InternalsVisibleTo("Meridian.Ui.Shared")' in p.read_text(encoding="utf-8", errors="ignore")
    )

    out: list[str] = []
    out.append("| Cluster | Files | Lines |\n| --- | ---: | ---: |")
    for c in sorted(sizes, key=lambda k: -lines[k]):
        out.append(f"| {c} | {sizes[c]} | {lines[c]} |")
    out.append("\n### Cross-cluster references (distinct type uses, source -> target)\n")
    out.append("| Source | Target | Type uses |\n| --- | --- | ---: |")
    for (s, d), n in sorted(edges.items(), key=lambda kv: -kv[1]):
        if s != "Adapter":
            out.append(f"| {s} | {d} | {n} |")
    out.append(f"\n### Movable files that reference the adapter layer ({len(back_refs)})\n")
    out.append("These must have the adapter dependency moved down before the file can leave Ui.Shared.\n")
    for f in sorted(back_refs):
        out.append(f"- `{f}`: {', '.join(sorted(back_refs[f]))}")
    multi = {k: v for k, v in internal_cross.items() if len(v) > 0}
    out.append(f"\n### Internal types used outside their own cluster ({len(multi)})\n")
    for k in sorted(multi):
        out.append(f"- `{k}` ({owner[k]}) used by: {', '.join(sorted(multi[k]))}")
    out.append("\n### Assemblies granting InternalsVisibleTo(\"Meridian.Ui.Shared\")\n")
    out.extend(f"- `{f}`" for f in ivt) if ivt else out.append("- none")

    report = "\n".join(out) + "\n"
    if args.output:
        args.output.write_text(report, encoding="utf-8")
    else:
        print(report, end="")


if __name__ == "__main__":
    main()
