#!/usr/bin/env python3
"""Run untrusted generation without exposing the Actions host runtime or caches."""
from __future__ import annotations

import argparse
import os
from pathlib import Path
import re
import shutil
import stat
import subprocess
import tempfile

MAX_EXPORT = 128 * 1024 * 1024
FILES = {"repair.patch", "generated.patch", "report.json", "generation-report.json",
         "generation.log", "dependencies.log", "bootstrap.log", "container.log"}
BOOTSTRAP = """set -eu
mkdir -p /work/tmp
python3 -m venv /work/venv
/work/venv/bin/python -m pip install PyYAML==6.0.2 > /output/bootstrap.log 2>&1
exec /work/venv/bin/python -I /work/repo/build/scripts/repair-generated.py \\
  --repo /work/repo --head-ref "$REPAIR_HEAD_SHA" --base-ref "$REPAIR_BASE_SHA" \\
  --install-dependencies --max-passes 3 --output-dir /output
"""


def container_command(work: Path, output: Path, image: str, head: str, base: str) -> list[str]:
    for sha in (head, base):
        if not re.fullmatch(r"[a-f0-9]{40}", sha):
            raise ValueError("Container inputs must be pinned SHA-1 commit IDs")
    if not re.fullmatch(r"[a-z0-9][a-z0-9_.:/@-]*", image):
        raise ValueError("Invalid image reference")
    return ["docker", "run", "--rm", "--read-only", "--cap-drop=ALL",
            "--security-opt=no-new-privileges", "--pids-limit=512", "--memory=6g", "--cpus=2",
            "--user", f"{os.getuid()}:{os.getgid()}", "--tmpfs", "/tmp:rw,nosuid,nodev,size=64m",
            "--mount", f"type=bind,source={work},target=/work",
            "--mount", f"type=bind,source={output},target=/output", "--workdir", "/work",
            "--env", f"REPAIR_HEAD_SHA={head}", "--env", f"REPAIR_BASE_SHA={base}",
            "--env", "TMPDIR=/work/tmp", "--env", "NPM_CONFIG_CACHE=/work/npm-cache",
            "--env", "NPM_CONFIG_USERCONFIG=/work/npmrc",
            "--env", "PIP_CACHE_DIR=/work/pip-cache", "--env", "GIT_CONFIG_GLOBAL=/dev/null",
            "--env", "GIT_CONFIG_NOSYSTEM=1", image, "sh", "-c", BOOTSTRAP]


def collect_exports(source: Path, destination: Path) -> None:
    """Copy only bounded regular files after the container has exited.

    In particular, upload-artifact must never follow a PR-controlled symlink to
    host files. Neither source paths nor destinations come from the candidate.
    """
    if source.is_symlink() or not source.is_dir() or destination.exists():
        raise ValueError("Export requires a regular source directory and a fresh destination")
    selected = []
    total = 0
    for directory, subdirs, filenames in os.walk(source, followlinks=False):
        root = Path(directory)
        for name in subdirs:
            path = root / name
            if path.is_symlink() or path.relative_to(source).as_posix() != "logs":
                raise ValueError("Unexpected export directory or symbolic link")
        for name in filenames:
            path = root / name
            relative = path.relative_to(source).as_posix()
            mode = path.lstat().st_mode
            allowed = relative in FILES or (relative.startswith("logs/") and "/" not in relative[5:] and relative.endswith(".log"))
            if not allowed or not stat.S_ISREG(mode) or path.stat().st_nlink != 1:
                raise ValueError("Unexpected export file, link, or special file")
            total += path.stat().st_size
            if total > MAX_EXPORT:
                raise ValueError("Generation exports exceed size limit")
            selected.append((path, relative))
    destination.mkdir(parents=True)
    for path, relative in selected:
        target = destination / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        # Docker has exited and its processes are gone; O_NOFOLLOW additionally
        # prevents a substituted last path component from becoming a host read.
        fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW)
        with os.fdopen(fd, "rb") as reader, target.open("wb") as writer:
            shutil.copyfileobj(reader, writer)


def run(repo: Path, output: Path, image: str, head: str, base: str) -> int:
    if not all(re.fullmatch(r"[a-f0-9]{40}", sha) for sha in (head, base)):
        raise ValueError("Sandbox inputs must be pinned SHA-1 commit IDs")
    if output.exists():
        raise ValueError("Export destination must not already exist")
    with tempfile.TemporaryDirectory(prefix="generated-repair-sandbox-") as temp:
        root = Path(temp)
        work, raw = root / "work", root / "output"
        work.mkdir()
        raw.mkdir()
        clone = work / "repo"
        # No shared object store or worktree metadata points back at the host's
        # trusted checkout. Docker sees only this disposable copy and raw output.
        subprocess.run(["git", "-c", "core.hooksPath=/dev/null", "clone", "--no-hardlinks", str(repo), str(clone)], check=True)
        for sha in (head, base):
            subprocess.run(["git", "-C", str(clone), "cat-file", "-e", f"{sha}^{{commit}}"], check=True)
        command = container_command(work, raw, image, head, base)
        # Avoid interpreting any container output as Actions workflow commands.
        with (raw / "container.log").open("wb") as log:
            result = subprocess.run(command, stdout=log, stderr=subprocess.STDOUT)
        collect_exports(raw, output)
        return result.returncode


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, default=Path.cwd())
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--image", default="meridian-generated-repair")
    parser.add_argument("--head", required=True)
    parser.add_argument("--base", required=True)
    args = parser.parse_args()
    try:
        return run(args.repo.resolve(), args.output_dir.resolve(), args.image, args.head, args.base)
    except (ValueError, OSError, subprocess.CalledProcessError) as exc:
        print(f"Generation sandbox failed: {exc}")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
