#!/usr/bin/env python3
"""buildctl - Meridian build control utility.

Provides environment health checks, build diagnostics, and tooling utilities
for the Meridian project.

Usage:
    python3 build/python/cli/buildctl.py doctor [--quick] [--no-fail-on-warn]
    python3 build/python/cli/buildctl.py build --project <path> --configuration <cfg>
    python3 build/python/cli/buildctl.py test --project <path> [--filter <expr>]
    python3 build/python/cli/buildctl.py validation-status
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
import time
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
_DEFAULT_ISOLATION_MAX_ROOT_SIZE_MB = 4096
_ISOLATED_BUILD_ARTIFACT_ROOTS = ("artifacts/bin", "artifacts/obj")
_VALIDATION_LOCK_RELATIVE_PATH = ".ai/locks/validation.lock"
_VALIDATION_RUNS_RELATIVE_PATH = ".ai/validation-runs"
_CONTENTIOUS_PROCESS_NAMES = ("dotnet.exe", "MSBuild.exe", "testhost.exe", "csc.exe", "VBCSCompiler.exe")


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


def _build_serialized_msbuild_args(args: argparse.Namespace) -> list[str]:
    return [
        *_build_msbuild_args(args),
        "/p:BuildInParallel=false",
        "/p:UseSharedCompilation=false",
    ]


def _have(tool: str) -> bool:
    return shutil.which(tool) is not None


def _utc_now() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def _safe_slug(value: str) -> str:
    slug = re.sub(r"[^a-zA-Z0-9]+", "-", value).strip("-").lower()
    return slug or "validation"


def _new_run_id(prefix: str) -> str:
    timestamp = datetime.now(timezone.utc).strftime("%Y%m%d%H%M%S")
    return f"{_safe_slug(prefix)}-{os.getpid()}-{timestamp}"


def _write_json(path: Path, payload: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def _validation_lock_path(repo_root: Path | None = None) -> Path:
    return (repo_root or REPO_ROOT) / _VALIDATION_LOCK_RELATIVE_PATH


def _validation_runs_dir(repo_root: Path | None = None) -> Path:
    return (repo_root or REPO_ROOT) / _VALIDATION_RUNS_RELATIVE_PATH


def _read_validation_lock(repo_root: Path | None = None) -> dict | None:
    lock_path = _validation_lock_path(repo_root)
    if not lock_path.exists():
        return None
    try:
        return json.loads(lock_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return {
            "path": str(lock_path),
            "error": "validation lock exists but could not be parsed",
        }


def _acquire_validation_lock(
    repo_root: Path,
    *,
    run_id: str,
    command: str,
    queue: bool,
    timeout_seconds: int,
) -> bool:
    lock_path = _validation_lock_path(repo_root)
    lock_path.parent.mkdir(parents=True, exist_ok=True)
    deadline = time.monotonic() + max(0, timeout_seconds)
    payload = {
        "runId": run_id,
        "pid": os.getpid(),
        "command": command,
        "startedAt": _utc_now(),
    }

    while True:
        try:
            fd = os.open(str(lock_path), os.O_CREAT | os.O_EXCL | os.O_WRONLY)
            with os.fdopen(fd, "w", encoding="utf-8") as handle:
                handle.write(json.dumps(payload, indent=2, sort_keys=True) + "\n")
            return True
        except FileExistsError:
            if not queue or time.monotonic() >= deadline:
                return False
            time.sleep(2)


def _release_validation_lock(repo_root: Path, run_id: str) -> None:
    lock_path = _validation_lock_path(repo_root)
    try:
        current = _read_validation_lock(repo_root)
        if current and current.get("runId") != run_id:
            return
        lock_path.unlink(missing_ok=True)
    except OSError as exc:
        print(f"WARN: Failed to release validation lock '{lock_path}': {exc}", file=sys.stderr)


def _find_powershell() -> str | None:
    return shutil.which("pwsh") or shutil.which("powershell")


def _get_active_repo_build_processes(repo_root: Path | None = None) -> list[dict[str, object]]:
    repo_root = repo_root or REPO_ROOT
    if platform.system().lower() != "windows":
        return []

    powershell = _find_powershell()
    if not powershell:
        return []

    repo = "'" + str(repo_root.resolve()).replace("'", "''") + "'"
    names = " OR ".join(f"Name = '{name}'" for name in _CONTENTIOUS_PROCESS_NAMES)
    script = f"""
$repo = [System.IO.Path]::GetFullPath({repo})
$processes = @(Get-CimInstance Win32_Process -Filter "{names}" -ErrorAction SilentlyContinue)
$repoBuildProcessIds = @(
  $processes | Where-Object {{
    $_.ProcessId -ne {os.getpid()} -and
    $_.CommandLine -and
    (
      $_.CommandLine.IndexOf($repo, [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
      $_.CommandLine.IndexOf("Meridian.Wpf", [System.StringComparison]::OrdinalIgnoreCase) -ge 0
    ) -and
    (
      $_.Name -eq "MSBuild.exe" -or
      $_.Name -eq "testhost.exe" -or
      $_.CommandLine -match '(?i)\\bdotnet(\\.exe)?"?\\s+(build|test|restore|msbuild|clean)\\b' -or
      $_.CommandLine.IndexOf("MSBuild.dll", [System.StringComparison]::OrdinalIgnoreCase) -ge 0
    )
  }} | Select-Object -ExpandProperty ProcessId
)
$result = @(
  $processes | Where-Object {{
    $_.ProcessId -ne {os.getpid()} -and
    (
      $repoBuildProcessIds -contains $_.ProcessId -or
      (($_.Name -eq "csc.exe" -or $_.Name -eq "VBCSCompiler.exe") -and $repoBuildProcessIds -contains $_.ParentProcessId)
    )
  }} | ForEach-Object {{
    [pscustomobject]@{{
      processId = $_.ProcessId
      parentProcessId = $_.ParentProcessId
      name = $_.Name
      commandLine = $_.CommandLine
    }}
  }}
)
$result | ConvertTo-Json -Compress
"""
    result = subprocess.run(
        [powershell, "-NoProfile", "-Command", script],
        capture_output=True,
        text=True,
        cwd=repo_root,
    )
    if result.returncode != 0 or not result.stdout.strip():
        return []

    try:
        payload = json.loads(result.stdout)
    except json.JSONDecodeError:
        return []

    if isinstance(payload, dict):
        return [payload]
    if isinstance(payload, list):
        return [item for item in payload if isinstance(item, dict)]
    return []


def _wait_for_no_active_repo_build_processes(timeout_seconds: int) -> list[dict[str, object]]:
    deadline = time.monotonic() + max(0, timeout_seconds)
    active = _get_active_repo_build_processes()
    while active and time.monotonic() < deadline:
        time.sleep(2)
        active = _get_active_repo_build_processes()
    return active


def _resolve_isolation_key(raw_key: str | None, *, run_id: str, disabled: bool) -> str | None:
    if disabled:
        return None
    if not raw_key or raw_key == "auto":
        return run_id
    return raw_key


def _prune_for_isolation(args: argparse.Namespace, isolation_key: str | None) -> None:
    if not isolation_key:
        return

    isolation_retention_days = getattr(args, "isolation_retention_days", _DEFAULT_ISOLATION_RETENTION_DAYS)
    isolation_retain_latest = getattr(args, "isolation_retain_latest", _DEFAULT_ISOLATION_RETAIN_LATEST)
    isolation_max_root_size_mb = getattr(
        args,
        "isolation_max_root_size_mb",
        _DEFAULT_ISOLATION_MAX_ROOT_SIZE_MB,
    )
    deleted_count, freed_bytes = _prune_isolated_build_artifacts(
        REPO_ROOT,
        max_age_days=isolation_retention_days,
        retain_latest=isolation_retain_latest,
        max_root_size_mb=isolation_max_root_size_mb,
        active_isolation_key=isolation_key,
    )
    if deleted_count:
        policies = []
        if isolation_retention_days > 0:
            policies.append(f"older than {isolation_retention_days} days")
        if isolation_retain_latest > 0:
            policies.append(f"beyond latest {isolation_retain_latest} per root")
        if isolation_max_root_size_mb > 0:
            policies.append(f"above {isolation_max_root_size_mb} MB per root")

        print(
            "INFO: Pruned "
            f"{deleted_count} isolated build artifact "
            f"{'directory' if deleted_count == 1 else 'directories'} "
            f"using age/count/size retention ({' or '.join(policies)}) "
            "from artifacts/bin and artifacts/obj "
            f"({_format_bytes(freed_bytes)} recovered)."
        )


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


def _default_isolation_max_root_size_mb() -> int:
    raw = os.environ.get("MERIDIAN_BUILD_ARTIFACT_MAX_ROOT_SIZE_MB")
    if raw is None:
        return _DEFAULT_ISOLATION_MAX_ROOT_SIZE_MB
    try:
        return int(raw)
    except ValueError:
        return _DEFAULT_ISOLATION_MAX_ROOT_SIZE_MB


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
    max_root_size_mb: int = _DEFAULT_ISOLATION_MAX_ROOT_SIZE_MB,
    active_isolation_key: str | None = None,
    now: datetime | None = None,
) -> tuple[int, int]:
    """Remove stale isolated MSBuild output directories under artifacts/bin and artifacts/obj."""
    if max_age_days <= 0 and retain_latest <= 0 and max_root_size_mb <= 0:
        return 0, 0

    reference_time = now or datetime.now(timezone.utc)
    if reference_time.tzinfo is None:
        reference_time = reference_time.replace(tzinfo=timezone.utc)
    cutoff_timestamp = reference_time.timestamp() - (max_age_days * 24 * 60 * 60)
    max_root_bytes = max_root_size_mb * 1024 * 1024 if max_root_size_mb > 0 else 0
    active_key = active_isolation_key.casefold() if active_isolation_key else None
    deleted_count = 0
    freed_bytes = 0

    resolved_repo_root = repo_root.resolve()

    for relative_root in _ISOLATED_BUILD_ARTIFACT_ROOTS:
        artifact_root_link = repo_root / relative_root
        if artifact_root_link.is_symlink():
            print(
                f"WARN: Skipping isolated build artifact root symlink: {artifact_root_link}",
                file=sys.stderr,
            )
            continue

        artifact_root = artifact_root_link.resolve()
        if not _path_is_relative_to(artifact_root, resolved_repo_root):
            print(
                f"WARN: Skipping isolated build artifact root outside repository: {artifact_root}",
                file=sys.stderr,
            )
            continue

        if not artifact_root.is_dir():
            continue

        candidates: list[tuple[Path, int, float]] = []
        active_bytes = 0
        for directory in artifact_root.iterdir():
            if not directory.is_dir() or directory.is_symlink():
                continue

            candidate_path = directory.resolve()
            if not _path_is_relative_to(candidate_path, artifact_root):
                print(
                    f"WARN: Skipping isolated build artifact candidate outside expected root: {candidate_path}",
                    file=sys.stderr,
                )
                continue

            if active_key and directory.name.casefold() == active_key:
                try:
                    active_bytes, _ = _directory_stats(candidate_path)
                except OSError as exc:
                    print(
                        f"WARN: Failed to inspect active isolated build artifact directory '{candidate_path}': {exc}",
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

        candidates_by_path = {path: (candidate_bytes, newest_mtime) for path, candidate_bytes, newest_mtime in candidates}
        delete_paths: set[Path] = set()
        for candidate_path, candidate_bytes, newest_mtime in candidates:
            age_expired = max_age_days > 0 and newest_mtime < cutoff_timestamp
            count_exceeded = retain_latest > 0 and candidate_path not in retained_by_count
            if age_expired or count_exceeded:
                delete_paths.add(candidate_path)

        if max_root_bytes > 0:
            projected_root_bytes = active_bytes + sum(candidate_bytes for _, candidate_bytes, _ in candidates)
            projected_root_bytes -= sum(candidates_by_path[path][0] for path in delete_paths)
            for candidate_path, candidate_bytes, _ in sorted(candidates, key=lambda item: item[2]):
                if projected_root_bytes <= max_root_bytes:
                    break
                if candidate_path in delete_paths:
                    continue

                delete_paths.add(candidate_path)
                projected_root_bytes -= candidate_bytes

        for candidate_path in delete_paths:
            resolved_candidate = candidate_path.resolve()
            if candidate_path.is_symlink() or not _path_is_relative_to(resolved_candidate, artifact_root):
                print(
                    f"WARN: Skipping isolated build artifact delete outside expected root: {candidate_path}",
                    file=sys.stderr,
                )
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
            freed_bytes += candidates_by_path[candidate_path][0]

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

_DOTNET_MIN = (10, 0)

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
        return False, False, "dotnet SDK not found — install .NET 10.0 SDK"
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
    if (major, minor) < (3, 10):
        return False, False, f"Python {version} found, 3.10+ required"
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
    # Strip each variable before the `or`: a whitespace-only scoped value is truthy in Python,
    # so selecting before trimming would pick it over a real MERIDIAN_DATABASE_URL and probe the
    # localhost default. Runtime propagation treats the scoped value as unset via
    # IsNullOrWhiteSpace and falls back to the unified URL — mirror that here. Keep the name of the
    # selected variable so diagnostics point the operator at the setting that actually supplied the
    # value rather than always naming MERIDIAN_DATABASE_URL.
    security_master = os.getenv("MERIDIAN_SECURITY_MASTER_CONNECTION_STRING", "").strip()
    database_url = os.getenv("MERIDIAN_DATABASE_URL", "").strip()
    if security_master:
        conn_str, conn_var = security_master, "MERIDIAN_SECURITY_MASTER_CONNECTION_STRING"
    else:
        conn_str, conn_var = database_url, "MERIDIAN_DATABASE_URL"
    host = _POSTGRES_DEFAULT_HOST
    port = _POSTGRES_DEFAULT_PORT

    # The runtime trims the unified URL and matches the scheme case-insensitively in
    # MeridianDatabaseEnvironment.NormalizeToConnectionString; mirror that here so an
    # uppercase or whitespace-padded URL is parsed rather than skipped.
    if conn_str.lower().startswith(("postgres://", "postgresql://")):
        # URL form: postgres://user:pass@host:port/db
        from urllib.parse import urlsplit

        try:
            parts = urlsplit(conn_str)
            resolved_host = parts.hostname
            resolved_port = parts.port
        except ValueError:
            # A structurally malformed URL (bad host, or a non-numeric/out-of-range
            # port that urlsplit or the port property rejects) is a configuration
            # error, not an unreachable server: the runtime rejects the same URL in
            # MeridianDatabaseEnvironment.NormalizeToConnectionString, so report it
            # loudly instead of probing a default port and masking the problem.
            return (
                False,
                True,
                f"{conn_var} is not a valid connection URL and would fail at startup",
                f"Fix {conn_var} to postgres://user:password@host:port/database"
                " with a numeric port in 1-65535",
            )
        if resolved_host:
            host = resolved_host
        if resolved_port:
            port = resolved_port
    elif conn_str:
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
            fix = f"Check {conn_var} or start your PostgreSQL server"
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

    if not _have("dotnet"):
        return False, False, "dotnet SDK not found", "Install from https://dot.net/download"

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
    _print_check("Python 3.10+", ok, is_warn, detail)
    if not ok:
        (warnings if is_warn else failures).append(f"Python: {detail}")

    ok, is_warn, detail = _check_dotnet()
    _print_check(".NET SDK 10+", ok, is_warn, detail)
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

    _prune_for_isolation(args, isolation_key)

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
# test and validation status commands
# ---------------------------------------------------------------------------


def _record_validation_run(run_path: Path, payload: dict, **updates: object) -> None:
    payload.update(updates)
    _write_json(run_path, payload)


def cmd_validation_status(args: argparse.Namespace) -> int:
    lock = _read_validation_lock()
    active_processes = _get_active_repo_build_processes()
    blocked = bool(lock or active_processes)
    status = {
        "timestamp": _utc_now(),
        "lock": lock,
        "activeRepoBuildProcesses": active_processes,
        "blocked": blocked,
    }

    if getattr(args, "json_output", None):
        _write_json(Path(args.json_output), status)

    if getattr(args, "summary", False) or not getattr(args, "json_output", None):
        print(f"blocked={str(blocked).lower()}")
        print(f"lock={'present' if lock else 'none'}")
        print(f"active_processes={len(active_processes)}")
        for process in active_processes[:10]:
            print(
                "  PID {pid} {name}: {command}".format(
                    pid=process.get("processId"),
                    name=process.get("name"),
                    command=str(process.get("commandLine", ""))[:240],
                )
            )

    return 1 if blocked and getattr(args, "fail_on_active", False) else 0


def cmd_test(args: argparse.Namespace) -> int:
    if getattr(args, "lane", "dotnet") != "dotnet":
        print("Only lane=dotnet is currently supported by buildctl test.", file=sys.stderr)
        return 2

    project: str = getattr(args, "project", "tests/Meridian.Tests/Meridian.Tests.csproj")
    configuration: str = getattr(args, "configuration", "Release")
    verbosity: str = getattr(args, "verbosity", os.environ.get("BUILD_VERBOSITY", "normal"))
    run_id = getattr(args, "run_id", None) or _new_run_id(f"test-{Path(project).stem}")
    isolation_key = _resolve_isolation_key(
        getattr(args, "isolation_key", "auto"),
        run_id=run_id,
        disabled=getattr(args, "no_isolation", False),
    )

    if getattr(args, "no_build", False) and getattr(args, "isolation_key", "auto") == "auto" and not getattr(args, "no_isolation", False):
        print(
            "--no-build cannot be used with the default auto isolation key because no matching build output exists.",
            file=sys.stderr,
        )
        return 2

    run_path = _validation_runs_dir() / f"{run_id}.json"
    run_payload: dict = {
        "runId": run_id,
        "command": "test",
        "lane": "dotnet",
        "project": project,
        "filter": getattr(args, "filter", ""),
        "configuration": configuration,
        "isolationKey": isolation_key,
        "startedAt": _utc_now(),
        "status": "started",
        "steps": [],
    }
    _write_json(run_path, run_payload)

    lock_acquired = False
    if not getattr(args, "allow_concurrent", False):
        lock_acquired = _acquire_validation_lock(
            REPO_ROOT,
            run_id=run_id,
            command=f"buildctl test --project {project}",
            queue=getattr(args, "queue", False),
            timeout_seconds=getattr(args, "queue_timeout_seconds", 300),
        )
        if not lock_acquired:
            lock = _read_validation_lock()
            _record_validation_run(
                run_path,
                run_payload,
                status="blocked",
                finishedAt=_utc_now(),
                blockReason="validation lock is held by another run",
                lock=lock,
            )
            print("Validation is already running; rerun with --queue to wait or --allow-concurrent to override.", file=sys.stderr)
            if lock:
                print(json.dumps(lock, indent=2, sort_keys=True), file=sys.stderr)
            return 3

    try:
        active_processes = _wait_for_no_active_repo_build_processes(
            getattr(args, "queue_timeout_seconds", 300) if getattr(args, "queue", False) else 0
        )
        if active_processes and not getattr(args, "allow_concurrent", False):
            _record_validation_run(
                run_path,
                run_payload,
                status="blocked",
                finishedAt=_utc_now(),
                blockReason="repo-owned build/test/compiler processes are already active",
                activeRepoBuildProcesses=active_processes,
            )
            print("Active repo-owned build/test/compiler processes detected; refusing to start shared validation.", file=sys.stderr)
            for process in active_processes[:10]:
                print(
                    "PID {pid} {name}: {command}".format(
                        pid=process.get("processId"),
                        name=process.get("name"),
                        command=str(process.get("commandLine", ""))[:240],
                    ),
                    file=sys.stderr,
                )
            return 3

        _prune_for_isolation(args, isolation_key)

        effective_args = argparse.Namespace(**vars(args))
        effective_args.isolation_key = isolation_key
        msbuild_args = _build_serialized_msbuild_args(effective_args)
        results_directory = Path(getattr(args, "results_directory", "") or (REPO_ROOT / ".ai/test-results" / run_id))
        if not results_directory.is_absolute():
            results_directory = REPO_ROOT / results_directory
        results_directory.mkdir(parents=True, exist_ok=True)

        step_results: list[dict[str, object]] = []

        def run_step(name: str, command: list[str]) -> int:
            print(f"{name}: {' '.join(command)}")
            started = time.monotonic()
            code = _run_passthrough(command)
            step_results.append(
                {
                    "name": name,
                    "command": command,
                    "exitCode": code,
                    "durationSeconds": round(time.monotonic() - started, 2),
                }
            )
            _record_validation_run(run_path, run_payload, steps=step_results)
            return code

        if getattr(args, "shutdown_build_servers", False):
            shutdown_code = run_step("shutdown-build-servers", ["dotnet", "build-server", "shutdown"])
            if shutdown_code != 0:
                _record_validation_run(run_path, run_payload, status="failed", finishedAt=_utc_now(), exitCode=shutdown_code)
                return shutdown_code

        if not getattr(args, "skip_restore", False):
            restore_code = run_step("restore", ["dotnet", "restore", project, "--verbosity", verbosity, *msbuild_args])
            if restore_code != 0:
                _record_validation_run(run_path, run_payload, status="failed", finishedAt=_utc_now(), exitCode=restore_code)
                return restore_code

        if not getattr(args, "no_build", False):
            build_code = run_step(
                "build",
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
                ],
            )
            if build_code != 0:
                _record_validation_run(run_path, run_payload, status="failed", finishedAt=_utc_now(), exitCode=build_code)
                return build_code

        test_command = [
            "dotnet",
            "test",
            project,
            "-c",
            configuration,
            "--verbosity",
            verbosity,
            "-nologo",
            "--no-restore",
            "--results-directory",
            str(results_directory),
        ]
        if not getattr(args, "no_build", False):
            test_command.append("--no-build")
        if getattr(args, "filter", ""):
            test_command.extend(["--filter", getattr(args, "filter")])
        if getattr(args, "settings", None):
            test_command.extend(["--settings", getattr(args, "settings")])
        for logger in getattr(args, "logger", None) or ["console;verbosity=normal"]:
            test_command.extend(["--logger", logger])
        for collector in getattr(args, "collect", []) or []:
            test_command.extend(["--collect", collector])
        test_command.extend(msbuild_args)

        test_code = run_step("test", test_command)
        status = "passed" if test_code == 0 else "failed"
        _record_validation_run(
            run_path,
            run_payload,
            status=status,
            finishedAt=_utc_now(),
            exitCode=test_code,
            resultsDirectory=str(results_directory),
        )
        print(f"Validation run artifact: {run_path}")
        return test_code
    finally:
        if lock_acquired:
            _release_validation_lock(REPO_ROOT, run_id)


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




def _is_sensitive_env_var(name: str) -> bool:
    lowered = name.lower()
    sensitive_markers = (
        "key",
        "secret",
        "token",
        "password",
        "connection_string",
        "connectionstring",
        "connstr",
        "sas",
        "private",
        "credential",
    )
    return any(marker in lowered for marker in sensitive_markers)


def _mask_env_value(value: str) -> str:
    if not value:
        return ""
    if len(value) <= 4:
        return "****"
    return f"{value[:2]}***{value[-1]}"


def _safe_env_snapshot() -> dict[str, str]:
    return {
        key: ("<redacted>" if _is_sensitive_env_var(key) else value)
        for key, value in os.environ.items()
    }

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
        "env": _safe_env_snapshot(),
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
            display_v1 = "<redacted>" if _is_sensitive_env_var(key) else _mask_env_value(v1)
            display_v2 = "<redacted>" if _is_sensitive_env_var(key) else _mask_env_value(v2)
            print(f"    {env1}: {display_v1}")
            print(f"    {env2}: {display_v2}")
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
    p_build.add_argument(
        "--isolation-max-root-size-mb",
        type=int,
        default=_default_isolation_max_root_size_mb(),
        help=(
            "Prune oldest isolated artifacts/bin and artifacts/obj output directories when either "
            "root exceeds this many MB; set 0 to disable size-based pruning."
        ),
    )
    p_build.add_argument("--property", action="append", default=[])

    # test
    p_test = sub.add_parser("test", help="Run contention-aware .NET tests with optional isolated outputs")
    p_test.add_argument("--lane", default="dotnet", choices=["dotnet"], help="Validation lane to run.")
    p_test.add_argument("--project", default="tests/Meridian.Tests/Meridian.Tests.csproj")
    p_test.add_argument("--configuration", default="Release")
    p_test.add_argument("--framework")
    p_test.add_argument("--runtime")
    p_test.add_argument("--filter", default="")
    p_test.add_argument("--verbosity", default=os.environ.get("BUILD_VERBOSITY", "normal"))
    p_test.add_argument("--skip-restore", action="store_true")
    p_test.add_argument("--no-build", action="store_true")
    p_test.add_argument("--no-isolation", action="store_true")
    p_test.add_argument("--full-wpf-build", action="store_true")
    p_test.add_argument("--shutdown-build-servers", action="store_true")
    p_test.add_argument("--allow-concurrent", action="store_true")
    p_test.add_argument("--queue", action="store_true", help="Wait for the validation lock and active repo build processes.")
    p_test.add_argument("--queue-timeout-seconds", type=int, default=300)
    p_test.add_argument("--run-id")
    p_test.add_argument("--isolation-key", default="auto")
    p_test.add_argument("--results-directory")
    p_test.add_argument("--settings")
    p_test.add_argument("--logger", action="append", default=None)
    p_test.add_argument("--collect", action="append", default=[])
    p_test.add_argument(
        "--isolation-retention-days",
        type=int,
        default=_default_isolation_retention_days(),
        help="Prune isolated artifacts/bin and artifacts/obj output directories older than this many days.",
    )
    p_test.add_argument(
        "--isolation-retain-latest",
        type=int,
        default=_default_isolation_retain_latest(),
        help="Retain this many newest isolated artifacts/bin and artifacts/obj output directories per root.",
    )
    p_test.add_argument(
        "--isolation-max-root-size-mb",
        type=int,
        default=_default_isolation_max_root_size_mb(),
        help="Prune oldest isolated artifacts when either root exceeds this many MB.",
    )
    p_test.add_argument("--property", action="append", default=[])

    # validation-status
    p_status = sub.add_parser("validation-status", help="Show local validation lock and active build/test processes")
    p_status.add_argument("--summary", action="store_true")
    p_status.add_argument("--json-output")
    p_status.add_argument("--fail-on-active", action="store_true")

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
    "test": cmd_test,
    "validation-status": cmd_validation_status,
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
