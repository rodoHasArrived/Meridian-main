#!/usr/bin/env python3
"""Validate the workstation cockpit acceptance matrix for CI gates."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any

REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_MATRIX = REPO_ROOT / "docs" / "status" / "workstation-cockpit-acceptance-matrix.json"


def _as_list(value: Any) -> list[Any]:
    return value if isinstance(value, list) else []


def _non_blank(value: Any) -> bool:
    return isinstance(value, str) and bool(value.strip())


def load_matrix(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8") as handle:
        data = json.load(handle)
    if not isinstance(data, dict):
        raise ValueError("matrix root must be a JSON object")
    return data


def validate_matrix(data: dict[str, Any], *, source: Path | None = None) -> list[str]:
    """Return validation errors for missing ownership, coverage, or artifact pointers."""

    errors: list[str] = []
    criteria = data.get("criteria")
    if not isinstance(criteria, list) or not criteria:
        location = f"{source}: " if source else ""
        return [f"{location}criteria must be a non-empty array."]

    seen_ids: set[str] = set()
    for index, criterion in enumerate(criteria):
        if not isinstance(criterion, dict):
            errors.append(f"criteria[{index}]: criterion must be an object.")
            continue

        criterion_id = criterion.get("id") if _non_blank(criterion.get("id")) else f"criteria[{index}]"
        if _non_blank(criterion.get("id")):
            if criterion_id in seen_ids:
                errors.append(f"{criterion_id}: duplicate criterion id.")
            seen_ids.add(str(criterion_id))
        else:
            errors.append(f"criteria[{index}]: missing non-blank id.")

        route_owners = _as_list(criterion.get("routeOwnership"))
        if not route_owners:
            errors.append(f"{criterion_id}: missing routeOwnership entries.")
        elif not any(isinstance(owner, dict) and _non_blank(owner.get("route")) for owner in route_owners):
            errors.append(f"{criterion_id}: routeOwnership must include at least one non-blank route.")

        tests = _as_list(criterion.get("automatedTests"))
        if not tests:
            errors.append(f"{criterion_id}: missing automatedTests entries.")
        elif not any(
            isinstance(test, dict)
            and _non_blank(test.get("project"))
            and _non_blank(test.get("filterableName"))
            for test in tests
        ):
            errors.append(
                f"{criterion_id}: automatedTests must include at least one entry with project and filterableName."
            )

        artifacts = _as_list(criterion.get("artifacts"))
        if not artifacts:
            errors.append(f"{criterion_id}: missing artifacts entries.")
        elif not any(isinstance(artifact, dict) and _non_blank(artifact.get("path")) for artifact in artifacts):
            errors.append(f"{criterion_id}: artifacts must include at least one non-blank path.")

    return errors


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--matrix",
        type=Path,
        default=DEFAULT_MATRIX,
        help="Path to workstation-cockpit-acceptance-matrix.json.",
    )
    parser.add_argument("--summary", action="store_true", help="Print a concise pass summary.")
    args = parser.parse_args(argv)

    matrix_path = args.matrix if args.matrix.is_absolute() else REPO_ROOT / args.matrix
    if not matrix_path.exists():
        print(f"Acceptance matrix validation failed: {matrix_path} does not exist.", file=sys.stderr)
        return 1

    try:
        data = load_matrix(matrix_path)
    except (OSError, ValueError, json.JSONDecodeError) as exc:
        print(f"Acceptance matrix validation failed: could not load {matrix_path}: {exc}", file=sys.stderr)
        return 1

    errors = validate_matrix(data, source=matrix_path)
    if errors:
        print("Acceptance matrix validation failed:", file=sys.stderr)
        for error in errors:
            print(f" - {error}", file=sys.stderr)
        return 1

    criteria_count = len(data.get("criteria", []))
    if args.summary:
        print(f"Acceptance matrix validation passed: {criteria_count} criteria checked.")
    else:
        print("Acceptance matrix validation passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
