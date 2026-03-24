#!/usr/bin/env python3
"""
AI Architecture Guard — static compliance checker for Meridian.

Scans source files for architecture violations that AI agents commonly
introduce.  Produces a structured report listing findings by severity
(CRITICAL, WARNING, INFO) so the agent can fix them before opening a PR.

Exit codes:
    0  No CRITICAL findings
    1  One or more CRITICAL findings found
    2  Script error (bad arguments, path not found, etc.)

Commands:
    check        Run all compliance checks (default)
    check-cpm    Check for Central Package Management violations only
    check-deps   Check for forbidden dependency directions only
    check-adrs   Check for missing [ImplementsAdr] attributes only
    check-channels  Check for raw Channel.Create* calls only
    check-sinks  Check for direct FileStream writes in storage sinks
    check-json   Check for reflection-based JSON serialization
    summary      Print a one-line summary (exit 0 = clean, 1 = violations)

Usage:
    python3 build/scripts/ai-architecture-check.py
    python3 build/scripts/ai-architecture-check.py check --src src/
    python3 build/scripts/ai-architecture-check.py check --json
    python3 build/scripts/ai-architecture-check.py summary
    python3 build/scripts/ai-architecture-check.py check-cpm
    python3 build/scripts/ai-architecture-check.py check-adrs

Options:
    --src PATH      Root directory to scan (default: src/)
    --json          Emit results as JSON instead of human-readable text
    --fail-on LEVEL Minimum severity to trigger non-zero exit (default: CRITICAL)
                    Options: CRITICAL, WARNING, INFO
    --no-color      Disable ANSI colour output
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from dataclasses import dataclass, field, asdict
from pathlib import Path
from typing import Sequence

# ---------------------------------------------------------------------------
# Data model
# ---------------------------------------------------------------------------

CRITICAL = "CRITICAL"
WARNING = "WARNING"
INFO = "INFO"

SEVERITY_RANK = {CRITICAL: 2, WARNING: 1, INFO: 0}


@dataclass
class Finding:
    severity: str          # CRITICAL | WARNING | INFO
    check: str             # Short check ID, e.g. "CPM-001"
    file: str              # Relative file path
    line: int              # 1-based line number
    message: str           # Human-readable description
    snippet: str = ""      # Offending line content (trimmed)
    fix: str = ""          # Short suggested fix


@dataclass
class CheckResult:
    check_id: str
    description: str
    files_scanned: int = 0
    findings: list[Finding] = field(default_factory=list)

    @property
    def critical_count(self) -> int:
        return sum(1 for f in self.findings if f.severity == CRITICAL)

    @property
    def warning_count(self) -> int:
        return sum(1 for f in self.findings if f.severity == WARNING)


# ---------------------------------------------------------------------------
# ANSI colour helpers
# ---------------------------------------------------------------------------

_USE_COLOR = True


def _c(code: str, text: str) -> str:
    if not _USE_COLOR:
        return text
    return f"\033[{code}m{text}\033[0m"


def red(t: str) -> str: return _c("31;1", t)
def yellow(t: str) -> str: return _c("33;1", t)
def cyan(t: str) -> str: return _c("36", t)
def green(t: str) -> str: return _c("32;1", t)
def dim(t: str) -> str: return _c("2", t)


# ---------------------------------------------------------------------------
# File scanning helpers
# ---------------------------------------------------------------------------

def _iter_cs_files(root: Path) -> list[Path]:
    """Return all .cs source files under *root*, excluding test/benchmark dirs."""
    files = []
    for p in root.rglob("*.cs"):
        # Skip test projects and benchmarks
        parts = p.parts
        if any(part in ("Tests", "Benchmarks", "obj", "bin") for part in parts):
            continue
        files.append(p)
    return files


def _iter_csproj_files(root: Path) -> list[Path]:
    """Return all .csproj files under *root*, excluding bin/obj."""
    files = []
    for p in root.rglob("*.csproj"):
        if "obj" in p.parts or "bin" in p.parts:
            continue
        files.append(p)
    return files


def _read_lines(path: Path) -> list[str]:
    try:
        return path.read_text(encoding="utf-8", errors="replace").splitlines()
    except OSError:
        return []


# ---------------------------------------------------------------------------
# Check: Central Package Management (CPM) violations
# ---------------------------------------------------------------------------
# ADR: Directory.Packages.props manages all versions. Project files must not
# include Version= attributes on PackageReference items (causes NU1008).

_CPM_PATTERN = re.compile(
    r'<PackageReference\s[^>]*Include\s*=\s*"[^"]+"\s[^>]*Version\s*=\s*"[^"]+"\s*/?>',
    re.IGNORECASE,
)


def check_cpm(root: Path) -> CheckResult:
    result = CheckResult(
        check_id="CPM",
        description="Central Package Management — no Version= on PackageReference",
    )
    for path in _iter_csproj_files(root):
        lines = _read_lines(path)
        result.files_scanned += 1
        for i, line in enumerate(lines, start=1):
            if _CPM_PATTERN.search(line):
                result.findings.append(Finding(
                    severity=CRITICAL,
                    check="CPM-001",
                    file=str(path),
                    line=i,
                    snippet=line.strip(),
                    message=(
                        "PackageReference has Version= attribute. "
                        "Remove it — version is managed in Directory.Packages.props."
                    ),
                    fix="Remove the Version=\"...\" attribute from this PackageReference.",
                ))
    return result


# ---------------------------------------------------------------------------
# Check: Forbidden dependency directions
# ---------------------------------------------------------------------------
# The dependency graph forbids:
#   Ui.Services  → Wpf host types   (reverse dependency)
#   Ui.Services  → WPF-only APIs    (platform leak)
#   Ui.Shared    → WPF-only APIs    (platform leak)
#   Any project  → Windows.*        (UWP removed)
#   ProviderSdk  → anything except Contracts

_DEP_RULES: list[tuple[str, re.Pattern[str], str, str, str, bool]] = [
    # (trigger_project_dir, forbidden_using_pattern, severity, check_id, message, exact_project_match)
    # exact_project_match=True: trigger only when the path is *inside* a directory with that project name
    (
        "Meridian.Ui.Services",
        re.compile(r"using\s+Meridian\.Wpf", re.IGNORECASE),
        CRITICAL, "DEP-001",
        "Ui.Services references Wpf host type — reverse dependency violation.",
        True,
    ),
    (
        "Meridian.Ui.Services",
        re.compile(r"using\s+Windows\.", re.IGNORECASE),
        CRITICAL, "DEP-002",
        "Ui.Services uses Windows.* (WinRT/UWP) API — platform leak.",
        True,
    ),
    (
        "Meridian.Ui.Shared",
        re.compile(r"using\s+Windows\.", re.IGNORECASE),
        CRITICAL, "DEP-003",
        "Ui.Shared uses Windows.* (WinRT/UWP) API — platform leak.",
        True,
    ),
    (
        "Meridian.ProviderSdk",
        re.compile(
            r"using\s+Meridian\.(Application|Infrastructure|Storage|Domain|Wpf)",
            re.IGNORECASE,
        ),
        CRITICAL, "DEP-004",
        "ProviderSdk references a non-Contracts project — violates ProviderSdk → Contracts-only rule.",
        True,
    ),
    (
        "",   # applies everywhere — exact_project_match ignored
        re.compile(r"using\s+Meridian\.Uwp", re.IGNORECASE),
        CRITICAL, "DEP-005",
        "Reference to removed UWP project (Meridian.Uwp). UWP was fully removed.",
        False,
    ),
    (
        "Meridian.FSharp",
        re.compile(
            r"using\s+Meridian\.(Application|Infrastructure|Storage|Domain|Wpf|Core)",
            re.IGNORECASE,
        ),
        CRITICAL, "DEP-006",
        "FSharp project references a non-Contracts project — violates FSharp → Contracts-only rule.",
        True,
    ),
]


def check_deps(root: Path) -> CheckResult:
    result = CheckResult(
        check_id="DEP",
        description="Forbidden dependency directions",
    )
    for path in _iter_cs_files(root):
        lines = _read_lines(path)
        result.files_scanned += 1
        # Normalise to forward-slash so matching works on Windows too
        path_str = str(path).replace("\\", "/")
        for trigger, pattern, severity, check_id, message, exact_match in _DEP_RULES:
            if trigger:
                if exact_match:
                    # Only match files inside the specific named project directory
                    if f"/{trigger}/" not in path_str:
                        continue
                else:
                    if trigger not in path_str:
                        continue
            for i, line in enumerate(lines, start=1):
                if pattern.search(line):
                    result.findings.append(Finding(
                        severity=severity,
                        check=check_id,
                        file=str(path),
                        line=i,
                        snippet=line.strip(),
                        message=message,
                        fix="Remove the forbidden using directive.",
                    ))
    return result


# ---------------------------------------------------------------------------
# Check: Missing [ImplementsAdr] attributes on providers
# ---------------------------------------------------------------------------
# Every IMarketDataClient or IHistoricalDataProvider implementation must
# have [ImplementsAdr("ADR-001", ...)] and [ImplementsAdr("ADR-004", ...)].

_PROVIDER_IMPL_PATTERN = re.compile(
    r":\s*(IMarketDataClient|IHistoricalDataProvider|ISymbolSearchProvider)\b"
)
_IMPLEMENTS_ADR_PATTERN = re.compile(r'\[ImplementsAdr\(')
_DATA_SOURCE_PATTERN = re.compile(r'\[DataSource\(')


def check_adrs(root: Path) -> CheckResult:
    result = CheckResult(
        check_id="ADR",
        description="Missing [ImplementsAdr] and [DataSource] attributes on providers",
    )
    # Only scan Infrastructure and ProviderSdk adapter directories
    adapter_root = root / "Meridian.Infrastructure" / "Adapters"
    if not adapter_root.exists():
        adapter_root = root  # fallback

    for path in adapter_root.rglob("*.cs"):
        if any(p in ("Tests", "obj", "bin", "_Template") for p in path.parts):
            continue

        content = path.read_text(encoding="utf-8", errors="replace")
        if not _PROVIDER_IMPL_PATTERN.search(content):
            continue

        result.files_scanned += 1
        lines = content.splitlines()

        has_implements_adr = bool(_IMPLEMENTS_ADR_PATTERN.search(content))
        has_data_source = bool(_DATA_SOURCE_PATTERN.search(content))

        if not has_implements_adr:
            # Find the class declaration line for better reporting
            class_line = next(
                (i + 1 for i, l in enumerate(lines) if _PROVIDER_IMPL_PATTERN.search(l)),
                1,
            )
            result.findings.append(Finding(
                severity=CRITICAL,
                check="ADR-001",
                file=str(path),
                line=class_line,
                message=(
                    "Provider class implements IMarketDataClient / IHistoricalDataProvider "
                    "but is missing [ImplementsAdr] attribute(s). "
                    "Add [ImplementsAdr(\"ADR-001\", \"...\")] and [ImplementsAdr(\"ADR-004\", \"...\")]."
                ),
                fix='Add [ImplementsAdr("ADR-001", "Core provider contract")] above the class.',
            ))

        if not has_data_source:
            class_line = next(
                (i + 1 for i, l in enumerate(lines) if _PROVIDER_IMPL_PATTERN.search(l)),
                1,
            )
            result.findings.append(Finding(
                severity=WARNING,
                check="ADR-005",
                file=str(path),
                line=class_line,
                message=(
                    "Provider class is missing [DataSource(\"name\")] attribute. "
                    "Required for DataSourceRegistry discovery (ADR-005)."
                ),
                fix='Add [DataSource("provider-name")] above the class.',
            ))

    return result


# ---------------------------------------------------------------------------
# Check: Raw Channel.Create* calls (ADR-013)
# ---------------------------------------------------------------------------
# Developers must use EventPipelinePolicy.*.CreateChannel<T>() — never
# Channel.CreateBounded or Channel.CreateUnbounded directly.

_RAW_CHANNEL_PATTERN = re.compile(
    r"Channel\.(CreateBounded|CreateUnbounded)\s*[<(]"
)


def check_channels(root: Path) -> CheckResult:
    result = CheckResult(
        check_id="CHAN",
        description="ADR-013: Raw Channel.Create* bypasses EventPipelinePolicy",
    )
    for path in _iter_cs_files(root):
        lines = _read_lines(path)
        result.files_scanned += 1
        for i, line in enumerate(lines, start=1):
            if _RAW_CHANNEL_PATTERN.search(line):
                result.findings.append(Finding(
                    severity=CRITICAL,
                    check="CHAN-001",
                    file=str(path),
                    line=i,
                    snippet=line.strip(),
                    message=(
                        "Raw Channel.CreateBounded/CreateUnbounded call. "
                        "Use EventPipelinePolicy.*.CreateChannel<T>() instead (ADR-013)."
                    ),
                    fix="Replace with EventPipelinePolicy.Default.CreateChannel<T>().",
                ))
    return result


# ---------------------------------------------------------------------------
# Check: Direct FileStream writes in storage sinks (ADR-007)
# ---------------------------------------------------------------------------
# All storage writes must go through AtomicFileWriter — never raw FileStream
# or File.WriteAllText.

_RAW_WRITE_PATTERN = re.compile(
    r"(File\.(WriteAll|AppendAll|Open|Create|OpenWrite)|new\s+FileStream\s*\()"
)


def check_sinks(root: Path) -> CheckResult:
    result = CheckResult(
        check_id="SINK",
        description="ADR-007: Direct FileStream writes bypass AtomicFileWriter",
    )
    sink_paths = list((root / "Meridian.Storage").rglob("*.cs")) if \
        (root / "Meridian.Storage").exists() else list(root.rglob("*Sink*.cs"))

    for path in sink_paths:
        if any(p in ("Tests", "obj", "bin") for p in path.parts):
            continue
        lines = _read_lines(path)
        result.files_scanned += 1
        for i, line in enumerate(lines, start=1):
            if _RAW_WRITE_PATTERN.search(line):
                # AtomicFileWriter itself is allowed to use FileStream
                if "AtomicFileWriter" in str(path):
                    continue
                result.findings.append(Finding(
                    severity=CRITICAL,
                    check="SINK-001",
                    file=str(path),
                    line=i,
                    snippet=line.strip(),
                    message=(
                        "Direct FileStream/File.Write* in storage path. "
                        "All sink writes must go through AtomicFileWriter (ADR-007). "
                        "Direct writes produce partial JSONL records on crash."
                    ),
                    fix="Route the write through AtomicFileWriter.WriteAsync().",
                ))
    return result


# ---------------------------------------------------------------------------
# Check: Reflection-based JSON serialization (ADR-014)
# ---------------------------------------------------------------------------
# All JsonSerializer calls must reference a source-generated context.

_REFLECTION_JSON_PATTERN = re.compile(
    r"JsonSerializer\.(Serialize|Deserialize|SerializeAsync|DeserializeAsync)\s*[<(]"
)
_SOURCE_GEN_JSON_PATTERN = re.compile(
    r"JsonSerializer\.(Serialize|Deserialize|SerializeAsync|DeserializeAsync)\s*\("
    r"[^,)]+,\s*(MarketDataJsonContext|[A-Z]\w+JsonContext)\."
)
# Also allow overloads that pass options containing a TypeInfoResolver
_OPTIONS_JSON_PATTERN = re.compile(r"JsonSerializerOptions\s*\{")


def check_json(root: Path) -> CheckResult:
    result = CheckResult(
        check_id="JSON",
        description="ADR-014: JsonSerializer must use source-generated context",
    )
    for path in _iter_cs_files(root):
        # Skip the context definition file itself
        if "JsonContext" in path.name:
            continue
        lines = _read_lines(path)
        result.files_scanned += 1
        for i, line in enumerate(lines, start=1):
            if not _REFLECTION_JSON_PATTERN.search(line):
                continue
            # OK if the same line includes a source-gen context reference
            if _SOURCE_GEN_JSON_PATTERN.search(line):
                continue
            result.findings.append(Finding(
                severity=WARNING,
                check="JSON-001",
                file=str(path),
                line=i,
                snippet=line.strip(),
                message=(
                    "Possible reflection-based JsonSerializer call. "
                    "Ensure a source-generated context (e.g. MarketDataJsonContext.Default.*) "
                    "is passed as the second argument (ADR-014)."
                ),
                fix=(
                    "Change to: JsonSerializer.Serialize(value, "
                    "MarketDataJsonContext.Default.MyType)"
                ),
            ))
    return result


# ---------------------------------------------------------------------------
# Check: Structured logging (no string interpolation in log calls)
# ---------------------------------------------------------------------------

_LOG_INTERPOLATION_PATTERN = re.compile(
    r'_logger\.(Log(Information|Warning|Error|Debug|Critical|Trace))\s*\(\s*\$"'
)


def check_logging(root: Path) -> CheckResult:
    result = CheckResult(
        check_id="LOG",
        description="Structured logging — no string interpolation in log calls",
    )
    for path in _iter_cs_files(root):
        lines = _read_lines(path)
        result.files_scanned += 1
        for i, line in enumerate(lines, start=1):
            if _LOG_INTERPOLATION_PATTERN.search(line):
                result.findings.append(Finding(
                    severity=INFO,
                    check="LOG-001",
                    file=str(path),
                    line=i,
                    snippet=line.strip(),
                    message=(
                        "String interpolation ($\"...\") in log call. "
                        "Use structured semantic params instead: "
                        "_logger.LogInformation(\"Got {Count}\", count)"
                    ),
                    fix='Replace $"..." with a message template and positional params.',
                ))
    return result


# ---------------------------------------------------------------------------
# Orchestration
# ---------------------------------------------------------------------------

ALL_CHECKS = {
    "cpm":      check_cpm,
    "deps":     check_deps,
    "adrs":     check_adrs,
    "channels": check_channels,
    "sinks":    check_sinks,
    "json":     check_json,
    "logging":  check_logging,
}


def run_all_checks(src_root: Path) -> list[CheckResult]:
    results = []
    for name, fn in ALL_CHECKS.items():
        results.append(fn(src_root))
    return results


# ---------------------------------------------------------------------------
# Reporting
# ---------------------------------------------------------------------------

def _severity_label(s: str) -> str:
    if s == CRITICAL:
        return red(f"[{s}]")
    if s == WARNING:
        return yellow(f"[{s}]")
    return cyan(f"[{s}]")


def print_human_report(results: list[CheckResult]) -> int:
    """Print human-readable report. Returns highest severity exit code."""
    total_critical = sum(r.critical_count for r in results)
    total_warning = sum(r.warning_count for r in results)
    total_findings = sum(len(r.findings) for r in results)
    total_files = sum(r.files_scanned for r in results)

    print()
    print("=" * 70)
    print("  AI Architecture Guard — Meridian")
    print("=" * 70)
    print(f"  Files scanned : {total_files}")
    print(f"  Total findings: {total_findings}  "
          f"({red(str(total_critical) + ' CRITICAL')}  "
          f"{yellow(str(total_warning) + ' WARNING')})")
    print()

    if total_findings == 0:
        print(green("  ✓ No architecture violations found."))
        print()
        return 0

    for cr in results:
        if not cr.findings:
            continue
        print(f"  {cyan('■')} {cr.check_id}: {cr.description}")
        for f in cr.findings:
            print(f"    {_severity_label(f.severity)} {dim(f.check)}")
            print(f"      {f.file}:{f.line}")
            print(f"      {f.message}")
            if f.snippet:
                print(f"      {dim('> ' + f.snippet[:120])}")
            if f.fix:
                print(f"      {green('Fix: ' + f.fix)}")
            print()

    if total_critical > 0:
        print(red(f"  ✗ {total_critical} CRITICAL finding(s). Fix before submitting."))
    else:
        print(yellow(f"  ⚠  {total_warning} WARNING(s). Review before submitting."))
    print()

    return 1 if total_critical > 0 else 0


def print_json_report(results: list[CheckResult]) -> int:
    data = {
        "summary": {
            "critical": sum(r.critical_count for r in results),
            "warning": sum(r.warning_count for r in results),
            "total_findings": sum(len(r.findings) for r in results),
            "files_scanned": sum(r.files_scanned for r in results),
        },
        "checks": [
            {
                "id": r.check_id,
                "description": r.description,
                "files_scanned": r.files_scanned,
                "critical": r.critical_count,
                "warning": r.warning_count,
                "findings": [asdict(f) for f in r.findings],
            }
            for r in results
        ],
    }
    print(json.dumps(data, indent=2))
    return 1 if data["summary"]["critical"] > 0 else 0


def print_summary(results: list[CheckResult]) -> int:
    critical = sum(r.critical_count for r in results)
    warning = sum(r.warning_count for r in results)
    if critical == 0 and warning == 0:
        print(green("✓ Architecture guard: clean"))
        return 0
    parts = []
    if critical:
        parts.append(red(f"{critical} CRITICAL"))
    if warning:
        parts.append(yellow(f"{warning} WARNING"))
    print(f"Architecture guard: {', '.join(parts)} — run `ai-arch-check` for details")
    return 1 if critical > 0 else 0


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------

def _build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(
        prog="ai-architecture-check",
        description="AI Architecture Guard — static compliance checker for Meridian",
    )
    p.add_argument(
        "--src",
        default="src",
        metavar="PATH",
        help="Source root to scan (default: src/)",
    )
    p.add_argument(
        "--json",
        action="store_true",
        help="Emit JSON output instead of human-readable text",
    )
    p.add_argument(
        "--no-color",
        action="store_true",
        help="Disable ANSI colour output",
    )
    p.add_argument(
        "--fail-on",
        choices=["CRITICAL", "WARNING", "INFO"],
        default="CRITICAL",
        metavar="LEVEL",
        help="Exit non-zero if any finding at or above this severity (default: CRITICAL)",
    )

    sub = p.add_subparsers(dest="command")
    sub.add_parser("check",          help="Run all compliance checks (default)")
    sub.add_parser("check-cpm",      help="CPM violations only")
    sub.add_parser("check-deps",     help="Forbidden dependency directions only")
    sub.add_parser("check-adrs",     help="Missing [ImplementsAdr] attributes only")
    sub.add_parser("check-channels", help="Raw Channel.Create* calls only")
    sub.add_parser("check-sinks",    help="Direct FileStream writes in sinks only")
    sub.add_parser("check-json",     help="Reflection JSON serialization only")
    sub.add_parser("summary",        help="One-line summary (useful in CI)")

    return p


def main(argv: Sequence[str] | None = None) -> int:
    global _USE_COLOR

    parser = _build_parser()
    args = parser.parse_args(argv)

    if args.no_color:
        _USE_COLOR = False

    # Resolve source root
    src_root = Path(args.src).resolve()
    if not src_root.exists():
        print(f"ERROR: Source root does not exist: {src_root}", file=sys.stderr)
        return 2

    command = args.command or "check"

    # Run selected checks
    if command == "summary":
        results = run_all_checks(src_root)
        return print_summary(results)

    check_map = {
        "check":          run_all_checks,
        "check-cpm": lambda r: [check_cpm(r)],
        "check-deps": lambda r: [check_deps(r)],
        "check-adrs": lambda r: [check_adrs(r)],
        "check-channels": lambda r: [check_channels(r)],
        "check-sinks": lambda r: [check_sinks(r)],
        "check-json": lambda r: [check_json(r)],
    }
    run_fn = check_map.get(command, run_all_checks)
    results = run_fn(src_root)

    # Report
    if args.json:
        exit_code = print_json_report(results)
    else:
        exit_code = print_human_report(results)

    # Apply --fail-on override
    fail_rank = SEVERITY_RANK[args.fail_on]
    worst = max(
        (SEVERITY_RANK.get(f.severity, 0) for r in results for f in r.findings),
        default=-1,
    )
    if worst >= fail_rank and exit_code == 0:
        exit_code = 1

    return exit_code


if __name__ == "__main__":
    sys.exit(main())
