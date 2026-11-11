#!/usr/bin/env python3
"""Meridian — public family record (stdlib only, offline).

The nine public families are the API contract; the physical folders under `components/` are an
implementation detail (`core` at 105 exports is precisely why the split exists). This script is the
one place that assignment is computed, so a family count can never disagree with the manifest again:
`docs/METRICS.md` claimed nine families covering all 303 exports while its own table summed to 284,
and nothing failed.

  python scripts/generate_families.py            # print a summary
  python scripts/generate_families.py --write     # (re)write api/families.json
  python scripts/generate_families.py --check     # exit 1 on drift, unclassified or phantom exports

Sources of truth, in order:
  _ds_manifest.json   which symbols exist on the namespace (the census)
  api/<family>.js     which family each symbol belongs to (the public contract)
  STABILITY (below)   the maturity register — stable / release-candidate / deprecated
"""
import hashlib, json, re, sys, pathlib

ROOT = pathlib.Path(__file__).resolve().parent.parent
OUT = ROOT / "api" / "families.json"
NAMES_RE = re.compile(r"var NAMES = \[([^\]]*)\];")
SRC_RE = re.compile(r"^components/(\w+)/([\w.]+)\.(?:jsx|tsx|js)$")

# Maturity register. Anything absent is `stable`. A release-candidate export is public and
# supported but may still change shape inside a minor — the Salt core/lab distinction, declared
# here rather than implied by how new a file looks.
STABILITY = {
    "EvidenceRail": "release-candidate",
    "EVIDENCE_SECTIONS": "release-candidate",
    "ConsequenceRail": "release-candidate",
    "CONSEQUENCE_KINDS": "release-candidate",
    "DecisionSnapshot": "release-candidate",
    "DecisionSnapshotPanel": "release-candidate",
    "SnapshotHooks": "release-candidate",
    # 4.3 — new in this line, shape may still move inside the minor
    "Assurance": "release-candidate",
    "ASSURANCE_STAGES": "release-candidate",
    "AssuranceChip": "release-candidate",
    "CertificationMatrix": "release-candidate",
    "LocalDate": "release-candidate",
    "LocalDateText": "release-candidate",
    "OptimisticCell": "release-candidate",
    "OptimisticProvider": "release-candidate",
    "OptimisticRowBar": "release-candidate",
    "OptimisticHooks": "release-candidate",
    "SelectionCompare": "release-candidate",
    "SelectionCompareProvider": "release-candidate",
    "SelectionCompareHooks": "release-candidate",
    "CompareToggle": "release-candidate",
    "CompareTray": "release-candidate",
    "CompareSheet": "release-candidate",
    "AnnotationLayer": "release-candidate",
    "AnnotationProvider": "release-candidate",
    "AnnotationAnchor": "release-candidate",
    "AnnotationPanel": "release-candidate",
    "AnnotationHooks": "release-candidate",
}

NOTE = ("The public contract. Physical folders under components/ are an implementation detail; this "
        "file is the family assignment every barrel, doc count and future ESM subpath is generated "
        "from. Never hand-edit.")

def load():
    manifest = json.loads((ROOT / "_ds_manifest.json").read_text(encoding="utf-8"))
    by_source = {c["name"]: c.get("sourcePath", "") for c in manifest["components"]}
    families = {}
    for path in sorted((ROOT / "api").glob("*.js")):
        if path.name == "legacy.js":
            continue
        m = NAMES_RE.search(path.read_text(encoding="utf-8"))
        if not m:
            continue
        names = [n.strip().strip('"') for n in m.group(1).split(",") if n.strip()]
        families[path.stem] = names
    return manifest, by_source, families

def build(manifest, by_source, families):
    owner, multi = {}, []
    for fam in sorted(families):
        for name in families[fam]:
            if name in owner:
                multi.append(f"{name} ({owner[name]} + {fam})")
            else:
                owner[name] = fam
    known = sorted(by_source, key=str)
    phantom = sorted(n for n in owner if n not in by_source)
    rows = []
    for name in known:
        src = by_source[name]
        m = SRC_RE.match(src)
        rows.append({
            "name": name,
            "family": owner.get(name, "unclassified"),
            "domain": m.group(1) if m else None,
            "module": m.group(2) if m else None,
            "source": src,
            "stability": STABILITY.get(name, "stable"),
        })
    unclassified = [r["name"] for r in rows if r["family"] == "unclassified"]
    counts = {}
    for r in rows:
        counts[r["family"]] = counts.get(r["family"], 0) + 1
    record = {
        "generated": "scripts/generate_families.py",
        "note": NOTE,
        "namespace": manifest.get("namespace"),
        "totals": {
            "exports": len(rows),
            "families": len(families),
            "unclassified": len(unclassified),
            "releaseCandidate": sum(1 for r in rows if r["stability"] == "release-candidate"),
        },
        "counts": {k: counts[k] for k in sorted(counts)},
        "unclassified": unclassified,
        "exports": rows,
    }
    return record, phantom, multi

def main():
    manifest, by_source, families = load()
    record, phantom, multi = build(manifest, by_source, families)
    text = json.dumps(record, indent=2, ensure_ascii=False) + "\n"
    problems = []
    if record["unclassified"]:
        problems.append(f"{len(record['unclassified'])} export(s) in no family: " + ", ".join(record["unclassified"][:8]))
    if phantom:
        problems.append(f"{len(phantom)} name(s) in a barrel but not on the namespace: " + ", ".join(phantom[:8]))
    if multi:
        problems.append("assigned to two families: " + ", ".join(multi[:8]))

    if "--write" in sys.argv:
        OUT.write_text(text, encoding="utf-8")
        print(f"wrote {OUT.relative_to(ROOT)} · {record['totals']['exports']} exports across "
              f"{record['totals']['families']} families")
        for p in problems:
            print("  ! " + p, file=sys.stderr)
        return 1 if problems else 0
    if "--check" in sys.argv:
        for p in problems:
            print("FAIL: " + p, file=sys.stderr)
        if not OUT.exists() or OUT.read_text(encoding="utf-8") != text:
            print("DRIFT: api/families.json — run with --write", file=sys.stderr)
            return 1
        if problems:
            return 1
        print(f"families in sync · {record['totals']['exports']} exports, 0 unclassified")
        return 0
    digest = hashlib.sha256(json.dumps(record["counts"], sort_keys=True).encode()).hexdigest()[:12]
    print(json.dumps(record["totals"], indent=2))
    print(json.dumps(record["counts"], indent=2))
    print("counts digest " + digest)
    for p in problems:
        print("  ! " + p)
    return 0

if __name__ == "__main__":
    sys.exit(main())
