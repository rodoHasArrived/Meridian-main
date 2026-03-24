#!/usr/bin/env python3
"""Validate high-friction tooling metadata references.

Checks a small set of repo metadata files that are easy to let drift:
- package.json node script entrypoints
- Makefile hard-coded node/python helper paths
- .github/dependabot.yml referenced directories
"""

from __future__ import annotations

import json
import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
PACKAGE_JSON = REPO_ROOT / "package.json"
MAKEFILE = REPO_ROOT / "Makefile"
DEPENDABOT = REPO_ROOT / ".github" / "dependabot.yml"


def load_package_paths() -> list[str]:
    package = json.loads(PACKAGE_JSON.read_text())
    scripts = package.get("scripts", {})
    paths: list[str] = []
    for command in scripts.values():
        match = re.match(r"\s*node\s+([^\s]+)", command)
        if match:
            paths.append(match.group(1))
    return paths


def load_makefile_paths() -> list[str]:
    text = MAKEFILE.read_text()
    matches = re.findall(r"(?:^|[\s@])(node|python3)\s+([^\s\\]+)", text, flags=re.MULTILINE)
    return [path for _tool, path in matches if "/" in path and not path.startswith("$(")]


def load_dependabot_directories() -> list[str]:
    directories: list[str] = []
    for line in DEPENDABOT.read_text().splitlines():
        match = re.match(r"\s*directory:\s*\"([^\"]+)\"", line)
        if match:
            directories.append(match.group(1))
    return directories


def validate_paths(paths: list[str], label: str, expect_dir: bool = False) -> list[str]:
    errors: list[str] = []
    for rel_path in paths:
        normalized = rel_path.lstrip("/")
        candidate = REPO_ROOT / normalized
        exists = candidate.is_dir() if expect_dir else candidate.exists()
        if not exists:
            kind = "directory" if expect_dir else "path"
            errors.append(f"{label}: missing {kind} '{rel_path}'")
    return errors


def main() -> int:
    errors: list[str] = []
    errors.extend(validate_paths(load_package_paths(), "package.json scripts"))
    errors.extend(validate_paths(load_makefile_paths(), "Makefile command"))
    errors.extend(validate_paths(load_dependabot_directories(), ".github/dependabot.yml", expect_dir=True))

    if errors:
        print("Tooling metadata validation failed:", file=sys.stderr)
        for error in errors:
            print(f"  - {error}", file=sys.stderr)
        return 1

    print("Tooling metadata validation passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
