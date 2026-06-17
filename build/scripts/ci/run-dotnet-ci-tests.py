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
import subprocess
import sys
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Sequence

DEFAULT_TEST_PROJECTS = [
    ("core", "tests/Meridian.Tests/Meridian.Tests.csproj"),
    ("fsharp", "tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj"),
    ("ui", "tests/Meridian.Ui.Tests/Meridian.Ui.Tests.csproj"),
    ("backtesting", "tests/Meridian.Backtesting.Tests/Meridian.Backtesting.Tests.csproj"),
    ("directlending", "tests/Meridian.DirectLending.Tests/Meridian.DirectLending.Tests.csproj"),
    ("fundstructure", "tests/Meridian.FundStructure.Tests/Meridian.FundStructure.Tests.csproj"),
    ("quantscript", "tests/Meridian.QuantScript.Tests/Meridian.QuantScript.Tests.csproj"),
]


@dataclass(frozen=True)
class TestProject:
    name: str
    path: str


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
        return [TestProject(name=name, path=path) for name, path in DEFAULT_TEST_PROJECTS]

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


def build_dotnet_test_command(
    project: TestProject,
    *,
    configuration: str,
    test_filter: str,
    results_dir: Path,
) -> list[str]:
    return [
        "dotnet",
        "test",
        project.path,
        "-c",
        configuration,
        "--no-restore",
        "--filter",
        test_filter,
        "--logger",
        f"trx;LogFilePrefix={project.name}",
        "--results-directory",
        str(results_dir),
        "/p:EnableWindowsTargeting=true",
    ]


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

    results_dir = Path(args.results_dir)
    results_dir.mkdir(parents=True, exist_ok=True)

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
