#!/usr/bin/env python3
"""Meridian — release validation orchestrator (restored 3.5, stdlib only, offline).

Runs every static gate in order and prints one verdict. No network, no browser — the
keyboard/ARIA and responsive browser matrix is prepared in tests/BROWSER_TEST_PLAN.md and
deliberately NOT claimed by this script.

  python scripts/validate_release.py
"""
import subprocess, sys, pathlib

ROOT = pathlib.Path(__file__).resolve().parent.parent
GATES = [
    [sys.executable, "scripts/check_design_system_governance.py"],
    [sys.executable, "scripts/generate_inventory.py", "--check"],
    [sys.executable, "scripts/generate_families.py", "--check"],
    [sys.executable, "scripts/release_identity.py", "--check"],
    [sys.executable, "scripts/sync_catalog.py", "--check"],
    [sys.executable, "scripts/sync_template_scaffold.py", "--check"],
]

def main():
    results = []
    for cmd in GATES:
        print("\n══ " + " ".join(cmd[1:]))
        rc = subprocess.call(cmd, cwd=ROOT)
        results.append((cmd[1], rc))
    rc = subprocess.call([sys.executable, "scripts/compile_bundle_extensions.py"], cwd=ROOT)
    results.append(("scripts/compile_bundle_extensions.py", rc))
    print("\n──────── release gates ────────")
    bad = 0
    for name, rc in results:
        print(("PASS  " if rc == 0 else "FAIL  ") + name)
        bad += 1 if rc else 0
    print("\nBrowser/NVDA validation: DEFERRED — see tests/BROWSER_TEST_PLAN.md (not claimed here).")
    return 1 if bad else 0

if __name__ == "__main__":
    sys.exit(main())
