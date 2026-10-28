#!/usr/bin/env python3
"""Synchronize the canonical shared context without changing host-specific guidance."""

from __future__ import annotations

import argparse
import os
from pathlib import Path
import re
import stat
import sys
import tempfile


REPO_ROOT = Path(__file__).resolve().parents[3]
CANONICAL = ".claude/skills/_shared/project-context.md"
MIRRORS = (
    ".codex/skills/_shared/project-context.md",
    ".agents/skills/_shared/project-context.md",
)
BEGIN = b"<!-- shared-context:begin"
END = b"<!-- shared-context:end -->"


def guidance_path(root: Path, relative: str) -> Path:
    """Resolve only existing, regular, non-symlink files inside the selected root."""
    candidate = root
    for part in Path(relative).parts:
        candidate /= part
        if candidate.is_symlink():
            raise ValueError(f"Symlinks are not allowed for guidance: {relative}")
    if not candidate.is_file() or not candidate.resolve().is_relative_to(root):
        raise ValueError(f"Missing or unsafe guidance file: {relative}")
    return candidate


def split_context(data: bytes, name: str) -> tuple[bytes, bytes, bytes]:
    """Require one complete marker pair; keep everything outside it byte-for-byte."""
    try:
        data.decode("utf-8")
    except UnicodeDecodeError as exc:
        raise ValueError(f"Guidance is not UTF-8: {name}") from exc
    if data.count(BEGIN) != 1 or data.count(END) != 1:
        raise ValueError(f"Expected exactly one shared-context marker pair: {name}")
    start = data.index(BEGIN)
    end = data.index(END)
    opening = re.match(rb"<!-- shared-context:begin[^\r\n]*?-->", data[start:])
    if opening is None or end < start + opening.end():
        raise ValueError(f"Malformed or reversed shared-context markers: {name}")
    stop = end + len(END)
    return data[:start], data[start:stop], data[stop:]


def generate(root: Path, *, write: bool = False) -> list[str]:
    """Return drifted paths; optionally repair them after validating every input."""
    root = root.resolve(strict=True)
    inputs: dict[str, tuple[Path, bytes, tuple[bytes, bytes, bytes]]] = {}
    for name in (CANONICAL, *MIRRORS):
        path = guidance_path(root, name)
        data = path.read_bytes()
        inputs[name] = (path, data, split_context(data, name))
    canonical = inputs[CANONICAL][2][1]
    pending = {
        name: before + canonical + after
        for name in MIRRORS
        for before, shared, after in (inputs[name][2],)
        if shared != canonical
    }
    if not write or not pending:
        return list(pending)

    # Stage all replacement files first. Invalid inputs or staging failures never
    # leave a partially synchronized set. Each replacement itself is atomic.
    staged: dict[str, Path] = {}
    try:
        for name, data in pending.items():
            path = inputs[name][0]
            with tempfile.NamedTemporaryFile(dir=path.parent, prefix=".guidance-", delete=False) as output:
                staged[name] = Path(output.name)
                output.write(data)
                output.flush()
                os.fsync(output.fileno())
            os.chmod(staged[name], stat.S_IMODE(path.stat().st_mode))
        for name, (path, original, _) in inputs.items():
            if guidance_path(root, name) != path or path.read_bytes() != original:
                raise ValueError(f"Guidance changed during generation; retry: {name}")
        for name, temporary in staged.items():
            os.replace(temporary, inputs[name][0])
    finally:
        for temporary in staged.values():
            temporary.unlink(missing_ok=True)
    return list(pending)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=REPO_ROOT)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--check", action="store_true", help="Check freshness without writing (default).")
    mode.add_argument("--write", action="store_true", help="Synchronize marked mirror sections.")
    parser.add_argument("--summary", action="store_true", help="Print a compact result.")
    args = parser.parse_args(argv)
    try:
        drift = generate(args.root, write=args.write)
    except (OSError, ValueError) as exc:
        print(f"Guidance generation failed: {exc}", file=sys.stderr)
        return 2
    if drift:
        action = "Synchronized" if args.write else "Shared guidance drift:"
        print(f"{action} {', '.join(drift)}")
        if not args.write:
            print("Repair: python build/scripts/ai/generate-guidance.py --write")
    elif args.summary:
        print("Shared guidance is current (2 mirrors).")
    return 0 if args.write or not drift else 1


if __name__ == "__main__":
    raise SystemExit(main())
