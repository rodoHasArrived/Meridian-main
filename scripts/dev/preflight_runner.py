#!/usr/bin/env python3
"""Shared preflight runner for CI + human workflow diagnostics."""

from __future__ import annotations

import argparse
import json
import os
import platform
import shutil
from datetime import datetime, timezone
from pathlib import Path


def issue(check: str, message: str, recommendation: str) -> dict[str, str]:
    return {"check": check, "message": message, "recommendation": recommendation}


def test_writable_dir(path: Path) -> bool:
    try:
        path.mkdir(parents=True, exist_ok=True)
        probe = path / f".preflight-write-{os.getpid()}.tmp"
        probe.write_text("ok", encoding="utf-8")
        probe.unlink(missing_ok=True)
        return True
    except OSError:
        return False


def run_preflight(args: argparse.Namespace) -> dict[str, object]:
    blocking: list[dict[str, str]] = []
    warnings: list[dict[str, str]] = []

    if args.require_windows and platform.system().lower() != "windows":
        blocking.append(issue("platform.windows", "Workflow requires Windows.", "Run on Windows with required desktop tooling."))

    for command in args.required_command:
        if shutil.which(command) is None:
            blocking.append(issue(f"command.{command}", f"Required command '{command}' was not found in PATH.", f"Install '{command}' and retry."))

    for raw_path in args.required_path:
        path = Path(raw_path)
        if not path.exists():
            blocking.append(issue(f"path.{raw_path}", f"Required path '{raw_path}' was not found.", "Verify the path exists before rerunning."))

    for raw_dir in args.writable_dir:
        if not test_writable_dir(Path(raw_dir)):
            blocking.append(issue(f"write.{raw_dir}", f"Directory '{raw_dir}' is not writable.", "Fix permissions or choose a writable location."))

    for env_name in args.required_env:
        if not os.getenv(env_name):
            blocking.append(issue(f"env.{env_name}", f"Required environment variable '{env_name}' is missing.", f"Set '{env_name}' and rerun."))

    for feature in args.feature_flag:
        name, expected = feature.split("=", 1)
        actual = os.getenv(name)
        if not actual:
            warnings.append(issue(f"feature.{name}", f"Feature flag '{name}' is not set.", f"Expected '{expected}' for non-fixture execution."))
        elif actual.lower() != expected.lower():
            warnings.append(issue(f"feature.{name}", f"Feature flag '{name}' is '{actual}' (expected '{expected}').", "Verify workflow mode and feature flags."))

    status = "blocked" if blocking else "ok"
    next_action = args.success_next_action if status == "ok" else "Resolve blocking checks and rerun preflight."

    return {
        "scenario": args.scenario,
        "status": status,
        "blockingChecks": blocking,
        "warnings": warnings,
        "nextAction": next_action,
        "generatedAtUtc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description="Run shared workflow preflight checks.")
    parser.add_argument("--scenario", required=True)
    parser.add_argument("--required-command", action="append", default=[])
    parser.add_argument("--required-path", action="append", default=[])
    parser.add_argument("--writable-dir", action="append", default=[])
    parser.add_argument("--required-env", action="append", default=[])
    parser.add_argument("--feature-flag", action="append", default=[], help="NAME=EXPECTED")
    parser.add_argument("--require-windows", action="store_true")
    parser.add_argument("--success-next-action", default="Proceed with workflow execution.")
    args = parser.parse_args()

    payload = run_preflight(args)
    print(json.dumps(payload, indent=2))
    return 0 if payload["status"] == "ok" else 2


if __name__ == "__main__":
    raise SystemExit(main())
