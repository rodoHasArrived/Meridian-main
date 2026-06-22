#!/usr/bin/env python3
"""Mark source documentation that needs review based on hash drift."""

from __future__ import annotations

import argparse
import importlib.util
import json
import sys
from pathlib import Path

from common import markdown_table, repo_path, repo_root, write_text_if_changed

STALE_JSON = Path("docs/source/generated/stale-docs.json")
STALE_MARKDOWN = Path("docs/source/generated/stale-docs.md")
HASH_MANIFEST = Path("docs/source/generated/source-hash-manifest.json")


def load_hash_module():
    script_path = Path(__file__).with_name("validate-doc-hashes.py")
    spec = importlib.util.spec_from_file_location("validate_doc_hashes_for_stale_docs", script_path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Unable to load {script_path}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


def load_existing_manifest(root: Path) -> dict:
    path = root / HASH_MANIFEST
    if not path.exists():
        return {"modules": []}
    return json.loads(path.read_text(encoding="utf-8"))


def stale_reasons(actual: dict, expected: dict | None) -> list[str]:
    if expected is None:
        return ["missing_hash_entry"]
    reasons: list[str] = []
    if actual.get("source_hash") != expected.get("source_hash"):
        reasons.append("source_hash_drift")
    if actual.get("readme_hash") != expected.get("readme_hash"):
        reasons.append("readme_hash_drift")
    if actual.get("readme_hash") is None:
        reasons.append("missing_readme")
    return reasons


def recommended_actions(reasons: list[str]) -> list[str]:
    actions: list[str] = []
    if "missing_readme" in reasons:
        actions.append("create the registered source README")
    if "source_hash_drift" in reasons:
        actions.append("review source changes and update the nearest README, TODOs, diagrams, or registry if behavior changed")
    if "readme_hash_drift" in reasons:
        actions.append("review README changes and rerender generated blocks if needed")
    if "missing_hash_entry" in reasons:
        actions.append("review the module baseline and refresh hashes after docs are current")
    actions.append("run validate-doc-hashes.py --write-module <MODULE_ID> only after the docs/code alignment review is complete")
    return list(dict.fromkeys(actions))


def build_stale_report(root: Path) -> dict:
    hash_module = load_hash_module()
    actual_manifest = hash_module.build_manifest(root)
    expected_manifest = load_existing_manifest(root)
    expected_by_id = {entry["id"]: entry for entry in expected_manifest.get("modules", [])}
    actual_by_id = {entry["id"]: entry for entry in actual_manifest.get("modules", [])}

    stale_modules = []
    for actual in actual_manifest.get("modules", []):
        reasons = stale_reasons(actual, expected_by_id.get(actual["id"]))
        if reasons:
            stale_modules.append(
                {
                    "module_id": actual["id"],
                    "path": actual["path"],
                    "readme": actual["readme"],
                    "reasons": reasons,
                    "recommended_actions": recommended_actions(reasons),
                }
            )

    removed_modules = []
    for expected_id, expected in sorted(expected_by_id.items()):
        if expected_id not in actual_by_id:
            removed_modules.append(
                {
                    "module_id": expected_id,
                    "path": expected.get("path"),
                    "readme": expected.get("readme"),
                    "reasons": ["removed_module_hash_entry"],
                    "recommended_actions": [
                        "remove or migrate the stale source hash entry",
                        "refresh hashes after registry and generated docs are current",
                    ],
                }
            )

    return {
        "schema": {"id": "meridian.stale-source-docs", "version": "1.0.0"},
        "generator": {"name": "build/scripts/docs/mark-stale-docs.py", "version": "1.0.0"},
        "contract": "Entries here identify registered source modules whose source or README hash differs from the reviewed documentation baseline.",
        "source_hash_manifest": str(HASH_MANIFEST).replace("\\", "/"),
        "stale_count": len(stale_modules),
        "removed_count": len(removed_modules),
        "stale_modules": stale_modules,
        "removed_modules": removed_modules,
    }


def render_markdown(report: dict) -> str:
    rows = []
    for entry in report.get("stale_modules", []):
        rows.append(
            [
                f"`{entry['module_id']}`",
                f"`{entry['path']}`",
                f"`{entry['readme']}`",
                ", ".join(f"`{reason}`" for reason in entry.get("reasons", [])),
            ]
        )
    for entry in report.get("removed_modules", []):
        rows.append(
            [
                f"`{entry['module_id']}`",
                f"`{entry.get('path') or '-'}`",
                f"`{entry.get('readme') or '-'}`",
                ", ".join(f"`{reason}`" for reason in entry.get("reasons", [])),
            ]
        )
    table = markdown_table(["Module", "Path", "README", "Reason"], rows) if rows else "- No stale source docs detected."
    return (
        "<!--\n"
        "generated: true\n"
        "generator: build/scripts/docs/mark-stale-docs.py\n"
        "do_not_edit: true\n"
        "-->\n\n"
        "# Stale Source Documentation\n\n"
        "This report marks registered source modules whose code or README hashes differ from the reviewed baseline.\n\n"
        f"- Stale modules: {report.get('stale_count', 0)}\n"
        f"- Removed module hash entries: {report.get('removed_count', 0)}\n\n"
        f"{table}\n"
    )


def stale_module_ids(root: Path) -> set[str]:
    path = root / STALE_JSON
    if not path.exists():
        return set()
    report = json.loads(path.read_text(encoding="utf-8"))
    return {entry["module_id"] for entry in report.get("stale_modules", [])}


def write_report(root: Path, report: dict) -> int:
    changed = 0
    if write_text_if_changed(root / STALE_JSON, json.dumps(report, indent=2, sort_keys=True)):
        changed += 1
    if write_text_if_changed(root / STALE_MARKDOWN, render_markdown(report)):
        changed += 1
    return changed


def main() -> int:
    parser = argparse.ArgumentParser(description="Mark source documentation that needs updates based on hash drift.")
    parser.add_argument("--root", default=str(repo_root()), help="Repository root.")
    parser.add_argument("--write", action="store_true", help="Write stale-doc reports under docs/source/generated/.")
    parser.add_argument("--fail-on-stale", action="store_true", help="Return non-zero when stale docs are found.")
    parser.add_argument("--summary", action="store_true", help="Print compact summary output.")
    args = parser.parse_args()

    root = repo_root(args.root)
    report = build_stale_report(root)
    changed = write_report(root, report) if args.write else 0
    stale_total = report.get("stale_count", 0) + report.get("removed_count", 0)
    if args.summary:
        suffix = f", {changed} report file(s) changed" if args.write else ""
        print(f"stale docs marked: {stale_total} stale entr{'y' if stale_total == 1 else 'ies'}{suffix}")
        if stale_total:
            for entry in report.get("stale_modules", [])[:10]:
                print(f"- {entry['module_id']}: {', '.join(entry['reasons'])}")
    return 1 if args.fail_on_stale and stale_total else 0


if __name__ == "__main__":
    raise SystemExit(main())
