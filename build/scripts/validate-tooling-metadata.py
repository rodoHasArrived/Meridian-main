#!/usr/bin/env python3
"""Validate high-friction tooling metadata references.

Checks the repo metadata that most often drifts:
- package.json script file and --prefix targets
- Makefile and make/*.mk include/helper script paths
- .github/dependabot.yml configured directories
"""

from __future__ import annotations

import argparse
import importlib.util
import json
import re
import shlex
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any

REPO_ROOT = Path(__file__).resolve().parents[2]
EXECUTORS = {"bash", "node", "python", "python3", "sh"}
POWERSHELL_EXECUTORS = {"powershell", "pwsh"}
CANONICAL_NODE_TOOL_TARGETS = ("generate-icons", "generate-diagrams")
MAKE_TARGET_PATTERN = re.compile(r"^([A-Za-z0-9][A-Za-z0-9_-]+)\s*:(?![=])")
MAKE_TARGET_HELP_PATTERN = re.compile(r"^([A-Za-z0-9][A-Za-z0-9_-]+)\s*:.*?##\s*(.+)$")


@dataclass(frozen=True)
class ToolingReference:
    source: str
    path: str
    kind: str = "path"

    @property
    def expect_dir(self) -> bool:
        return self.kind == "directory"


@dataclass(frozen=True)
class MakeTarget:
    name: str
    source: str
    line: int
    has_help: bool


def _try_import_yaml() -> Any:
    try:
        import yaml  # type: ignore[import-untyped]

        return yaml
    except ImportError:
        return None


def _repo_path(root: Path, rel_path: str) -> Path:
    normalized = rel_path.strip()
    if normalized == "/":
        return root
    return root / normalized.lstrip("/")


def _is_repo_path(token: str) -> bool:
    token = token.strip().strip("'\"")
    if not token or token.startswith(("$", "-", "http://", "https://")):
        return False
    return "/" in token or token.startswith(".")


def _clean_token(token: str) -> str:
    return token.strip().strip("'\"").rstrip(",;)")


def _tokenize(line: str) -> list[str]:
    line = line.strip().lstrip("@-+")
    try:
        return shlex.split(line, comments=False, posix=True)
    except ValueError:
        return line.split()


def _logical_make_lines(text: str) -> list[str]:
    lines: list[str] = []
    current = ""
    for raw_line in text.splitlines():
        line = raw_line.rstrip()
        if line.endswith("\\"):
            current += line[:-1] + " "
            continue
        lines.append(current + line)
        current = ""
    if current:
        lines.append(current)
    return lines


def _makefiles(root: Path) -> list[Path]:
    make_dir = root / "make"
    return [root / "Makefile", *sorted(make_dir.glob("*.mk"))]


def _load_make_help_categories(root: Path = REPO_ROOT) -> tuple[tuple[str, str], ...]:
    renderer = root / "build" / "scripts" / "docs" / "render-make-help.py"
    spec = importlib.util.spec_from_file_location("render_make_help", renderer)
    if spec is None or spec.loader is None:
        return ()
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    categories = getattr(module, "CATEGORIES", ())
    return tuple((str(name), str(pattern)) for name, pattern in categories)


def _phony_targets(text: str) -> set[str]:
    phonies: set[str] = set()
    lines = text.splitlines()
    for index, line in enumerate(lines):
        if not line.startswith(".PHONY:"):
            continue
        chunk = line.split(":", 1)[1]
        next_index = index + 1
        while chunk.rstrip().endswith("\\") and next_index < len(lines):
            chunk = chunk.rstrip()[:-1] + " " + lines[next_index].strip()
            next_index += 1
        phonies.update(chunk.split())
    return phonies


def load_makefile_targets(root: Path = REPO_ROOT) -> tuple[list[MakeTarget], set[str]]:
    targets: list[MakeTarget] = []
    phonies: set[str] = set()
    for makefile in _makefiles(root):
        if not makefile.exists():
            continue
        source = makefile.relative_to(root).as_posix()
        text = makefile.read_text(encoding="utf-8")
        phonies.update(_phony_targets(text))
        for line_number, line in enumerate(text.splitlines(), start=1):
            if line.startswith("\t") or not line.strip() or line.startswith(("#", ".")):
                continue
            if ":=" in line or "?=" in line or "+=" in line or line.startswith(("ifeq", "endif", "include")):
                continue
            match = MAKE_TARGET_PATTERN.match(line)
            if match:
                targets.append(
                    MakeTarget(
                        name=match.group(1),
                        source=source,
                        line=line_number,
                        has_help=MAKE_TARGET_HELP_PATTERN.match(line) is not None,
                    )
                )
    return targets, phonies


def load_package_references(root: Path = REPO_ROOT) -> list[ToolingReference]:
    package_json = root / "package.json"
    package = json.loads(package_json.read_text(encoding="utf-8"))
    refs: list[ToolingReference] = []

    for script_name, command in package.get("scripts", {}).items():
        tokens = _tokenize(str(command))
        for index, token in enumerate(tokens):
            if token == "--prefix" and index + 1 < len(tokens):
                path = _clean_token(tokens[index + 1])
                if _is_repo_path(path):
                    refs.append(ToolingReference(f"package.json script '{script_name}'", path, "directory"))
            if token in EXECUTORS and index + 1 < len(tokens):
                path = _clean_token(tokens[index + 1])
                if _is_repo_path(path):
                    refs.append(ToolingReference(f"package.json script '{script_name}'", path))
    return refs


def load_makefile_references(root: Path = REPO_ROOT) -> list[ToolingReference]:
    refs: list[ToolingReference] = []
    for makefile in _makefiles(root):
        if not makefile.exists():
            continue
        source = makefile.relative_to(root).as_posix()
        for line in _logical_make_lines(makefile.read_text(encoding="utf-8")):
            include_match = re.match(r"\s*-?include\s+([^#\s]+)", line)
            if include_match:
                path = _clean_token(include_match.group(1))
                if _is_repo_path(path):
                    refs.append(ToolingReference(f"Makefile include ({source})", path))

            tokens = _tokenize(line)
            for index, token in enumerate(tokens):
                if token in EXECUTORS and index + 1 < len(tokens):
                    path = _clean_token(tokens[index + 1])
                    if _is_repo_path(path):
                        refs.append(ToolingReference(f"Makefile helper ({source})", path))
                if token in POWERSHELL_EXECUTORS:
                    for option_index, option in enumerate(tokens[index + 1 :], start=index + 1):
                        if option.lower() == "-file" and option_index + 1 < len(tokens):
                            path = _clean_token(tokens[option_index + 1])
                            if _is_repo_path(path):
                                refs.append(ToolingReference(f"Makefile helper ({source})", path))
                            break
    return refs


def _minimal_dependabot_directories(text: str) -> list[str]:
    directories: list[str] = []
    for line in text.splitlines():
        match = re.match(r"\s*directory:\s*['\"]?([^'\"#\n]+)['\"]?", line)
        if match:
            directories.append(match.group(1).strip())
    return directories


def load_dependabot_references(root: Path = REPO_ROOT) -> list[ToolingReference]:
    dependabot = root / ".github" / "dependabot.yml"
    text = dependabot.read_text(encoding="utf-8")
    directories: list[str] = []
    yaml = _try_import_yaml()
    if yaml is not None:
        data = yaml.safe_load(text) or {}
        updates = data.get("updates", []) if isinstance(data, dict) else []
        directories = [str(item.get("directory")) for item in updates if isinstance(item, dict) and item.get("directory")]
    else:
        directories = _minimal_dependabot_directories(text)

    return [ToolingReference(".github/dependabot.yml", directory, "directory") for directory in directories]


def load_makefile_target_recipes(root: Path = REPO_ROOT) -> dict[str, str]:
    recipes: dict[str, str] = {}
    target_pattern = re.compile(r"^([A-Za-z0-9_-]+):[^=]*$", re.MULTILINE)
    for makefile in _makefiles(root):
        if not makefile.exists():
            continue
        lines = makefile.read_text(encoding="utf-8").splitlines()
        for index, line in enumerate(lines):
            match = target_pattern.match(line)
            if not match:
                continue
            target = match.group(1)
            recipe_lines: list[str] = []
            for recipe_line in lines[index + 1 :]:
                if recipe_line.startswith("\t"):
                    recipe_lines.append(recipe_line.strip())
                    continue
                if recipe_line and not recipe_line.startswith("#"):
                    break
            recipes[target] = "\n".join(recipe_lines)
    return recipes


def validate_canonical_node_entrypoints(root: Path = REPO_ROOT) -> list[str]:
    package = json.loads((root / "package.json").read_text(encoding="utf-8"))
    scripts = package.get("scripts", {})
    recipes = load_makefile_target_recipes(root)
    errors: list[str] = []

    for target in CANONICAL_NODE_TOOL_TARGETS:
        if target not in scripts:
            errors.append(f"package.json scripts: missing '{target}'")

        recipe = recipes.get(target, "")
        if not recipe:
            errors.append(f"Makefile target: missing '{target}'")
            continue
        if f"npm run {target}" not in recipe:
            errors.append(f"Makefile target '{target}' must call 'npm run {target}'")
        direct_node_pattern = rf"(?:^|[\s@])node\s+[^\n]*(?:{target}|generate-[^\s]+)\.(?:mjs|js)"
        if re.search(direct_node_pattern, recipe):
            errors.append(f"Makefile target '{target}' must not call node directly")

    return errors


def validate_makefile_targets(root: Path = REPO_ROOT) -> list[str]:
    targets, phonies = load_makefile_targets(root)
    errors: list[str] = []
    by_name: dict[str, list[MakeTarget]] = {}
    for target in targets:
        by_name.setdefault(target.name, []).append(target)

    for name, definitions in sorted(by_name.items()):
        if len(definitions) > 1:
            locations = ", ".join(f"{definition.source}:{definition.line}" for definition in definitions)
            errors.append(f"Makefile target '{name}' is defined multiple times: {locations}")

    target_names = set(by_name)
    for name in sorted(target_names - phonies):
        definition = by_name[name][0]
        errors.append(f"Makefile target '{name}' is missing from .PHONY ({definition.source}:{definition.line})")

    for name in sorted(phonies - target_names):
        errors.append(f".PHONY lists missing Makefile target '{name}'")

    for target in targets:
        if not target.has_help:
            errors.append(f"Makefile target '{target.name}' is missing a help description ({target.source}:{target.line})")

    categories = _load_make_help_categories(root)
    if not categories:
        errors.append("Make help categories could not be loaded from build/scripts/docs/render-make-help.py")
        return errors

    uncategorized = sorted(name for name in target_names if not any(re.search(pattern, name) for _, pattern in categories))
    for name in uncategorized:
        errors.append(f"Makefile target '{name}' is not assigned to a make help category")

    return errors


def validate_references(references: list[ToolingReference], root: Path = REPO_ROOT) -> list[str]:
    errors: list[str] = []
    for reference in references:
        candidate = _repo_path(root, reference.path)
        exists = candidate.is_dir() if reference.expect_dir else candidate.exists()
        if not exists:
            errors.append(f"{reference.source}: missing {reference.kind} '{reference.path}'")
    return errors


def validate_tooling_metadata(root: Path = REPO_ROOT) -> list[str]:
    references: list[ToolingReference] = []
    references.extend(load_package_references(root))
    references.extend(load_makefile_references(root))
    references.extend(load_dependabot_references(root))
    errors = validate_references(references, root)
    errors.extend(validate_canonical_node_entrypoints(root))
    errors.extend(validate_makefile_targets(root))
    return errors


# Backwards-compatible helpers used by older tests.
def load_package_paths() -> list[str]:
    return [reference.path for reference in load_package_references() if not reference.expect_dir]


def load_makefile_paths() -> list[tuple[str, str]]:
    prefix = "Makefile helper ("
    paths: list[tuple[str, str]] = []
    for reference in load_makefile_references():
        if reference.source.startswith(prefix):
            paths.append((reference.source[len(prefix) : -1], reference.path))
    return paths


def load_dependabot_directories() -> list[str]:
    return [reference.path for reference in load_dependabot_references()]


def validate_paths(paths: list[str], label: str, expect_dir: bool = False) -> list[str]:
    kind = "directory" if expect_dir else "path"
    refs = [ToolingReference(label, path, kind) for path in paths]
    return validate_references(refs)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Validate tooling metadata paths.")
    parser.add_argument("--root", type=Path, default=REPO_ROOT, help="Repository root to validate.")
    args = parser.parse_args(argv)

    errors = validate_tooling_metadata(args.root.resolve())
    if errors:
        print("Tooling metadata validation failed:", file=sys.stderr)
        for error in errors:
            print(f"  - {error}", file=sys.stderr)
        return 1

    print("Tooling metadata validation passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
