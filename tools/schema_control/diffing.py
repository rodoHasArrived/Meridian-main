"""Deterministic manifest and generated-artifact comparison."""

from __future__ import annotations

import json
from collections.abc import Mapping
from pathlib import Path
from typing import Any

from .common import fingerprint, normalize_value, sha256_bytes


def diff_values(base: Any, current: Any, path: str = "$") -> list[dict[str, Any]]:
    """Return a compact structural JSON diff with stable paths and ordering."""

    base = normalize_value(base)
    current = normalize_value(current)
    if base == current:
        return []
    if isinstance(base, Mapping) and isinstance(current, Mapping):
        changes: list[dict[str, Any]] = []
        keys = sorted(set(base) | set(current))
        for key in keys:
            child_path = f"{path}.{key}"
            if key not in base:
                changes.append(
                    {"change": "added", "path": child_path, "current": current[key]}
                )
            elif key not in current:
                changes.append(
                    {"change": "removed", "path": child_path, "base": base[key]}
                )
            else:
                changes.extend(diff_values(base[key], current[key], child_path))
        return changes
    if isinstance(base, list) and isinstance(current, list):
        base_keys = [
            _stable_key(item) if isinstance(item, Mapping) else "" for item in base
        ]
        current_keys = [
            _stable_key(item) if isinstance(item, Mapping) else "" for item in current
        ]
        if (
            all(base_keys)
            and all(current_keys)
            and len(set(base_keys)) == len(base_keys)
            and len(set(current_keys)) == len(current_keys)
        ):
            base_by_key = dict(zip(base_keys, base, strict=True))
            current_by_key = dict(zip(current_keys, current, strict=True))
            return diff_values(base_by_key, current_by_key, path)
        return [{"change": "changed", "path": path, "base": base, "current": current}]
    return [{"change": "changed", "path": path, "base": base, "current": current}]


def _stable_key(item: Mapping[str, Any]) -> str:
    composite_keys = (
        ("source", "target", "kind", "label"),
        ("schema", "relation", "name"),
        ("schema", "table", "name"),
        ("schema", "signature"),
        ("schema", "name", "kind"),
    )
    for keys in composite_keys:
        values = [item.get(key) for key in keys]
        if all(value not in (None, "") for value in values):
            return "|".join(str(value) for value in values)
    for key in ("id", "key", "full_name", "path", "name", "object"):
        value = item.get(key)
        if value not in (None, ""):
            return str(value)
    return ""


def build_manifest_diff(
    base: Mapping[str, Any], current: Mapping[str, Any]
) -> dict[str, Any]:
    changes = diff_values(base, current)
    counts = {"added": 0, "removed": 0, "changed": 0}
    for change in changes:
        counts[str(change["change"])] += 1
    result: dict[str, Any] = {
        "format": "meridian.schema-diff.v1",
        "base_fingerprint": fingerprint(base),
        "current_fingerprint": fingerprint(current),
        "counts": counts,
        "changes": changes,
    }
    result["fingerprint"] = fingerprint(result)
    return result


def _tree_hashes(root: Path) -> dict[str, str]:
    if not root.exists():
        return {}
    return {
        path.relative_to(root).as_posix(): sha256_bytes(path.read_bytes())
        for path in sorted(root.rglob("*"), key=lambda item: item.as_posix())
        if path.is_file()
    }


def compare_artifact_trees(expected: Path, candidate: Path) -> dict[str, Any]:
    """Compare two artifact roots without depending on Git or platform diff tools."""

    expected_hashes = _tree_hashes(expected)
    candidate_hashes = _tree_hashes(candidate)
    expected_paths = set(expected_hashes)
    candidate_paths = set(candidate_hashes)
    added = sorted(candidate_paths - expected_paths)
    removed = sorted(expected_paths - candidate_paths)
    changed = sorted(
        path
        for path in expected_paths & candidate_paths
        if expected_hashes[path] != candidate_hashes[path]
    )
    result: dict[str, Any] = {
        "format": "meridian.artifact-drift.v1",
        "clean": not (added or removed or changed),
        "added": added,
        "removed": removed,
        "changed": changed,
        "expected": expected_hashes,
        "candidate": candidate_hashes,
    }
    result["fingerprint"] = fingerprint(result)
    return result


def load_json_tree(root: Path) -> dict[str, Any]:
    """Load every JSON file under ``root`` into one repo-relative mapping."""

    result: dict[str, Any] = {}
    if not root.exists():
        return result
    for path in sorted(root.rglob("*.json"), key=lambda item: item.as_posix()):
        result[path.relative_to(root).as_posix()] = json.loads(
            path.read_text(encoding="utf-8")
        )
    return result


def render_diff_markdown(
    diff: Mapping[str, Any], title: str = "Schema change report"
) -> str:
    counts = diff.get("counts", {})
    lines = [
        f"# {title}",
        "",
        f"- Added: {counts.get('added', 0)}",
        f"- Removed: {counts.get('removed', 0)}",
        f"- Changed: {counts.get('changed', 0)}",
        "",
    ]
    changes = diff.get("changes", [])
    if not changes:
        lines.append("No structural changes detected.")
    else:
        lines.extend(["| Change | Path |", "| --- | --- |"])
        for item in changes:
            lines.append(f"| {item.get('change', '')} | `{item.get('path', '')}` |")
    return "\n".join(lines) + "\n"
