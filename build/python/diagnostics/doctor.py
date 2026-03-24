import json
import os
import shutil
import socket
import urllib.request
from dataclasses import dataclass
from pathlib import Path

from core.utils import colorize


@dataclass
class DoctorResult:
    name: str
    status: str
    details: str
    expected: str
    fix: str | None = None


class Doctor:
    def __init__(self, root: Path, quick: bool) -> None:
        self.root = root
        self.quick = quick
        self.results: list[DoctorResult] = []

    def run(self) -> list[DoctorResult]:
        self._check_dotnet()
        self._check_node()
        self._check_python()
        self._check_git()
        self._check_disk()
        self._check_ports()
        self._check_config()
        self._check_docker()
        if not self.quick:
            self._check_network()
        return self.results

    def _check_dotnet(self) -> None:
        if shutil.which("dotnet") is None:
            self.results.append(
                DoctorResult(
                    name=".NET SDK",
                    status="fail",
                    details="Not installed",
                    expected="dotnet 9.0+",
                    fix="Install from https://dot.net/download",
                )
            )
            return
        version = self._command_version(["dotnet", "--version"])
        major = int(version.split(".")[0]) if version and version[0].isdigit() else 0
        status = "pass" if major >= 9 else "warn"
        self.results.append(
            DoctorResult(
                name=".NET SDK",
                status=status,
                details=f"Installed {version}",
                expected="dotnet 9.0+",
                fix="Update dotnet SDK" if status != "pass" else None,
            )
        )

    def _check_node(self) -> None:
        if shutil.which("node") is None:
            self.results.append(
                DoctorResult(
                    name="Node.js",
                    status="warn",
                    details="Not installed (required for diagram generation)",
                    expected="node 18+",
                    fix="Install from https://nodejs.org",
                )
            )
            return
        version = self._command_version(["node", "--version"])
        # version is e.g. "v20.11.0"; strip leading 'v'
        ver_str = version.lstrip("v")
        major = int(ver_str.split(".")[0]) if ver_str and ver_str[0].isdigit() else 0
        status = "pass" if major >= 18 else "warn"
        self.results.append(
            DoctorResult(
                name="Node.js",
                status=status,
                details=f"Installed {version}",
                expected="node 18+",
                fix="Update Node.js to v18 or later from https://nodejs.org" if status != "pass" else None,
            )
        )

    def _check_python(self) -> None:
        python_bin = shutil.which("python3") or shutil.which("python")
        if python_bin is None:
            self.results.append(
                DoctorResult(
                    name="Python",
                    status="warn",
                    details="Not installed (required for build tooling)",
                    expected="python 3.10+",
                    fix="Install from https://www.python.org/downloads",
                )
            )
            return
        cmd = [python_bin, "--version"]
        version = self._command_version(cmd)
        # version is e.g. "Python 3.11.4"
        ver_part = version.split()[-1] if version else ""
        parts = ver_part.split(".")
        try:
            major, minor = int(parts[0]), int(parts[1]) if len(parts) > 1 else 0
        except (ValueError, IndexError):
            major, minor = 0, 0
        status = "pass" if (major > 3 or (major == 3 and minor >= 10)) else "warn"
        self.results.append(
            DoctorResult(
                name="Python",
                status=status,
                details=f"Installed {version}",
                expected="python 3.10+",
                fix="Update Python to 3.10 or later from https://www.python.org/downloads" if status != "pass" else None,
            )
        )

    def _check_git(self) -> None:
        if shutil.which("git") is None:
            self.results.append(
                DoctorResult(
                    name="Git",
                    status="fail",
                    details="Not installed",
                    expected="git 2.x",
                    fix="Install git from https://git-scm.com",
                )
            )
            return
        version = self._command_version(["git", "--version"])
        self.results.append(
            DoctorResult(
                name="Git",
                status="pass",
                details=f"Installed {version}",
                expected="git 2.x",
            )
        )

    def _check_disk(self) -> None:
        usage = shutil.disk_usage(self.root)
        free_gb = usage.free / (1024**3)
        status = "pass" if free_gb >= 5 else "warn"
        self.results.append(
            DoctorResult(
                name="Disk space",
                status=status,
                details=f"{free_gb:.1f} GB free",
                expected=">= 5 GB free",
                fix="Free disk space" if status != "pass" else None,
            )
        )

    def _check_ports(self) -> None:
        port = int(os.getenv("HTTP_PORT", "8080"))
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
            sock.settimeout(0.5)
            result = sock.connect_ex(("127.0.0.1", port))
        status = "pass" if result != 0 else "warn"
        detail = "Available" if result != 0 else "In use"
        self.results.append(
            DoctorResult(
                name=f"Port {port}",
                status=status,
                details=detail,
                expected="Available",
                fix="Stop service on the port" if status != "pass" else None,
            )
        )

    def _check_config(self) -> None:
        config_path = self.root / "config" / "appsettings.json"
        if config_path.exists():
            self.results.append(
                DoctorResult(
                    name="Configuration",
                    status="pass",
                    details="config/appsettings.json present",
                    expected="Configuration file present",
                )
            )
        else:
            self.results.append(
                DoctorResult(
                    name="Configuration",
                    status="warn",
                    details="config/appsettings.json missing",
                    expected="Configuration file present",
                    fix="Run: make setup-config",
                )
            )

    def _check_docker(self) -> None:
        if shutil.which("docker") is None:
            self.results.append(
                DoctorResult(
                    name="Docker",
                    status="warn",
                    details="Not installed (optional)",
                    expected="Docker installed for container builds",
                    fix="Install Docker Desktop or Engine",
                )
            )
            return
        version = self._command_version(["docker", "--version"])
        self.results.append(
            DoctorResult(
                name="Docker",
                status="pass",
                details=f"Installed {version}",
                expected="Docker available",
            )
        )

    def _check_network(self) -> None:
        try:
            with urllib.request.urlopen("https://api.nuget.org/v3/index.json", timeout=2) as response:
                status = response.status
        except Exception:
            status = 0
        if status == 200:
            self.results.append(
                DoctorResult(
                    name="NuGet registry",
                    status="pass",
                    details="Reachable",
                    expected="Network access",
                )
            )
        else:
            self.results.append(
                DoctorResult(
                    name="NuGet registry",
                    status="warn",
                    details="Not reachable",
                    expected="Network access",
                    fix="Check proxy or network access",
                )
            )

    def _command_version(self, command: list[str]) -> str:
        result = shutil.which(command[0])
        if result is None:
            return "not-installed"
        output = os.popen(" ".join(command)).read().strip()
        return output or "unknown"


def format_results(results: list[DoctorResult]) -> str:
    lines = []
    for result in results:
        status_symbol = {
            "pass": colorize("✓", "0;32"),
            "warn": colorize("⚠", "1;33"),
            "fail": colorize("✗", "0;31"),
        }[result.status]
        lines.append(f"[{result.name}]")
        lines.append(f"  Status: {status_symbol} {result.status.capitalize()}")
        lines.append(f"  Details: {result.details}")
        lines.append(f"  Expected: {result.expected}")
        if result.fix:
            lines.append(f"  Fix: {result.fix}")
        lines.append("")
    return "\n".join(lines)


def exit_code(results: list[DoctorResult], fail_on_warn: bool = True) -> int:
    if any(result.status == "fail" for result in results):
        return 2
    if fail_on_warn and any(result.status == "warn" for result in results):
        return 1
    return 0


def run_doctor(root: Path, quick: bool, json_output: bool, fail_on_warn: bool = True) -> int:
    doctor = Doctor(root, quick)
    results = doctor.run()
    if json_output:
        payload = [result.__dict__ for result in results]
        print(json.dumps(payload, indent=2))
    else:
        header = "Build Environment Doctor"
        print(colorize(header, "0;36"))
        print(colorize("=" * len(header), "0;36"))
        print(format_results(results))
    return exit_code(results, fail_on_warn)
