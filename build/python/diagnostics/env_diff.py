import os
import platform
import subprocess
from datetime import datetime
from pathlib import Path

from core.utils import ensure_directory, write_json


def capture_environment(root: Path, name: str) -> Path:
    env = {
        "name": name,
        "timestamp": _timestamp(),
        "os": platform.platform(),
        "architecture": platform.machine(),
        "env_vars": _filtered_env(),
        "tool_versions": _tool_versions(),
        "nuget_sources": _nuget_sources(root),
    }
    env_dir = root / ".build-system" / "envs"
    ensure_directory(env_dir)
    path = env_dir / f"{name}.json"
    write_json(path, env)
    return path


def diff_envs(env1: dict, env2: dict) -> dict:
    diff = {"tool_versions": {}, "env_vars": {}, "nuget_sources": {}}
    for key in set(env1.get("tool_versions", {})) | set(env2.get("tool_versions", {})):
        if env1.get("tool_versions", {}).get(key) != env2.get("tool_versions", {}).get(key):
            diff["tool_versions"][key] = {
                "left": env1.get("tool_versions", {}).get(key),
                "right": env2.get("tool_versions", {}).get(key),
            }
    for key in set(env1.get("env_vars", {})) | set(env2.get("env_vars", {})):
        if env1.get("env_vars", {}).get(key) != env2.get("env_vars", {}).get(key):
            diff["env_vars"][key] = {
                "left": env1.get("env_vars", {}).get(key),
                "right": env2.get("env_vars", {}).get(key),
            }
    if env1.get("nuget_sources") != env2.get("nuget_sources"):
        diff["nuget_sources"] = {"left": env1.get("nuget_sources"), "right": env2.get("nuget_sources")}
    return diff


def _filtered_env() -> dict:
    keys = [
        "DOTNET_ROOT",
        "DOTNET_ENVIRONMENT",
        "NUGET_PACKAGES",
        "CI",
        "CONFIGURATION",
    ]
    return {key: os.getenv(key) for key in keys if os.getenv(key) is not None}


def _tool_versions() -> dict:
    return {
        "dotnet": _run_version(["dotnet", "--version"]),
        "git": _run_version(["git", "--version"]),
        "docker": _run_version(["docker", "--version"]),
    }


def _run_version(command: list[str]) -> str:
    try:
        result = subprocess.run(command, capture_output=True, text=True, check=False)
    except FileNotFoundError:
        return "not-installed"
    return result.stdout.strip() or result.stderr.strip() or "unknown"


def _nuget_sources(root: Path) -> list[str]:
    try:
        result = subprocess.run(
            ["dotnet", "nuget", "list", "source"],
            cwd=str(root),
            capture_output=True,
            text=True,
            check=False,
        )
    except FileNotFoundError:
        return ["dotnet-not-installed"]
    if result.returncode != 0:
        return ["unavailable"]
    return [line.strip() for line in result.stdout.splitlines() if line.strip()]


def _timestamp() -> str:
    return datetime.utcnow().isoformat(timespec="seconds") + "Z"
