#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Any

try:
    import yaml
except ImportError:  # pragma: no cover - optional for fixture-only mode
    yaml = None


# ---------------------------------------------------------------------------
# Canonical enum validation (shared with tools/roadmap/validate_roadmap.py)
# ---------------------------------------------------------------------------

CANONICAL = {
    "status": {"not_started", "in_progress", "blocked", "completed", "cancelled"},
    "health": {"green", "yellow", "red", "unknown"},
    "priority": {"low", "medium", "high", "critical"},
    "evidence_posture": {"none", "draft", "partial", "verified", "audited"},
    "completion_term": {"done", "complete", "completed", "closed", "shipped"},
}
REQUIRED_FIELDS = {"id", "name", "status", "health", "priority", "evidence_posture", "completion_term", "owner", "last_updated"}
ALLOWED_FIELDS = REQUIRED_FIELDS | {"summary", "exit_criteria", "links", "created_at", "notes", "extensions"}

REQUIRED_FRONT_MATTER_KEYS = ("module_id", "owner", "status", "last_verified")
REQUIRED_HEADINGS = (
    "Module Purpose",
    "Ownership and Runtime",
    "Dependencies and Integrations",
    "Operational Notes",
)
GENERATED_BLOCK_BEGIN = "<!-- GENERATED:MODULE_OVERVIEW BEGIN -->"
GENERATED_BLOCK_END = "<!-- GENERATED:MODULE_OVERVIEW END -->"

# ---------------------------------------------------------------------------
# Source README coverage / lifecycle-transition validation
# ---------------------------------------------------------------------------

ALLOWED_TRANSITIONS = {"added", "moved", "split", "merged", "deprecated", "archived"}


class ReadmeContractError(ValueError):
    pass


def _check(node: Any, path: str = "$") -> list[str]:
    errors: list[str] = []
    if isinstance(node, dict):
        for key, value in node.items():
            child_path = f"{path}.{key}"
            if key in CANONICAL and value not in CANONICAL[key]:
                allowed = ", ".join(sorted(CANONICAL[key]))
                errors.append(f"{child_path}: invalid '{value}' for {key}; allowed: [{allowed}]")
            errors.extend(_check(value, child_path))
    elif isinstance(node, list):
        for i, item in enumerate(node):
            errors.extend(_check(item, f"{path}[{i}]") )
    return errors


def validate_file(path: Path) -> int:
    data = json.loads(path.read_text(encoding="utf-8"))
    errors = _check(data)
    if isinstance(data, dict):
        missing = sorted(field for field in REQUIRED_FIELDS if field not in data)
        unexpected = sorted(field for field in data if field not in ALLOWED_FIELDS)
        for field in missing:
            errors.append(f"$.{field}: missing required field")
        for field in unexpected:
            errors.append(f"$.{field}: unexpected field")
    if errors:
        print(f"{path} failed enum validation:")
        for e in errors:
            print(f"  - {e}")
        return 1
    print(f"{path} passed enum validation")
    return 0


def _extract_front_matter(text: str) -> tuple[dict[str, Any], str]:
    if yaml is None:
        raise ReadmeContractError("PyYAML is required for README front matter validation")
    if not text.startswith("---\n"):
        raise ReadmeContractError("front matter opening marker '---' is missing")

    closing = text.find("\n---\n", 4)
    if closing == -1:
        raise ReadmeContractError("front matter closing marker '---' is missing")

    raw_front_matter = text[4:closing]
    remainder = text[closing + len("\n---\n") :]
    front_matter = yaml.safe_load(raw_front_matter) or {}
    if not isinstance(front_matter, dict):
        raise ReadmeContractError("front matter must be a YAML mapping")
    return front_matter, remainder


def validate_readme_contract(readme_path: Path, expected_module_id: str) -> list[str]:
    errors: list[str] = []
    text = readme_path.read_text(encoding="utf-8")

    try:
        front_matter, body = _extract_front_matter(text)
    except ReadmeContractError as ex:
        return [str(ex)]

    for key in REQUIRED_FRONT_MATTER_KEYS:
        if not front_matter.get(key):
            errors.append(f"missing front matter key '{key}'")

    module_id_value = front_matter.get("module_id")
    if module_id_value and module_id_value != expected_module_id:
        errors.append(
            f"front matter key 'module_id' value '{module_id_value}' does not match coverage module id '{expected_module_id}'"
        )

    headings = set(re.findall(r"^##\s+(.+?)\s*$", body, flags=re.MULTILINE))
    for heading in REQUIRED_HEADINGS:
        if heading not in headings:
            errors.append(f"missing required heading '## {heading}'")

    begin_index = body.find(GENERATED_BLOCK_BEGIN)
    end_index = body.find(GENERATED_BLOCK_END)
    if begin_index == -1:
        errors.append(f"missing generated block begin marker '{GENERATED_BLOCK_BEGIN}'")
    if end_index == -1:
        errors.append(f"missing generated block end marker '{GENERATED_BLOCK_END}'")
    if begin_index != -1 and end_index != -1 and begin_index > end_index:
        errors.append("generated block markers are reversed (begin marker appears after end marker)")

    return errors


def _load_yaml(path: Path) -> Any:
    if yaml is None:
        raise RuntimeError("PyYAML is required for source YAML validation mode.")
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
        if not module_path:
            errors.append(f"source-modules.yml module '{module_id}' missing required 'path'.")
        else:
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

    coverage_modules = [
        m
        for m in _as_list((coverage_doc.get("readme_coverage") or {}).get("modules"))
        if isinstance(m, dict)
    ]
    moved_to_paths: set[str] = set()
    moved_from_paths: set[str] = set()

    for module in coverage_modules:
        transitions = [t for t in _as_list(module.get("transitions")) if isinstance(t, dict)]
        for transition in transitions:
            transition_type = transition.get("type")
            if transition_type not in ALLOWED_TRANSITIONS:
                errors.append(
                    f"Invalid transition type '{transition_type}'. Allowed: {sorted(ALLOWED_TRANSITIONS)}"
                )

            roadmap = transition.get("roadmap") or {}
            module_id = module.get("id", "<unknown>")
            for field in ("id", "url", "status"):
                if not roadmap.get(field):
                    errors.append(
                        f"Transition '{transition_type}' for module '{module_id}' missing roadmap.{field}"
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
        module_id = str(module.get("id", "<unknown>"))
        readme_path = module.get("readme_path")
        if readme_path and not (repo_root / readme_path).exists():
            errors.append(f"README path does not exist: {readme_path}")
        if readme_path and readme_path in moved_from_paths and readme_path not in moved_to_paths:
            errors.append(
                f"Stale README path '{readme_path}' referenced after move/rename; update to current path."
            )
        if readme_path and (repo_root / readme_path).exists():
            readme_errors = validate_readme_contract(repo_root / readme_path, module_id)
            for element_error in readme_errors:
                errors.append(
                    f"README contract violation [module={module_id}] [path={readme_path}] [missing={element_error}]"
                )

    return errors


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Validate source README coverage and module metadata."
    )
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
    parser.add_argument(
        "--fixtures-dir",
        help="Validate canonical source-doc JSON fixtures from this directory (valid-*/invalid-* expectation by filename).",
    )
    args = parser.parse_args()

    if args.fixtures_dir:
        fixtures_dir = Path(args.fixtures_dir)
        if not fixtures_dir.exists():
            print(f"Error: fixtures directory not found: {fixtures_dir}", file=sys.stderr)
            return 2
        exit_code = 0
        for json_file in sorted(fixtures_dir.glob("*.json")):
            rc = validate_file(json_file)
            should_fail = json_file.name.startswith("invalid-")
            if should_fail and rc == 0:
                print(f"{json_file} expected failure but passed")
                exit_code = 1
            elif not should_fail and rc != 0:
                print(f"{json_file} expected pass but failed")
                exit_code = 1
            elif should_fail:
                print(f"{json_file} correctly failed")
            else:
                print(f"{json_file} correctly passed")
        return exit_code

    errors = validate(Path(args.modules), Path(args.coverage), Path(args.repo_root))
    if errors:
        for error in errors:
            print(f"ERROR: {error}")
        return 1

    print("Source documentation validation passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
