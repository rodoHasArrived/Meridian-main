#!/usr/bin/env python3
"""Prepare missing workstation assets before a standalone .NET CI lane."""
from __future__ import annotations

import argparse
from pathlib import Path
import shutil
import subprocess
import sys


ROOT = Path(__file__).resolve().parents[2]
ASSETS = "src/Meridian.Ui/wwwroot/workstation"


def ensure(root: Path, runner=subprocess.run) -> int:
    if not (root / ASSETS / "index.html").is_file():
        npm = shutil.which("npm") or "npm"
        dashboard = "src/Meridian.Ui/dashboard"
        commands = (
            [npm, "--prefix", dashboard, "ci", "--include=optional"],
            [sys.executable, "build/scripts/generate-ui-api-routes-ts.py"],
            [sys.executable, "build/scripts/generate-workspace-catalog-ts.py"],
            [npm, "--prefix", dashboard, "run", "build"],
        )
        for command in commands:
            code = runner(command, cwd=root, check=False).returncode
            if code:
                return code
    return runner([sys.executable, "build/scripts/ci/check-workstation-assets.py"], cwd=root, check=False).returncode


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, default=ROOT)
    args = parser.parse_args()
    try:
        return ensure(args.repo.resolve())
    except OSError as error:
        print(f"Cannot prepare workstation assets: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
