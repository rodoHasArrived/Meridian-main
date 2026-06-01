#!/usr/bin/env python3
"""Validate generated program-state summaries against roadmap registry data."""

from __future__ import annotations

import argparse
import importlib.util
import sys
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
DEFAULT_REPO_ROOT = SCRIPT_DIR.parent
GENERATOR_PATH = SCRIPT_DIR / "generate_program_state_summary.py"
DEFAULT_JSON_OUT = "docs/status/program-state-summary.json"
DEFAULT_MD_OUT = "docs/status/program-state-summary.md"

SPEC = importlib.util.spec_from_file_location("generate_program_state_summary", GENERATOR_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError(f"Unable to load {GENERATOR_PATH}")
generator = importlib.util.module_from_spec(SPEC)
sys.modules["generate_program_state_summary"] = generator
SPEC.loader.exec_module(generator)


def validate_generated_outputs(repo_root: Path) -> list[str]:
    program, rows = generator.load_program_state(repo_root)
    expected_json = generator.render_json(program, rows)
    expected_markdown = generator.render_markdown(program, rows)

    errors: list[str] = []
    json_out = repo_root / DEFAULT_JSON_OUT
    md_out = repo_root / DEFAULT_MD_OUT

    if not json_out.exists():
        errors.append(f"missing generated JSON summary: {DEFAULT_JSON_OUT}")
    elif json_out.read_text(encoding="utf-8") != expected_json:
        errors.append(f"generated JSON summary is out of date: {DEFAULT_JSON_OUT}")

    if not md_out.exists():
        errors.append(f"missing generated Markdown summary: {DEFAULT_MD_OUT}")
    elif md_out.read_text(encoding="utf-8") != expected_markdown:
        errors.append(f"generated Markdown summary is out of date: {DEFAULT_MD_OUT}")

    return errors


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--repo-root",
        type=Path,
        default=DEFAULT_REPO_ROOT,
        help="Repository root directory. Defaults to the parent of scripts/.",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    repo_root = args.repo_root.resolve()

    try:
        errors = validate_generated_outputs(repo_root)
    except ValueError as exc:
        errors = [str(exc)]

    if errors:
        print("Program state consistency check failed:\n", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1

    print("Program state consistency check passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
