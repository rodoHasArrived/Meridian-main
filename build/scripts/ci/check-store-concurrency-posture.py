#!/usr/bin/env python3
"""Guard: every file-backed store declares or structurally carries a concurrency posture.

File-store locking is inconsistent by design and that is fine -- some stores lease across
processes, some are append-only, some are deliberately single-writer. What is not fine is being
unable to answer "is it safe for a second process to touch this store?" without reading its source,
which is what #2697 was filed about.

A store's posture is satisfied one of three ways, checked transitively through base classes so a
subclass inherits its base's posture:

1. It takes a cross-process lease (AcquireMutationLease, or a FileShare.None lock file).
2. It serialises read-modify-write in-process (SemaphoreSlim or lock) -- which is what the shared
   JsonFileSnapshotStore base does, covering most stores in the tree.
3. Its doc comment states the posture explicitly -- "Concurrency posture: ...", or the
   single-writer wording JsonlFilePaperSessionStore established.

A store matching none of those is unclassified, and this fails. The point is not to force every
store onto a lease: an append-only evidence log and a per-entity atomic replace have no
read-modify-write sequence for a lease to protect, so for those the answer is to state the posture,
not to add ceremony.
"""

from __future__ import annotations

import argparse
import os
import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
SOURCE_ROOT = REPO_ROOT / "src"

STORE_NAME = re.compile(
    r"^(?:File[A-Za-z0-9_]*|Jsonl[A-Za-z0-9_]*)(?:Store|Repository)$",
)

CLASS_DECL = re.compile(
    r"^[ \t]*(?:public\s+|internal\s+|private\s+|protected\s+)?"
    r"(?:sealed\s+|abstract\s+|static\s+|partial\s+)*class\s+(?P<name>[A-Za-z0-9_]+)"
    r"\s*(?:<[^>{]*>)?\s*(?::\s*(?P<bases>[^{]+))?",
    re.MULTILINE,
)

CROSS_PROCESS_LEASE = re.compile(r"AcquireMutationLease|FileShare\.None")
IN_PROCESS_SERIALIZED = re.compile(r"SemaphoreSlim|\block\s*\(")
DECLARED_POSTURE = re.compile(
    r"Concurrency posture:|single[- ]writer|Exactly one process may write"
    r"|cross-process transactional locking",
    re.IGNORECASE,
)

EXCLUDED_DIRECTORY_NAMES = {"bin", "node_modules", "obj"}


def _iter_sources(source_root: Path) -> list[Path]:
    paths: list[Path] = []
    for current_root, directories, files in os.walk(source_root, topdown=True, followlinks=False):
        directories[:] = sorted(d for d in directories if d.lower() not in EXCLUDED_DIRECTORY_NAMES)
        paths.extend(Path(current_root) / f for f in files if f.lower().endswith(".cs"))
    return sorted(paths)


def _declarations(source_root: Path) -> tuple[dict[str, set[str]], dict[str, set[str]]]:
    """(type -> files declaring it, type -> base type names)."""
    files: dict[str, set[str]] = {}
    bases: dict[str, set[str]] = {}
    for path in _iter_sources(source_root):
        text = path.read_text(encoding="utf-8", errors="replace")
        for match in CLASS_DECL.finditer(text):
            name = match.group("name")
            files.setdefault(name, set()).add(str(path))
            declared = bases.setdefault(name, set())
            if match.group("bases"):
                for base in match.group("bases").split(","):
                    base = base.strip().split("<")[0].split(".")[-1].strip()
                    if base:
                        declared.add(base)
    return files, bases


def classify(source_root: Path) -> dict[str, str]:
    """Maps every file-backed store type to its resolved posture."""
    files, bases = _declarations(source_root)
    text_cache: dict[str, str] = {}

    def read(path: str) -> str:
        if path not in text_cache:
            text_cache[path] = Path(path).read_text(encoding="utf-8", errors="replace")
        return text_cache[path]

    def body(name: str, seen: frozenset[str] = frozenset()) -> str:
        if name in seen or name not in files:
            return ""
        seen = seen | {name}
        parts = [read(p) for p in sorted(files[name])]
        parts.extend(body(b, seen) for b in sorted(bases.get(name, ())))
        return "".join(parts)

    postures: dict[str, str] = {}
    for name in sorted(n for n in files if STORE_NAME.match(n)):
        source = body(name)
        if CROSS_PROCESS_LEASE.search(source):
            postures[name] = "cross-process-lease"
        elif DECLARED_POSTURE.search(source):
            postures[name] = "declared"
        elif IN_PROCESS_SERIALIZED.search(source):
            postures[name] = "in-process-serialized"
        else:
            postures[name] = "unclassified"
    return postures


def main() -> int:
    parser = argparse.ArgumentParser(description="Enforce declared file-store concurrency postures.")
    parser.add_argument("--source-root", default=str(SOURCE_ROOT))
    parser.add_argument("--summary", action="store_true", help="Print the per-posture counts.")
    args = parser.parse_args()

    postures = classify(Path(args.source_root))
    unclassified = sorted(n for n, p in postures.items() if p == "unclassified")

    if args.summary:
        for posture in ("cross-process-lease", "declared", "in-process-serialized", "unclassified"):
            count = sum(1 for p in postures.values() if p == posture)
            print(f"  {posture}: {count}")

    if unclassified:
        print("File-store concurrency posture guard FAILED.", file=sys.stderr)
        print(
            "These file-backed stores carry no concurrency posture -- no cross-process lease, no "
            "in-process serialization, and no stated posture in their doc comment, so whether a "
            "second writer is safe cannot be answered without reading them (#2697):",
            file=sys.stderr,
        )
        for name in unclassified:
            print(f"  {name}", file=sys.stderr)
        print(
            "\nEither serialize the store's read-modify-write, or state the posture in its doc "
            'comment beginning "Concurrency posture: ...". An append-only log or a per-entity '
            "atomic replace has no read-modify-write to protect, so stating the posture is the "
            "correct answer there -- a lease would be ceremony.",
            file=sys.stderr,
        )
        return 1

    print(f"File-store concurrency posture guard: all {len(postures)} file-backed stores classified.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
