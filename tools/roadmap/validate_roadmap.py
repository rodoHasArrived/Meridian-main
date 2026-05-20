#!/usr/bin/env python3
from __future__ import annotations

import json
from pathlib import Path
from typing import Any

CANONICAL = {
    "status": {"not_started", "in_progress", "blocked", "completed", "cancelled"},
    "health": {"green", "yellow", "red", "unknown"},
    "priority": {"low", "medium", "high", "critical"},
    "evidence_posture": {"none", "draft", "partial", "verified", "audited"},
    "completion_term": {"done", "complete", "completed", "closed", "shipped"},
}


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
            errors.extend(_check(item, f"{path}[{i}]"))
    return errors


def validate_file(path: Path) -> int:
    data = json.loads(path.read_text(encoding="utf-8"))
    errors = _check(data)
    if errors:
        print(f"{path} failed enum validation:")
        for e in errors:
            print(f"  - {e}")
        return 1
    print(f"{path} passed enum validation")
    return 0


if __name__ == "__main__":
    fixtures = Path(__file__).parent / "fixtures"
    exit_code = 0
    for json_file in sorted(fixtures.glob("*.json")):
        rc = validate_file(json_file)
        is_invalid_fixture = json_file.name.startswith("invalid-")
        if is_invalid_fixture and rc == 0:
            print(f"{json_file} expected failure but passed")
            exit_code = 1
        elif not is_invalid_fixture and rc != 0:
            print(f"{json_file} expected pass but failed")
            exit_code = 1
        elif is_invalid_fixture and rc != 0:
            print(f"{json_file} correctly failed")
        else:
            print(f"{json_file} correctly passed")
    raise SystemExit(exit_code)
