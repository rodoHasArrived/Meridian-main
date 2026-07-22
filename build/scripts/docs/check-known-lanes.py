#!/usr/bin/env python3
"""Fail when lane-matrix docs reference unknown lane names."""
from __future__ import annotations

import json
import pathlib
import re
import sys

REPO_ROOT = pathlib.Path(__file__).resolve().parents[3]
LANE_MANIFEST_PATH = REPO_ROOT / "build" / "ci" / "lane-manifest.json"

FILES = [
    pathlib.Path("README.md"),
    pathlib.Path("archive/docs/developer/build-test-run.md"),
    pathlib.Path(".github/workflows/README.md"),
]

pattern = re.compile(r"\|\s*`([a-z][a-z-]+)`\s*\|")


def load_known_lanes() -> set[str]:
    payload = json.loads(LANE_MANIFEST_PATH.read_text(encoding="utf-8"))
    return {str(lane["id"]) for lane in payload.get("lanes", []) if isinstance(lane, dict) and lane.get("id")}


KNOWN = load_known_lanes()
unknown: list[tuple[str, str]] = []
for file_path in FILES:
    text = file_path.read_text(encoding="utf-8")
    for lane in pattern.findall(text):
        if lane.startswith("verify-") or lane in {"bootstrap", "quality-gate", "targeted-test"}:
            if lane not in KNOWN:
                unknown.append((str(file_path), lane))

if unknown:
    print("Unknown lane name(s) found in lane matrices:", file=sys.stderr)
    for file_path, lane in unknown:
        print(f"  - {lane} ({file_path})", file=sys.stderr)
    print("Known lanes: " + ", ".join(sorted(KNOWN)), file=sys.stderr)
    sys.exit(1)

print("Known lane name check passed.")
