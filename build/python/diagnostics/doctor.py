import json
import os
import re
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
        self._check_docker_daemon()
        self._check_postgres()
        self._check_global_json()
        self._check_packages_props()
        self._check_env_vars()
        self._check_native_tools()
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

    def _check_docker_daemon(self) -> None:
        """Check Docker is installed and its daemon is reachable."""
        if shutil.which("docker") is None:
            self.results.append(
                DoctorResult(
                    name="Docker",
                    status="warn",
                    details="Not installed (optional — needed for Testcontainers and PostgreSQL)",
                    expected="Docker installed and daemon running",
                    fix="Install Docker Desktop or Engine from https://docs.docker.com/get-docker/",
                )
            )
            return
        version = self._command_version(["docker", "--version"])
        # Verify the daemon is actually running
        rc = os.system("docker info > /dev/null 2>&1")  # noqa: S605
        if rc == 0:
            self.results.append(
                DoctorResult(
                    name="Docker",
                    status="pass",
                    details=f"Installed and daemon reachable ({version})",
                    expected="Docker daemon running",
                )
            )
        else:
            self.results.append(
                DoctorResult(
                    name="Docker",
                    status="warn",
                    details=f"Installed ({version}) but daemon not running",
                    expected="Docker daemon running",
                    fix="Start Docker Desktop, or run: sudo systemctl start docker",
                )
            )

    def _check_postgres(self) -> None:
        """Try a TCP connection to PostgreSQL and report fix hints."""
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
            self.results.append(
                DoctorResult(
                    name=f"PostgreSQL {host}:{port}",
                    status="pass",
                    details="Reachable",
                    expected="TCP connection to PostgreSQL",
                )
            )
        except (socket.timeout, ConnectionRefusedError, OSError):
            fix = (
                f"Check MERIDIAN_SECURITY_MASTER_CONNECTION_STRING"
                if conn_str
                else _POSTGRES_DOCKER_FIX
            )
            self.results.append(
                DoctorResult(
                    name=f"PostgreSQL {host}:{port}",
                    status="warn",
                    details=(
                        "UNAVAILABLE — set MERIDIAN_SECURITY_MASTER_CONNECTION_STRING"
                        " or start Docker"
                    ),
                    expected="TCP connection to PostgreSQL",
                    fix=fix,
                )
            )

    def _check_global_json(self) -> None:
        """Verify the installed .NET SDK satisfies the global.json version constraint."""
        global_json = self.root / "global.json"
        if not global_json.exists():
            return

        try:
            data = json.loads(global_json.read_text(encoding="utf-8"))
            required = data.get("sdk", {}).get("version", "")
            roll_forward = data.get("sdk", {}).get("rollForward", "latestMinor")
        except (json.JSONDecodeError, OSError):
            self.results.append(
                DoctorResult(
                    name="global.json",
                    status="warn",
                    details="Could not parse global.json",
                    expected="Valid global.json SDK constraint",
                )
            )
            return

        if not required:
            return

        dotnet = shutil.which("dotnet")
        if dotnet is None:
            return  # already reported by _check_dotnet

        installed = os.popen("dotnet --version").read().strip()

        try:
            req_parts = [int(x) for x in required.split(".")]
            ins_parts = [int(x) for x in installed.split(".")]
            if roll_forward in ("latestMinor", "latestPatch", "minor", "patch", "feature"):
                ok = ins_parts[0] == req_parts[0] and ins_parts >= req_parts
            elif roll_forward == "disable":
                ok = ins_parts == req_parts
            else:
                ok = ins_parts >= req_parts
        except (ValueError, IndexError):
            ok = True  # cannot verify, assume satisfied

        if ok:
            self.results.append(
                DoctorResult(
                    name="global.json SDK constraint",
                    status="pass",
                    details=f"requires {required} (rollForward={roll_forward}), installed {installed}",
                    expected="SDK constraint satisfied",
                )
            )
        else:
            self.results.append(
                DoctorResult(
                    name="global.json SDK constraint",
                    status="warn",
                    details=f"requires {required} (rollForward={roll_forward}), installed {installed}",
                    expected="SDK constraint satisfied",
                    fix=f"Install .NET SDK {required} from https://dot.net/download",
                )
            )

    def _check_packages_props(self) -> None:
        """Verify every PackageVersion entry has a Version attribute."""
        props = self.root / "Directory.Packages.props"
        if not props.exists():
            self.results.append(
                DoctorResult(
                    name="Directory.Packages.props",
                    status="fail",
                    details="File not found",
                    expected="Central package management file present",
                )
            )
            return

        try:
            content = props.read_text(encoding="utf-8")
        except OSError:
            self.results.append(
                DoctorResult(
                    name="Directory.Packages.props",
                    status="warn",
                    details="Could not read file",
                    expected="All PackageVersion entries have Version attributes",
                )
            )
            return

        no_version = re.findall(
            r'<PackageVersion\s+Include="([^"]+)"(?![^>]*Version=)[^>]*/?>',
            content,
        )
        if no_version:
            sample = ", ".join(no_version[:3])
            self.results.append(
                DoctorResult(
                    name="Directory.Packages.props",
                    status="warn",
                    details=f"PackageVersion entries missing Version: {sample}",
                    expected="All PackageVersion entries have Version attributes",
                    fix='Add Version="x.y.z" to each flagged PackageVersion entry',
                )
            )
            return

        total = len(re.findall(r'<PackageVersion\s+Include=', content))
        self.results.append(
            DoctorResult(
                name="Directory.Packages.props",
                status="pass",
                details=f"All {total} package versions present",
                expected="All PackageVersion entries have Version attributes",
            )
        )

    def _check_env_vars(self) -> None:
        """Check provider credential environment variables."""
        for var, provider, purpose in _PROVIDER_ENV_VARS:
            value = os.getenv(var, "")
            if value:
                if len(value) > 4:
                    masked = f"{value[:2]}***{value[-1]}"
                else:
                    masked = "****"
                self.results.append(
                    DoctorResult(
                        name=var,
                        status="pass",
                        details=f"Set ({masked})",
                        expected=f"{provider} credential",
                    )
                )
            else:
                self.results.append(
                    DoctorResult(
                        name=var,
                        status="warn",
                        details="Not set — related tests will skip",
                        expected=f"{provider} credential for {purpose}",
                        fix=f"export {var}=<your-key>",
                    )
                )

    def _check_native_tools(self) -> None:
        """Check CMake and C++ compiler (optional — needed for CppTrader)."""
        if shutil.which("cmake"):
            version = self._command_version(["cmake", "--version"])
            self.results.append(
                DoctorResult(
                    name="CMake",
                    status="pass",
                    details=f"Installed {version.splitlines()[0]}",
                    expected="CMake (optional — CppTrader)",
                )
            )
        else:
            self.results.append(
                DoctorResult(
                    name="CMake",
                    status="warn",
                    details="Not installed (optional — needed for CppTrader native engine)",
                    expected="CMake (optional — CppTrader)",
                    fix="Install CMake from https://cmake.org/download/",
                )
            )

        cpp = shutil.which("g++") or shutil.which("clang++") or shutil.which("cl")
        if cpp:
            compiler_name = Path(cpp).name
            self.results.append(
                DoctorResult(
                    name="C++ compiler",
                    status="pass",
                    details=f"{compiler_name} found",
                    expected="C++ compiler (optional — CppTrader)",
                )
            )
        else:
            self.results.append(
                DoctorResult(
                    name="C++ compiler",
                    status="warn",
                    details="Not found (optional — needed for CppTrader native engine)",
                    expected="C++ compiler (optional — CppTrader)",
                    fix="Linux: apt install build-essential  |  macOS: xcode-select --install",
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
            lines.append(f"  Fix: → Run: {result.fix}")
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
