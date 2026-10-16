#!/usr/bin/env python3
"""Merge existing reviewed module fingerprints; never create a new review baseline.

Every selected entry must be identical to a parent's reviewed entry and describe
exactly the final checkout. A clean textual Git merge does not prove this.
"""
from __future__ import annotations

import argparse
import importlib.util
import json
import os
import re
import subprocess
import sys
from pathlib import Path, PurePosixPath

MANIFEST = "docs/source/generated/source-hash-manifest.json"
REGISTRY = "docs/source/data/source-modules.yml"
SCHEMA = {"id": "meridian.source-doc-hashes", "version": "1.0.0"}
GENERATOR = {"name": "build/scripts/docs/validate-doc-hashes.py", "version": "1.1.0"}
ENTRY_KEYS = {"id", "path", "readme", "source_hash", "readme_hash"}


def load_validator():
    scripts = Path(__file__).resolve().parent / "docs"
    # Load the reviewed implementation accompanying this command, never arbitrary
    # code from the checkout being inspected (also used by the trusted writer).
    sys.path.insert(0, str(scripts))
    try:
        spec = importlib.util.spec_from_file_location("reviewed_hash_validator", scripts / "validate-doc-hashes.py")
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        return module
    finally:
        sys.path.pop(0)


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"Duplicate JSON key: {key}")
        result[key] = value
    return result


def safe_path(value):
    if (not isinstance(value, str) or not value or "\\" in value
            or PurePosixPath(value).is_absolute() or any(part in {"", ".", ".."} for part in value.split("/"))):
        raise ValueError(f"Unsafe repository path: {value!r}")
    return value


def parse_manifest(content: bytes | None, label: str) -> dict | None:
    if content is None:
        return None
    try:
        manifest = json.loads(content, object_pairs_hook=unique_object)
        if not isinstance(manifest, dict) or set(manifest) != {"schema", "generator", "contract", "modules"}:
            raise ValueError("Expected schema, generator, contract and modules only")
        if manifest["schema"] != SCHEMA or manifest["generator"] != GENERATOR:
            raise ValueError("Unsupported schema or hash generator version")
        if not isinstance(manifest["contract"], str) or not manifest["contract"]:
            raise ValueError("Missing hash contract")
        module_map(manifest)
        return manifest
    except (ValueError, TypeError, KeyError, UnicodeError) as exc:
        raise ValueError(f"Invalid {label} reviewed manifest: {exc}") from exc


def module_map(manifest: dict | None) -> dict:
    if manifest is None:
        return {}
    modules = manifest.get("modules")
    if not isinstance(modules, list):
        raise ValueError("modules must be a list")
    result = {}
    for entry in modules:
        if not isinstance(entry, dict) or set(entry) != ENTRY_KEYS:
            raise ValueError("Malformed module entry")
        identity = entry["id"]
        if not isinstance(identity, str) or not identity or identity in result:
            raise ValueError(f"Missing or duplicate module ID: {identity!r}")
        safe_path(entry["path"])
        safe_path(entry["readme"])
        for field in ("source_hash", "readme_hash"):
            value = entry[field]
            if field == "readme_hash" and value is None:
                continue
            if not isinstance(value, str) or re.fullmatch(r"[0-9a-f]{64}", value) is None:
                raise ValueError(f"Invalid {field} for {identity}")
        result[identity] = entry
    return result


def contained_path(root: Path, relative: str) -> Path:
    path = root / safe_path(relative)
    if not path.resolve().is_relative_to(root.resolve()):
        raise ValueError(f"Path escapes checkout: {relative}")
    if path.is_symlink() or any(parent.is_symlink() for parent in path.parents if parent != root and parent.is_relative_to(root)):
        raise ValueError(f"Path must not contain symlinks: {relative}")
    return path


def actual_manifest(root: Path) -> dict:
    validator = load_validator()
    registry_path = contained_path(root, REGISTRY)
    registry = validator.load_data(registry_path)
    if not isinstance(registry, dict) or not isinstance(registry.get("modules"), list):
        raise ValueError("Module registry must contain a modules list")
    identities = set()
    for module in registry["modules"]:
        if not isinstance(module, dict) or not isinstance(module.get("id"), str) or not module["id"]:
            raise ValueError("Module registry entry has no ID")
        if module["id"] in identities:
            raise ValueError(f"Duplicate registry module ID: {module['id']}")
        identities.add(module["id"])
        for key in ("path", "readme"):
            contained_path(root, module.get(key))
        module_path = root / module["path"]
        if not module_path.is_dir():
            raise ValueError(f"Registered module directory is missing: {module['path']}")
        for current, directories, files in os.walk(module_path):
            directories[:] = [name for name in directories if name not in validator.SOURCE_EXCLUDES]
            if any((Path(current) / name).is_symlink() for name in directories + files):
                raise ValueError(f"Module contains symlinks: {module['id']}")
    actual = validator.build_manifest(root)
    module_map(actual)
    return actual


def merge_manifests(base: dict | None, ours: dict | None, theirs: dict | None, actual: dict) -> tuple[dict | None, dict]:
    """Three-way keyed merge, verified against the final module registry/content."""
    maps = [module_map(value) for value in (base, ours, theirs)]
    final = module_map(actual)
    decisions, errors, merged = {}, {}, []
    for identity in sorted(set().union(final, *maps)):
        ancestor, left, right = (mapping.get(identity) for mapping in maps)
        current = final.get(identity)
        if left == right:
            candidate, origin = left, "both"
        elif left == ancestor:
            candidate, origin = right, "base_parent"
        elif right == ancestor:
            candidate, origin = left, "head_parent"
        else:
            matches = [(value, label) for value, label in ((left, "head_parent"), (right, "base_parent")) if value == current and value is not None]
            if not matches:
                errors[identity] = "Both reviewed entries changed; merged module matches neither reviewed parent"
                continue
            candidate, origin = matches[0]
        if candidate != current:
            errors[identity] = "Merged module/README differs from the selected reviewed entry; module-specific review required"
            continue
        decisions[identity] = "deleted" if candidate is None else origin
        if candidate is not None:
            merged.append(candidate)
    metadata = [{key: value for key, value in manifest.items() if key != "modules"} if manifest else None for manifest in (base, ours, theirs)]
    ancestor, left, right = metadata
    if left == right or right == ancestor:
        selected = left
    elif left == ancestor:
        selected = right
    else:
        selected = None
        errors["$manifest"] = "Conflicting manifest metadata requires review"
    if selected is None and merged:
        errors["$manifest"] = "No reviewed parent manifest exists"
    result = None if selected is None else {**selected, "modules": merged}
    report = {"successful": not errors, "decisions": decisions, "review_required": errors}
    return result, report


def git(root: Path, *args: str) -> bytes:
    return subprocess.check_output(["git", "--literal-pathspecs", *args], cwd=root, stderr=subprocess.PIPE)


def manifest_at(root: Path, revision: str) -> dict | None:
    # ls-tree distinguishes a deliberately absent manifest from invalid refs or
    # inaccessible Git objects; never turn a Git failure into a deletion.
    if not git(root, "ls-tree", "-z", revision, "--", MANIFEST):
        return None
    return parse_manifest(git(root, "show", f"{revision}:{MANIFEST}"), revision)


def merge_checkout(root: Path, ancestor: str, head: str, base: str, *, apply: bool = False) -> dict:
    values = [manifest_at(root, revision) for revision in (ancestor, head, base)]
    if all(value is None for value in values) and not (root / REGISTRY).exists():
        return {"successful": True, "decisions": {}, "review_required": {}, "not_applicable": True}
    actual = actual_manifest(root)
    merged, report = merge_manifests(*values, actual)
    if report["successful"] and apply:
        path = contained_path(root, MANIFEST)
        if merged is None:
            path.unlink(missing_ok=True)
        else:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(json.dumps(merged, indent=2, sort_keys=True) + "\n", encoding="utf-8")
        if path.exists() or git(root, "ls-files", "--", MANIFEST):
            git(root, "add", "-A", "--", MANIFEST)
    return report


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, default=Path.cwd())
    parser.add_argument("--ancestor", required=True)
    parser.add_argument("--head", required=True)
    parser.add_argument("--base", required=True)
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()
    try:
        refs = [git(args.repo, "rev-parse", "--verify", "--end-of-options", f"{value}^{{commit}}").decode().strip() for value in (args.ancestor, args.head, args.base)]
        report = merge_checkout(args.repo.resolve(), *refs, apply=args.apply)
        print(json.dumps(report, indent=2))
        return 0 if report["successful"] else 1
    except (ValueError, OSError, subprocess.CalledProcessError) as exc:
        print(f"Reviewed hash merge failed: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
