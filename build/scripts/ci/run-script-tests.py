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
    return {str(name): str(reason) for name, reason in modules.items()}


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
    for module_name in sorted(set(quarantined) - excluded):
        print(
            f"NOTE: quarantine entry '{module_name}' matched no discovered module; "
            "remove it if the suite was fixed or deleted."
        )

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
