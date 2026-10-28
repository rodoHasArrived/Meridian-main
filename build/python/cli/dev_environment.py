"""Profile-aware development readiness and explicit lockfile restores.

Checks never install tools, seed data, or contact a database. Command output is
deliberately excluded from reports because package managers may print feed secrets.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import platform
import re
import shutil
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path


DASHBOARD = "src/Meridian.Ui/dashboard"
PROFILES = ("fixture-only", "backend-connected")
_VERSION = re.compile(r"v?(\d+)\.(\d+)\.(\d+)(?:[-+][\w.\-]+)?")


def add_parser(subparsers: argparse._SubParsersAction) -> None:
    parser = subparsers.add_parser("environment", help="Check or restore a development profile")
    parser.add_argument("--profile", choices=PROFILES, default="fixture-only")
    parser.add_argument("--restore", action="store_true", help="Explicitly restore locked npm and backend dependencies")
    parser.add_argument("--json-output", type=Path, help="Write a credential-free environment report")
    parser.add_argument("--summary", action="store_true", help="Print compact readiness and dependency evidence")


def _run(command: list[str], root: Path, timeout: int) -> tuple[dict, str]:
    started = time.monotonic()
    try:
        completed = subprocess.run(
            command, cwd=root, capture_output=True, text=True,
            encoding="utf-8", errors="replace", check=False, timeout=timeout,
        )
        code, output, error = completed.returncode, completed.stdout.strip(), None
    except subprocess.TimeoutExpired:
        code, output, error = None, "", "timeout"
    except OSError:
        code, output, error = None, "", "could-not-start"
    return {
        "exit_code": code,
        "elapsed_ms": round((time.monotonic() - started) * 1000, 3),
        "error": error,
    }, output


def _npm_command(npm: str, node: str) -> list[str] | None:
    # npm.cmd requires shell interpretation on Windows. Invoke its JS entrypoint
    # with Node instead so paths with spaces need no shell quoting.
    if Path(npm).suffix.lower() in (".cmd", ".bat"):
        entrypoint = Path(npm).parent / "node_modules/npm/bin/npm-cli.js"
        return [node, str(entrypoint)] if entrypoint.is_file() else None
    return [npm]


def _inputs(root: Path, profile: str) -> list[str]:
    required = [
        "package.json", "package-lock.json", f"{DASHBOARD}/package.json",
        f"{DASHBOARD}/package-lock.json", "scripts/dev/web-dev.mjs",
        f"{DASHBOARD}/src/lib/dev-fixtures.ts", f"{DASHBOARD}/vite.config.ts",
    ]
    if profile == "backend-connected":
        required += ["global.json", "Directory.Packages.props", "Directory.Build.props",
                     "src/Meridian/Meridian.csproj", "config/appsettings.sample.json"]
    optional = set()
    for pattern in (
        "scripts/dev/owned-process*", f"{DASHBOARD}/src/lib/dev-fixtures*.ts",
        f"{DASHBOARD}/src/lib/*dev-fixtures*.ts", f"{DASHBOARD}/src/lib/dev-fixtures/**/*.ts",
    ):
        optional.update(path.relative_to(root).as_posix() for path in root.glob(pattern) if path.is_file())
    return sorted(set(required) | optional)


def inspect_environment(root: Path, profile: str, restore: bool = False) -> dict:
    """Return readiness evidence; dependency presence alone never proves a lock restore."""
    if profile not in PROFILES:
        raise ValueError("Unknown development profile")
    root = root.resolve()
    checks: list[dict] = []
    commands: list[dict] = []
    tools: dict[str, str | None] = {"python": platform.python_version()}

    def check(name: str, ok: bool, detail: str) -> None:
        checks.append({"name": name, "status": "pass" if ok else "fail", "detail": detail})

    check("python", sys.version_info >= (3, 11), "Python 3.11+ is required for development tooling")
    hashes = {}
    for relative in _inputs(root, profile):
        try:
            hashes[relative] = hashlib.sha256((root / relative).read_bytes()).hexdigest()
        except OSError:
            check(f"input:{relative}", False, "Required input is missing or unreadable")
    check("inputs", not any(item["name"].startswith("input:") for item in checks), "Dependency, launcher, and fixture input hashes")
    for relative in ("package.json", "package-lock.json", f"{DASHBOARD}/package.json", f"{DASHBOARD}/package-lock.json"):
        if relative not in hashes:
            continue
        try:
            document = json.loads((root / relative).read_text(encoding="utf-8"))
            if not isinstance(document, dict):
                raise ValueError
            if relative.endswith("package-lock.json") and not document.get("lockfileVersion"):
                raise ValueError
        except (ValueError, OSError):
            check(f"json:{relative}", False, "Dependency input must be a valid package/lockfile JSON object")

    node, npm = shutil.which("node"), shutil.which("npm")
    npm_command = _npm_command(npm, node) if npm and node else None
    executables = {"node": [node] if node else None, "npm": npm_command}
    if profile == "backend-connected":
        dotnet = shutil.which("dotnet")
        executables["dotnet"] = [dotnet] if dotnet else None
        try:
            sdk = json.loads((root / "global.json").read_text(encoding="utf-8"))["sdk"]["version"]
            if not isinstance(sdk, str) or not _VERSION.fullmatch(sdk):
                raise ValueError
            check("sdk-input", True, f"SDK resolution uses global.json ({sdk})")
        except (OSError, ValueError, KeyError, TypeError):
            check("sdk-input", False, "global.json must declare a valid SDK version")
    for name, executable in executables.items():
        tools[name] = None
        if not executable:
            check(name, False, f"Install {name} and make it available on PATH")
            continue
        result, output = _run([*executable, "--version"], root, 15)
        commands.append({"name": f"probe:{name}", "command": [name, "--version"], **result})
        match = _VERSION.fullmatch(output)
        if result["exit_code"] != 0 or not match:
            check(name, False, f"{name} version probe failed; verify installation and selected SDK")
            continue
        version = tuple(int(part) for part in match.groups())
        tools[name] = output
        allowed = version >= (22, 12, 0) if name == "node" else True
        check(name, allowed, "Node.js 22.12+ required" if name == "node" else "Version probe succeeded")

    fingerprint = hashlib.sha256(json.dumps(
        {"profile": profile, "inputs": hashes, "tools": tools, "os": platform.system(), "architecture": platform.machine()},
        sort_keys=True, separators=(",", ":"),
    ).encode("utf-8")).hexdigest()
    preflight_ok = all(item["status"] == "pass" for item in checks)
    restore_status = "not-requested"
    if restore:
        restore_status = "blocked"
        if preflight_ok:
            restore_status = "succeeded"
            restores = [
                ("restore:root-npm", [*npm_command, "ci", "--include=optional"], ["npm", "ci", "--include=optional"]),
                ("restore:dashboard-npm", [*npm_command, "--prefix", DASHBOARD, "ci", "--include=optional"], ["npm", "--prefix", DASHBOARD, "ci", "--include=optional"]),
            ]
            if profile == "backend-connected":
                tail = ["restore", "src/Meridian/Meridian.csproj", "/p:EnableWindowsTargeting=true"]
                restores.append(("restore:dotnet", [*executables["dotnet"], *tail], ["dotnet", *tail]))
            for label, command, display in restores:
                result, _ = _run(command, root, 600)
                commands.append({"name": label, "command": display, **result})
                ok = result["exit_code"] == 0
                check(label, ok, "Lockfile restore succeeded" if ok else "Restore failed; rerun the displayed command locally for diagnostics")
                if not ok:
                    restore_status = "failed"
                    break
    for relative in ("node_modules", f"{DASHBOARD}/node_modules"):
        check(f"dependencies:{relative}", (root / relative).is_dir(), "Dependency directory present (presence alone does not prove lockfile consistency)")
    ready = all(item["status"] == "pass" for item in checks)
    return {
        "schema_version": 1,
        "timestamp": datetime.now(timezone.utc).isoformat(),
        "profile": profile,
        "status": "ready" if ready else "blocked",
        "restore_status": restore_status,
        "dependency_evidence": "lockfile-restored" if restore_status == "succeeded" and ready else "availability-only",
        "fingerprint": fingerprint,
        "input_sha256": hashes,
        "tool_versions": tools,
        "platform": {"os": platform.system(), "architecture": platform.machine()},
        "checks": checks,
        "commands": commands,
    }


def cmd_environment(args: argparse.Namespace, repo_root: Path) -> int:
    repo_root = repo_root.resolve()
    output_path = None
    if args.json_output:
        try:
            requested = Path(args.json_output)
            output_path = (requested if requested.is_absolute() else repo_root / requested).resolve()
            # Protect inputs for both profiles, including symlink/hardlink aliases.
            # Validate before any probes or explicit restore can change dependencies.
            protected = {
                (repo_root / relative).resolve()
                for profile in PROFILES for relative in _inputs(repo_root, profile)
            }
            if output_path in protected or output_path.is_dir() or (
                output_path.exists() and any(path.exists() and output_path.samefile(path) for path in protected)
            ):
                print("Environment JSON output must be a report file, not a dependency or fixture input")
                return 1
        except (OSError, RuntimeError):
            print("Invalid environment JSON output path")
            return 1
    report = inspect_environment(repo_root, args.profile, args.restore)
    print(f"Environment {report['profile']}: {report['status']} ({report['dependency_evidence']})")
    print(f"Fingerprint: {report['fingerprint']}")
    for item in report["checks"]:
        if item["status"] != "pass" or not args.summary:
            print(f"  {item['status']}: {item['name']} — {item['detail']}")
    if output_path is not None:
        try:
            output_path.parent.mkdir(parents=True, exist_ok=True)
            output_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
        except OSError:
            print("Could not write environment JSON report")
            return 1
    return 0 if report["status"] == "ready" else 1
