#!/usr/bin/env python3
"""Reject generation drift and export a complete patch without changing the index."""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import tempfile
import sys
from pathlib import Path


sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from generated_output_policy import POLICY, delivery, export_ci_outputs, is_ci_output


EXCLUSIONS = [":(exclude)docs/diagrams/uml/*.png", ":(exclude)docs/diagrams/uml/*.svg"]


def check(root: Path, output: Path) -> bool:
    output = output.resolve()
    if not output.is_relative_to((root / "artifacts").resolve()):
        raise ValueError("Repair evidence must be under ignored artifacts/")
    output.mkdir(parents=True, exist_ok=True)
    policy_path = root / POLICY
    policy = json.loads(policy_path.read_text(encoding="utf-8")) if policy_path.exists() else {}
    config = delivery(policy)
    exclusions = EXCLUSIONS + [f":(exclude,literal){name}" for name in config.get("ci_files", []) + config.get("ci_trees", [])]
    # Machine outputs are deliberately absent from Git; a force-add must not
    # silently restore the old manual-commit workflow.
    tracked = subprocess.check_output(["git", "ls-files", "-z"], cwd=root).decode().split("\0")
    tracked_ci = sorted(name for name in tracked if name and is_ci_output(name, policy))
    manifest = export_ci_outputs(root, output, policy)
    with tempfile.TemporaryDirectory(prefix="meridian-generation-index-") as temp:
        environment = dict(os.environ, GIT_INDEX_FILE=str(Path(temp) / "index"))

        def git(*args: str) -> bytes:
            return subprocess.check_output(["git", *args], cwd=root, env=environment)

        git("read-tree", "HEAD")
        git("add", "--all", "--", ".")
        patch = git("diff", "--cached", "--binary", "--no-ext-diff", "--no-textconv", "HEAD", "--", ".", *exclusions)
        names = git("diff", "--cached", "--name-only", "-z", "HEAD", "--", ".", *exclusions)
    paths = [name.decode("utf-8") for name in names.split(b"\0") if name]
    (output / "generated-repair.patch").write_bytes(patch)
    (output / "drift.json").write_text(json.dumps({"current": not paths and not tracked_ci, "changed_paths": paths, "tracked_ci_outputs": tracked_ci, "ci_outputs": manifest}, indent=2) + "\n")
    if paths or tracked_ci:
        message = (
            "Reviewed generated files are stale (or CI outputs were force-added). Download generated-repair.patch from the documentation artifact, "
            "or run `python build/scripts/repair-generated.py --help` for isolated merge repair. "
            "Run `python build/python/cli/buildctl.py regen --fix` to refresh local outputs. "
            "Review and commit only the reviewed-document patch; CI-produced files are in generated/.\n"
        )
        print(message)
        if summary := os.environ.get("GITHUB_STEP_SUMMARY"):
            with open(summary, "a", encoding="utf-8") as stream:
                stream.write(message)
    return not paths and not tracked_ci


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
