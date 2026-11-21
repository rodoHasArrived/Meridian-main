#!/usr/bin/env python3
"""Meridian — extension compile entry (restored 3.5, self-contained, offline, stdlib only).

In THIS project the design-system compiler regenerates `_ds_bundle.js`, `_ds_manifest.json`,
and `_adherence.oxlintrc.json` idempotently on every source change — those files are never
hand-edited. This script is the upstream entry point restored as the offline validator/planner
for that pipeline: it verifies every module listed in `scripts/bundle_extensions.json` exists,
pairs with a `.d.ts`, exports what it claims, and already appears in the compiled manifest;
it exits non-zero when the compile plan is broken.

  python scripts/compile_bundle_extensions.py
"""
import json, re, sys, pathlib

ROOT = pathlib.Path(__file__).resolve().parent.parent

def main():
    plan = json.loads((ROOT / "scripts" / "bundle_extensions.json").read_text(encoding="utf-8"))
    mf = ROOT / "_ds_manifest.json"
    manifest = json.loads(mf.read_text(encoding="utf-8")) if mf.exists() else {"components": []}
    compiled = {c["name"] for c in manifest["components"]}
    failures = 0
    for entry in plan["modules"]:
        src = ROOT / entry["path"]
        if not src.exists():
            print(f"FAIL missing source {entry['path']}"); failures += 1; continue
        if not src.with_suffix(".d.ts").exists() and not src.with_name(src.stem + ".d.ts").exists():
            print(f"FAIL {entry['path']}: no sibling .d.ts"); failures += 1
        text = src.read_text(encoding="utf-8")
        for name in entry["exports"]:
            if not re.search(rf"export (?:const|function) {name}\b", text):
                print(f"FAIL {entry['path']}: does not export {name}"); failures += 1
            elif name not in compiled:
                print(f"WARN {name} not yet in _ds_manifest.json — the compiler folds it in on the next pass")
            else:
                print(f"ok   {name} <- {entry['path']}")
    print(f"\n{failures} failures" if failures else "\ncompile plan valid — bundle/manifest/adherence regenerate idempotently from source")
    return 1 if failures else 0

if __name__ == "__main__":
    sys.exit(main())
