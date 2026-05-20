#!/usr/bin/env python3
from __future__ import annotations

import argparse
import sys
from pathlib import Path
from typing import Any

import yaml


ALLOWED_TRANSITIONS = {"added", "moved", "split", "merged", "deprecated", "archived"}


def _load_yaml(path: Path) -> Any:
    with path.open("r", encoding="utf-8") as handle:
        return yaml.safe_load(handle) or {}


def _as_list(value: Any) -> list[Any]:
    return value if isinstance(value, list) else []


def validate(modules_path: Path, coverage_path: Path, repo_root: Path) -> list[str]:
    errors: list[str] = []
    modules_doc = _load_yaml(modules_path)
    coverage_doc = _load_yaml(coverage_path)

    modules = [m for m in _as_list(modules_doc.get("modules")) if isinstance(m, dict)]

    module_ids: list[str] = []
    for module in modules:
        module_id = module.get("id")
        if not module_id:
            errors.append("source-modules.yml entry missing required 'id'.")
            continue

        module_ids.append(module_id)
        module_path = module.get("path")
        if module_path:
            resolved = repo_root / module_path
            if not resolved.exists():
                errors.append(
                    f"source-modules.yml module '{module_id}' path does not exist: {module_path}"
                )

    seen: set[str] = set()
    duplicates: set[str] = set()
    for module_id in module_ids:
        if module_id in seen:
            duplicates.add(module_id)
        seen.add(module_id)

    for duplicate in sorted(duplicates):
        errors.append(f"source-modules.yml duplicate module id: {duplicate}")

    coverage_modules = _as_list(coverage_doc.get("readme_coverage", {}).get("modules"))
    moved_to_paths: set[str] = set()
    moved_from_paths: set[str] = set()

    for module in coverage_modules:
        transitions = _as_list(module.get("transitions"))
        for transition in transitions:
            transition_type = transition.get("type")
            if transition_type not in ALLOWED_TRANSITIONS:
                errors.append(
                    f"Invalid transition type '{transition_type}'. Allowed: {sorted(ALLOWED_TRANSITIONS)}"
                )

            roadmap = transition.get("roadmap", {})
            for field in ("id", "url", "status"):
                if not roadmap.get(field):
                    errors.append(
                        f"Transition '{transition_type}' for module '{module.get('id', '<unknown>')}' missing roadmap.{field}"
                    )

            if transition_type == "moved":
                from_path = transition.get("from_path")
                to_path = transition.get("to_path")
                if from_path:
                    moved_from_paths.add(from_path)
                else:
                    errors.append("Moved transition missing from_path.")
                if to_path:
                    moved_to_paths.add(to_path)
                    if not (repo_root / to_path).exists():
                        errors.append(f"Moved transition to_path does not exist: {to_path}")
                else:
                    errors.append("Moved transition missing to_path.")

    for module in coverage_modules:
        readme_path = module.get("readme_path")
        if readme_path and not (repo_root / readme_path).exists():
            errors.append(f"README path does not exist: {readme_path}")
        if readme_path and readme_path in moved_from_paths and readme_path not in moved_to_paths:
            errors.append(
                f"Stale README path '{readme_path}' referenced after move/rename; update to current path."
            )

    return errors


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate source README coverage and module metadata.")
    parser.add_argument(
        "--modules",
        default="docs/source/data/source-modules.yml",
        help="Path to source modules YAML.",
    )
    parser.add_argument(
        "--coverage",
        default="docs/source/data/source-readme-coverage.yml",
        help="Path to README coverage YAML.",
    )
    parser.add_argument("--repo-root", default=".", help="Repository root for path resolution.")
    args = parser.parse_args()

    errors = validate(Path(args.modules), Path(args.coverage), Path(args.repo_root))
    if errors:
        for error in errors:
            print(f"ERROR: {error}")
        return 1

    print("Source documentation validation passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
