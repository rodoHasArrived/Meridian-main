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
from dataclasses import dataclass
from urllib.parse import unquote, urlsplit

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
            for m in re.finditer(r"#[0-9A-Fa-f]{3,8}\b", line):
                before = line[: m.start()]
                if "var(--" in before and before.rstrip().endswith(","):
                    continue  # var(--token, #hex) fallback — allowed
                if re.search(r"var\(--[\w-]+\s*,\s*$", before) or "var(--" in line[max(0, m.start() - 48): m.start()]:
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

def declared_template_entries(root=None):
    """Read the authored entry contract, including retained compatibility names.

    The registry is authoritative; deriving PascalCase from a directory rejected
    a valid retained filename and silently skipped additional sibling entries.
    """
    root = ROOT if root is None else pathlib.Path(root)
    registry = root / "api" / "template-eligibility.json"
    try:
        templates = json.loads(registry.read_text(encoding="utf-8"))["templates"]
    except Exception as error:
        fail("template", f"entry registry unreadable: {error}")
        return []
    entries = []
    for slug, record in sorted(templates.items()):
        if pathlib.PurePosixPath(slug).name != slug or slug in (".", "..") or "\\" in slug:
            fail("template", f"invalid template directory {slug!r}")
            continue
        declared = record.get("entries", [])
        if not declared:
            fail("template", f"{slug}: no declared .dc.html entry")
        seen = set()
        for filename in declared:
            if not isinstance(filename, str) or pathlib.PurePosixPath(filename).name != filename or "\\" in filename or not filename.endswith(".dc.html"):
                fail("template", f"{slug}: invalid entry filename {filename!r}")
                continue
            if filename in seen:
                fail("template", f"{slug}: duplicate declared entry {filename}")
                continue
            seen.add(filename)
            entry = root / "templates" / slug / filename
            if not entry.is_file():
                fail("template", f"{slug}: declared entry {filename} missing")
            else:
                entries.append(entry)
        actual = {p.name for p in (root / "templates" / slug).glob("*.dc.html")}
        for filename in sorted(actual - seen):
            fail("template", f"{slug}: undeclared entry {filename}")
    return entries


def check_templates():
    for entry in declared_template_entries():
        folder, slug = entry.parent, entry.parent.name
        label = str(entry.relative_to(ROOT / "templates"))
        src = entry.read_text(encoding="utf-8")
        if "@template" not in src[:1200]:
            fail("template", f"{label}: @template marker not near the top")
        if not (folder / "ds-base.js").exists():
            fail("template", f"{label}: ds-base.js missing")
        if "$preview" not in src:
            fail("template", f"{label}: $preview missing from data-props")
        for tweak in ("density", "theme", "environment", "dataState"):
            if f"&quot;{tweak}&quot;" not in src and f'"{tweak}"' not in src:
                fail("template", f"{label}: standard tweak '{tweak}' missing")
        if "DataStateSwitch" not in src:
            fail("template", f"{label}: DataStateSwitch not used")
        if "<h1" not in src:
            warn("template", f"{label}: no literal <h1> (may live in an imported sibling)")
        for match in re.finditer(r'"(\.\./[\w-]+/[\w]+\.dc\.html)"', src):
            target = (folder / match.group(1)).resolve()
            if not target.exists():
                fail("routes", f"{label}: broken route {match.group(1)}")

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


@dataclass(frozen=True)
class Violation:
    """Structured result retained for the checker’s original Python consumers."""
    code: str
    message: str


def entry_markup(path, visited=None):
    """Include local imported screen markup without executing authored code."""
    visited = set() if visited is None else visited
    path = path.resolve()
    if path in visited or not path.is_file() or not path.is_relative_to(ROOT.resolve()):
        return ""
    visited.add(path)
    source = path.read_text(encoding="utf-8")
    imports = re.findall(r'<x-import\b[^>]*\bfrom=["\']([^"\']+)["\']', source)
    children = [path.parent / value for value in imports if value.startswith(".")]
    manifest = ROOT / "_ds_manifest.json"
    if manifest.is_file():
        exports = {item["name"]: item["sourcePath"] for item in json.loads(manifest.read_text())["components"]}
        for name in re.findall(r'component-from-global-scope=["\'][\w$]+\.([A-Z]\w*)["\']', source):
            if name in exports:
                children.append(ROOT / exports[name])
    for name in re.findall(r'<dc-import\b[^>]*\bname=["\']([\w-]+)["\']', source):
        local = path.parent / f"{name}.dc.html"
        children.extend([local] if local.is_file() else (ROOT / "templates").glob(f"*/{name}.dc.html"))
    return source + "\n" + "\n".join(entry_markup(child, visited) for child in children)


def check_document_contracts():
    """Retain link, upload, geometry and rendered-entry checks from the legacy API.

    These supplement the current release gates; they do not replace strict
    registry discovery or exempt a source because a fixture lacks a manifest.
    """
    for path in sorted(ROOT.rglob("*.html")):
        if any(part in {"node_modules", "dist"} for part in path.relative_to(ROOT).parts):
            continue
        source = path.read_text(encoding="utf-8")
        for _, value in re.findall(r'\b(href|src)=["\']([^"\']+)["\']', source):
            parsed = urlsplit(value)
            if parsed.scheme or parsed.netloc or not parsed.path or any(c in value for c in "{}$<>"):
                continue
            relative = unquote(parsed.path)
            if "uploads" in pathlib.PurePosixPath(relative).parts:
                fail("local-upload-reference", f"{path.relative_to(ROOT)} references local upload {value}")
            target = ROOT / relative.lstrip("/") if relative.startswith("/") else path.parent / relative
            if not target.exists():
                fail("local-link", f"{path.relative_to(ROOT)} references missing {value}")
        if not path.is_relative_to(ROOT / "templates") or path.name.endswith(".card.html"):
            continue
        if path.name != "index.html" and not path.name.endswith(".dc.html"):
            continue
        markup = entry_markup(path)
        if not re.search(r'<meta\b[^>]*name=["\']viewport["\']', source, re.I):
            fail("template-viewport", f"{path.relative_to(ROOT)} has no viewport declaration")
        if not re.search(r'<main\b|role=["\']main["\']', markup, re.I):
            fail("template-main-landmark", f"{path.relative_to(ROOT)} has no main landmark in entry/imported screen")
        if not re.search(r'<h1\b', markup, re.I):
            fail("template-heading", f"{path.relative_to(ROOT)} has no h1 in entry/imported screen")
        if re.search(r'<h1\b[^>]*>\s*(?:Governance|Research|Execution)\s*</h1>', markup, re.I):
            fail("legacy-workspace", f"{path.relative_to(ROOT)} uses a superseded root workspace name")
        for number, line in enumerate(blank_comments(source), 1):
            for match in re.finditer(r"#[0-9A-Fa-f]{3,8}\b", line):
                before = line[max(0, match.start() - 65):match.start()]
                if "var(--" not in before and "url(" not in line:
                    fail("raw-hex", f"{path.relative_to(ROOT)}:{number} bare {match.group(0)}")
    for path in sorted(ROOT.glob("components/*/*.jsx")):
        for number, line in enumerate(blank_comments(path.read_text(encoding="utf-8")), 1):
            for match in re.finditer(r"border-radius\s*:\s*(\d+(?:\.\d+)?)px", line):
                if float(match.group(1)) > 8:
                    fail("large-radius", f"{path.relative_to(ROOT)}:{number} radius {match.group(1)}px exceeds 8px")


def run_checks(root=ROOT, baseline=None):
    """Pure structured API; a passed baseline cannot waive current enforcement.

    Small fixture trees omit package release metadata. A real package with a
    public manifest receives every release gate in addition to document checks.
    Global CLI state is restored even when malformed input raises an exception.
    """
    global ROOT, fails, warns
    previous = ROOT, fails, warns
    ROOT, fails, warns = pathlib.Path(root), [], []
    try:
        check_components()
        if (ROOT / "_ds_manifest.json").exists():
            check_manifest(); check_templates(); check_eligibility(); check_generated()
        check_document_contracts()
        violations = []
        for message in fails:
            code = message[1:message.index("]")]
            if code == "trio" and ".prompt.md" in message:
                code = "prompt-coverage"
            violations.append(Violation(code, message))
        return violations
    finally:
        ROOT, fails, warns = previous

def main():
    check_components(); check_manifest(); check_templates(); check_eligibility(); check_generated()
    check_document_contracts()
    for w in warns: print("WARN  " + w)
    for f in fails: print("FAIL  " + f)
    print(f"\n{len(fails)} failures · {len(warns)} warnings")
    return 1 if fails else 0

if __name__ == "__main__":
    sys.exit(main())
