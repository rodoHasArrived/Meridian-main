#!/usr/bin/env python3
"""Validate observable AI task briefs without inventing scope or acceptance behavior."""
from __future__ import annotations

from pathlib import Path, PurePosixPath
from typing import Any

REQUIRED_FIELDS = {"desiredBehavior", "acceptanceExample", "ownedPaths", "exclusions", "validationCommands"}
OPTIONAL_FIELDS = {"schemaVersion", "sharedContract"}
UI_PATHS = ("src/Meridian.Ui/dashboard", "src/Meridian.Wpf", "tests/Meridian.Wpf.Tests")


def _text(value: Any, field: str) -> str:
    if not isinstance(value, str) or not value.strip():
        raise ValueError(f"Task brief {field} must be a non-empty string")
    if len(value) > 12000:
        raise ValueError(f"Task brief {field} exceeds 12000 characters")
    return value.strip()


def _strings(value: Any, field: str, *, nonempty: bool) -> list[str]:
    if not isinstance(value, list) or len(value) > 40 or (nonempty and not value):
        raise ValueError(f"Task brief {field} must be a {'non-empty ' if nonempty else ''}list (at most 40 strings)")
    return [_text(item, field) for item in value]


def owned_path(root: Path, value: str) -> str:
    """Allow future files while rejecting traversal, absolute paths and external symlinks."""
    normalized = value.replace("\\", "/")
    path = PurePosixPath(normalized)
    if path.is_absolute() or ".." in path.parts or ":" in normalized or str(path) == "." or "\x00" in normalized:
        raise ValueError(f"Task brief ownedPaths needs a safe repository-relative path: {value}")
    if not (root / normalized).resolve().is_relative_to(root):
        raise ValueError(f"Task brief ownedPaths escapes repository: {value}")
    return path.as_posix()


def validate_brief(root: Path, data: Any) -> dict[str, Any]:
    """Return a normalized brief. Exclusions are prose; commands are recorded, never run."""
    root = root.resolve()
    if not isinstance(data, dict):
        raise ValueError("Task brief must be a JSON object")
    unknown = set(data) - REQUIRED_FIELDS - OPTIONAL_FIELDS
    if unknown:
        raise ValueError(f"Unknown task brief fields: {', '.join(sorted(unknown))}")
    missing = REQUIRED_FIELDS - set(data)
    if missing:
        raise ValueError(f"Missing task brief fields: {', '.join(sorted(missing))}")
    if "schemaVersion" in data and (type(data["schemaVersion"]) is not int or data["schemaVersion"] != 1):
        raise ValueError("Task brief schemaVersion must be 1")
    result = {
        "schemaVersion": 1,
        "desiredBehavior": _text(data["desiredBehavior"], "desiredBehavior"),
        "acceptanceExample": _text(data["acceptanceExample"], "acceptanceExample"),
        "ownedPaths": sorted(set(owned_path(root, value) for value in _strings(data["ownedPaths"], "ownedPaths", nonempty=True))),
        "exclusions": _strings(data["exclusions"], "exclusions", nonempty=False),
        "validationCommands": _strings(data["validationCommands"], "validationCommands", nonempty=True),
    }
    if "sharedContract" in data:
        result["sharedContract"] = _text(data["sharedContract"], "sharedContract")
        if result["sharedContract"].lower().startswith("none:") and not result["sharedContract"][5:].strip():
            raise ValueError("Task brief sharedContract 'none:' requires a presentation-only explanation")
    ui_owned = any(path == prefix or path.startswith(prefix + "/") for path in result["ownedPaths"] for prefix in UI_PATHS)
    if ui_owned and "sharedContract" not in result:
        raise ValueError("Browser/WPF task briefs require sharedContract: name the shared seam or explain 'none: presentation-only'")
    return result
