#!/usr/bin/env python3
"""Validate desktop screenshot catalog contract, assets, workflow capture names, and README table."""

from __future__ import annotations

import json
import re
import sys
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
CONTRACT_PATH = REPO_ROOT / "docs/screenshots/desktop/catalog.json"
README_PATH = REPO_ROOT / "docs/screenshots/README.md"
WORKFLOWS_PATH = REPO_ROOT / "scripts/dev/desktop-workflows.json"


README_ROW_PATTERN = re.compile(
    r"^\|\s*(D\d{2})\s*\|\s*([^|]+?)\s*\|\s*\[`desktop/([^`]+)`\]\(desktop/[^)]+\)\s*\|\s*$"
)


def load_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def fail(errors: list[str]) -> int:
    print("Screenshot contract validation failed:", file=sys.stderr)
    for error in errors:
        print(f"- {error}", file=sys.stderr)
    return 1


def validate() -> int:
    errors: list[str] = []

    contract = load_json(CONTRACT_PATH)
    screenshots = contract.get("screenshots", [])
    desktop_dir = REPO_ROOT / contract["desktopDirectory"]

    required_filenames = [entry["filename"] for entry in screenshots]
    required_ids = [entry["id"] for entry in screenshots]
    id_to_filename = {entry["id"]: entry["filename"] for entry in screenshots}
    valid_capture_names = {entry["captureName"] for entry in screenshots}

    png_files = sorted(path.name for path in desktop_dir.glob("*.png"))
    missing = sorted(name for name in required_filenames if name not in png_files)
    unexpected = sorted(name for name in png_files if name not in required_filenames)

    if missing:
        errors.append(f"Missing required screenshots: {', '.join(missing)}")
    if unexpected:
        errors.append(
            "Unexpected screenshot files present (update catalog.json intentionally if these are canonical): "
            + ", ".join(unexpected)
        )

    readme_rows: list[tuple[str, str, str]] = []
    for line in README_PATH.read_text(encoding="utf-8").splitlines():
        match = README_ROW_PATTERN.match(line)
        if match:
            readme_rows.append((match.group(1), match.group(2).strip(), match.group(3).strip()))

    if len(readme_rows) != len(screenshots):
        errors.append(
            f"README desktop table has {len(readme_rows)} entries but catalog has {len(screenshots)} entries"
        )

    readme_ids = [row[0] for row in readme_rows]
    if readme_ids != required_ids:
        errors.append(
            "README IDs/order mismatch catalog IDs. "
            f"README={readme_ids}, catalog={required_ids}"
        )

    for row_id, _label, readme_file in readme_rows:
        expected = id_to_filename.get(row_id)
        if expected is None:
            errors.append(f"README contains unknown screenshot ID: {row_id}")
            continue
        if readme_file != expected:
            errors.append(
                f"README filename mismatch for {row_id}: README={readme_file}, catalog={expected}"
            )

    workflows = load_json(WORKFLOWS_PATH)
    workflow_items = workflows.get("workflows", [])
    screenshot_catalog = next((w for w in workflow_items if w.get("name") == "screenshot-catalog"), None)
    if screenshot_catalog is None:
        errors.append("scripts/dev/desktop-workflows.json missing screenshot-catalog workflow")
    else:
        seen_capture_names: set[str] = set()
        for step in screenshot_catalog.get("steps", []):
            capture_name = step.get("captureName")
            if not capture_name:
                continue
            if capture_name in seen_capture_names:
                errors.append(f"Duplicate captureName in screenshot-catalog workflow: {capture_name}")
            seen_capture_names.add(capture_name)
            if capture_name not in valid_capture_names:
                errors.append(
                    f"Workflow captureName '{capture_name}' is not listed in docs/screenshots/desktop/catalog.json"
                )

    if errors:
        return fail(errors)

    print(
        "Screenshot contract validation passed "
        f"({len(screenshots)} catalog entries, {len(png_files)} PNG files checked)."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(validate())
