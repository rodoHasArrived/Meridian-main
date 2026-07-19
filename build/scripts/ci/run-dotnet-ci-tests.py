#!/usr/bin/env python3
"""Run Meridian's CI .NET test projects and report all failures.

The GitHub Actions CI lane should not stop at the first failing test project: a
single run is more useful when it identifies every broken test slice and uploads
a compact summary alongside TRX artifacts. This runner executes each configured
project, records every exit code, writes JSON/Markdown summaries, and exits
non-zero only after all projects have been attempted.
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Sequence

CORE_TEST_PROJECT_PATH = "tests/Meridian.Tests/Meridian.Tests.csproj"

# Test projects that cannot execute on the ubuntu PR lane and are exercised by the
# windows-desktop workflows instead: Meridian.Wpf.Tests compiles an empty stub off-Windows
# (EnableDefaultCompileItems=false) and Meridian.LifecycleSupervisor.Tests targets
# net10.0-windows. verify_test_project_coverage() accepts these as wired.
WINDOWS_ONLY_TEST_PROJECTS = [
    "tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj",
    "tests/Meridian.LifecycleSupervisor.Tests/Meridian.LifecycleSupervisor.Tests.csproj",
]

# Projects that are shared support libraries, not runnable test projects.
SUPPORT_TEST_PROJECTS = {
    "tests/Meridian.TestSupport/Meridian.TestSupport.csproj",
}

DEFAULT_TEST_PROJECTS = [
    ("core-application", "tests/Meridian.Tests/Meridian.Tests.csproj", "FullyQualifiedName~Meridian.Tests.Application"),
    (
        "core-ui-workstation-endpoints",
        "tests/Meridian.Tests/Meridian.Tests.csproj",
        "FullyQualifiedName~Meridian.Tests.Ui.WorkstationEndpointsTests",
    ),
    (
        "core-ui-other",
        "tests/Meridian.Tests/Meridian.Tests.csproj",
        "FullyQualifiedName~Meridian.Tests.Ui&FullyQualifiedName!~Meridian.Tests.Ui.WorkstationEndpointsTests",
    ),
    ("core-infrastructure", "tests/Meridian.Tests/Meridian.Tests.csproj", "FullyQualifiedName~Meridian.Tests.Infrastructure"),
    ("core-storage", "tests/Meridian.Tests/Meridian.Tests.csproj", "FullyQualifiedName~Meridian.Tests.Storage"),
    ("core-data", "tests/Meridian.Tests/Meridian.Tests.csproj", "FullyQualifiedName~Meridian.Tests.DataIntegration"),
    (
        "core-execution-strategy",
        "tests/Meridian.Tests/Meridian.Tests.csproj",
        (
            "FullyQualifiedName~Meridian.Tests.Execution|FullyQualifiedName~Meridian.Tests.Strategies|"
            "FullyQualifiedName~Meridian.Tests.Backfill|FullyQualifiedName~Meridian.Tests.SecurityMaster"
        ),
    ),
    (
        "core-market-instruments",
        "tests/Meridian.Tests/Meridian.Tests.csproj",
        (
            "FullyQualifiedName~Meridian.Tests.Integration|FullyQualifiedName~Meridian.Tests.PortfolioRecords|"
            "FullyQualifiedName~Meridian.Tests.Credentials|FullyQualifiedName~Meridian.Tests.Commodities|"
            "FullyQualifiedName~Meridian.Tests.CertificatesOfDeposit|FullyQualifiedName~Meridian.Tests.CryptoCurrency|"
            "FullyQualifiedName~Meridian.Tests.Deposits|FullyQualifiedName~Meridian.Tests.MoneyMarketFunds|"
            "FullyQualifiedName~Meridian.Tests.Entities|FullyQualifiedName~Meridian.Tests.Futures|"
            "FullyQualifiedName~Meridian.Tests.Options|FullyQualifiedName~Meridian.Tests.Equity|"
            "FullyQualifiedName~Meridian.Tests.FixedIncome|FullyQualifiedName~Meridian.Tests.FxSpot"
        ),
    ),
    (
        "core-platform-domain-root",
        "tests/Meridian.Tests/Meridian.Tests.csproj",
        (
            "FullyQualifiedName~Meridian.Tests.Pipeline|FullyQualifiedName~Meridian.Tests.Platform|"
            "FullyQualifiedName~Meridian.Tests.ProviderSdk|FullyQualifiedName~Meridian.Tests.Providers|"
            "FullyQualifiedName~Meridian.Tests.Monitoring|FullyQualifiedName~Meridian.Tests.FinancialOperations|"
            "FullyQualifiedName~Meridian.Tests.Ledger|FullyQualifiedName~Meridian.Tests.Core|"
            "FullyQualifiedName~Meridian.Tests.Domain|FullyQualifiedName~Meridian.Tests.Models|"
            "FullyQualifiedName~Meridian.Tests.Reconciliation|FullyQualifiedName~Meridian.Tests.Treasury|"
            "FullyQualifiedName~Meridian.Tests.Instruments|FullyQualifiedName~Meridian.Tests.Contracts|"
            "FullyQualifiedName~Meridian.Tests.Risk|FullyQualifiedName~Meridian.Tests.Config|"
            "FullyQualifiedName~Meridian.Tests.Architecture|FullyQualifiedName~Meridian.Tests.Workflow|"
            "FullyQualifiedName~Meridian.Tests.Services|FullyQualifiedName~Meridian.Tests.Derivatives|"
            "FullyQualifiedName~Meridian.Tests.Indicators|FullyQualifiedName~Meridian.Tests.AssetOperations|"
            "FullyQualifiedName~Meridian.Tests.ReferenceData|FullyQualifiedName~Meridian.Tests.Identity|"
            "FullyQualifiedName~Meridian.Tests.Wpf|FullyQualifiedName~Meridian.Tests.Compliance|"
            "FullyQualifiedName~Meridian.Tests.Serialization|FullyQualifiedName~Meridian.Tests.TradingCalendarTests|"
            "FullyQualifiedName~Meridian.Tests.CronExpressionParserTests|FullyQualifiedName~Meridian.Tests.SymbolSearch|"
            "FullyQualifiedName~Meridian.Tests.OrderEventPayloadTests|FullyQualifiedName~Meridian.Tests.MarketDepthCollectorTests|"
            "FullyQualifiedName~Meridian.Tests.CliModeResolverTests|FullyQualifiedName~Meridian.Tests.OptionContractSpecTests|"
            "FullyQualifiedName~Meridian.Tests.L3OrderBookCollectorTests|FullyQualifiedName~Meridian.Tests.OptionQuoteTests|"
            "FullyQualifiedName~Meridian.Tests.GreeksSnapshotTests|FullyQualifiedName~Meridian.Tests.TradeDataCollectorTests|"
            "FullyQualifiedName~Meridian.Tests.OptionTradeTests|FullyQualifiedName~Meridian.Tests.OptionChainSnapshotTests|"
            "FullyQualifiedName~Meridian.Tests.GracefulShutdownTests|FullyQualifiedName~Meridian.Tests.OpenInterestUpdateTests|"
            "FullyQualifiedName~Meridian.Tests.LiveDataAccessTests|FullyQualifiedName~Meridian.Tests.FilePermissionsServiceTests|"
            "FullyQualifiedName~Meridian.Tests.TradeModelTests|FullyQualifiedName~Meridian.Tests.BboQuotePayloadTests|"
            "FullyQualifiedName~Meridian.Tests.OrderBookLevelTests|FullyQualifiedName~Meridian.Tests.AlpacaQuoteRoutingTests|"
            "FullyQualifiedName~Meridian.Tests.PrometheusMetricsTests|FullyQualifiedName~Meridian.Tests.SessionStatsCollectorTests|"
            "FullyQualifiedName~Meridian.Tests.QuoteCollectorTests|FullyQualifiedName~Meridian.Tests.StatementReconciliationServiceTests|"
            "FullyQualifiedName~Meridian.Tests.WebSocketResiliencePolicyTests|FullyQualifiedName~Meridian.Tests.CompositePublisherTests|"
            "FullyQualifiedName~Meridian.Tests.ConnectionRetryIntegrationTests|FullyQualifiedName~Meridian.Tests.FilePermissionsDiagnosticTests|"
            "FullyQualifiedName~Meridian.Tests.WebSocketHeartbeatTests|FullyQualifiedName~Meridian.Tests.PrometheusMetricsUpdaterTests|"
            "FullyQualifiedName~Meridian.Tests.ExponentialBackoffTests|FullyQualifiedName~Meridian.Tests.CircuitBreakerTests"
        ),
    ),
    ("core-reporting", "tests/Meridian.Tests/Meridian.Tests.csproj", "FullyQualifiedName~Meridian.Tests.Reporting"),
    ("fsharp", "tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj", None),
    ("ui", "tests/Meridian.Ui.Tests/Meridian.Ui.Tests.csproj", None),
    ("backtesting", "tests/Meridian.Backtesting.Tests/Meridian.Backtesting.Tests.csproj", None),
    ("directlending", "tests/Meridian.DirectLending.Tests/Meridian.DirectLending.Tests.csproj", None),
    ("fundstructure", "tests/Meridian.FundStructure.Tests/Meridian.FundStructure.Tests.csproj", None),
    ("quantscript", "tests/Meridian.QuantScript.Tests/Meridian.QuantScript.Tests.csproj", None),
    ("designmodules", "tests/Meridian.DesignModules.Tests/Meridian.DesignModules.Tests.csproj", None),
    ("lifecycle", "tests/Meridian.Lifecycle.Tests/Meridian.Lifecycle.Tests.csproj", None),
]

_POSITIVE_FILTER_PREFIX = re.compile(r"(?<!!)FullyQualifiedName~([A-Za-z0-9_.]+)")


def build_core_remainder_filter(projects: Sequence[tuple[str, str, str | None]]) -> str:
    """Build a catch-all filter for Meridian.Tests namespaces no explicit core shard matches.

    The shard roster is a hand-maintained whitelist; before this remainder existed, a test
    namespace that matched no shard fragment (Meridian.Tests.Reporting was one) silently
    never ran on the PR lane. The remainder shard executes everything in Meridian.Tests
    minus the prefixes already claimed by the explicit core shards, so a newly created
    namespace runs automatically instead of being skipped.
    """
    excluded: list[str] = []
    seen: set[str] = set()
    for _, path, filter_expression in projects:
        if path != CORE_TEST_PROJECT_PATH or not filter_expression:
            continue
        for prefix in _POSITIVE_FILTER_PREFIX.findall(filter_expression):
            if prefix not in seen:
                seen.add(prefix)
                excluded.append(prefix)

    terms = ["FullyQualifiedName~Meridian.Tests"]
    terms.extend(f"FullyQualifiedName!~{prefix}" for prefix in sorted(excluded))
    return "&".join(terms)


DEFAULT_TEST_PROJECTS.append(
    ("core-remainder", CORE_TEST_PROJECT_PATH, build_core_remainder_filter(DEFAULT_TEST_PROJECTS))
)


def discover_test_project_paths(repo_root: Path) -> list[str]:
    tests_dir = repo_root / "tests"
    paths: list[str] = []
    for pattern in ("*/*.csproj", "*/*.fsproj"):
        for project_file in sorted(tests_dir.glob(pattern)):
            paths.append(project_file.relative_to(repo_root).as_posix())
    return paths


def verify_test_project_coverage(repo_root: Path, projects: Sequence["TestProject"]) -> list[str]:
    """Return the tests/* projects that no CI lane runs.

    Every runnable project under tests/ must be either in the shard roster (this lane) or in
    WINDOWS_ONLY_TEST_PROJECTS (the windows-desktop lane). Anything else is a silent coverage
    gap — exactly how four whole test projects previously never ran on pull requests.
    """
    wired = {project.path for project in projects}
    wired.update(WINDOWS_ONLY_TEST_PROJECTS)
    return [
        path
        for path in discover_test_project_paths(repo_root)
        if path not in wired and path not in SUPPORT_TEST_PROJECTS
    ]


@dataclass(frozen=True)
class TestProject:
    name: str
    path: str
    filter_expression: str | None = None


@dataclass(frozen=True)
class TestResult:
    name: str
    path: str
    exit_code: int
    command: list[str]

    @property
    def status(self) -> str:
        return "passed" if self.exit_code == 0 else "failed"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Run Meridian .NET CI test projects with aggregate reporting.")
    parser.add_argument("--configuration", default="Release", help="dotnet test configuration.")
    parser.add_argument(
        "--filter",
        default="Category!=Integration&Category!=Performance",
        help="dotnet test filter expression.",
    )
    parser.add_argument(
        "--results-dir",
        default="artifacts/test-results/dotnet",
        help="Directory where TRX files and summaries should be written.",
    )
    parser.add_argument(
        "--summary-output",
        default="artifacts/test-results/dotnet/ci-dotnet-test-summary.md",
        help="Markdown summary output path.",
    )
    parser.add_argument(
        "--json-output",
        default="artifacts/test-results/dotnet/ci-dotnet-test-summary.json",
        help="JSON summary output path.",
    )
    parser.add_argument(
        "--project",
        action="append",
        default=[],
        metavar="NAME=PATH",
        help="Override/default-add a test project entry. Repeatable; when present, replaces defaults.",
    )
    parser.add_argument("--dry-run", action="store_true", help="Print commands and write summaries without running tests.")
    return parser.parse_args()


def parse_project_entries(entries: Sequence[str]) -> list[TestProject]:
    if not entries:
        return [TestProject(name=name, path=path, filter_expression=filter_expression) for name, path, filter_expression in DEFAULT_TEST_PROJECTS]

    projects: list[TestProject] = []
    for entry in entries:
        if "=" not in entry:
            raise ValueError(f"Project entry '{entry}' must use NAME=PATH format.")
        name, path = entry.split("=", 1)
        name = name.strip()
        path = path.strip()
        if not name or not path:
            raise ValueError(f"Project entry '{entry}' must include non-empty NAME and PATH values.")
        projects.append(TestProject(name=name, path=path))
    return projects


def combine_filters(base_filter: str, project_filter: str | None) -> str:
    if not project_filter:
        return base_filter
    if not base_filter:
        return project_filter
    return f"({base_filter})&({project_filter})"


def build_dotnet_test_command(
    project: TestProject,
    *,
    configuration: str,
    test_filter: str,
    results_dir: Path,
) -> list[str]:
    combined_filter = combine_filters(test_filter, project.filter_expression)
    return [
        "dotnet",
        "test",
        project.path,
        "-c",
        configuration,
        "--no-restore",
        "--no-build",
        "--filter",
        combined_filter,
        # Abort and identify a hung test rather than letting the whole CI job wall-clock out.
        # xunit.runner.json's longRunningTestSeconds only warns; blame-hang actively terminates
        # the test host after the timeout and emits a sequence file naming the offending test.
        # Mirrors the full-coverage lane in .github/workflows/ci.yml.
        "--blame-hang",
        "--blame-hang-timeout",
        "10m",
        "--logger",
        f"trx;LogFilePrefix={project.name}",
        "--results-directory",
        str(results_dir),
        "/p:EnableWindowsTargeting=true",
    ]


def build_dotnet_build_command(
    project: TestProject,
    *,
    configuration: str,
) -> list[str]:
    return [
        "dotnet",
        "build",
        project.path,
        "-c",
        configuration,
        "--no-restore",
        "/p:EnableWindowsTargeting=true",
    ]


def get_unique_build_projects(projects: Sequence[TestProject]) -> list[TestProject]:
    seen_paths: set[str] = set()
    unique_projects: list[TestProject] = []
    for project in projects:
        if project.path in seen_paths:
            continue
        seen_paths.add(project.path)
        unique_projects.append(project)
    return unique_projects


def run_builds(
    projects: Sequence[TestProject],
    *,
    configuration: str,
    dry_run: bool,
) -> list[TestResult]:
    results: list[TestResult] = []
    for project in get_unique_build_projects(projects):
        command = build_dotnet_build_command(project, configuration=configuration)
        print(f"::group::dotnet build {project.path}", flush=True)
        print(" ".join(command), flush=True)
        if dry_run:
            exit_code = 0
        else:
            completed = subprocess.run(command, check=False)
            exit_code = completed.returncode
        print(f"::endgroup::", flush=True)
        results.append(TestResult(f"build:{project.name}", project.path, exit_code, command))
    return results


def run_tests(
    projects: Sequence[TestProject],
    *,
    configuration: str,
    test_filter: str,
    results_dir: Path,
    dry_run: bool,
) -> list[TestResult]:
    results: list[TestResult] = []
    for project in projects:
        command = build_dotnet_test_command(
            project,
            configuration=configuration,
            test_filter=test_filter,
            results_dir=results_dir,
        )
        print(f"::group::dotnet test {project.name}", flush=True)
        print(" ".join(command), flush=True)
        if dry_run:
            exit_code = 0
        else:
            completed = subprocess.run(command, check=False)
            exit_code = completed.returncode
        print(f"::endgroup::", flush=True)
        results.append(TestResult(project.name, project.path, exit_code, command))
    return results


def write_summaries(results: Sequence[TestResult], *, summary_output: Path, json_output: Path) -> None:
    summary_output.parent.mkdir(parents=True, exist_ok=True)
    json_output.parent.mkdir(parents=True, exist_ok=True)

    failed = [result for result in results if result.exit_code != 0]
    payload = {
        "total": len(results),
        "passed": len(results) - len(failed),
        "failed": len(failed),
        "results": [asdict(result) | {"status": result.status} for result in results],
    }
    json_output.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")

    lines = [
        "### .NET CI test project summary",
        "",
        f"- Total projects: {payload['total']}",
        f"- Passed: {payload['passed']}",
        f"- Failed: {payload['failed']}",
        "",
        "| Project | Status | Exit code |",
        "| --- | --- | ---: |",
    ]
    for result in results:
        icon = "✅" if result.exit_code == 0 else "❌"
        lines.append(f"| `{result.path}` | {icon} {result.status} | {result.exit_code} |")
    summary_output.write_text("\n".join(lines) + "\n", encoding="utf-8")


def main() -> int:
    args = parse_args()
    try:
        projects = parse_project_entries(args.project)
    except ValueError as exc:
        print(str(exc), file=sys.stderr)
        return 2

    # Only enforce wiring completeness for the default roster: --project overrides are
    # deliberate narrow runs (e.g. the targeted-test workflow).
    if not args.project:
        repo_root = Path(__file__).resolve().parents[3]
        unwired = verify_test_project_coverage(repo_root, projects)
        if unwired:
            print("Test projects not wired to any CI lane:", file=sys.stderr)
            for path in unwired:
                print(f"- {path}", file=sys.stderr)
            print(
                "Add each project to DEFAULT_TEST_PROJECTS (ubuntu lane) or "
                "WINDOWS_ONLY_TEST_PROJECTS (windows-desktop lane) in "
                "build/scripts/ci/run-dotnet-ci-tests.py.",
                file=sys.stderr,
            )
            return 2

    results_dir = Path(args.results_dir)
    results_dir.mkdir(parents=True, exist_ok=True)

    build_results = run_builds(
        projects,
        configuration=args.configuration,
        dry_run=args.dry_run,
    )
    build_failures = [result for result in build_results if result.exit_code != 0]
    if build_failures:
        write_summaries(build_results, summary_output=Path(args.summary_output), json_output=Path(args.json_output))
        print("Failing .NET test project builds:", file=sys.stderr)
        for result in build_failures:
            print(f"- {result.name}: {result.path} exited {result.exit_code}", file=sys.stderr)
        return 1

    results = run_tests(
        projects,
        configuration=args.configuration,
        test_filter=args.filter,
        results_dir=results_dir,
        dry_run=args.dry_run,
    )
    write_summaries(results, summary_output=Path(args.summary_output), json_output=Path(args.json_output))

    failed = [result for result in results if result.exit_code != 0]
    if failed:
        print("Failing .NET test projects:", file=sys.stderr)
        for result in failed:
            print(f"- {result.name}: {result.path} exited {result.exit_code}", file=sys.stderr)
        return 1

    print(f"All {len(results)} .NET test projects passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
