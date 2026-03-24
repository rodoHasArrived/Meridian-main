#!/usr/bin/env python3
"""buildctl - Meridian build control utility.

Provides environment health checks, build diagnostics, and tooling utilities
for the Meridian project.

Usage:
    python3 build/python/cli/buildctl.py doctor [--quick] [--no-fail-on-warn]
    python3 build/python/cli/buildctl.py build --project <path> --configuration <cfg>
    python3 build/python/cli/buildctl.py collect-debug --project <path> --configuration <cfg>
    python3 build/python/cli/buildctl.py build-profile
    python3 build/python/cli/buildctl.py validate-data --directory <dir>
    python3 build/python/cli/buildctl.py analyze-errors
    python3 build/python/cli/buildctl.py build-graph --project <path>
    python3 build/python/cli/buildctl.py fingerprint --configuration <cfg>
    python3 build/python/cli/buildctl.py env-capture <name>
    python3 build/python/cli/buildctl.py env-diff <env1> <env2>
    python3 build/python/cli/buildctl.py impact --file <path>
    python3 build/python/cli/buildctl.py bisect --good <ref> --bad <ref>
    python3 build/python/cli/buildctl.py metrics
    python3 build/python/cli/buildctl.py history
"""

from __future__ import annotations

import argparse
import json
import os
import platform
import re
import shutil
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

REPO_ROOT = Path(__file__).resolve().parents[3]
GREEN = "\033[0;32m"
YELLOW = "\033[1;33m"
RED = "\033[0;31m"
BLUE = "\033[0;34m"
NC = "\033[0m"

PASS = f"{GREEN}✓ pass{NC}"
WARN = f"{YELLOW}⚠ warn{NC}"
FAIL = f"{RED}✗ FAIL{NC}"


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------


def _color(text: str, code: str) -> str:
    """Apply ANSI colour if stdout is a TTY."""
    if sys.stdout.isatty():
        return f"{code}{text}{NC}"
    return text


def _run(cmd: list[str], *, capture: bool = True) -> "subprocess.CompletedProcess[str]":
    return subprocess.run(
        cmd,
        capture_output=capture,
        text=True,
        cwd=REPO_ROOT,
    )


def _have(tool: str) -> bool:
    return shutil.which(tool) is not None


def _utc_now() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


# ---------------------------------------------------------------------------
# doctor command
# ---------------------------------------------------------------------------

_REQUIRED_FILES = [
    "Meridian.sln",
    "Directory.Packages.props",
    "Directory.Build.props",
    "config/appsettings.sample.json",
]

_REQUIRED_DIRS = [
    "src",
    "tests",
    "build",
    "docs",
]

_DOTNET_MIN = (9, 0)


def _check_dotnet() -> tuple[bool, bool, str]:
    """Returns (ok, is_warning, message)."""
    if not _have("dotnet"):
        return False, False, "dotnet SDK not found — install .NET 9.0 SDK"
    result = _run(["dotnet", "--version"])
    if result.returncode != 0:
        return False, False, "dotnet --version failed"
    version_str = result.stdout.strip()
    try:
        parts = version_str.split(".")
        major, minor = int(parts[0]), int(parts[1])
        if (major, minor) < _DOTNET_MIN:
            return (
                False,
                True,
                f"dotnet {version_str} found, {_DOTNET_MIN[0]}.{_DOTNET_MIN[1]}+ required",
            )
        return True, False, f"dotnet {version_str}"
    except (IndexError, ValueError):
        return True, True, f"dotnet {version_str} (could not parse version)"


def _check_python() -> tuple[bool, bool, str]:
    version = platform.python_version()
    major, minor = sys.version_info[:2]
    if (major, minor) < (3, 8):
        return False, False, f"Python {version} found, 3.8+ required"
    return True, False, f"Python {version}"


def _check_git() -> tuple[bool, bool, str]:
    if not _have("git"):
        return False, True, "git not found — version control unavailable"
    result = _run(["git", "--version"])
    version = result.stdout.strip() if result.returncode == 0 else "unknown"
    return True, False, version


def _check_files() -> list[tuple[str, bool, bool, str]]:
    results = []
    for rel in _REQUIRED_FILES:
        path = REPO_ROOT / rel
        if path.exists():
            results.append((rel, True, False, "present"))
        else:
            results.append((rel, False, False, f"missing: {path}"))
    return results


def _check_dirs() -> list[tuple[str, bool, bool, str]]:
    results = []
    for rel in _REQUIRED_DIRS:
        path = REPO_ROOT / rel
        if path.is_dir():
            results.append((rel, True, False, "present"))
        else:
            results.append((rel, False, False, f"missing directory: {path}"))
    return results


def _check_solution_restore(quick: bool) -> tuple[bool, bool, str]:
    if quick:
        sln = REPO_ROOT / "Meridian.sln"
        if sln.exists():
            return True, False, "Meridian.sln found (restore skipped in quick mode)"
        return False, False, "Meridian.sln not found"
    result = _run(
        [
            "dotnet",
            "restore",
            "Meridian.sln",
            "/p:EnableWindowsTargeting=true",
            "--verbosity",
            "quiet",
        ]
    )
    if result.returncode == 0:
        return True, False, "dotnet restore succeeded"
    return False, False, f"dotnet restore failed: {result.stderr.strip()[:200]}"


def _print_check(label: str, ok: bool, is_warn: bool, detail: str, *, width: int = 38) -> None:
    if ok:
        status = PASS if sys.stdout.isatty() else "pass"
    elif is_warn:
        status = WARN if sys.stdout.isatty() else "warn"
    else:
        status = FAIL if sys.stdout.isatty() else "FAIL"
    padded = label.ljust(width)
    print(f"  {padded} {status}  {detail}")


def cmd_doctor(args: argparse.Namespace) -> int:
    quick: bool = getattr(args, "quick", False)
    no_fail_on_warn: bool = getattr(args, "no_fail_on_warn", False)

    print()
    print(_color("Meridian Environment Health Check", BLUE) if sys.stdout.isatty() else "Meridian Environment Health Check")
    print(_color("=" * 50, BLUE) if sys.stdout.isatty() else "=" * 50)
    print()

    failures: list[str] = []
    warnings: list[str] = []

    # --- Tooling ---
    print(_color("Tooling", BLUE) if sys.stdout.isatty() else "Tooling")

    ok, is_warn, detail = _check_python()
    _print_check("Python 3.8+", ok, is_warn, detail)
    if not ok:
        (warnings if is_warn else failures).append(f"Python: {detail}")

    ok, is_warn, detail = _check_dotnet()
    _print_check(".NET SDK 9+", ok, is_warn, detail)
    if not ok:
        (warnings if is_warn else failures).append(f".NET SDK: {detail}")

    ok, is_warn, detail = _check_git()
    _print_check("Git", ok, is_warn, detail)
    if not ok:
        (warnings if is_warn else failures).append(f"Git: {detail}")

    # Optional tools — only warn
    for tool, label in [("docker", "Docker"), ("node", "Node.js")]:
        if _have(tool):
            result = _run([tool, "--version"])
            ver = result.stdout.strip().splitlines()[0] if result.returncode == 0 else "unknown"
            _print_check(label, True, False, ver)
        else:
            _print_check(label, False, True, f"{tool} not found (optional)")
            warnings.append(f"{label} not installed (optional)")

    print()

    # --- Repository files ---
    print(_color("Repository files", BLUE) if sys.stdout.isatty() else "Repository files")

    for rel, ok, is_warn, detail in _check_files():
        _print_check(rel, ok, is_warn, detail)
        if not ok:
            (warnings if is_warn else failures).append(f"File {rel}: {detail}")

    print()

    # --- Repository directories ---
    print(_color("Repository directories", BLUE) if sys.stdout.isatty() else "Repository directories")

    for rel, ok, is_warn, detail in _check_dirs():
        _print_check(rel, ok, is_warn, detail)
        if not ok:
            (warnings if is_warn else failures).append(f"Directory {rel}: {detail}")

    print()

    # --- Solution restore ---
    print(_color("Solution", BLUE) if sys.stdout.isatty() else "Solution")

    ok, is_warn, detail = _check_solution_restore(quick)
    _print_check("dotnet restore", ok, is_warn, detail)
    if not ok:
        (warnings if is_warn else failures).append(f"Restore: {detail}")

    print()

    # --- Summary ---
    if failures:
        print(_color(f"✗ {len(failures)} check(s) failed:", RED) if sys.stdout.isatty() else f"FAIL: {len(failures)} check(s) failed:")
        for msg in failures:
            print(f"  - {msg}")
        print()
        return 1

    if warnings:
        msg = f"⚠ {len(warnings)} warning(s):"
        print(_color(msg, YELLOW) if sys.stdout.isatty() else f"WARN: {len(warnings)} warning(s):")
        for w in warnings:
            print(f"  - {w}")
        print()
        if no_fail_on_warn:
            print(_color("Environment check passed (warnings present, --no-fail-on-warn set).", YELLOW) if sys.stdout.isatty() else "WARN: Environment check passed with warnings.")
            return 0
        return 1

    print(_color("✓ Environment check passed.", GREEN) if sys.stdout.isatty() else "OK: Environment check passed.")
    return 0


# ---------------------------------------------------------------------------
# build command
# ---------------------------------------------------------------------------


def cmd_build(args: argparse.Namespace) -> int:
    project: str = getattr(args, "project", "Meridian.sln")
    configuration: str = getattr(args, "configuration", "Release")
    verbosity: str = os.environ.get("BUILD_VERBOSITY", "normal")

    print(f"Building {project} ({configuration})...")
    result = _run(
        [
            "dotnet",
            "build",
            project,
            "-c",
            configuration,
            "/p:EnableWindowsTargeting=true",
            "--verbosity",
            verbosity,
        ],
        capture=False,
    )
    return result.returncode


# ---------------------------------------------------------------------------
# collect-debug command
# ---------------------------------------------------------------------------


def cmd_collect_debug(args: argparse.Namespace) -> int:
    project: str = getattr(args, "project", "Meridian.sln")
    configuration: str = getattr(args, "configuration", "Release")
    bundle_dir = REPO_ROOT / "debug-bundle"
    bundle_dir.mkdir(exist_ok=True)

    print(f"Collecting debug bundle for {project} ({configuration}) -> {bundle_dir}")

    info: dict = {
        "timestamp": _utc_now(),
        "project": project,
        "configuration": configuration,
        "platform": platform.platform(),
        "python": platform.python_version(),
    }

    result = _run(["dotnet", "--version"])
    info["dotnet"] = result.stdout.strip() if result.returncode == 0 else "unavailable"

    result = _run(["git", "rev-parse", "HEAD"])
    info["git_sha"] = result.stdout.strip() if result.returncode == 0 else "unavailable"

    result = _run(["git", "status", "--short"])
    info["git_status"] = result.stdout.strip() if result.returncode == 0 else "unavailable"

    out_file = bundle_dir / "debug-info.json"
    out_file.write_text(json.dumps(info, indent=2), encoding="utf-8")
    print(f"Debug bundle written to {out_file}")
    return 0


# ---------------------------------------------------------------------------
# build-profile command
# ---------------------------------------------------------------------------


def cmd_build_profile(_args: argparse.Namespace) -> int:
    print("Building with timing information...")
    start = datetime.now(timezone.utc)
    result = _run(
        [
            "dotnet",
            "build",
            "Meridian.sln",
            "-c",
            "Release",
            "/p:EnableWindowsTargeting=true",
            "--verbosity",
            "normal",
        ],
        capture=False,
    )
    elapsed = (datetime.now(timezone.utc) - start).total_seconds()
    print(f"\nBuild completed in {elapsed:.1f}s (exit code {result.returncode})")
    return result.returncode


# ---------------------------------------------------------------------------
# validate-data command
# ---------------------------------------------------------------------------


def cmd_validate_data(args: argparse.Namespace) -> int:
    directory: str = getattr(args, "directory", "data/")
    data_dir = REPO_ROOT / directory

    if not data_dir.is_dir():
        print(f"Data directory not found: {data_dir}")
        print("Nothing to validate.")
        return 0

    jsonl_files = list(data_dir.rglob("*.jsonl"))
    if not jsonl_files:
        print(f"No .jsonl files found under {data_dir}")
        return 0

    errors = 0
    for f in jsonl_files:
        for lineno, line in enumerate(f.read_text(encoding="utf-8").splitlines(), start=1):
            line = line.strip()
            if not line:
                continue
            try:
                json.loads(line)
            except json.JSONDecodeError as exc:
                print(f"  {f.relative_to(REPO_ROOT)}:{lineno}: {exc}")
                errors += 1

    if errors:
        print(f"\n{errors} JSON parse error(s) found.")
        return 1

    print(f"Validated {len(jsonl_files)} JSONL file(s) — no errors.")
    return 0


# ---------------------------------------------------------------------------
# analyze-errors command
# ---------------------------------------------------------------------------

_ERROR_PATTERNS: list[tuple[re.Pattern, str]] = [
    (re.compile(r"error NU1008"), "CPM violation: remove Version= from PackageReference"),
    (re.compile(r"error NETSDK1100"), "Add /p:EnableWindowsTargeting=true to build command"),
    (re.compile(r"error CS\d+"), "C# compile error"),
    (re.compile(r"error FS\d+"), "F# compile error"),
    (re.compile(r"error MSB\d+"), "MSBuild error"),
]


def cmd_analyze_errors(_args: argparse.Namespace) -> int:
    """Read build output from stdin and report known error patterns."""
    lines = sys.stdin.read().splitlines()
    found: list[str] = []
    for line in lines:
        print(line)
        for pattern, hint in _ERROR_PATTERNS:
            if pattern.search(line):
                found.append(f"  Hint: {hint}  ({line.strip()[:120]})")

    if found:
        print()
        print("Known error patterns detected:")
        for msg in found:
            print(msg)
        return 1
    return 0


# ---------------------------------------------------------------------------
# build-graph command
# ---------------------------------------------------------------------------


def cmd_build_graph(args: argparse.Namespace) -> int:
    project: str = getattr(args, "project", "Meridian.sln")
    print(f"Generating dependency graph for {project}...")
    result = _run(
        ["dotnet", "build", project, "/p:EnableWindowsTargeting=true", "--graph", "--verbosity", "quiet"],
        capture=False,
    )
    return result.returncode


# ---------------------------------------------------------------------------
# fingerprint command
# ---------------------------------------------------------------------------


def cmd_fingerprint(args: argparse.Namespace) -> int:
    configuration: str = getattr(args, "configuration", "Release")
    print(f"Generating build fingerprint ({configuration})...")

    result = _run(["git", "rev-parse", "HEAD"])
    sha = result.stdout.strip() if result.returncode == 0 else "unknown"

    result = _run(["git", "describe", "--tags", "--always"])
    tag = result.stdout.strip() if result.returncode == 0 else "unknown"

    result = _run(["dotnet", "--version"])
    dotnet_ver = result.stdout.strip() if result.returncode == 0 else "unknown"

    fingerprint = {
        "timestamp": _utc_now(),
        "configuration": configuration,
        "git_sha": sha,
        "git_tag": tag,
        "dotnet": dotnet_ver,
        "platform": platform.platform(),
    }
    print(json.dumps(fingerprint, indent=2))
    return 0


# ---------------------------------------------------------------------------
# env-capture command
# ---------------------------------------------------------------------------


def cmd_env_capture(args: argparse.Namespace) -> int:
    name: str = getattr(args, "name", "snapshot")
    snapshots_dir = REPO_ROOT / ".ai" / "env-snapshots"
    snapshots_dir.mkdir(parents=True, exist_ok=True)

    snapshot: dict = {
        "name": name,
        "timestamp": _utc_now(),
        "platform": platform.platform(),
        "python": platform.python_version(),
        "env": {k: v for k, v in os.environ.items() if not k.lower().endswith(("key", "secret", "token", "password"))},
    }

    result = _run(["dotnet", "--version"])
    snapshot["dotnet"] = result.stdout.strip() if result.returncode == 0 else "unavailable"

    out = snapshots_dir / f"{name}.json"
    out.write_text(json.dumps(snapshot, indent=2), encoding="utf-8")
    print(f"Environment snapshot saved to {out}")
    return 0


# ---------------------------------------------------------------------------
# env-diff command
# ---------------------------------------------------------------------------


def cmd_env_diff(args: argparse.Namespace) -> int:
    env1: str = args.env1
    env2: str = args.env2
    snapshots_dir = REPO_ROOT / ".ai" / "env-snapshots"

    f1 = snapshots_dir / f"{env1}.json"
    f2 = snapshots_dir / f"{env2}.json"

    if not f1.exists():
        print(f"Snapshot not found: {f1}")
        return 1
    if not f2.exists():
        print(f"Snapshot not found: {f2}")
        return 1

    d1: dict = json.loads(f1.read_text(encoding="utf-8"))
    d2: dict = json.loads(f2.read_text(encoding="utf-8"))

    def _flat(d: dict, prefix: str = "") -> dict:
        out: dict = {}
        for k, v in d.items():
            key = f"{prefix}.{k}" if prefix else k
            if isinstance(v, dict):
                out.update(_flat(v, key))
            else:
                out[key] = str(v)
        return out

    flat1 = _flat(d1)
    flat2 = _flat(d2)
    all_keys = sorted(set(flat1) | set(flat2))

    diffs = 0
    for key in all_keys:
        v1 = flat1.get(key, "<missing>")
        v2 = flat2.get(key, "<missing>")
        if v1 != v2:
            print(f"  {key}:")
            print(f"    {env1}: {v1}")
            print(f"    {env2}: {v2}")
            diffs += 1

    if diffs == 0:
        print("No differences found.")
    else:
        print(f"\n{diffs} difference(s) between {env1} and {env2}.")
    return 0


# ---------------------------------------------------------------------------
# impact command
# ---------------------------------------------------------------------------


def cmd_impact(args: argparse.Namespace) -> int:
    file: str = args.file
    print(f"Analysing build impact of {file}...")
    result = _run(["git", "log", "--oneline", "-10", "--", file])
    if result.returncode == 0 and result.stdout.strip():
        print("Recent commits touching this file:")
        for line in result.stdout.strip().splitlines():
            print(f"  {line}")
    else:
        print("  No recent commits found for this file.")
    return 0


# ---------------------------------------------------------------------------
# bisect command
# ---------------------------------------------------------------------------


def cmd_bisect(args: argparse.Namespace) -> int:
    good: str = args.good
    bad: str = args.bad
    print(f"Build bisect: good={good}, bad={bad}")
    print("To bisect manually, run:")
    print(f"  git bisect start")
    print(f"  git bisect bad {bad}")
    print(f"  git bisect good {good}")
    print("  git bisect run dotnet build Meridian.sln /p:EnableWindowsTargeting=true -c Release -q")
    return 0


# ---------------------------------------------------------------------------
# metrics command
# ---------------------------------------------------------------------------


def cmd_metrics(_args: argparse.Namespace) -> int:
    print("Build metrics summary")
    print("=" * 40)

    result = _run(["git", "log", "--oneline", "-20"])
    commits = result.stdout.strip().splitlines() if result.returncode == 0 else []
    print(f"  Recent commits analysed : {len(commits)}")

    src_files = list((REPO_ROOT / "src").rglob("*.cs")) if (REPO_ROOT / "src").is_dir() else []
    test_files = list((REPO_ROOT / "tests").rglob("*.cs")) if (REPO_ROOT / "tests").is_dir() else []
    print(f"  C# source files         : {len(src_files)}")
    print(f"  C# test files           : {len(test_files)}")
    return 0


# ---------------------------------------------------------------------------
# history command
# ---------------------------------------------------------------------------


def cmd_history(_args: argparse.Namespace) -> int:
    print("Build history (last 10 commits)")
    print("=" * 40)
    result = _run(["git", "log", "--oneline", "-10"])
    if result.returncode == 0:
        for line in result.stdout.strip().splitlines():
            print(f"  {line}")
    else:
        print("  Unable to retrieve git history.")
    return 0


# ---------------------------------------------------------------------------
# Argument parser
# ---------------------------------------------------------------------------


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="buildctl",
        description="Meridian build control utility",
    )
    sub = parser.add_subparsers(dest="command", required=True)

    # doctor
    p_doctor = sub.add_parser("doctor", help="Run environment health check")
    p_doctor.add_argument("--quick", action="store_true", help="Skip dotnet restore")
    p_doctor.add_argument(
        "--no-fail-on-warn",
        action="store_true",
        dest="no_fail_on_warn",
        help="Exit 0 even if warnings are present",
    )

    # build
    p_build = sub.add_parser("build", help="Run build diagnostics")
    p_build.add_argument("--project", default="Meridian.sln")
    p_build.add_argument("--configuration", default="Release")

    # collect-debug
    p_cd = sub.add_parser("collect-debug", help="Collect debug bundle")
    p_cd.add_argument("--project", default="Meridian.sln")
    p_cd.add_argument("--configuration", default="Release")

    # build-profile
    sub.add_parser("build-profile", help="Build with timing information")

    # validate-data
    p_vd = sub.add_parser("validate-data", help="Validate JSONL data files")
    p_vd.add_argument("--directory", default="data/")

    # analyze-errors
    sub.add_parser("analyze-errors", help="Analyse build output for known error patterns (reads stdin)")

    # build-graph
    p_bg = sub.add_parser("build-graph", help="Generate dependency graph")
    p_bg.add_argument("--project", default="Meridian.sln")

    # fingerprint
    p_fp = sub.add_parser("fingerprint", help="Generate build fingerprint")
    p_fp.add_argument("--configuration", default="Release")

    # env-capture
    p_ec = sub.add_parser("env-capture", help="Capture environment snapshot")
    p_ec.add_argument("name", nargs="?", default="snapshot")

    # env-diff
    p_ed = sub.add_parser("env-diff", help="Compare two environment snapshots")
    p_ed.add_argument("env1")
    p_ed.add_argument("env2")

    # impact
    p_impact = sub.add_parser("impact", help="Analyse build impact for a file")
    p_impact.add_argument("--file", required=True)

    # bisect
    p_bisect = sub.add_parser("bisect", help="Build bisect helper")
    p_bisect.add_argument("--good", required=True)
    p_bisect.add_argument("--bad", required=True)

    # metrics
    sub.add_parser("metrics", help="Show build metrics summary")

    # history
    sub.add_parser("history", help="Show build history summary")

    return parser


# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------

_COMMANDS = {
    "doctor": cmd_doctor,
    "build": cmd_build,
    "collect-debug": cmd_collect_debug,
    "build-profile": cmd_build_profile,
    "validate-data": cmd_validate_data,
    "analyze-errors": cmd_analyze_errors,
    "build-graph": cmd_build_graph,
    "fingerprint": cmd_fingerprint,
    "env-capture": cmd_env_capture,
    "env-diff": cmd_env_diff,
    "impact": cmd_impact,
    "bisect": cmd_bisect,
    "metrics": cmd_metrics,
    "history": cmd_history,
}


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()
    handler = _COMMANDS.get(args.command)
    if handler is None:
        print(f"Unknown command: {args.command}", file=sys.stderr)
        return 2
    return handler(args)


if __name__ == "__main__":
    sys.exit(main())
