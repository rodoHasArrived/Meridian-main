#!/usr/bin/env python3
"""Run the tests/scripts unittest suites with a visible quarantine list.

The repository accumulated ~75 script-test suites of which only a handful were
wired to any CI lane; several of the never-run suites had already rotted (audit
finding P9). This runner gates every suite by default and excludes only the
modules named in script-test-quarantine.json — printing each exclusion on every
run so the quarantine cannot rot silently.
"""

from __future__ import annotations

import argparse
import json
import os
from datetime import date
import sys
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
# Suites import repo-rooted modules (tools.schema_control, tests.scripts.*), which
# `python -m unittest` resolves via the cwd; resolve them regardless of invocation dir.
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))
DEFAULT_START_DIR = REPO_ROOT / "tests" / "scripts"
DEFAULT_QUARANTINE = Path(__file__).resolve().parent / "script-test-quarantine.json"


def load_quarantine(path: Path) -> dict[str, str]:
    payload = json.loads(path.read_text(encoding="utf-8"))
    modules = payload.get("quarantined_modules", {})
    if not isinstance(modules, dict):
        raise ValueError(f"'quarantined_modules' in {path} must be an object of module -> reason.")
    result = {}
    for name, entry in modules.items():
        if not isinstance(entry, dict) or any(not entry.get(k) for k in ("reason", "owner", "reviewBy", "tracking")):
            raise ValueError(f"Untracked quarantine {name}: reason, owner, reviewBy and tracking are required.")
        if date.fromisoformat(entry["reviewBy"]) < date.today():
            raise ValueError(f"Quarantine review overdue: {name} ({entry['reviewBy']})")
        if not (DEFAULT_START_DIR / f"{name}.py").is_file():
            raise ValueError(f"Quarantine names a missing test module: {name}")
        tracking_file = REPO_ROOT / entry["tracking"].split("#", 1)[0]
        if not tracking_file.is_file():
            raise ValueError(f"Quarantine tracking document is missing: {name}")
        result[name] = f"{entry['reason']} Owner: {entry['owner']}; review by {entry['reviewBy']}; {entry['tracking']}"
    return result


def resolve_module_name(test: unittest.TestCase) -> str:
    module = type(test).__module__.split(".")[0]
    if module == "unittest":
        # Import failures surface as unittest.loader._FailedTest whose test-method
        # name is the unimportable module's name (module files are test_*.py).
        return test.id().split(".")[-1]
    return module


def iter_tests(suite: unittest.TestSuite):
    for item in suite:
        if isinstance(item, unittest.TestSuite):
            yield from iter_tests(item)
        else:
            yield item


def partition_suite(
    discovered: unittest.TestSuite, quarantined: dict[str, str]
) -> tuple[unittest.TestSuite, set[str]]:
    kept = unittest.TestSuite()
    excluded: set[str] = set()
    for test in iter_tests(discovered):
        module_name = resolve_module_name(test)
        if module_name in quarantined:
            excluded.add(module_name)
        else:
            kept.addTest(test)
    return kept, excluded


def main() -> int:
    parser = argparse.ArgumentParser(description="Run tests/scripts suites with quarantine.")
    parser.add_argument("--start-dir", default=str(DEFAULT_START_DIR))
    parser.add_argument("--quarantine", default=str(DEFAULT_QUARANTINE))
    parser.add_argument("--verbosity", type=int, default=1)
    args = parser.parse_args()

    quarantined = load_quarantine(Path(args.quarantine))
    discovered = unittest.TestLoader().discover(start_dir=args.start_dir, pattern="test_*.py")
    suite, excluded = partition_suite(discovered, quarantined)

    for module_name in sorted(excluded):
        print(f"QUARANTINED (skipped): {module_name} — {quarantined[module_name]}")
    unmatched = set(quarantined) - excluded
    if unmatched or not suite.countTestCases():
        raise ValueError(f"Invalid discovery: unmatched quarantine={sorted(unmatched)}, selected={suite.countTestCases()}")
    if os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as summary:
            summary.write("\n### Script quarantine\n\n" + "\n".join(f"- {name}: {quarantined[name]}" for name in sorted(excluded)) + "\n")

    runner = unittest.TextTestRunner(verbosity=args.verbosity, buffer=True)
    result = runner.run(suite)

    print(
        f"Script-test lane: ran {result.testsRun} tests, "
        f"{len(excluded)} module(s) quarantined, "
        f"failures={len(result.failures)}, errors={len(result.errors)}, "
        f"skipped={len(result.skipped)}."
    )
    return 0 if result.wasSuccessful() else 1


if __name__ == "__main__":
    raise SystemExit(main())
