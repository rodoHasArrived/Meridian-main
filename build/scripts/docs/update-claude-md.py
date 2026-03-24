#!/usr/bin/env python3
"""Sync repository structure code blocks in AI instruction markdown files."""

from __future__ import annotations

import argparse
import difflib
import re
from pathlib import Path

SECTION_HEADINGS = (
    "Repository Layout",
    "Repository Structure",
    "Project Structure",
    "Solution Layout",
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Sync repository-structure sections across markdown files.")
    parser.add_argument("--claude-md", default="CLAUDE.md", help="Markdown file to update")
    parser.add_argument(
        "--structure-source",
        default="docs/generated/repository-structure.md",
        help="Source markdown with canonical repository structure",
    )
    parser.add_argument("--dry-run", action="store_true", help="Show diff without writing files")
    return parser.parse_args()


def extract_first_fenced_block(markdown: str) -> str:
    match = re.search(r"```(?:text)?\n.*?\n```", markdown, flags=re.DOTALL)
    if not match:
        raise ValueError("No fenced code block found in structure source")
    block = match.group(0)
    if not block.startswith("```text\n"):
        inner = block.split("\n", 1)[1].rsplit("\n```", 1)[0]
        return f"```text\n{inner}\n```"
    return block


def find_section_bounds(lines: list[str]) -> tuple[int, int] | None:
    heading_pattern = re.compile(r"^##\s+(.*)$")
    for i, line in enumerate(lines):
        match = heading_pattern.match(line)
        if not match:
            continue
        title = match.group(1).strip()
        if title not in SECTION_HEADINGS:
            continue
        end = len(lines)
        for j in range(i + 1, len(lines)):
            if lines[j].startswith("## "):
                end = j
                break
        return i, end
    return None


def replace_first_fenced_block(section_text: str, replacement_block: str) -> str:
    block_pattern = re.compile(r"```(?:\w+)?\n.*?\n```", re.DOTALL)
    match = block_pattern.search(section_text)
    if match:
        return section_text[: match.start()] + replacement_block + section_text[match.end() :]

    if not section_text.endswith("\n"):
        section_text += "\n"
    return section_text + "\n" + replacement_block + "\n"


def update_target(markdown: str, replacement_block: str) -> str:
    lines = markdown.splitlines(keepends=True)
    bounds = find_section_bounds(lines)
    if bounds is None:
        addition = "\n## Repository Structure\n\n" + replacement_block + "\n"
        if markdown.endswith("\n"):
            return markdown + addition.lstrip("\n")
        return markdown + addition

    start, end = bounds
    section_text = "".join(lines[start:end])
    updated_section = replace_first_fenced_block(section_text, replacement_block)
    return "".join(lines[:start]) + updated_section + "".join(lines[end:])


def main() -> int:
    args = parse_args()
    target_path = Path(args.claude_md)
    source_path = Path(args.structure_source)

    if not source_path.exists():
        raise SystemExit(f"Structure source not found: {source_path}")
    if not target_path.exists():
        raise SystemExit(f"Target markdown file not found: {target_path}")

    source_content = source_path.read_text(encoding="utf-8")
    replacement_block = extract_first_fenced_block(source_content)

    original = target_path.read_text(encoding="utf-8")
    updated = update_target(original, replacement_block)

    if original == updated:
        print(f"No changes needed: {target_path}")
        return 0

    if args.dry_run:
        diff = difflib.unified_diff(
            original.splitlines(),
            updated.splitlines(),
            fromfile=str(target_path),
            tofile=f"{target_path} (updated)",
            lineterm="",
        )
        for line in diff:
            print(line)
        return 0

    target_path.write_text(updated, encoding="utf-8")
    print(f"Updated {target_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
