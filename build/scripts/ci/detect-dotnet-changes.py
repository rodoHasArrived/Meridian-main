#!/usr/bin/env python3
"""Run .NET for all changes except a known set of documentation-only paths.

Use a local git diff, not the paginated/truncated PR files API. Both sides of a
rename are inspected, so moving source into docs cannot hide a source deletion.
Unknown paths and empty diffs require .NET. Invalid revisions fail detection.
"""
from __future__ import annotations

import argparse
import re
import subprocess
from pathlib import Path, PurePosixPath

DOC_SUFFIXES = {".md", ".rst", ".txt", ".png", ".jpg", ".jpeg", ".gif", ".svg",
                ".webp", ".pdf", ".mmd", ".mermaid", ".puml", ".drawio"}


def is_documentation(path: str) -> bool:
    parts = PurePosixPath(path).parts
    if not parts or ".." in parts:
        return False
    if len(parts) == 1:
        return PurePosixPath(path).suffix == ".md"
    in_docs = parts[0] == "docs" or parts[:2] == ("archive", "docs")
    return in_docs and PurePosixPath(path).suffix in DOC_SUFFIXES


def requires_dotnet(paths: list[str]) -> bool:
    return not paths or any(not is_documentation(path) for path in paths)


def changed_paths(base: str, head: str) -> list[str]:
    if not all(re.fullmatch(r"[0-9a-f]{40}", revision) for revision in (base, head)):
        raise ValueError("Expected full commit SHAs for both change boundaries")
    result = subprocess.run(
        ["git", "diff", "--name-only", "--no-renames", "-z", base, head, "--"],
        check=True, capture_output=True,
    )
    return [path.decode("utf-8", errors="surrogateescape") for path in result.stdout.split(b"\0") if path]


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base", required=True)
    parser.add_argument("--head", required=True)
    parser.add_argument("--github-output", type=Path, required=True)
    args = parser.parse_args()
    paths = changed_paths(args.base, args.head)
    required = requires_dotnet(paths)
    with args.github_output.open("a", encoding="utf-8") as output:
        output.write(f"dotnet={'true' if required else 'false'}\n")
    print(f"Inspected {len(paths)} changed paths; .NET required: {required}")


if __name__ == "__main__":
    main()
