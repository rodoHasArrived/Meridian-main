#!/usr/bin/env python3
"""Meridian — template eligibility gate (stdlib only, offline).

`api/template-eligibility.json` is HAND-AUTHORED: whether a screen is eligible for a primitive is a
design judgment and cannot be generated. What is generated is everything downstream of it — the
adoption block in docs/METRICS.md — and everything checkable about it:

Gates:
  coverage    every templates/<slug>/ is classified; no phantom slug in the manifest
  complete    every template classifies all N primitives exactly once (eligible XOR exempt)
  reason      every exemption cites a declared reason code; every code is used
  entries     manifest entries match the .dc.html files on disk
  scan        recomputed reference counts match the manifest
  stray       no template references a primitive it is declared exempt from
  arithmetic  adoption totals recompute from the per-template rows

Usage:
  python3 scripts/check_template_eligibility.py            # check, exit 1 on failure
  python3 scripts/check_template_eligibility.py --write     # also regenerate the METRICS block
"""
import json, re, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
MANIFEST = ROOT / "api" / "template-eligibility.json"
METRICS = ROOT / "docs" / "METRICS.md"
BEGIN, END = "<!-- adoption:generated:begin -->", "<!-- adoption:generated:end -->"

# Mirrors the detection used when the manifest was authored. A primitive is "referenced" if the
# template's own .dc.html source names it — a provider imported and never mounted would read as
# adoption, which is why the stray/scan gates exist rather than a bare grep count.
RX = {
    "ctx": re.compile(r"WorkspaceContext(?:Bar|Provider)"),
    "sel": re.compile(r"LinkedSelection|useLinkedSelection"),
    "tim": re.compile(r"TimeContextProvider"),
    "obj": re.compile(r"ObjectHeader"),
    "dec": re.compile(r"DecisionFlowProvider"),
    "grd": re.compile(r"ActionGuardList"),
    "con": re.compile(r"ContinuityFrame|useContinuity"),
    "fig": re.compile(r"AnalyticalFigure"),
    "dat": re.compile(r"\bDatum\b"),
    "cap": re.compile(r"\bCapsule\b"),
}

fails = []
def fail(gate, msg): fails.append(f"[{gate}] {msg}")

def scan(slug):
    folder = ROOT / "templates" / slug
    entries = sorted(p.name for p in folder.glob("*.dc.html"))
    src = "".join((folder / e).read_text(encoding="utf-8", errors="replace") for e in entries)
    refs = {}
    for k, rx in RX.items():
        n = len(rx.findall(src))
        if n:
            refs[k] = n
    return entries, refs

def main():
    if not MANIFEST.exists():
        print("FAIL  [coverage] api/template-eligibility.json missing")
        return 1
    mf = json.loads(MANIFEST.read_text(encoding="utf-8"))
    prims = list(mf["primitives"])
    reasons = mf["exemptionReasons"]
    tpls = mf["templates"]

    on_disk = sorted(
        p.name for p in (ROOT / "templates").iterdir()
        if p.is_dir() and not p.name.startswith("_")
    )
    for slug in on_disk:
        if slug not in tpls:
            fail("coverage", f"{slug} is not classified — a new template must be classified before it ships")
    for slug in tpls:
        if slug not in on_disk:
            fail("coverage", f"manifest classifies {slug}, which is not a template folder")

    used_reasons = set()
    for slug in sorted(set(on_disk) & set(tpls)):
        t = tpls[slug]
        elig, exempt = list(t["eligible"]), dict(t["exempt"])
        both = set(elig) & set(exempt)
        if both:
            fail("complete", f"{slug} claims {sorted(both)} as both eligible and exempt")
        missing = [p for p in prims if p not in elig and p not in exempt]
        if missing:
            fail("complete", f"{slug} leaves {missing} unclassified")
        unknown = [p for p in elig + list(exempt) if p not in prims]
        if unknown:
            fail("complete", f"{slug} classifies unknown primitive(s) {unknown}")
        for p, code in exempt.items():
            if code not in reasons:
                fail("reason", f"{slug}/{p} cites undeclared reason '{code}'")
            else:
                used_reasons.add(code)

        entries, refs = scan(slug)
        if entries != list(t["entries"]):
            fail("entries", f"{slug} entries {t['entries']} != on disk {entries}")
        if refs != dict(t["references"]):
            fail("scan", f"{slug} references {t['references']} != scanned {refs}")
        strays = sorted(k for k in refs if k in exempt)
        if strays:
            fail("stray", f"{slug} uses {strays} while declaring them exempt — fix the screen or the classification")
        conf = sorted(k for k in elig if refs.get(k))
        if conf != sorted(t["conforming"]):
            fail("arithmetic", f"{slug} conforming {t['conforming']} != recomputed {conf}")

    for code in reasons:
        if code not in used_reasons:
            fail("reason", f"reason code '{code}' is declared but unused — delete it or use it")

    rows, tot_e, tot_c = {}, 0, 0
    for p in prims:
        e = [s for s in tpls if p in tpls[s]["eligible"]]
        c = [s for s in e if p in tpls[s]["conforming"]]
        rows[p] = (len(e), len(c))
        tot_e += len(e); tot_c += len(c)
        declared = mf["adoption"][p]
        if (declared["eligible"], declared["conforming"]) != rows[p]:
            fail("arithmetic", f"adoption.{p} declares {declared['eligible']}/{declared['conforming']}, recomputes {len(e)}/{len(c)}")
    if (mf["totals"]["eligibleCells"], mf["totals"]["conformingCells"]) != (tot_e, tot_c):
        fail("arithmetic", f"totals declare {mf['totals']['eligibleCells']}/{mf['totals']['conformingCells']}, recompute {tot_e}/{tot_c}")

    if "--write" in sys.argv and not fails:
        write_metrics(mf, prims, rows, tot_e, tot_c)

    for f in fails:
        print("FAIL  " + f)
    pct = round(tot_c / tot_e * 100, 1) if tot_e else 0.0
    print(f"{'FAIL' if fails else 'OK'}  template eligibility — {len(tpls)} templates, "
          f"{tot_e} eligible cells of {len(tpls) * len(prims)}, {tot_c} conforming ({pct}%)")
    return 1 if fails else 0

def write_metrics(mf, prims, rows, tot_e, tot_c):
    tpls = mf["templates"]
    pct = round(tot_c / tot_e * 100, 1)
    zero = [s for s in sorted(tpls) if not tpls[s]["eligible"]]
    worst = [s for s in tpls if tpls[s]["eligible"] and not tpls[s]["conforming"]]
    entries = sum(len(tpls[s]["entries"]) for s in tpls)
    body = ["| Primitive | Eligible | Conforming | Adoption |", "|---|---:|---:|---:|"]
    for p in prims:
        e, c = rows[p]
        name = mf["primitives"][p].split(" / ")[0]
        share = f"{round(c / e * 100, 1)}%" if e else "—"
        body.append(f"| `{name}` | {e} | {c} | {share} |")
    body.append(f"| **total** | **{tot_e}** | **{tot_c}** | **{pct}%** |")
    cap_e, cap_c = rows["cap"]
    block = f"""{BEGIN}
Generated by `scripts/check_template_eligibility.py` from `api/template-eligibility.json`, which
classifies all **{len(tpls)} templates** ({entries} `.dc.html` entries) against the {len(prims)} operating-language
primitives in `guidelines/TEMPLATE_CONTRACT.md`. Adoption is **eligible templates conforming ÷
templates eligible**. Requiring every primitive on every template would be consistency theater:
{len(tpls) * len(prims)} cells exist, only **{tot_e}** are eligible, and {len(zero)} templates ({', '.join(zero)}) are eligible for none.

{chr(10).join(body)}

**{tot_c} of {tot_e} eligible cells conform — {pct}%.** {len(worst)} of the {len(tpls) - len(zero)} templates with any eligibility
conform on nothing yet; migration is real work, not a rounding error. The four partially migrated
templates are `alm-review` ({len(tpls['alm-review']['conforming'])}/{len(tpls['alm-review']['eligible'])}), `strategy-formation` ({len(tpls['strategy-formation']['conforming'])}/{len(tpls['strategy-formation']['eligible'])}), `close-management` ({len(tpls['close-management']['conforming'])}/{len(tpls['close-management']['eligible'])}) and
`reconciliation` ({len(tpls['reconciliation']['conforming'])}/{len(tpls['reconciliation']['eligible'])}).

Two figures deliberately absent from this table: a target, and a trend. A percentage is a diagnosis
of where local state still lives, not a number to move. `Capsule` reads {round(cap_c / cap_e * 100, 1) if cap_e else 0}% on {cap_e} eligible
templates because its use condition requires working move/resize affordances — the restrictive rule
in the contract is the intended answer, not a gap.
{END}"""
    text = METRICS.read_text(encoding="utf-8")
    i, j = text.index(BEGIN), text.index(END) + len(END)
    METRICS.write_text(text[:i] + block + text[j:], encoding="utf-8")

if __name__ == "__main__":
    sys.exit(main())
