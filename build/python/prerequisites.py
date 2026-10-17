"""Read-only prerequisites shared by doctor, CI, and setup tooling.

Use the running interpreter, global.json, .nvmrc and the existing requirements
files rather than keeping independent version/package lists in each caller.
No check installs packages, restores projects, or needs provider credentials.
"""

from __future__ import annotations

import argparse
import json
import os
import platform
import re
import shlex
import shutil
import subprocess
import sys
from dataclasses import asdict, dataclass
from importlib import metadata
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
PYTHON_MIN = (3, 11)


@dataclass
class CheckResult:
    name: str
    status: str
    details: str
    expected: str
    fix: str | None = None


@dataclass(frozen=True)
class Profile:
    tools: tuple[str, ...]
    files: tuple[str, ...] = ()
    requirements: str | None = None
    windows: bool = False
    monitoring: bool = False


_DOTNET_FILES = ("global.json", "Meridian.sln", "Directory.Build.props", "Directory.Packages.props")
_BROWSER_FILES = (".nvmrc", "src/Meridian.Ui/dashboard/package.json", "src/Meridian.Ui/dashboard/package-lock.json")
PROFILES = {
    "unit-test": Profile(("dotnet", "git"), _DOTNET_FILES),
    "browser": Profile(("node", "npm", "git"), _BROWSER_FILES),
    "desktop": Profile(("dotnet", "git", "pwsh"), _DOTNET_FILES + ("src/Meridian.Wpf/Meridian.Wpf.csproj",), windows=True),
    "full-quality-gate": Profile(
        ("dotnet", "node", "npm", "git", "pwsh", "actionlint"),
        _DOTNET_FILES + _BROWSER_FILES,
        "build/scripts/ci/requirements.txt",
        monitoring=True,
    ),
}
PROFILE_NAMES = tuple(PROFILES)
LANES = {
    "quality-gate": PROFILES["full-quality-gate"],
    "verify-fast": Profile(("dotnet", "node", "npm", "git"), _DOTNET_FILES + _BROWSER_FILES),
    "verify-dotnet": PROFILES["unit-test"],
    "verify-browser": PROFILES["browser"],
    "verify-docs": Profile(("git",), requirements="build/scripts/docs/requirements.txt", monitoring=True),
    "verify-workflows": Profile(("git", "pwsh", "actionlint"), requirements="build/scripts/ci/requirements.txt"),
}


def _command_version(command: list[str], root: Path) -> tuple[int, str]:
    try:
        # Windows resolves npm to npm.cmd via PATHEXT. Keep the resolved path
        # instead of asking CreateProcess to find a bare command name.
        command = [shutil.which(command[0]) or command[0], *command[1:]]
        result = subprocess.run(command, cwd=root, capture_output=True, text=True, timeout=15)
        return result.returncode, (result.stdout or result.stderr).strip()
    except (OSError, subprocess.TimeoutExpired) as exc:
        return 1, f"Unable to run {command[0]}: {type(exc).__name__}"


def _version(value: str) -> tuple[int, int, int] | None:
    match = re.search(r"(?<!\d)(\d+)\.(\d+)(?:\.(\d+))?", value)
    return tuple(int(part or 0) for part in match.groups()) if match else None


def check_python() -> CheckResult:
    installed = ".".join(str(part) for part in sys.version_info[:3])
    ok = sys.version_info[:2] >= PYTHON_MIN
    return CheckResult(
        "Python", "pass" if ok else "fail", f"Running Python {installed} ({sys.executable})", "Python 3.11+",
        None if ok else "Install Python 3.11 or newer from https://www.python.org/downloads/ and rerun with that interpreter.",
    )


def dotnet_channel(root: Path) -> str:
    sdk = json.loads((root / "global.json").read_text(encoding="utf-8"))["sdk"]
    version = _version(sdk["version"])
    if version is None:
        raise ValueError("global.json sdk.version must be a .NET SDK version")
    return f"{version[0]}.{version[1]}"


def _sdk_compatible(installed: tuple[int, int, int], required: tuple[int, int, int], policy: str) -> bool:
    if policy == "disable":
        return installed == required
    if installed < required:
        return False
    if policy in ("patch", "latestPatch"):
        return installed[:2] == required[:2] and installed[2] // 100 == required[2] // 100
    if policy in ("feature", "latestFeature"):
        return installed[:2] == required[:2]
    if policy in ("minor", "latestMinor"):
        return installed[0] == required[0]
    return policy in ("major", "latestMajor")


def _check_dotnet(root: Path) -> CheckResult:
    try:
        sdk = json.loads((root / "global.json").read_text(encoding="utf-8"))["sdk"]
        required = sdk["version"]
        required_version = _version(required)
        if required_version is None:
            raise ValueError("Invalid SDK version")
        policy = sdk.get("rollForward", "patch")
    except (OSError, KeyError, TypeError, ValueError) as exc:
        return CheckResult(".NET SDK", "fail", f"Cannot read SDK constraint: {type(exc).__name__}", "Valid global.json", "Restore global.json from Git and rerun doctor.")
    expected = f".NET SDK {required} (global.json rollForward={policy})"
    fix = f"Install .NET SDK {required} or a compatible {policy} SDK from https://dot.net/download and put dotnet on PATH."
    if not shutil.which("dotnet"):
        return CheckResult(".NET SDK", "fail", "dotnet not found on PATH", expected, fix)
    rc, output = _command_version(["dotnet", "--version"], root)
    installed = _version(output)
    ok = rc == 0 and installed is not None and _sdk_compatible(installed, required_version, policy)
    if "-" in output and not sdk.get("allowPrerelease", True):
        ok = False
    return CheckResult(".NET SDK", "pass" if ok else "fail", f"Selected SDK: {output or 'unavailable'}", expected, None if ok else fix)


def check_node(root: Path) -> CheckResult:
    try:
        required = int((root / ".nvmrc").read_text(encoding="utf-8").strip())
        if required <= 0:
            raise ValueError("Invalid Node major version")
    except (OSError, ValueError):
        return CheckResult("Node.js", "fail", "Missing or invalid .nvmrc", "Supported Node major version in .nvmrc", "Restore .nvmrc from Git and rerun doctor.")
    expected = f"Node.js {required}.x (supported browser version)"
    fix = f"Run nvm install {required} && nvm use {required}, or install Node.js {required} from https://nodejs.org/; rerun in a new shell."
    if not shutil.which("node"):
        return CheckResult("Node.js", "fail", "node not found on PATH", expected, fix)
    rc, output = _command_version(["node", "--version"], root)
    version = _version(output)
    ok = rc == 0 and version is not None and version[0] == required
    return CheckResult("Node.js", "pass" if ok else "fail", f"Installed: {output or 'version unavailable'}", expected, None if ok else fix)


def _check_tool(tool: str, root: Path) -> CheckResult:
    if tool == "dotnet":
        return _check_dotnet(root)
    if tool == "node":
        return check_node(root)
    name, command, expected, fix = {
        "npm": ("npm", ["npm", "--version"], "npm available", "Install the supported Node.js version from .nvmrc with npm from https://nodejs.org/ and put npm on PATH."),
        "git": ("Git", ["git", "--version"], "Git 2.x or newer", "Install Git from https://git-scm.com/downloads and put git on PATH."),
        "pwsh": ("PowerShell", ["pwsh", "-NoProfile", "-Command", "$PSVersionTable.PSVersion.ToString()"], "PowerShell 7+ (pwsh)", "Install PowerShell 7 from https://learn.microsoft.com/powershell/scripting/install/installing-powershell and put pwsh on PATH."),
        "actionlint": ("actionlint", ["actionlint", "-version"], "actionlint available", "Install actionlint from https://github.com/rhysd/actionlint/releases and put actionlint on PATH."),
    }[tool]
    if not shutil.which(tool):
        return CheckResult(name, "fail", f"{tool} not found on PATH", expected, fix)
    rc, output = _command_version(command, root)
    version = _version(output)
    ok = rc == 0 and version is not None
    if ok and tool in ("pwsh", "git"):
        ok = version[0] >= (7 if tool == "pwsh" else 2)
    return CheckResult(name, "pass" if ok else "fail", f"Installed: {output or 'version unavailable'}", expected, None if ok else fix)


def _requirements(path: Path) -> list[tuple[str, str]]:
    packages = []
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.split("#", 1)[0].strip()
        if not line:
            continue
        if line.startswith("-r "):
            packages.extend(_requirements(path.parent / line[3:].strip()))
            continue
        name, separator, version = line.partition("==")
        if not separator or not version:
            raise ValueError(f"Expected a pinned package in {path.name}: {line}")
        packages.append((name.strip(), version.strip()))
    return packages


def _check_packages(root: Path, requirements: str) -> list[CheckResult]:
    if platform.system() == "Windows":
        # PowerShell needs the call operator for a quoted executable path.
        interpreter = "& '" + sys.executable.replace("'", "''") + "'"
    else:
        interpreter = shlex.quote(sys.executable)
    fix = f"{interpreter} -m pip install --requirement {requirements}"
    try:
        packages = _requirements(root / requirements)
    except (OSError, ValueError) as exc:
        return [CheckResult("Python requirements", "fail", str(exc), requirements, "Restore the requirements files from Git.")]
    results = []
    for name, required in packages:
        try:
            installed = metadata.version(name)
        except metadata.PackageNotFoundError:
            installed = "not installed"
        ok = installed == required
        results.append(CheckResult(f"Python package {name}", "pass" if ok else "fail", f"Installed: {installed}", f"{name}=={required} ({requirements})", None if ok else fix))
    return results


def _check_monitoring(root: Path) -> list[CheckResult]:
    # Match validate-monitoring-deployment.py: no Docker daemon is required.
    strict = bool(os.environ.get("GITHUB_ACTIONS"))
    missing_status = "fail" if strict else "warn"
    promtool = os.environ.get("PROMTOOL")
    promtool = promtool if promtool and Path(promtool).is_file() else shutil.which("promtool")
    results = [CheckResult("promtool", "pass" if promtool else missing_status, "Available" if promtool else "Not installed (optional for local CI)", "Prometheus rule validation", None if promtool else "Install promtool from https://prometheus.io/download/ or set PROMTOOL to its executable path.")]
    compose = False
    if shutil.which("docker"):
        compose = _command_version(["docker", "compose", "version"], root)[0] == 0
    if not compose and shutil.which("docker-compose"):
        compose = _command_version(["docker-compose", "version"], root)[0] == 0
    results.append(CheckResult("Docker Compose", "pass" if compose else missing_status, "Available" if compose else "Not installed (optional for local CI)", "Compose CLI for configuration validation", None if compose else "Install Docker Compose from https://docs.docker.com/compose/install/ (the daemon is not required)."))
    return results


def check_prerequisites(root: Path, profile: str, *, skip_python_packages: bool = False, skip_dotnet: bool = False) -> list[CheckResult]:
    definition = PROFILES.get(profile) or LANES.get(profile)
    if definition is None:
        raise ValueError(f"Unknown prerequisite profile: {profile}")
    results = [check_python()]
    for tool in definition.tools:
        if tool != "dotnet" or not skip_dotnet:
            results.append(_check_tool(tool, root))
    for relative in definition.files:
        ok = (root / relative).is_file()
        results.append(CheckResult(relative, "pass" if ok else "fail", "Present" if ok else "Missing", "Repository prerequisite", None if ok else f"Restore {relative} from Git; run from a complete Meridian checkout."))
    if definition.requirements and not skip_python_packages:
        results.extend(_check_packages(root, definition.requirements))
    if definition.windows:
        ok = platform.system() == "Windows"
        results.append(CheckResult("Desktop platform", "pass" if ok else "fail", platform.system(), "Windows for native WPF builds and tests", None if ok else "Run the desktop profile on Windows with the .NET desktop development workload and Windows SDK installed."))
    if definition.monitoring:
        results.extend(_check_monitoring(root))
    return results


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    selection = parser.add_mutually_exclusive_group(required=True)
    selection.add_argument("--profile", choices=PROFILE_NAMES)
    selection.add_argument("--lane", choices=tuple(LANES))
    selection.add_argument("--dotnet-channel", action="store_true")
    selection.add_argument("--python-version-ok", action="store_true", help="Check an interpreter for shell bootstrap selection.")
    parser.add_argument("--skip-python-packages", action="store_true", help="Setup only: packages will be installed separately.")
    parser.add_argument("--skip-dotnet", action="store_true", help="Setup only: .NET restore is disabled.")
    parser.add_argument("--json", action="store_true", help="Emit structured diagnostic results.")
    args = parser.parse_args(argv)
    if args.python_version_ok:
        return int(check_python().status != "pass")
    if args.dotnet_channel:
        try:
            print(dotnet_channel(REPO_ROOT))
            return 0
        except (OSError, KeyError, TypeError, ValueError) as exc:
            print(f"Cannot read global.json: {exc}", file=sys.stderr)
            return 1
    results = check_prerequisites(REPO_ROOT, args.profile or args.lane, skip_python_packages=args.skip_python_packages, skip_dotnet=args.skip_dotnet)
    if args.json:
        print(json.dumps([asdict(result) for result in results], indent=2))
    else:
        print(f"Meridian prerequisites: {args.profile or args.lane}")
        for result in results:
            print(f"  {result.status.upper():4} {result.name}: {result.details}; expected {result.expected}")
            if result.fix:
                print(f"       Fix: {result.fix}")
    return int(any(result.status == "fail" for result in results))


if __name__ == "__main__":
    raise SystemExit(main())
