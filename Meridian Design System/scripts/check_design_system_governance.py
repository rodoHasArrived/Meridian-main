#!/usr/bin/env python3
"""Meridian — static design-system governance checks (restored 3.5, stdlib only, offline).

Gates (all static; no network, no browser):
  trio            every components/<domain>/<Name>.jsx has <Name>.d.ts and <Name>.prompt.md
  raw-hex         no bare hex colors in component source outside var(--token, #hex) fallbacks
  no-important    no !important in component source
  manifest        every manifest component's sourcePath exists; every PascalCase jsx pairs into it
  template        template contract — entry name, @template first comment, $preview + the four
                  standard tweaks, DataStateSwitch, h1, ds-base.js sibling
  routes          every ../<slug>/<File>.dc.html referenced by a template resolves to a file
  aria-refs       static aria-controls/aria-labelledby/aria-activedescendant values must be
                  computed (template literal / expression), never a hard-coded string id
  eligibility     every templates/<slug>/ is classified in api/template-eligibility.json
  generated       generated files present and never hand-edited markers intact

  python scripts/check_design_system_governance.py           # report; exit 1 on failures
"""
import json, re, sys, pathlib

ROOT = pathlib.Path(__file__).resolve().parent.parent
fails, warns = [], []

def fail(gate, msg): fails.append(f"[{gate}] {msg}")
def warn(gate, msg): warns.append(f"[{gate}] {msg}")

# A declaration that must beat an inline style or a universal descendant rule cannot be expressed
# by specificity, so `!important` is governed rather than forbidden: the declaration carries a
# `/* important: <reason> */` pragma and the gate reads it. Everything else wins by order.
IMPORTANT_PRAGMA = re.compile(r"/\*\s*important:\s*\S")

def blank_comments(src):
    """Blank // and /* */ comments, preserving line count and column-free content.

    Prose is not a style: `no !important needed` inside a comment was a false gate failure.
    """
    out, in_block = [], False
    for raw in src.splitlines():
        line, kept = raw, ""
        while line:
            if in_block:
                end = line.find("*/")
                if end < 0:
                    line = ""
                    break
                line, in_block = line[end + 2:], False
                continue
            start, slash = line.find("/*"), line.find("//")
            if slash >= 0 and (start < 0 or slash < start):
                kept += line[:slash]
                break
            if start < 0:
                kept += line
                break
            kept += line[:start]
            line, in_block = line[start + 2:], True
        out.append(kept)
    return out

def check_components():
    for jsx in sorted(ROOT.glob("components/*/*.jsx")):
        name = jsx.stem
        if not name[0].isupper():
            continue
        if not jsx.with_suffix(".d.ts").exists():
            fail("trio", f"{jsx.relative_to(ROOT)} has no {name}.d.ts")
        if not jsx.with_name(name + ".prompt.md").exists():
            fail("trio", f"{jsx.relative_to(ROOT)} has no {name}.prompt.md")
        src = jsx.read_text(encoding="utf-8")
        code = blank_comments(src)
        for i, line in enumerate(src.splitlines(), 1):
            if "!important" in code[i - 1] and not IMPORTANT_PRAGMA.search(line):
                fail("no-important", f"{jsx.relative_to(ROOT)}:{i} undeclared !important — "
                                     "state why with /* important: <reason> */ or win by order")
            # Scan the comment-blanked line, as the !important check does: a palette value
            # quoted in a `//` header comment is prose, not a style, and three shell
            # components failed this gate on their own descriptions.
            scanned = code[i - 1]
            for m in re.finditer(r"#[0-9A-Fa-f]{3,8}\b", scanned):
                before = scanned[: m.start()]
                if "var(--" in before and before.rstrip().endswith(","):
                    continue  # var(--token, #hex) fallback — allowed
                if re.search(r"var\(--[\w-]+\s*,\s*$", before) or "var(--" in scanned[max(0, m.start() - 48): m.start()]:
                    continue
                if "svg+xml" in line or "url(" in line:
                    continue
                fail("raw-hex", f"{jsx.relative_to(ROOT)}:{i} bare {m.group(0)}")
        for m in re.finditer(r'aria-(controls|labelledby|activedescendant)="([^"{]+)"', src):
            fail("aria-refs", f"{jsx.relative_to(ROOT)} hard-coded {m.group(0)}")

def check_manifest():
    mf = ROOT / "_ds_manifest.json"
    if not mf.exists():
        fail("generated", "_ds_manifest.json missing — compiler has not run")
        return
    manifest = json.loads(mf.read_text(encoding="utf-8"))
    names = set()
    for comp in manifest["components"]:
        names.add(comp["name"])
        src = comp.get("sourcePath")
        if src and not (ROOT / src).exists():
            fail("manifest", f"manifest references missing {src}")
    for jsx in ROOT.glob("components/*/*.jsx"):
        if jsx.stem[0].isupper():
            exported = re.findall(r"export (?:const|function) ([A-Z]\w+)", jsx.read_text(encoding="utf-8"))
            for e in exported:
                if e not in names:
                    warn("manifest", f"{jsx.relative_to(ROOT)} export {e} not in manifest (stale — recompiles at end of turn)")

def check_templates():
    for folder in sorted(p for p in (ROOT / "templates").iterdir() if p.is_dir() and not p.name.startswith("_")):
        slug = folder.name
        pascal = "".join(w.capitalize() for w in slug.split("-"))
        entry = folder / f"{pascal}.dc.html"
        if not entry.exists():
            fail("template", f"{slug}: entry {pascal}.dc.html missing")
            continue
        src = entry.read_text(encoding="utf-8")
        if "<!-- @template " not in src.split("\n", 12)[0] and "@template" not in src[:1200]:
            fail("template", f"{slug}: @template marker not near the top")
        if not (folder / "ds-base.js").exists():
            fail("template", f"{slug}: ds-base.js missing")
        if "$preview" not in src:
            fail("template", f"{slug}: $preview missing from data-props")
        for tweak in ("density", "theme", "environment", "dataState"):
            if f"&quot;{tweak}&quot;" not in src and f'"{tweak}"' not in src:
                fail("template", f"{slug}: standard tweak '{tweak}' missing")
        if "DataStateSwitch" not in src:
            fail("template", f"{slug}: DataStateSwitch not used")
        if "<h1" not in src:
            warn("template", f"{slug}: no literal <h1> (may live in an imported sibling)")
        for m in re.finditer(r'"(\.\./[\w-]+/[\w]+\.dc\.html)"', src):
            target = (folder / m.group(1)).resolve()
            if not target.exists():
                fail("routes", f"{slug}: broken route {m.group(1)}")

def check_eligibility():
    """A new template must be classified before it ships. Full validation (completeness, reason
    codes, reference scan, adoption arithmetic) lives in scripts/check_template_eligibility.py;
    this gate only catches the omission that would silently corrupt the adoption figure."""
    mf = ROOT / "api" / "template-eligibility.json"
    if not mf.exists():
        fail("eligibility", "api/template-eligibility.json missing")
        return
    try:
        classified = set(json.loads(mf.read_text(encoding="utf-8"))["templates"])
    except Exception as e:
        fail("eligibility", f"api/template-eligibility.json unreadable: {e}")
        return
    on_disk = {p.name for p in (ROOT / "templates").iterdir() if p.is_dir() and not p.name.startswith("_")}
    for slug in sorted(on_disk - classified):
        fail("eligibility", f"{slug} is not classified in api/template-eligibility.json — "
                            "classify it before it ships or the adoption figure is a lie")
    for slug in sorted(classified - on_disk):
        fail("eligibility", f"api/template-eligibility.json classifies {slug}, which no longer exists")

def check_generated():
    for f in ("_ds_bundle.js", "_ds_manifest.json", "_adherence.oxlintrc.json", "support.js"):
        if not (ROOT / f).exists():
            fail("generated", f"{f} missing")
    readme = (ROOT / "README.md").read_text(encoding="utf-8")
    if "inventory:generated:begin" not in readme:
        fail("generated", "README.md inventory markers missing")

def main():
    check_components(); check_manifest(); check_templates(); check_eligibility(); check_generated()
    for w in warns: print("WARN  " + w)
    for f in fails: print("FAIL  " + f)
    print(f"\n{len(fails)} failures · {len(warns)} warnings")
    return 1 if fails else 0

if __name__ == "__main__":
    sys.exit(main())
