#!/usr/bin/env python3
"""Generate lightweight repository structure and workflow/provider documentation.

This script intentionally has no third-party dependencies so it can run in CI.
"""

from __future__ import annotations

import argparse
import fnmatch
import os
from pathlib import Path


EXCLUDED_DIR_NAMES = {
    ".ai",
    ".artifacts",
    ".git",
    ".idea",
    ".mypy_cache",
    ".nuget",
    ".playwright-cli",
    ".pytest_cache",
    ".ruff_cache",
    ".tmp",
    ".vs",
    "__pycache__",
    "artifacts",
    "bin",
    "coverage",
    "logs",
    "node_modules",
    "obj",
    "obj-codex",
    "output",
    "archive",
    "TestResults",
    "wwwroot",
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
    "*_wpftmp.csproj",
)
EXCLUDED_ROOT_FILE_NAMES = {
    "package-lock.json",
}
EXCLUDED_FILE_NAMES = {
    "appsettings.json",
    # Runtime scheduler artifact (present only while a session has a cron scheduled); excluding it keeps
    # the generated tree deterministic across local and CI checkouts.
    "scheduled_tasks.lock",
}
REPOSITORY_DISPLAY_NAME = "Meridian-main"
STABLE_GENERATED_AT = "1970-01-01 00:00:00 UTC"


def utc_now() -> str:
    return STABLE_GENERATED_AT


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
        if len(relative_parts) == 1 and relative_parts[0] in EXCLUDED_ROOT_FILE_NAMES:
            return True

    name = path.name
    if name in EXCLUDED_FILE_NAMES:
        return True

    if path.is_file():
        if name in EXCLUDED_FILE_NAMES:
            return True
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

    lines.append(REPOSITORY_DISPLAY_NAME)
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


def strip_yaml_scalar(value: str) -> str:
    return value.strip().strip('"\'')


def extract_workflow_name(workflow_file: Path) -> str:
    for line in workflow_file.read_text(encoding="utf-8").splitlines():
        stripped = line.strip()
        if stripped.startswith("name:"):
            return strip_yaml_scalar(stripped.split(":", 1)[1])
    return workflow_file.stem


def extract_workflow_triggers(workflow_file: Path) -> list[str]:
    lines = workflow_file.read_text(encoding="utf-8").splitlines()
    triggers: list[str] = []

    for index, line in enumerate(lines):
        if not line.startswith("on:"):
            continue

        inline = line.split(":", 1)[1].strip()
        if inline.startswith("[") and inline.endswith("]"):
            return [strip_yaml_scalar(item) for item in inline.strip("[]").split(",") if item.strip()]
        if inline:
            return [strip_yaml_scalar(inline)]

        for child in lines[index + 1 :]:
            if not child.strip() or child.lstrip().startswith("#"):
                continue
            indent = len(child) - len(child.lstrip(" "))
            if indent == 0:
                break
            if indent > 2:
                continue
            stripped = child.strip()
            if stripped.startswith("-"):
                trigger = strip_yaml_scalar(stripped[1:].strip())
            else:
                trigger = strip_yaml_scalar(stripped.split(":", 1)[0])
            if trigger and trigger not in triggers:
                triggers.append(trigger)
        break

    return triggers


def format_workflow_triggers(triggers: list[str]) -> str:
    if not triggers:
        return "Not declared"
    labels = {
        "pull_request": "pull_request",
        "push": "push",
        "schedule": "schedule",
        "workflow_call": "workflow_call",
        "workflow_dispatch": "workflow_dispatch",
    }
    return ", ".join(labels.get(trigger, trigger) for trigger in triggers)


def generate_workflows_overview(root: Path) -> str:
    workflows_dir = root / ".github" / "workflows"
    workflow_files = sorted(workflows_dir.glob("*.yml")) + sorted(workflows_dir.glob("*.yaml"))

    lines = [
        "# Workflows Overview",
        "",
        "> Auto-generated by `build/scripts/docs/generate-structure-docs.py --workflows-only`. Do not edit manually.",
        "",
        "This inventory is generated from `.github/workflows/*.yml` and `.github/workflows/*.yaml` on disk.",
        "",
        f"- Workflow count: `{len(workflow_files)}`",
        "",
        "| Workflow File | Name | Triggers |",
        "|---|---|---|",
    ]

    for workflow_file in workflow_files:
        name = extract_workflow_name(workflow_file)
        triggers = format_workflow_triggers(extract_workflow_triggers(workflow_file))
        rel = workflow_file.relative_to(root).as_posix()
        lines.append(f"| `{rel}` | {name} | {triggers} |")

    lines.append("")
    return "\n".join(lines)


def generate_provider_registry(root: Path) -> str:
    src = root / "src"
    providers: list[Path] = []

    for dirpath, dirnames, filenames in os.walk(src):
        current = Path(dirpath)
        dirnames[:] = [
            dirname
            for dirname in dirnames
            if not is_excluded(current / dirname, root)
        ]

        for filename in filenames:
            path = current / filename
            if path.suffix != ".cs":
                continue
            if "Provider" not in path.stem:
                continue
            if is_excluded(path, root):
                continue
            providers.append(path.relative_to(root))

    providers = sorted(
        providers,
        key=lambda path: path.as_posix().lower(),
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
        lines.append(f"| `{provider.as_posix()}` |")

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
