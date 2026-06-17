#!/usr/bin/env python3
"""Validate generated documentation and source README hash alignment."""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path

from common import EXCLUDE_DIRS, Finding, emit_findings, load_data, repo_path, repo_root, sha256_file, sha256_manifest_file, sha256_text, write_text_if_changed

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
    return sorted(files)


def tree_hash(root: Path, module_path: Path) -> str:
    parts: list[str] = []
    for file_path in iter_source_files(module_path):
        parts.append(f"{repo_path(file_path, root)}:{sha256_file(file_path)}")
    return sha256_text("\n".join(parts))


def build_manifest(root: Path) -> dict:
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
                "readme_hash": sha256_file(readme_path) if readme_path.exists() else None,
            }
        )
    return {
        "schema": {"id": "meridian.source-doc-hashes", "version": "1.0.0"},
        "generator": {"name": "build/scripts/docs/validate-doc-hashes.py", "version": "1.0.0"},
        "contract": "Hash drift means code or README content changed after the last source-doc hash refresh.",
        "modules": modules,
    }


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


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate documentation hash alignment.")
    parser.add_argument("--root", default=str(repo_root()), help="Repository root.")
    parser.add_argument("--write", action="store_true", help="Refresh docs/source/generated/source-hash-manifest.json.")
    parser.add_argument("--summary", action="store_true", help="Print compact summary output.")
    args = parser.parse_args()

    root = repo_root(args.root)
    actual_manifest = build_manifest(root)
    manifest_path = root / HASH_MANIFEST
    if args.write:
        changed = write_text_if_changed(manifest_path, json.dumps(actual_manifest, indent=2, sort_keys=True))
        if args.summary:
            print(f"doc hash manifest refreshed: {1 if changed else 0} file(s) changed")
        return 0

    findings = validate_generated_manifests(root)
    findings.extend(validate_source_hash_manifest(root, actual_manifest))
    return emit_findings(findings, args.summary, "documentation hash validation")


if __name__ == "__main__":
    raise SystemExit(main())
