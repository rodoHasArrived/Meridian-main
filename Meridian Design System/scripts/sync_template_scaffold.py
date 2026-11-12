#!/usr/bin/env python3
"""Meridian — template scaffold sync (restored 3.5, stdlib only, offline).

`templates/_scaffold/{ds-base.js,support.js}` are the canonical copies; every template folder
carries byte-identical siblings. Never author them per folder.

  python scripts/sync_template_scaffold.py            # report drift
  python scripts/sync_template_scaffold.py --write    # copy canonical files over drifted ones
  python scripts/sync_template_scaffold.py --check    # exit 1 on drift (CI gate)
"""
import sys, pathlib

ROOT = pathlib.Path(__file__).resolve().parent.parent
SCAFFOLD = ROOT / "templates" / "_scaffold"
FILES = ["ds-base.js", "support.js"]

def main():
    canon = {f: (SCAFFOLD / f).read_bytes() for f in FILES if (SCAFFOLD / f).exists()}
    if len(canon) != len(FILES):
        print("scaffold canonical files missing under templates/_scaffold/", file=sys.stderr)
        return 2
    drift = []
    for folder in sorted(p for p in (ROOT / "templates").iterdir() if p.is_dir() and not p.name.startswith("_")):
        for f in FILES:
            target = folder / f
            if not target.exists() or target.read_bytes() != canon[f]:
                drift.append(target)
                if "--write" in sys.argv:
                    target.write_bytes(canon[f])
    if drift:
        verb = "rewrote" if "--write" in sys.argv else "drifted"
        for t in drift:
            print(f"{verb}: {t.relative_to(ROOT)}")
        if "--check" in sys.argv:
            return 1
    else:
        print("scaffold in sync")
    return 0

if __name__ == "__main__":
    sys.exit(main())
