#!/usr/bin/env python3
"""Meridian — inventory generator (restored 3.5, self-contained, stdlib only, offline).

Regenerates the census between the `<!-- inventory:generated:begin/end -->` markers in
README.md from _ds_manifest.json — never hand-edit that block. Idempotent: running twice
produces identical bytes.

  python scripts/generate_inventory.py            # print the block
  python scripts/generate_inventory.py --write    # rewrite README.md in place
  python scripts/generate_inventory.py --check    # exit 1 on drift (CI gate)
"""
import json, re, sys, collections, pathlib

ROOT = pathlib.Path(__file__).resolve().parent.parent
DOMAINS = ["core", "data", "charts", "accounting", "trading", "operations", "shell", "decision"]

def load():
    manifest = json.loads((ROOT / "_ds_manifest.json").read_text(encoding="utf-8"))
    by_module = collections.OrderedDict()  # (domain, module) -> [exports]
    for comp in manifest["components"]:
        src = comp.get("sourcePath", "")
        m = re.match(r"components/(\w+)/([\w.]+)\.(jsx|tsx|js)$", src)
        if not m:
            continue
        key = (m.group(1), m.group(2))
        by_module.setdefault(key, []).append(comp["name"])
    return by_module

def render(by_module, namespace=None):
    if namespace is None:
        namespace = json.loads((ROOT / "_ds_manifest.json").read_text(encoding="utf-8"))["namespace"]
    total_modules = len(by_module)
    total_exports = sum(len(v) for v in by_module.values())
    out = []
    out.append(f"**{total_modules} modules across {len(DOMAINS)} domains → {total_exports} exported symbols** "
               f"on `window.{namespace}` — this census is generated from `_ds_manifest.json` by "
               "`scripts/generate_inventory.py` (run with `--write` after adding a component; CI fails on drift). "
               "Per-component one-liners live in each `<Name>.prompt.md`, when-to-reach-for guidance in "
               "`guidelines/PATTERNS.md`, the domain map in `components/README.md`.")
    for domain in DOMAINS:
        mods = {k[1]: v for k, v in by_module.items() if k[0] == domain}
        if not mods:
            continue
        n_exp = sum(len(v) for v in mods.values())
        out.append("")
        out.append(f"### `{domain}/` — {len(mods)} modules · {n_exp} exports")
        out.append("")
        parts = []
        for mod in sorted(mods, key=str):
            exps = sorted(mods[mod], key=str)
            if len(exps) == 1 and exps[0] == mod:
                parts.append(f"`{mod}`")
            else:
                parts.append(f"`{mod}` → " + " + ".join(f"`{e}`" for e in exps))
        out.append(" · ".join(parts))
    return "\n".join(out)

def main():
    by_module = load()
    block = render(by_module)
    readme = (ROOT / "README.md").read_text(encoding="utf-8")
    begin, end = "<!-- inventory:generated:begin -->", "<!-- inventory:generated:end -->"
    if begin not in readme or end not in readme:
        print("README.md: inventory markers missing", file=sys.stderr)
        return 2
    pre, rest = readme.split(begin, 1)
    _, post = rest.split(end, 1)
    updated = pre + begin + "\n" + block + "\n" + end + post
    if "--write" in sys.argv:
        (ROOT / "README.md").write_text(updated, encoding="utf-8")
        print(f"README.md inventory updated · {len(by_module)} modules")
        return 0
    if "--check" in sys.argv:
        if updated != readme:
            print("DRIFT: README.md inventory does not match _ds_manifest.json — run with --write", file=sys.stderr)
            return 1
        print("inventory in sync")
        return 0
    print(block)
    return 0

if __name__ == "__main__":
    sys.exit(main())
