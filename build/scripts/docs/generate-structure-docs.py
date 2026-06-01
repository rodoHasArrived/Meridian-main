#!/usr/bin/env python3
"""Generate lightweight repository structure and workflow/provider documentation.

This script intentionally has no third-party dependencies so it can run in CI.
"""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import fnmatch
from pathlib import Path


EXCLUDED_DIR_NAMES = {
    ".artifacts",
    ".git",
    ".idea",
    ".mypy_cache",
    ".nuget",
    ".playwright-cli",
    ".pytest_cache",
    ".ruff_cache",
    ".vs",
    "__pycache__",
    "artifacts",
    "bin",
    "coverage",
    "logs",
    "node_modules",
    "obj",
    "obj-codex",
    "archive",
    "TestResults",
}

EXCLUDED_ROOT_DIR_NAMES = {
    "data",
}

EXCLUDED_FILE_SUFFIXES = {
    ".bak",
    ".log",
    ".pyc",
    ".pyo",
    ".tmp",
}

EXCLUDED_FILE_PATTERNS = (
    "*.backup-*",
)


def utc_now() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M:%S UTC")


def is_excluded(path: Path, root: Path | None = None) -> bool:
    if path.is_symlink():
        return True

    parts = path.parts
    if any(part in EXCLUDED_DIR_NAMES for part in parts):
        return True

    if root is not None:
        try:
            relative_parts = path.relative_to(root).parts
        except ValueError:
            relative_parts = parts
        if relative_parts and relative_parts[0] in EXCLUDED_ROOT_DIR_NAMES:
            return True

    name = path.name
    if path.is_file():
        if path.suffix.lower() in EXCLUDED_FILE_SUFFIXES:
            return True
        if any(fnmatch.fnmatch(name, pattern) for pattern in EXCLUDED_FILE_PATTERNS):
            return True

    return False


def render_tree(root: Path) -> str:
    lines: list[str] = []

    def walk(current: Path, prefix: str = "") -> None:
        try:
            entries = sorted(
                [p for p in current.iterdir() if not is_excluded(p, root)],
                key=lambda p: (p.is_file(), p.name.lower()),
            )
        except PermissionError:
            return

        for index, entry in enumerate(entries):
            connector = "└── " if index == len(entries) - 1 else "├── "
            lines.append(f"{prefix}{connector}{entry.name}")
            if entry.is_dir():
                extension = "    " if index == len(entries) - 1 else "│   "
                walk(entry, prefix + extension)

    lines.append(root.name)
    walk(root)
    return "\n".join(lines)


def generate_repository_structure(root: Path) -> str:
    tree = render_tree(root)
    return "\n".join(
        [
            "# Repository Structure",
            "",
            f"> Auto-generated on {utc_now()}. Do not edit manually.",
            "",
            "```text",
            tree,
            "```",
            "",
        ]
    )


def extract_workflow_name(workflow_file: Path) -> str:
    for line in workflow_file.read_text(encoding="utf-8").splitlines():
        stripped = line.strip()
        if stripped.startswith("name:"):
            return stripped.split(":", 1)[1].strip()
    return workflow_file.stem


def generate_workflows_overview(root: Path) -> str:
    workflows_dir = root / ".github" / "workflows"
    workflow_files = sorted(workflows_dir.glob("*.yml")) + sorted(workflows_dir.glob("*.yaml"))

    lines = [
        "# Workflows Overview",
        "",
        f"> Auto-generated on {utc_now()}. Do not edit manually.",
        "",
        "| Workflow File | Name |",
        "|---|---|",
    ]

    for workflow_file in workflow_files:
        name = extract_workflow_name(workflow_file)
        rel = workflow_file.relative_to(root)
        lines.append(f"| `{rel}` | {name} |")

    lines.append("")
    return "\n".join(lines)


def generate_provider_registry(root: Path) -> str:
    src = root / "src"
    providers = sorted(
        [
            path.relative_to(root)
            for path in src.rglob("*.cs")
            if "Provider" in path.stem and not is_excluded(path, root)
        ]
    )

    lines = [
        "# Provider Registry",
        "",
        f"> Auto-generated on {utc_now()}. Do not edit manually.",
        "",
        "| Provider Candidate |",
        "|---|",
    ]

    for provider in providers:
        lines.append(f"| `{provider}` |")

    if not providers:
        lines.append("| _No provider files discovered_ |")

    lines.append("")
    return "\n".join(lines)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Generate documentation artifacts for repo structure and workflows.")
    parser.add_argument("--output", required=True, help="Path to output markdown file")
    parser.add_argument("--format", default="markdown", help="Output format (currently markdown only)")
    parser.add_argument("--workflows-only", action="store_true", help="Generate workflows overview")
    parser.add_argument("--providers-only", action="store_true", help="Generate provider registry")
    parser.add_argument("--extract-attributes", action="store_true", help="Reserved option for compatibility")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    if args.format.lower() != "markdown":
        raise SystemExit("Only markdown format is supported")

    root = Path.cwd()
    output_path = Path(args.output)
    output_path.parent.mkdir(parents=True, exist_ok=True)

    if args.workflows_only and args.providers_only:
        raise SystemExit("--workflows-only and --providers-only are mutually exclusive")

    if args.workflows_only:
        content = generate_workflows_overview(root)
    elif args.providers_only:
        content = generate_provider_registry(root)
    else:
        content = generate_repository_structure(root)

    output_path.write_text(content, encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
