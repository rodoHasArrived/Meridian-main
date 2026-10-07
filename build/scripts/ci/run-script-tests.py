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
from concurrent.futures import ProcessPoolExecutor, as_completed
from contextlib import redirect_stderr, redirect_stdout
import io
import json
import multiprocessing
import os
from datetime import date
import sys
import tempfile
import time
import traceback
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
# Suites import repo-rooted modules (tools.schema_control, tests.scripts.*), which
# `python -m unittest` resolves via the cwd; resolve them regardless of invocation dir.
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))
DEFAULT_START_DIR = REPO_ROOT / "tests" / "scripts"
DEFAULT_QUARANTINE = Path(__file__).resolve().parent / "script-test-quarantine.json"
# Both suites exercise many real PowerShell processes. Start them first so their
# native process startup costs overlap instead of extending the end of the lane.
LONG_RUNNING_MODULES = ("test_production_recovery", "test_recovery_evidence")


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


def run_module(module_name: str, start_dir: str, verbosity: int, expected_ids: list[str]) -> dict:
    """Run one complete module in a fresh process, including its class fixtures."""
    print(f"Starting script module: {module_name}", flush=True)
    started = time.monotonic()
    output = io.StringIO()
    with tempfile.TemporaryDirectory(prefix="meridian-script-test-") as temporary:
        # Scripts exercised by fixtures may write a GitHub summary. Give them a
        # real, private file instead of appending fixture evidence to the job's report.
        if os.environ.get("GITHUB_STEP_SUMMARY"):
            os.environ["GITHUB_STEP_SUMMARY"] = str(Path(temporary) / "summary.md")
        with redirect_stdout(output), redirect_stderr(output):
            suite = unittest.TestLoader().discover(start_dir=start_dir, pattern=f"{module_name}.py")
            discovered_ids = sorted(test.id() for test in iter_tests(suite))
            if discovered_ids != expected_ids:
                raise RuntimeError(
                    f"Test discovery changed for {module_name}: "
                    f"expected {len(expected_ids)} tests, found {len(discovered_ids)}"
                )
            result = unittest.TextTestRunner(stream=output, verbosity=verbosity, buffer=True).run(suite)
    return {
        "testsRun": result.testsRun,
        "failures": len(result.failures),
        "errors": len(result.errors),
        "skipped": len(result.skipped),
        "successful": result.wasSuccessful(),
        "duration": time.monotonic() - started,
        "output": output.getvalue(),
    }


def run_modules(modules: dict[str, list[str]], start_dir: str, verbosity: int, workers: int) -> dict:
    totals = {"testsRun": 0, "failures": 0, "errors": 0, "skipped": 0, "successful": True}
    # Tests change process globals (mocks, environment, locale and unittest's
    # output buffering). Spawn each module in its own interpreter; never reuse a
    # worker for another module or run unittest suites concurrently in threads.
    with ProcessPoolExecutor(
        max_workers=min(workers, len(modules)),
        mp_context=multiprocessing.get_context("spawn"),
        max_tasks_per_child=1,
    ) as executor:
        pending = {
            executor.submit(run_module, module, start_dir, verbosity, expected_ids): module
            for module, expected_ids in modules.items()
        }
        for future in as_completed(pending):
            module = pending[future]
            try:
                result = future.result()
            except (Exception, SystemExit):
                totals["errors"] += 1
                totals["successful"] = False
                print(f"Script module crashed: {module}\n{traceback.format_exc()}", flush=True)
                continue
            for field in ("testsRun", "failures", "errors", "skipped"):
                totals[field] += result[field]
            totals["successful"] = totals["successful"] and result["successful"]
            status = "passed" if result["successful"] else "failed"
            print(
                f"Finished script module: {module}: {status} "
                f"({result['testsRun']} tests, {result['duration']:.3f}s)",
                flush=True,
            )
            if not result["successful"] or verbosity > 1:
                print(result["output"], end="", flush=True)
    return totals


def main() -> int:
    parser = argparse.ArgumentParser(description="Run tests/scripts suites with quarantine.")
    parser.add_argument("--start-dir", default=str(DEFAULT_START_DIR))
    parser.add_argument("--quarantine", default=str(DEFAULT_QUARANTINE))
    parser.add_argument("--verbosity", type=int, default=1)
    parser.add_argument("--workers", type=int, default=2, help="Maximum isolated test-module processes (default: 2).")
    args = parser.parse_args()
    if args.workers < 1:
        parser.error("--workers must be at least 1")

    quarantined = load_quarantine(Path(args.quarantine))
    discovered = unittest.TestLoader().discover(start_dir=args.start_dir, pattern="test_*.py")
    suite, excluded = partition_suite(discovered, quarantined)

    for module_name in sorted(excluded):
        print(f"QUARANTINED (skipped): {module_name} — {quarantined[module_name]}", flush=True)
    unmatched = set(quarantined) - excluded
    if unmatched or not suite.countTestCases():
        raise ValueError(f"Invalid discovery: unmatched quarantine={sorted(unmatched)}, selected={suite.countTestCases()}")
    if os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as summary:
            summary.write("\n### Script quarantine\n\n" + "\n".join(f"- {name}: {quarantined[name]}" for name in sorted(excluded)) + "\n")

    selected_ids: dict[str, list[str]] = {}
    for test in iter_tests(suite):
        selected_ids.setdefault(resolve_module_name(test), []).append(test.id())
    module_order = sorted(
        selected_ids,
        key=lambda name: (name not in LONG_RUNNING_MODULES, name),
    )
    modules = {module: sorted(selected_ids[module]) for module in module_order}
    result = run_modules(modules, args.start_dir, args.verbosity, args.workers)

    print(
        f"Script-test lane: ran {result['testsRun']} tests, "
        f"{len(excluded)} module(s) quarantined, "
        f"failures={result['failures']}, errors={result['errors']}, "
        f"skipped={result['skipped']}."
    )
    return 0 if result["successful"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
