#!/usr/bin/env python3
from __future__ import annotations

import argparse
from dataclasses import dataclass
from pathlib import Path
import sys


@dataclass(frozen=True)
class CheckResult:
    name: str
    passed: bool
    detail: str


def is_relative_to(path: Path, other: Path) -> bool:
    try:
        path.relative_to(other)
        return True
    except ValueError:
        return False


def resolve_targets(repo_root: Path, raw_paths: list[str]) -> list[Path]:
    targets: list[Path] = []
    for raw_path in raw_paths:
        candidate = Path(raw_path)
        target = candidate.resolve() if candidate.is_absolute() else (repo_root / candidate).resolve()
        if not target.exists():
            raise FileNotFoundError(f"Target path does not exist: {raw_path}")
        targets.append(target)
    return targets


def read_required_text(repo_root: Path, target_roots: list[Path], relative_path: str) -> str:
    path = (repo_root / relative_path).resolve()
    if not path.exists():
        raise FileNotFoundError(f"Required file is missing: {relative_path}")

    if not any(is_relative_to(path, target_root) for target_root in target_roots):
        joined_targets = ", ".join(str(target_root) for target_root in target_roots)
        raise FileNotFoundError(
            f"Required file '{relative_path}' is outside the requested --paths scope: {joined_targets}"
        )

    return path.read_text(encoding="utf-8")


def require_all_tokens(text: str, tokens: list[str]) -> list[str]:
    return [token for token in tokens if token not in text]


def count_check(text: str, token: str, minimum: int) -> tuple[bool, str]:
    count = text.count(token)
    if count >= minimum:
        return True, f"Found '{token}' {count} times."
    return False, f"Expected '{token}' at least {minimum} times, found {count}."


def build_checks(repo_root: Path, target_roots: list[Path]) -> list[CheckResult]:
    results: list[CheckResult] = []

    theme_surfaces = read_required_text(repo_root, target_roots, "src/Meridian.Wpf/Styles/ThemeSurfaces.xaml")
    missing_styles = require_all_tokens(
        theme_surfaces,
        [
            'x:Key="ShellSectionLabelStyle"',
            'x:Key="WorkspaceContextStripStyle"',
            'x:Key="WorkspaceCommandBarSurfaceStyle"',
            'x:Key="WorkspaceQueueCardStyle"',
            'x:Key="WorkspaceInspectorCardStyle"',
            'x:Key="WorkspaceEmptyStateCardStyle"',
        ],
    )
    results.append(
        CheckResult(
            name="Workspace shell styles are defined",
            passed=not missing_styles,
            detail="All shared shell surface styles are present."
            if not missing_styles
            else f"Missing style keys: {', '.join(missing_styles)}",
        )
    )

    models_text = read_required_text(repo_root, target_roots, "src/Meridian.Wpf/Models/WorkspaceShellChromeModels.cs")
    missing_model_tokens = require_all_tokens(
        models_text,
        [
            "class WorkspaceShellContext",
            "class WorkspaceCommandGroup",
            "class WorkspaceCommandItem",
            "class WorkspaceQueueItem",
            "class WorkspaceRecentItem",
        ],
    )
    results.append(
        CheckResult(
            name="Shared workspace shell models exist",
            passed=not missing_model_tokens,
            detail="Shell context, command, queue, and recent-item models are present."
            if not missing_model_tokens
            else f"Missing model definitions: {', '.join(missing_model_tokens)}",
        )
    )

    shell_service_text = read_required_text(
        repo_root,
        target_roots,
        "src/Meridian.Wpf/Services/WorkspaceShellContextService.cs",
    )
    app_text = read_required_text(repo_root, target_roots, "src/Meridian.Wpf/App.xaml.cs")
    service_tokens_missing = require_all_tokens(shell_service_text, ["class WorkspaceShellContextService"])
    app_tokens_missing = require_all_tokens(app_text, ["AddSingleton<WpfServices.WorkspaceShellContextService>()"])
    results.append(
        CheckResult(
            name="Workspace shell context service is wired",
            passed=not service_tokens_missing and not app_tokens_missing,
            detail="Shell context service class exists and is registered in App.xaml.cs."
            if not service_tokens_missing and not app_tokens_missing
            else "Missing: "
            + ", ".join(service_tokens_missing + app_tokens_missing),
        )
    )

    main_page = read_required_text(repo_root, target_roots, "src/Meridian.Wpf/Views/MainPage.xaml")
    navigation_tokens = [
        '<TextBlock Text="Home"',
        '<TextBlock Text="Active Work"',
        '<TextBlock Text="Review / Alerts"',
        '<TextBlock Text="Admin / Support"',
    ]
    nav_details: list[str] = []
    nav_passed = True
    for token in navigation_tokens:
        passed, detail = count_check(main_page, token, 4)
        nav_passed &= passed
        nav_details.append(detail)
    results.append(
        CheckResult(
            name="Sidebar navigation is grouped for each workspace",
            passed=nav_passed,
            detail=" ".join(nav_details),
        )
    )

    governance_page = read_required_text(
        repo_root,
        target_roots,
        "src/Meridian.Wpf/Views/GovernanceWorkspaceShellPage.xaml",
    )
    governance_missing = require_all_tokens(
        governance_page,
        [
            "WorkspaceShellContextStripControl",
            "WorkspaceCommandBarControl",
            'Text="Needs Attention"',
            'Text="Fund Ops"',
            'Text="Reconciliation"',
            'Text="Diagnostics"',
            'Text="Alerts"',
            'Content="Switch Fund"',
        ],
    )
    results.append(
        CheckResult(
            name="Governance pilot exposes queue and empty-state UX",
            passed=not governance_missing,
            detail="Governance shell includes shared chrome, grouped queues, and the Switch Fund empty-state action."
            if not governance_missing
            else f"Missing governance UX tokens: {', '.join(governance_missing)}",
        )
    )

    workspace_pages = {
        "Research": "src/Meridian.Wpf/Views/ResearchWorkspaceShellPage.xaml",
        "Trading": "src/Meridian.Wpf/Views/TradingWorkspaceShellPage.xaml",
        "Data Operations": "src/Meridian.Wpf/Views/DataOperationsWorkspaceShellPage.xaml",
    }
    page_failures: list[str] = []
    for workspace_name, relative_path in workspace_pages.items():
        text = read_required_text(repo_root, target_roots, relative_path)
        missing_tokens = require_all_tokens(
            text,
            ["WorkspaceShellContextStripControl", "WorkspaceCommandBarControl"],
        )
        if missing_tokens:
            page_failures.append(f"{workspace_name}: missing {', '.join(missing_tokens)}")
    results.append(
        CheckResult(
            name="Research, Trading, and Data Operations adopt shared shell chrome",
            passed=not page_failures,
            detail="All non-governance workspace shells include the shared context strip and command bar."
            if not page_failures
            else "; ".join(page_failures),
        )
    )

    return results


def render_report(repo_root: Path, target_roots: list[Path], results: list[CheckResult]) -> str:
    passed = sum(1 for result in results if result.passed)
    failed = len(results) - passed

    lines = [
        "## WPF Finance UX Check Report",
        "",
        f"- Repo root: `{repo_root}`",
        f"- Checked paths: {', '.join(f'`{target}`' for target in target_roots)}",
        "",
        "| Check | Result | Details |",
        "|-------|--------|---------|",
    ]

    for result in results:
        status = "PASS" if result.passed else "FAIL"
        lines.append(f"| {result.name} | {status} | {result.detail} |")

    lines.extend(
        [
            "",
            f"Summary: {passed} passed, {failed} failed.",
        ]
    )

    return "\n".join(lines) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Validate the workstation-shell UX structure for Meridian.Wpf."
    )
    parser.add_argument("--root", default=".", help="Repository root. Defaults to the current directory.")
    parser.add_argument(
        "--paths",
        nargs="+",
        required=True,
        help="One or more source roots that should contain the WPF workstation-shell files.",
    )
    parser.add_argument("--output", help="Optional output file for the markdown report.")
    args = parser.parse_args()

    repo_root = Path(args.root).resolve()
    target_roots = resolve_targets(repo_root, args.paths)
    results = build_checks(repo_root, target_roots)
    report = render_report(repo_root, target_roots, results)

    if args.output:
        Path(args.output).write_text(report, encoding="utf-8")
    else:
        print(report, end="")

    return 0 if all(result.passed for result in results) else 1


if __name__ == "__main__":
    raise SystemExit(main())
