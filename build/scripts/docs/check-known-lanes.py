#!/usr/bin/env python3
"""Fail when lane-matrix docs reference unknown lane names."""
from __future__ import annotations

import pathlib
import re
import sys

KNOWN = {
    "bootstrap",
    "verify-fast",
    "verify-full",
    "verify-docs",
    "verify-desktop",
    "verify-release",
}

FILES = [
    pathlib.Path("README.md"),
    pathlib.Path("docs/developer/build-test-run.md"),
    pathlib.Path(".github/workflows/README.md"),
]

pattern = re.compile(r"\|\s*`([a-z][a-z-]+)`\s*\|")
unknown: list[tuple[str, str]] = []
for file_path in FILES:
    text = file_path.read_text(encoding="utf-8")
    for lane in pattern.findall(text):
        if lane.startswith("verify-") or lane == "bootstrap":
            if lane not in KNOWN:
                unknown.append((str(file_path), lane))

if unknown:
    print("Unknown lane name(s) found in lane matrices:", file=sys.stderr)
    for file_path, lane in unknown:
        print(f"  - {lane} ({file_path})", file=sys.stderr)
    print("Known lanes: " + ", ".join(sorted(KNOWN)), file=sys.stderr)
    sys.exit(1)

print("Known lane name check passed.")
