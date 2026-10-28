#!/usr/bin/env python3
"""Validate generated documentation and source README hash alignment."""

from __future__ import annotations

import argparse
import copy
import importlib.util
import json
import os
import re
import sys
from pathlib import Path

from common import EXCLUDE_DIRS, Finding, emit_findings, load_data, repo_path, repo_root, sha256_manifest_file, sha256_text, write_text_if_changed
from reviewed_doc_fingerprints import (
    LEGACY_VERSION, REVIEW_VERSION, generated_blocks, manifest_version, metadata,
    normalized_text, reviewed_readme_hash,
)

HASH_MANIFEST = Path("docs/source/generated/source-hash-manifest.json")
SOURCE_SUFFIXES = {
    ".cs",
    ".csproj",
    ".fs",
    ".fsproj",
    ".props",
    ".targets",
    ".ts",
    ".tsx",
    ".js",
    ".jsx",
    ".json",
    ".mjs",
    ".cjs",
    ".html",
    ".css",
    ".scss",
    ".xaml",
    ".xml",
}
SOURCE_EXCLUDES = EXCLUDE_DIRS | {"dist", "coverage", "wwwroot"}


def iter_source_files(path: Path) -> list[Path]:
    files: list[Path] = []
    if not path.exists():
        return files
    for current, dirs, filenames in os.walk(path):
        dirs[:] = [directory for directory in dirs if directory not in SOURCE_EXCLUDES]
        current_path = Path(current)
        for filename in filenames:
            candidate = current_path / filename
            if candidate.suffix.lower() in SOURCE_SUFFIXES:
                files.append(candidate)
    # Path ordering folds case on Windows but is ordinal on Linux. Hash the same
    # repository-relative POSIX ordering on both hosts, independent of checkout root.
    return sorted(files, key=lambda file_path: file_path.relative_to(path).as_posix())


def tree_hash(root: Path, module_path: Path) -> str:
    parts: list[str] = []
    for file_path in iter_source_files(module_path):
        # Git text checkouts can use LF or CRLF; the source content is unchanged.
        # The shared manifest hasher normalizes UTF-8 text and preserves binary bytes.
        parts.append(f"{repo_path(file_path, root)}:{sha256_manifest_file(file_path)}")
    return sha256_text("\n".join(parts))


def build_manifest(root: Path, schema_version: str | None = None) -> dict:
    if schema_version is None:
        existing = root / HASH_MANIFEST
        schema_version = manifest_version(load_json(existing)) if existing.exists() else LEGACY_VERSION
    header = metadata(schema_version)
    modules_data = load_data(root / "docs/source/data/source-modules.yml")
    modules = []
    for module in sorted(modules_data.get("modules", []), key=lambda item: item["id"]):
        module_path = root / module["path"]
        readme_path = root / module["readme"]
        modules.append(
            {
                "id": module["id"],
                "path": module["path"],
                "readme": module["readme"],
                "source_hash": tree_hash(root, module_path),
                "readme_hash": (
                    reviewed_readme_hash(readme_path.read_bytes(), module["id"])
                    if schema_version == REVIEW_VERSION else sha256_manifest_file(readme_path)
                ) if readme_path.exists() else None,
            }
        )
    return {**header, "modules": modules}


def load_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def validate_generated_manifests(root: Path) -> list[Finding]:
    findings: list[Finding] = []
    for manifest_path in [
        root / "docs/roadmap/generated/MANIFEST.json",
        root / "docs/source/generated/MANIFEST.json",
    ]:
        if not manifest_path.exists():
            findings.append(Finding("error", repo_path(manifest_path, root), "generated manifest is missing"))
            continue
        manifest = load_json(manifest_path)
        for section in ["inputs", "outputs"]:
            for entry in manifest.get(section, []):
                path = root / entry["path"]
                if not path.exists():
                    findings.append(Finding("error", repo_path(manifest_path, root), f"{section} path is missing: {entry['path']}"))
                    continue
                actual = sha256_manifest_file(path)
                if actual != entry.get("sha256"):
                    findings.append(Finding("error", entry["path"], f"{section} hash mismatch; rerun the owning renderer"))
    return findings


def validate_source_hash_manifest(root: Path, actual_manifest: dict) -> list[Finding]:
    manifest_path = root / HASH_MANIFEST
    if not manifest_path.exists():
        return [Finding("error", repo_path(manifest_path, root), "source hash manifest is missing; run with --write")]
    expected = load_json(manifest_path)
    if manifest_version(expected) != manifest_version(actual_manifest):
        return [Finding("error", repo_path(manifest_path, root), "fingerprint versions differ; use the explicit reviewed README migration")]
    findings: list[Finding] = []
    expected_by_id = {entry["id"]: entry for entry in expected.get("modules", [])}
    for actual in actual_manifest.get("modules", []):
        expected_entry = expected_by_id.get(actual["id"])
        if expected_entry is None:
            findings.append(Finding("error", repo_path(manifest_path, root), f"missing module hash entry {actual['id']}"))
            continue
        if actual["source_hash"] != expected_entry.get("source_hash"):
            findings.append(Finding("error", actual["path"], "source hash drift; update docs or rerun hash manifest after review"))
        if actual["readme_hash"] != expected_entry.get("readme_hash"):
            findings.append(Finding("error", actual["readme"], "README hash drift; rerun source docs and refresh hash manifest after review"))
    return findings


def validate_readme_generated_blocks(root: Path) -> list[Finding]:
    """Check renderer-owned bytes without making them part of review evidence."""
    path = Path(__file__).with_name("render-source-docs.py")
    spec = importlib.util.spec_from_file_location("reviewed_readme_renderer", path)
    renderer = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(renderer)
    modules, todos, roadmap = renderer.module_maps(root)
    findings = []
    for module in modules.get("modules", []):
        readme = root / module["readme"]
        if not readme.exists():
            findings.append(Finding("error", module["readme"], "source README is missing"))
            continue
        text = normalized_text(readme.read_bytes())
        spans = generated_blocks(text, module["id"])
        trace, checklist = renderer.readme_blocks(module, todos, roadmap)
        replacements = {"source-roadmap-traceability": trace, "source-todos": checklist}
        for start, stop, begin, _ in spans:
            name = next(name for name in replacements if begin.startswith(f"<!-- {name}:"))
            if text[start:stop] != "\n" + replacements[name].rstrip() + "\n":
                findings.append(Finding("error", module["readme"], f"{name} generated block is stale; rerun render-source-docs.py"))
    return findings


def migrate_reviewed_readmes(root: Path, *, apply: bool = True) -> tuple[bool, int]:
    """Convert proven v1 README bytes, preserving every accepted source hash.

    Source drift can remain visible after migration. README drift cannot be
    blessed: every old full-content fingerprint must match before any write.
    """
    path = root / HASH_MANIFEST
    expected = load_json(path)
    if manifest_version(expected) != LEGACY_VERSION:
        raise ValueError("Reviewed README migration requires a v1 manifest")
    actual = build_manifest(root, LEGACY_VERSION)
    actual_by_id = {entry["id"]: entry for entry in actual["modules"]}
    old_entries = expected.get("modules")
    if not isinstance(old_entries, list):
        raise ValueError("Reviewed modules must be a list")
    old_ids = [entry.get("id") for entry in old_entries if isinstance(entry, dict)]
    if len(old_ids) != len(old_entries) or len(set(old_ids)) != len(old_ids) or set(old_ids) != set(actual_by_id):
        raise ValueError("Migration requires exactly the registered reviewed module entries")
    migrated = []
    for old in old_entries:
        current = actual_by_id[old["id"]]
        if set(old) != set(current) or any(old[key] != current[key] for key in ("id", "path", "readme")):
            raise ValueError(f"{old['id']}: module identity changed; review before migration")
        if not isinstance(old["source_hash"], str) or not re.fullmatch("[0-9a-f]{64}", old["source_hash"]):
            raise ValueError(f"{old['id']}: invalid accepted source fingerprint")
        if old["readme_hash"] is None or old["readme_hash"] != current["readme_hash"]:
            raise ValueError(f"{old['id']}: v1 README bytes do not match the reviewed baseline; review before migration")
        readme = root / current["readme"]
        if readme.is_symlink() or not readme.resolve().is_relative_to(root.resolve()):
            raise ValueError(f"{old['id']}: README must be inside the checkout without a symlink")
        entry = copy.deepcopy(old)
        entry["readme_hash"] = reviewed_readme_hash(readme.read_bytes(), old["id"])
        migrated.append(entry)
    result = {**metadata(REVIEW_VERSION), "modules": sorted(migrated, key=lambda entry: entry["id"])}
    if not apply:
        return False, len(migrated)
    return write_text_if_changed(path, json.dumps(result, indent=2, sort_keys=True)), len(migrated)


def refresh_source_hash_manifest(root: Path, actual_manifest: dict, module_ids: list[str] | None = None) -> tuple[bool, int]:
    manifest_path = root / HASH_MANIFEST
    if not module_ids:
        return write_text_if_changed(manifest_path, json.dumps(actual_manifest, indent=2, sort_keys=True)), len(actual_manifest.get("modules", []))

    requested = set(module_ids)
    actual_by_id = {entry["id"]: entry for entry in actual_manifest.get("modules", [])}
    missing = sorted(requested - set(actual_by_id))
    if missing:
        raise ValueError(f"unknown source module id(s): {', '.join(missing)}")

    if manifest_path.exists():
        next_manifest = load_json(manifest_path)
        if manifest_version(next_manifest) != manifest_version(actual_manifest):
            raise ValueError("Narrow writes cannot change fingerprint schema; use the explicit reviewed README migration")
    else:
        next_manifest = {
            "schema": actual_manifest["schema"],
            "generator": actual_manifest["generator"],
            "contract": actual_manifest["contract"],
            "modules": [],
        }

    modules = []
    seen = set()
    for entry in next_manifest.get("modules", []):
        module_id = entry.get("id")
        if module_id in requested:
            modules.append(actual_by_id[module_id])
            seen.add(module_id)
        else:
            modules.append(entry)

    for module_id in sorted(requested - seen):
        modules.append(actual_by_id[module_id])

    next_manifest["schema"] = actual_manifest["schema"]
    next_manifest["generator"] = actual_manifest["generator"]
    next_manifest["contract"] = actual_manifest["contract"]
    next_manifest["modules"] = sorted(modules, key=lambda item: item["id"])
    changed = write_text_if_changed(manifest_path, json.dumps(next_manifest, indent=2, sort_keys=True))
    return changed, len(requested)


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate documentation hash alignment.")
    parser.add_argument("--root", default=str(repo_root()), help="Repository root.")
    write_group = parser.add_mutually_exclusive_group()
    write_group.add_argument("--write", action="store_true", help="Refresh every docs/source/generated/source-hash-manifest.json module entry.")
    write_group.add_argument(
        "--migrate-reviewed-readmes", action="store_true",
        help="Explicitly migrate verified v1 README bytes to v2 handwritten fingerprints without refreshing accepted source hashes.",
    )
    write_group.add_argument(
        "--check-readme-migration", action="store_true",
        help="Verify v1 README migration preconditions without changing the reviewed manifest.",
    )
    write_group.add_argument(
        "--write-module",
        action="append",
        metavar="MODULE_ID",
        help="Refresh only the named registered source module hash entry. Repeat for multiple reviewed modules.",
    )
    parser.add_argument("--summary", action="store_true", help="Print compact summary output.")
    args = parser.parse_args()

    root = repo_root(args.root)
    if args.migrate_reviewed_readmes or args.check_readme_migration:
        try:
            changed, count = migrate_reviewed_readmes(root, apply=args.migrate_reviewed_readmes)
        except (ValueError, OSError) as error:
            print(f"ERROR: {error}", file=sys.stderr)
            return 1
        action = "migrated" if args.migrate_reviewed_readmes else "eligible for migration"
        print(f"reviewed README fingerprints {action}: {count} module(s), {int(changed)} file(s) changed; accepted source hashes preserved")
        return 0
    try:
        actual_manifest = build_manifest(root)
    except (ValueError, OSError) as error:
        print(f"ERROR: {error}", file=sys.stderr)
        return 1
    if args.write or args.write_module:
        try:
            changed, refreshed = refresh_source_hash_manifest(root, actual_manifest, args.write_module)
        except ValueError as error:
            print(f"ERROR: {error}", file=sys.stderr)
            return 1
        if args.summary:
            scope = f"{refreshed} module entr{'y' if refreshed == 1 else 'ies'}" if args.write_module else "all module entries"
            print(f"doc hash manifest refreshed ({scope}): {1 if changed else 0} file(s) changed")
        return 0

    findings = validate_generated_manifests(root)
    findings.extend(validate_source_hash_manifest(root, actual_manifest))
    if manifest_version(actual_manifest) == REVIEW_VERSION:
        try:
            findings.extend(validate_readme_generated_blocks(root))
        except (ValueError, OSError) as error:
            findings.append(Finding("error", str(HASH_MANIFEST), f"generated README freshness could not be verified: {error}"))
    return emit_findings(findings, args.summary, "documentation hash validation")


if __name__ == "__main__":
    raise SystemExit(main())
