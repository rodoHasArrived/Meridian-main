#!/usr/bin/env python3
"""Reject generation drift and export a complete patch without changing the index."""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import tempfile
from pathlib import Path


EXCLUSIONS = [":(exclude)docs/diagrams/uml/*.png", ":(exclude)docs/diagrams/uml/*.svg"]


def check(root: Path, output: Path) -> bool:
    output = output.resolve()
    if not output.is_relative_to((root / "artifacts").resolve()):
        raise ValueError("Repair evidence must be under ignored artifacts/")
    output.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="meridian-generation-index-") as temp:
        environment = dict(os.environ, GIT_INDEX_FILE=str(Path(temp) / "index"))

        def git(*args: str) -> bytes:
            return subprocess.check_output(["git", *args], cwd=root, env=environment)

        git("read-tree", "HEAD")
        git("add", "--all", "--", ".", *EXCLUSIONS)
        patch = git("diff", "--cached", "--binary", "--no-ext-diff", "--no-textconv", "HEAD", "--", ".", *EXCLUSIONS)
        names = git("diff", "--cached", "--name-only", "-z", "HEAD", "--", ".", *EXCLUSIONS)
    paths = [name.decode("utf-8") for name in names.split(b"\0") if name]
    (output / "generated-repair.patch").write_bytes(patch)
    (output / "drift.json").write_text(json.dumps({"current": not paths, "changed_paths": paths}, indent=2) + "\n")
    if paths:
        message = (
            "Generated files are stale. Download generated-repair.patch from the documentation artifact, "
            "or run `python build/scripts/repair-generated.py --help` for isolated merge repair. "
            "For documentation-only refresh run `python build/scripts/run-maintained-generation.py --docs-only`. "
            "Review the patch, commit it, and let CI validate the new revision.\n"
        )
        print(message)
        if summary := os.environ.get("GITHUB_STEP_SUMMARY"):
            with open(summary, "a", encoding="utf-8") as stream:
                stream.write(message)
    return not paths


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, default=Path(__file__).resolve().parents[3])
    parser.add_argument("--output", type=Path, default=Path("artifacts/docs"))
    args = parser.parse_args()
    root = args.repo.resolve()
    try:
        return 0 if check(root, root / args.output) else 1
    except (OSError, ValueError, subprocess.CalledProcessError) as error:
        print(f"Generated output check failed: {error}")
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
