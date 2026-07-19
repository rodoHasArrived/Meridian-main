#!/usr/bin/env python3
"""Validate trigger and negative-control fixtures for the simulated-user-panel skill."""

from __future__ import annotations

import argparse
import csv
import json
import subprocess
import sys
import tempfile
from pathlib import Path

SKILL_DIR = Path(__file__).resolve().parents[1]
DEFAULT_PATH = SKILL_DIR / "evals" / "trigger-prompts.csv"
REPO_ROOT = SKILL_DIR.parents[2]
ROUTE_LINTER = REPO_ROOT / "build" / "scripts" / "docs" / "prompt-route-linter.py"
ROUTE_RULES = REPO_ROOT / "docs" / "ai" / "codex" / "prompt-route-rules.json"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--file", type=Path, default=DEFAULT_PATH)
    parser.add_argument("--summary", action="store_true")
    parser.add_argument("--json", action="store_true")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    failures: list[str] = []
    with args.file.open(newline="", encoding="utf-8") as handle:
        rows = list(csv.DictReader(handle))
    required = {"prompt", "should_trigger", "expected_lane"}
    if not rows:
        failures.append("trigger prompt set is empty")
    if rows and set(rows[0]) != required:
        failures.append("trigger prompt columns must be prompt, should_trigger, expected_lane")
    true_count = 0
    false_count = 0
    routed_count = 0
    prompts: set[str] = set()
    if not ROUTE_LINTER.is_file() or not ROUTE_RULES.is_file():
        failures.append("prompt route linter or rules file is missing")
    with tempfile.TemporaryDirectory(prefix="meridian-sup-trigger-") as temp_dir:
        for index, row in enumerate(rows, start=2):
            prompt = row.get("prompt", "").strip()
            label = row.get("should_trigger", "").strip().lower()
            expected_skill = row.get("expected_lane", "").strip()
            if not prompt or not expected_skill:
                failures.append(f"row {index} has blank prompt or expected_lane")
            if prompt in prompts:
                failures.append(f"row {index} duplicates a prompt")
            prompts.add(prompt)
            if label == "true":
                true_count += 1
                if expected_skill != "meridian-simulated-user-panel":
                    failures.append(f"row {index} positive control expects {expected_skill}")
            elif label == "false":
                false_count += 1
                if expected_skill == "meridian-simulated-user-panel":
                    failures.append(f"row {index} negative control expects the panel skill")
            else:
                failures.append(f"row {index} has invalid should_trigger value")

            if not prompt or not expected_skill or not ROUTE_LINTER.is_file() or not ROUTE_RULES.is_file():
                continue
            report_path = Path(temp_dir) / f"route-{index}.json"
            process = subprocess.run(
                [
                    sys.executable,
                    str(ROUTE_LINTER),
                    "--rules",
                    str(ROUTE_RULES),
                    "--prompt",
                    prompt,
                    "--json-output",
                    str(report_path),
                ],
                cwd=REPO_ROOT,
                capture_output=True,
                text=True,
                check=False,
            )
            if process.returncode != 0 or not report_path.is_file():
                detail = process.stderr.strip() or process.stdout.strip() or "no route report"
                failures.append(f"row {index} route linter failed: {detail}")
                continue
            actual_skill = json.loads(report_path.read_text(encoding="utf-8"))["match"]["skill"]
            if actual_skill != expected_skill:
                failures.append(
                    f"row {index} expected {expected_skill} but routed to {actual_skill}"
                )
            else:
                routed_count += 1
    if true_count < 3 or false_count < 3:
        failures.append("trigger prompt set requires at least three positive and three negative controls")
    payload = {
        "status": "pass" if not failures else "fail",
        "failures": failures,
        "summary": {
            "total": len(rows),
            "positive": true_count,
            "negative": false_count,
            "routed": routed_count,
            "failure_count": len(failures),
        },
    }
    if args.json:
        print(json.dumps(payload, indent=2))
    else:
        print(
            f"check_trigger_prompts status={payload['status']} positive={true_count} "
            f"negative={false_count} routed={routed_count}"
        )
        if args.summary:
            for failure in failures:
                print(f"- {failure}")
    return 0 if not failures else 1


if __name__ == "__main__":
    raise SystemExit(main())
