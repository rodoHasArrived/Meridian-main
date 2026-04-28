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
import socket
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
_DEFAULT_ISOLATION_RETENTION_DAYS = 14
_DEFAULT_ISOLATION_RETAIN_LATEST = 10
_ISOLATED_BUILD_ARTIFACT_ROOTS = ("artifacts/bin", "artifacts/obj")


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


def _run_passthrough(cmd: list[str]) -> int:
    result = subprocess.run(
        cmd,
        cwd=REPO_ROOT,
        text=True,
    )
    return result.returncode


def _build_msbuild_args(args: argparse.Namespace) -> list[str]:
    msbuild_args = ["/p:EnableWindowsTargeting=true", "-maxcpucount:1", "/nr:false"]

    framework = getattr(args, "framework", None)
    if framework:
        msbuild_args.append(f"/p:TargetFramework={framework}")

    runtime = getattr(args, "runtime", None)
    if runtime:
        msbuild_args.extend(["-r", runtime])

    isolation_key = getattr(args, "isolation_key", None)
    if isolation_key:
        msbuild_args.append(f"/p:MeridianBuildIsolationKey={isolation_key}")

    if getattr(args, "full_wpf_build", False):
        msbuild_args.append("/p:EnableFullWpfBuild=true")

    for prop in getattr(args, "property", []) or []:
        if not prop:
            continue
        if prop.startswith(("/p:", "-p:")):
            msbuild_args.append(prop)
        else:
            msbuild_args.append(f"/p:{prop}")

    return msbuild_args


def _have(tool: str) -> bool:
    return shutil.which(tool) is not None


def _utc_now() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def _default_isolation_retention_days() -> int:
    raw = os.environ.get("MERIDIAN_BUILD_ARTIFACT_RETENTION_DAYS")
    if raw is None:
        return _DEFAULT_ISOLATION_RETENTION_DAYS
    try:
        return int(raw)
    except ValueError:
        return _DEFAULT_ISOLATION_RETENTION_DAYS


def _default_isolation_retain_latest() -> int:
    raw = os.environ.get("MERIDIAN_BUILD_ARTIFACT_RETAIN_LATEST")
    if raw is None:
        return _DEFAULT_ISOLATION_RETAIN_LATEST
    try:
        return int(raw)
    except ValueError:
        return _DEFAULT_ISOLATION_RETAIN_LATEST


def _format_bytes(byte_count: int) -> str:
    if byte_count >= 1024**3:
        return f"{byte_count / 1024**3:.2f} GB"
    if byte_count >= 1024**2:
        return f"{byte_count / 1024**2:.2f} MB"
    if byte_count >= 1024:
        return f"{byte_count / 1024:.2f} KB"
    return f"{byte_count} B"


def _path_is_relative_to(path: Path, root: Path) -> bool:
    try:
        path.relative_to(root)
        return True
    except ValueError:
        return False


def _directory_stats(path: Path) -> tuple[int, float]:
    total = 0
    newest_mtime = path.stat().st_mtime
    for child in path.rglob("*"):
        try:
            child_stat = child.stat()
        except OSError:
            continue
        newest_mtime = max(newest_mtime, child_stat.st_mtime)
        if child.is_file():
            total += child_stat.st_size
    return total, newest_mtime


def _prune_isolated_build_artifacts(
    repo_root: Path,
    *,
    max_age_days: int,
    retain_latest: int = _DEFAULT_ISOLATION_RETAIN_LATEST,
    active_isolation_key: str | None = None,
    now: datetime | None = None,
) -> tuple[int, int]:
    """Remove stale isolated MSBuild output directories under artifacts/bin and artifacts/obj."""
    if max_age_days <= 0 and retain_latest <= 0:
        return 0, 0

    reference_time = now or datetime.now(timezone.utc)
    if reference_time.tzinfo is None:
        reference_time = reference_time.replace(tzinfo=timezone.utc)
    cutoff_timestamp = reference_time.timestamp() - (max_age_days * 24 * 60 * 60)
    active_key = active_isolation_key.casefold() if active_isolation_key else None
    deleted_count = 0
    freed_bytes = 0

    for relative_root in _ISOLATED_BUILD_ARTIFACT_ROOTS:
        artifact_root = (repo_root / relative_root).resolve()
        if not artifact_root.is_dir():
            continue

        candidates: list[tuple[Path, int, float]] = []
        for directory in artifact_root.iterdir():
            if not directory.is_dir() or directory.is_symlink():
                continue

            if active_key and directory.name.casefold() == active_key:
                continue

            candidate_path = directory.resolve()
            if not _path_is_relative_to(candidate_path, artifact_root):
                print(
                    f"WARN: Skipping isolated build artifact candidate outside expected root: {candidate_path}",
                    file=sys.stderr,
                )
                continue

            try:
                candidate_bytes, newest_mtime = _directory_stats(candidate_path)
            except OSError as exc:
                print(
                    f"WARN: Failed to inspect isolated build artifact directory '{candidate_path}': {exc}",
                    file=sys.stderr,
                )
                continue

            candidates.append((candidate_path, candidate_bytes, newest_mtime))

        retained_by_count: set[Path] = set()
        if retain_latest > 0:
            retained_by_count = {
                path
                for path, _, _ in sorted(candidates, key=lambda item: item[2], reverse=True)[:retain_latest]
            }

        for candidate_path, candidate_bytes, newest_mtime in candidates:
            age_expired = max_age_days > 0 and newest_mtime < cutoff_timestamp
            count_exceeded = retain_latest > 0 and candidate_path not in retained_by_count
            if not age_expired and not count_exceeded:
                continue

            try:
                shutil.rmtree(candidate_path)
            except OSError as exc:
                print(
                    f"WARN: Failed to prune isolated build artifact directory '{candidate_path}': {exc}",
                    file=sys.stderr,
                )
                continue

            deleted_count += 1
            freed_bytes += candidate_bytes

    return deleted_count, freed_bytes


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

# ---------------------------------------------------------------------------
# Provider environment variable registry
# Each tuple: (env_var_name, provider_label, affected_capability)
# ---------------------------------------------------------------------------

_PROVIDER_ENV_VARS: list[tuple[str, str, str]] = [
    ("ALPACA_KEY_ID",         "Alpaca",          "streaming and historical data"),
    ("ALPACA_SECRET_KEY",     "Alpaca",          "streaming and historical data"),
    ("POLYGON_API_KEY",       "Polygon",         "streaming and historical tests"),
    ("NYSE_API_KEY",          "NYSE",            "TAQ streaming"),
    ("TIINGO_API_TOKEN",      "Tiingo",          "historical data"),
    ("FINNHUB_API_KEY",       "Finnhub",         "historical data and symbol search"),
    ("ALPHA_VANTAGE_API_KEY", "Alpha Vantage",   "historical data"),
    ("NASDAQ_API_KEY",        "Nasdaq Data Link","historical data"),
]

_POSTGRES_DEFAULT_HOST = "localhost"
_POSTGRES_DEFAULT_PORT = 5432
_POSTGRES_DOCKER_FIX = (
    "docker run --rm -p 5432:5432 -e POSTGRES_PASSWORD=secret postgres:16-alpine"
)


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


def _check_docker_daemon() -> tuple[bool, bool, str, str | None]:
    """Check that Docker is installed AND its daemon is reachable."""
    if not _have("docker"):
        return (
            False,
            True,
            "Docker not installed (optional — needed for Testcontainers and PostgreSQL)",
            "Install Docker Desktop or Engine from https://docs.docker.com/get-docker/",
        )
    result = _run(["docker", "info"])
    if result.returncode == 0:
        ver_result = _run(["docker", "--version"])
        ver = (
            ver_result.stdout.strip().splitlines()[0]
            if ver_result.returncode == 0
            else "unknown"
        )
        return True, False, f"daemon reachable ({ver})", None
    return (
        False,
        True,
        "Docker installed but daemon not running",
        "Start Docker Desktop, or run: sudo systemctl start docker",
    )


def _check_postgres() -> tuple[bool, bool, str, str | None]:
    """Attempt a TCP connection to the PostgreSQL port and report fix hints."""
    conn_str = os.getenv("MERIDIAN_SECURITY_MASTER_CONNECTION_STRING", "")
    host = _POSTGRES_DEFAULT_HOST
    port = _POSTGRES_DEFAULT_PORT

    if conn_str:
        host_m = re.search(r"[Hh]ost=([^;, ]+)", conn_str)
        port_m = re.search(r"[Pp]ort=(\d+)", conn_str)
        if host_m:
            host = host_m.group(1)
        if port_m:
            port = int(port_m.group(1))

    try:
        with socket.create_connection((host, port), timeout=1.0):
            pass
        label = f"PostgreSQL on {host}:{port}"
        return True, False, f"{label} — reachable", None
    except (socket.timeout, ConnectionRefusedError, OSError):
        label = f"PostgreSQL on {host}:{port}"
        if conn_str:
            fix = "Check MERIDIAN_SECURITY_MASTER_CONNECTION_STRING or start your PostgreSQL server"
        else:
            fix = _POSTGRES_DOCKER_FIX
        return (
            False,
            True,
            (
                f"{label} — UNAVAILABLE"
                " (set MERIDIAN_SECURITY_MASTER_CONNECTION_STRING or start Docker)"
            ),
            fix,
        )


def _check_env_vars() -> list[tuple[str, bool, bool, str, str | None]]:
    """Return a check row per provider credential env var."""
    results: list[tuple[str, bool, bool, str, str | None]] = []
    for var, provider, purpose in _PROVIDER_ENV_VARS:
        value = os.getenv(var, "")
        if value:
            if len(value) > 4:
                masked = f"{value[:2]}***{value[-1]}"
            else:
                masked = "****"
            results.append((var, True, False, f"set ({masked})", None))
        else:
            fix = f"export {var}=<your-key>  # enables {provider} {purpose}"
            results.append(
                (
                    var,
                    False,
                    True,
                    "not set — related tests will skip",
                    fix,
                )
            )
    return results


def _check_global_json() -> tuple[bool, bool, str, str | None]:
    """Verify the installed .NET SDK satisfies the global.json version constraint."""
    global_json = REPO_ROOT / "global.json"
    if not global_json.exists():
        return True, False, "global.json not found (no constraint to verify)", None

    try:
        data = json.loads(global_json.read_text(encoding="utf-8"))
        required = data.get("sdk", {}).get("version", "")
        roll_forward = data.get("sdk", {}).get("rollForward", "latestMinor")
    except (json.JSONDecodeError, OSError) as exc:
        return False, True, f"global.json could not be parsed: {exc}", None

    if not required:
        return True, False, "global.json: no SDK version constraint", None

    result = _run(["dotnet", "--version"])
    if result.returncode != 0:
        return False, False, "dotnet SDK not found", "Install from https://dot.net/download"

    installed = result.stdout.strip()

    try:
        req_parts = [int(x) for x in required.split(".")]
        ins_parts = [int(x) for x in installed.split(".")]
        # For latestMinor / latestPatch / minor / patch: major must match,
        # installed must be >= required
        if roll_forward in ("latestMinor", "latestPatch", "minor", "patch", "feature"):
            ok = ins_parts[0] == req_parts[0] and ins_parts >= req_parts
        elif roll_forward == "disable":
            ok = ins_parts == req_parts
        else:
            # "latestMajor" or unknown — only require installed >= required
            ok = ins_parts >= req_parts
    except (ValueError, IndexError):
        return (
            True,
            True,
            f"global.json constraint could not be verified (installed {installed})",
            None,
        )

    if ok:
        return (
            True,
            False,
            f"satisfied — requires {required} (rollForward={roll_forward}), installed {installed}",
            None,
        )
    return (
        False,
        True,
        f"requires {required} (rollForward={roll_forward}), installed {installed}",
        f"Install .NET SDK {required} from https://dot.net/download",
    )


def _check_packages_props() -> tuple[bool, bool, str, str | None]:
    """Verify every PackageVersion entry in Directory.Packages.props has a Version."""
    props = REPO_ROOT / "Directory.Packages.props"
    if not props.exists():
        return False, False, "Directory.Packages.props not found", None

    try:
        content = props.read_text(encoding="utf-8")
    except OSError as exc:
        return False, True, f"could not read Directory.Packages.props: {exc}", None

    # Entries that have Include= but no Version=
    no_version = re.findall(
        r'<PackageVersion\s+Include="([^"]+)"(?![^>]*Version=)[^>]*/?>',
        content,
    )
    if no_version:
        sample = ", ".join(no_version[:3])
        return (
            False,
            True,
            f"PackageVersion entries without Version attribute: {sample}",
            "Add Version=\"x.y.z\" to each flagged PackageVersion in Directory.Packages.props",
        )

    total = len(re.findall(r'<PackageVersion\s+Include=', content))
    return True, False, f"all {total} package versions present", None


def _check_native_tools() -> list[tuple[str, bool, bool, str, str | None]]:
    """Check CMake and a C++ compiler (optional — needed for CppTrader)."""
    results: list[tuple[str, bool, bool, str, str | None]] = []

    if _have("cmake"):
        ver_result = _run(["cmake", "--version"])
        ver = (
            ver_result.stdout.strip().splitlines()[0]
            if ver_result.returncode == 0
            else "unknown"
        )
        results.append(("CMake", True, False, ver, None))
    else:
        results.append((
            "CMake",
            False,
            True,
            "not found (optional — needed for CppTrader native engine)",
            "Install CMake from https://cmake.org/download/",
        ))

    cpp = shutil.which("g++") or shutil.which("clang++") or shutil.which("cl")
    if cpp:
        compiler_name = Path(cpp).name
        results.append(("C++ compiler", True, False, f"{compiler_name} found", None))
    else:
        results.append((
            "C++ compiler",
            False,
            True,
            "not found (optional — needed for CppTrader native engine)",
            "Linux: apt install build-essential  |  macOS: xcode-select --install",
        ))

    return results


def _print_check(label: str, ok: bool, is_warn: bool, detail: str, *, width: int = 38) -> None:
    if ok:
        status = PASS if sys.stdout.isatty() else "pass"
    elif is_warn:
        status = WARN if sys.stdout.isatty() else "warn"
    else:
        status = FAIL if sys.stdout.isatty() else "FAIL"
    padded = label.ljust(width)
    print(f"  {padded} {status}  {detail}")


def _print_fix(fix: str) -> None:
    """Print an actionable fix hint indented below the check line."""
    arrow = _color("  →", YELLOW) if sys.stdout.isatty() else "  ->"
    print(f"  {arrow} Run: {fix}")


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

    if _have("node"):
        result = _run(["node", "--version"])
        ver = result.stdout.strip().splitlines()[0] if result.returncode == 0 else "unknown"
        _print_check("Node.js", True, False, f"{ver} (optional — diagram generation)")
    else:
        _print_check("Node.js", False, True, "not found (optional — needed for diagram generation)")
        _print_fix("Install from https://nodejs.org")
        warnings.append("Node.js not installed (optional)")

    print()

    # --- Services ---
    print(_color("Services", BLUE) if sys.stdout.isatty() else "Services")

    ok, is_warn, detail, fix = _check_docker_daemon()
    _print_check("Docker daemon", ok, is_warn, detail)
    if fix:
        _print_fix(fix)
    if not ok:
        (warnings if is_warn else failures).append(f"Docker: {detail}")

    ok, is_warn, detail, fix = _check_postgres()
    _print_check("PostgreSQL", ok, is_warn, detail)
    if fix:
        _print_fix(fix)
    if not ok:
        (warnings if is_warn else failures).append(f"PostgreSQL: {detail}")

    print()

    # --- Native tools (optional) ---
    print(
        _color("Native tools (optional — CppTrader)", BLUE)
        if sys.stdout.isatty()
        else "Native tools (optional — CppTrader)"
    )

    for label, ok, is_warn, detail, fix in _check_native_tools():
        _print_check(label, ok, is_warn, detail)
        if fix:
            _print_fix(fix)
        if not ok:
            (warnings if is_warn else failures).append(f"{label}: {detail}")

    print()

    # --- Provider credentials ---
    print(_color("Provider credentials", BLUE) if sys.stdout.isatty() else "Provider credentials")

    for var, ok, is_warn, detail, fix in _check_env_vars():
        _print_check(var, ok, is_warn, detail)
        if fix:
            _print_fix(fix)
        if not ok:
            (warnings if is_warn else failures).append(f"{var}: {detail}")

    print()

    # --- Repository files ---
    print(_color("Repository files", BLUE) if sys.stdout.isatty() else "Repository files")

    ok, is_warn, detail, fix = _check_global_json()
    _print_check("global.json SDK constraint", ok, is_warn, detail)
    if fix:
        _print_fix(fix)
    if not ok:
        (warnings if is_warn else failures).append(f"global.json: {detail}")

    ok, is_warn, detail, fix = _check_packages_props()
    _print_check("Directory.Packages.props", ok, is_warn, detail)
    if fix:
        _print_fix(fix)
    if not ok:
        (warnings if is_warn else failures).append(f"Directory.Packages.props: {detail}")

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
    verbosity: str = getattr(args, "verbosity", os.environ.get("BUILD_VERBOSITY", "normal"))
    msbuild_args = _build_msbuild_args(args)
    isolation_key = getattr(args, "isolation_key", None)

    if isolation_key:
        deleted_count, freed_bytes = _prune_isolated_build_artifacts(
            REPO_ROOT,
            max_age_days=getattr(args, "isolation_retention_days", _DEFAULT_ISOLATION_RETENTION_DAYS),
            retain_latest=getattr(args, "isolation_retain_latest", _DEFAULT_ISOLATION_RETAIN_LATEST),
            active_isolation_key=isolation_key,
        )
        if deleted_count:
            print(
                "INFO: Pruned "
                f"{deleted_count} isolated build artifact "
                f"{'directory' if deleted_count == 1 else 'directories'} "
                "using age/count retention "
                "(older than "
                f"{getattr(args, 'isolation_retention_days', _DEFAULT_ISOLATION_RETENTION_DAYS)} days "
                "or beyond latest "
                f"{getattr(args, 'isolation_retain_latest', _DEFAULT_ISOLATION_RETAIN_LATEST)} per root) "
                "from artifacts/bin and artifacts/obj "
                f"({_format_bytes(freed_bytes)} recovered)."
            )

    if getattr(args, "shutdown_build_servers", False):
        print("Shutting down dotnet build servers...")
        shutdown_code = _run_passthrough(["dotnet", "build-server", "shutdown"])
        if shutdown_code != 0:
            return shutdown_code

    if not getattr(args, "skip_restore", False):
        print(f"Restoring {project}...")
        restore_code = _run_passthrough(
            ["dotnet", "restore", project, "--verbosity", verbosity, *msbuild_args]
        )
        if restore_code != 0:
            return restore_code

    print(f"Building {project} ({configuration})...")
    return _run_passthrough(
        [
            "dotnet",
            "build",
            project,
            "-c",
            configuration,
            "--verbosity",
            verbosity,
            "-nologo",
            "--no-restore",
            *msbuild_args,
        ]
    )


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
    p_build = sub.add_parser("build", help="Restore once and build sequentially")
    p_build.add_argument("--project", default="Meridian.sln")
    p_build.add_argument("--configuration", default="Release")
    p_build.add_argument("--framework")
    p_build.add_argument("--runtime")
    p_build.add_argument("--verbosity", default=os.environ.get("BUILD_VERBOSITY", "normal"))
    p_build.add_argument("--skip-restore", action="store_true")
    p_build.add_argument("--full-wpf-build", action="store_true")
    p_build.add_argument("--shutdown-build-servers", action="store_true")
    p_build.add_argument("--isolation-key")
    p_build.add_argument(
        "--isolation-retention-days",
        type=int,
        default=_default_isolation_retention_days(),
        help=(
            "Prune isolated artifacts/bin and artifacts/obj output directories older than this "
            "many days before an isolated build; set 0 to disable age-based pruning."
        ),
    )
    p_build.add_argument(
        "--isolation-retain-latest",
        type=int,
        default=_default_isolation_retain_latest(),
        help=(
            "Retain this many newest isolated artifacts/bin and artifacts/obj output directories "
            "per root before count-based pruning; set 0 to disable count-based pruning."
        ),
    )
    p_build.add_argument("--property", action="append", default=[])

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
