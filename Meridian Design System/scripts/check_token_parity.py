#!/usr/bin/env python3
"""Meridian — token parity gate (stdlib only, offline).

tokens/parity-manifest.json has claimed since 3.1 that this script exists. It didn't.
This is it, scoped to what is actually shared with the upstream app:

  1 NAMES   Every name in aliasContract + workingNameContract resolves in the CSS
            closure (tokens/*.css). The upstream console authors CSS against these
            names; a missing one means pasted app CSS silently resolves to nothing.
  2 ALIASES Every contract name is a var() reference, not a duplicate hex — an alias
            that hardcodes a value can drift from the token it mirrors.
  3 VALUES  Every entry in `tokens` matches tokens/*.css: the light value in the
            default theme graph, the dark value in BOTH dark blocks of
            colors-dark.css, and the light value re-asserted in the forced-light block.

VALUES are deliberately not compared against the upstream app: parity-manifest.json
declares valuesShared:false — the dashboard moved to its own console palette. Only the
name surface is shared, and only names 1-2 speak to it.

  python scripts/check_token_parity.py          # gate
  python scripts/check_token_parity.py --names  # names only, skip the value pass
"""
import json, pathlib, re, sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
MANIFEST = ROOT / "tokens" / "parity-manifest.json"
CLOSURE = ["colors.css", "theme.css", "colors-dark.css", "visualization.css",
           "base.css", "elevation.css", "typography.css", "fonts.css",
           "print.css", "contrast-modes.css"]

DECL = re.compile(r"(?:^|[;{])\s*(--[a-z][a-z0-9-]*)\s*:\s*([^;}]+)", re.M)
PLAIN_ROOT = re.compile(r"(?:^|\})\s*(?::root|\.meridian-root)\s*\{", re.M)


def norm(value):
    v = " ".join(value.split()).lower().rstrip(";")
    v = re.sub(r"/\*.*?\*/", "", v).strip()
    m = re.fullmatch(r"#([0-9a-f])([0-9a-f])([0-9a-f])", v)
    if m:
        return "#" + "".join(c * 2 for c in m.groups())
    return v


def strip_comments(text):
    return re.sub(r"/\*.*?\*/", " ", text, flags=re.S)


def blocks(text):
    """Yield (selector, body) for every top-level-ish rule block, comments stripped."""
    text = strip_comments(text)
    out, depth, start, sel_start = [], 0, None, 0
    for i, ch in enumerate(text):
        if ch == "{":
            depth += 1
            if depth == 1:
                out.append([text[sel_start:i].strip(), i + 1, None])
                start = i + 1
        elif ch == "}":
            if depth == 1 and out:
                out[-1][2] = i
                sel_start = i + 1
            depth = max(0, depth - 1)
    return [(s, text[a:b]) for s, a, b in out if b is not None]


def declared(body):
    return {m.group(1): norm(m.group(2)) for m in DECL.finditer(body)}


def light_graph(files):
    """Default theme graph: plain :root blocks of colors.css then theme.css, in order."""
    graph = {}
    for name in ("colors.css", "theme.css"):
        text = files.get(name, "")
        for sel, body in blocks(text):
            if sel in (":root", ".meridian-root", ":root, .meridian-root"):
                graph.update(declared(body))
        # media-wrapped or attribute-scoped blocks are other branches of the graph
    return graph


def dark_blocks(text):
    """The three declared blocks of colors-dark.css, by selector shape."""
    found = {"auto": {}, "manual": {}, "forced-light": {}}
    text = strip_comments(text)
    # 1. @media (prefers-color-scheme: dark) { :root { ... } }
    m = re.search(r"@media[^{]*prefers-color-scheme:\s*dark[^{]*\{(.*)\n\}", text, re.S)
    if m:
        for sel, body in blocks(m.group(1)):
            if ":root" in sel:
                found["auto"].update(declared(body))
    for sel, body in blocks(text):
        s = sel.replace(" ", "")
        if 'data-theme="dark"' in s:
            found["manual"].update(declared(body))
        elif 'data-theme="light"' in s:
            found["forced-light"].update(declared(body))
    return found


def main():
    names_only = "--names" in sys.argv
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    files = {}
    for name in CLOSURE:
        path = ROOT / "tokens" / name
        if not path.exists():
            print(f"MISSING FILE  tokens/{name} (listed in the closure)")
            return 1
        files[name] = path.read_text(encoding="utf-8")

    all_decls = {}
    for name, text in files.items():
        for sel, body in blocks(strip_comments(text)):
            for token, value in declared(body).items():
                all_decls.setdefault(token, []).append((name, value))

    contract = list(manifest.get("aliasContract", []))
    tiers = {"aliasContract": list(manifest.get("aliasContract", []))}
    for tier, names in (manifest.get("workingNameContract") or {}).items():
        if tier.startswith("$"):
            continue
        tiers[tier] = list(names)
        contract += names

    findings = []
    for token in contract:
        if token not in all_decls:
            findings.append(f"NAME     {token} is in the contract but nothing in tokens/*.css defines it")

    # 2. contract names must be var() references, not duplicated literals.
    exempt = {"--ws-shadow", "--shadow-workstation", "--shadow-panel", "--shadow-soft",
              "--shadow-menu", "--radius-chip", "--radius-button", "--radius-card",
              "--green", "--red", "--orange", "--purple", "--accent-ghost", "--bg",
              "--bg-light", "--bg-medium", "--bg-hover", "--bg-active", "--text-primary",
              "--text-secondary", "--text-muted", "--border-hover", "--border-strong",
              "--border-divider", "--chart-grid-major"}
    for token in contract:
        if token in exempt or token not in all_decls:
            continue
        literal = [f"{f}: {v}" for f, v in all_decls[token]
                   if v.startswith("#") or v.startswith("rgb")]
        if literal:
            findings.append(f"ALIAS    {token} hardcodes a value ({literal[0]}) — must be a var() reference")

    value_findings = []
    if not names_only:
        light = light_graph(files)
        dark = dark_blocks(files["colors-dark.css"])
        for token, rec in manifest.get("tokens", {}).items():
            want_l, want_d = norm(rec["light"]), norm(rec["dark"])
            got_l = light.get(token)
            if got_l is None:
                value_findings.append(f"VALUE    {token} recorded light {want_l} but the default theme graph does not define it")
            elif got_l != want_l:
                value_findings.append(f"VALUE    {token} light: css {got_l} ≠ manifest {want_l}")
            for label in ("auto", "manual"):
                got = dark[label].get(token)
                if got is None:
                    if want_d != want_l:
                        value_findings.append(f"VALUE    {token} flips to {want_d} but the {label} dark block does not set it")
                elif got != want_d:
                    value_findings.append(f"VALUE    {token} {label} dark: css {got} ≠ manifest {want_d}")
            got_f = dark["forced-light"].get(token)
            if got_f is not None and got_f != want_l:
                value_findings.append(f"VALUE    {token} forced-light: css {got_f} ≠ manifest light {want_l}")

    print("── token parity ──")
    print(f"contract names : {len(contract)} across {len(tiers)} tiers "
          f"({', '.join(f'{k} {len(v)}' for k, v in tiers.items())})")
    print(f"flip set       : {len(manifest.get('tokens', {}))} tokens"
          + (" (value pass skipped)" if names_only else ""))
    print(f"closure        : {len(files)} files, {len(all_decls)} declared tokens")
    for line in findings + value_findings:
        print(line)
    total = len(findings) + len(value_findings)
    print(f"\n{'FAIL' if total else 'PASS'}  {total} finding(s)")
    for note in manifest.get("divergences", []):
        print(f"  note: {note['name']} → {note['here']} (upstream {note['upstream']}) — recorded divergence")
    return 1 if total else 0


if __name__ == "__main__":
    sys.exit(main())
