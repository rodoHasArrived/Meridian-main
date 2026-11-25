#!/usr/bin/env python3
"""Run the Meridian service-backed integration suite as its declared concurrent processes.

`integration_evidence.MERIDIAN_PROCESSES` splits the suite into complementary filters, each
with its own database. This runner starts one `dotnet test --no-build` per process, gives
each an isolated copy of the built test output (coverage instruments assemblies in place, so
two collectors must never share a directory), a private temporary directory, and connection
strings for its own database. Every process is attempted; any failure fails the run.
"""

from __future__ import annotations

import argparse
import importlib.util
import os
import re
import shutil
import subprocess
import sys
import tempfile
import threading
import time
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
SUITE = "meridian-integrations"
PRIMARY_DATABASE = "meridian"


def load_contract() -> dict:
    spec = importlib.util.spec_from_file_location(
        "integration_evidence", Path(__file__).with_name("integration_evidence.py"))
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module.SUITES[SUITE]


def process_environment(environment: dict[str, str], database: str, temporary: str) -> dict[str, str]:
    """Point every Meridian connection string at this process's database."""
    result = dict(environment)
    result.update({name: temporary for name in ("TMPDIR", "TMP", "TEMP")})
    if database == PRIMARY_DATABASE:
        return result
    pattern = re.compile(rf"(?i)(?<![A-Za-z])Database={re.escape(PRIMARY_DATABASE)}(?=;|$)")
    rewritten = 0
    for name, value in environment.items():
        if name.startswith("MERIDIAN_") and name.endswith("_CONNECTION_STRING"):
            updated, count = pattern.subn(f"Database={database}", value)
            if count != 1:
                raise ValueError(f"{name} does not name the {PRIMARY_DATABASE} database exactly once")
            result[name] = updated
            rewritten += 1
    if not rewritten:
        raise ValueError("No Meridian connection strings were available to isolate")
    return result


def build_command(contract: dict, name: str, *, configuration: str, results_dir: Path,
                  settings: str, output: Path | None) -> list[str]:
    command = [
        "dotnet", "test", contract["project"], "-c", configuration, "--no-restore", "--no-build",
        "--collect", "XPlat Code Coverage", "--settings", settings,
        "--results-directory", str(results_dir),
        "--logger", f"trx;LogFilePrefix={SUITE}-{name}",
        "--filter", f"({contract['filter']})&({contract['processes'][name]['filter']})",
    ]
    if output is not None:
        command.extend(["--output", str(output)])
    return command


def isolated_output(built: Path, name: str) -> Path:
    """Copy the built output beside the original so repository-relative lookups still resolve."""
    if not (built / "Meridian.Tests.dll").is_file():
        raise ValueError(f"Built test output is missing: {built}")
    target = built.with_name(f"{built.name}-{name}")
    if target.exists():
        shutil.rmtree(target)
    shutil.copytree(built, target, symlinks=True)
    return target


def run(command: list[str], environment: dict[str, str], name: str, lock: threading.Lock) -> int:
    with lock:
        print(f"[{name}] {' '.join(command)}", flush=True)
    started = time.perf_counter()
    try:
        process = subprocess.Popen(command, cwd=REPO_ROOT, env=environment, text=True, errors="replace",
                                   stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    except OSError as error:
        with lock:
            print(f"[{name}] Unable to launch: {error}", file=sys.stderr, flush=True)
        return 127
    assert process.stdout is not None
    for line in process.stdout:
        with lock:
            print(f"[{name}] {line.rstrip()}", flush=True)
    code = process.wait()
    with lock:
        print(f"[{name}] exited {code} after {time.perf_counter() - started:.1f}s", flush=True)
    return code


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--configuration", default="Release")
    parser.add_argument("--results-dir", type=Path, required=True)
    parser.add_argument("--settings", default="tests/coverlet.runsettings")
    parser.add_argument("--built-output", type=Path, default=Path("tests/Meridian.Tests/bin/Release/net10.0"))
    args = parser.parse_args()

    contract = load_contract()
    lock = threading.Lock()
    codes: dict[str, int] = {}
    with tempfile.TemporaryDirectory(prefix="meridian-integrations-", ignore_cleanup_errors=True) as temporary:
        try:
            plans = []
            for name, process in contract["processes"].items():
                private = Path(temporary) / name
                private.mkdir()
                # The primary database's process keeps the original output, as before.
                output = (None if process["database"] == PRIMARY_DATABASE
                          else isolated_output((REPO_ROOT / args.built_output).resolve(), name))
                plans.append((name, build_command(
                    contract, name, configuration=args.configuration, results_dir=args.results_dir,
                    settings=args.settings, output=output,
                ), process_environment(dict(os.environ), process["database"], str(private))))
        except (OSError, ValueError) as error:
            print(f"Cannot prepare integration processes: {error}", file=sys.stderr)
            return 2
        threads = [
            threading.Thread(target=lambda n=name, c=command, e=environment: codes.__setitem__(n, run(c, e, n, lock)))
            for name, command, environment in plans
        ]
        for thread in threads:
            thread.start()
        for thread in threads:
            thread.join()

    failed = sorted(name for name in contract["processes"] if codes.get(name) != 0)
    if failed:
        print(f"Meridian integration processes failed: {', '.join(failed)}", file=sys.stderr)
        return 1
    print(f"All {len(codes)} Meridian integration processes passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
