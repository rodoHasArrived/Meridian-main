#!/usr/bin/env python3
"""Fail production certification when TRX evidence is missing or contains skipped tests."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET


SKIPPED_OUTCOMES = {"notexecuted", "skipped"}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--results-dir", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument(
        "--require-trx-prefix",
        action="append",
        default=[],
        help="Require at least one passing TRX result whose file name starts with this prefix.",
    )
    return parser.parse_args()


def local_name(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def collect_evidence(results_dir: Path, required_prefixes: list[str] | None = None) -> dict[str, object]:
    required_prefixes = required_prefixes or []
    trx_files = sorted(results_dir.rglob("*.trx"))
    if not trx_files:
        raise ValueError(f"production certification produced no TRX files under {results_dir}")

    totals = {"passed": 0, "failed": 0, "skipped": 0, "other": 0}
    skipped: list[dict[str, str]] = []
    file_results: list[dict[str, object]] = []
    for trx_file in trx_files:
        file_totals = {"passed": 0, "failed": 0, "skipped": 0, "other": 0}
        root = ET.parse(trx_file).getroot()
        for element in root.iter():
            if local_name(element.tag) != "UnitTestResult":
                continue
            outcome = element.attrib.get("outcome", "").strip().lower()
            if outcome == "passed":
                totals["passed"] += 1
                file_totals["passed"] += 1
            elif outcome == "failed":
                totals["failed"] += 1
                file_totals["failed"] += 1
            elif outcome in SKIPPED_OUTCOMES:
                totals["skipped"] += 1
                file_totals["skipped"] += 1
                skipped.append(
                    {
                        "test": element.attrib.get("testName", "unknown"),
                        "outcome": element.attrib.get("outcome", "unknown"),
                        "trx": trx_file.as_posix(),
                    }
                )
            else:
                totals["other"] += 1
                file_totals["other"] += 1
        file_results.append({"trx": trx_file.as_posix(), "totals": file_totals})

    required_suites = []
    for prefix in required_prefixes:
        matching_files = [
            result for result in file_results if Path(str(result["trx"])).name.startswith(prefix)
        ]
        passed = sum(int(result["totals"]["passed"]) for result in matching_files)  # type: ignore[index]
        required_suites.append(
            {
                "prefix": prefix,
                "matchingTrxFiles": [str(result["trx"]) for result in matching_files],
                "passed": passed,
                "satisfied": bool(matching_files) and passed > 0,
            }
        )

    return {
        "schemaVersion": 1,
        "trxFiles": [path.as_posix() for path in trx_files],
        "fileResults": file_results,
        "requiredSuites": required_suites,
        "totals": totals,
        "skippedTests": skipped,
        "certifiable": (
            totals["failed"] == 0
            and totals["skipped"] == 0
            and totals["other"] == 0
            and totals["passed"] > 0
            and all(bool(suite["satisfied"]) for suite in required_suites)
        ),
    }


def validation_errors(evidence: dict[str, object]) -> list[str]:
    totals = evidence["totals"]
    assert isinstance(totals, dict)
    errors: list[str] = []
    if int(totals["passed"]) == 0:
        errors.append("production certification executed no passing tests")
    if int(totals["failed"]):
        errors.append(f"production certification contains {totals['failed']} failed tests")
    if int(totals["skipped"]):
        errors.append(f"production certification contains {totals['skipped']} skipped tests")
    if int(totals["other"]):
        errors.append(f"production certification contains {totals['other']} unknown outcomes")
    required_suites = evidence["requiredSuites"]
    assert isinstance(required_suites, list)
    for suite in required_suites:
        assert isinstance(suite, dict)
        if not bool(suite["satisfied"]):
            errors.append(f"required TRX suite '{suite['prefix']}' produced no passing tests")
    return errors


def main() -> int:
    args = parse_args()
    try:
        evidence = collect_evidence(args.results_dir, args.require_trx_prefix)
    except (ET.ParseError, OSError, ValueError) as error:
        print(error, file=sys.stderr)
        return 1

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")

    errors = validation_errors(evidence)
    if errors:
        for error in errors:
            print(error, file=sys.stderr)
        skipped = evidence["skippedTests"]
        assert isinstance(skipped, list)
        for item in skipped:
            assert isinstance(item, dict)
            print(f"- {item['test']} ({item['trx']})", file=sys.stderr)
        return 1

    totals = evidence["totals"]
    assert isinstance(totals, dict)
    print(f"production certification: {totals['passed']} passed, zero failed, zero skipped")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
