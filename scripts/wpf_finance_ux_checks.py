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


def is_required_path_in_scope(repo_root: Path, target_roots: list[Path], relative_path: str) -> bool:
    path = (repo_root / relative_path).resolve()
    return path.exists() and any(is_relative_to(path, target_root) for target_root in target_roots)


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
        'AutomationProperties.AutomationId="WorkspacePrimaryNavList"',
        'AutomationProperties.AutomationId="WorkspaceSecondaryNavList"',
        'AutomationProperties.AutomationId="WorkspaceOverflowNavList"',
        'AutomationProperties.AutomationId="RelatedWorkflowNavList"',
    ]
    missing_navigation_tokens = require_all_tokens(main_page, navigation_tokens)
    results.append(
        CheckResult(
            name="Sidebar navigation exposes grouped workflow lists",
            passed=not missing_navigation_tokens,
            detail="Home, active-work, review-alert, and related workflow navigation groups are present."
            if not missing_navigation_tokens
            else f"Missing navigation tokens: {', '.join(missing_navigation_tokens)}",
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
            'Content="Operations"',
            'Content="Accounting"',
            'Text="Reconciliation"',
            'Text="Reporting"',
            'Text="Audit"',
            'Content="Switch Context"',
        ],
    )
    results.append(
        CheckResult(
            name="Governance pilot exposes queue and empty-state UX",
            passed=not governance_missing,
            detail="Governance shell includes shared chrome, explicit governance lanes, grouped queues, and the Switch Context empty-state action."
            if not governance_missing
            else f"Missing governance UX tokens: {', '.join(governance_missing)}",
        )
    )

    context_shell_pages = {
        "Research": "src/Meridian.Wpf/Views/ResearchWorkspaceShellPage.xaml",
        "Trading": "src/Meridian.Wpf/Features/Trading/Shell/TradingWorkspaceShellPage.xaml",
        "Portfolio": "src/Meridian.Wpf/Features/Portfolio/Shell/PortfolioWorkspaceShellPage.xaml",
        "Accounting": "src/Meridian.Wpf/Views/GovernanceWorkspaceShellPage.xaml",
        "Reporting": "src/Meridian.Wpf/Features/Reporting/Shell/ReportingWorkspaceShellPage.xaml",
        "Data": "src/Meridian.Wpf/Features/Data/Shell/DataWorkspaceShellPage.xaml",
        "Settings": "src/Meridian.Wpf/Features/Settings/Shell/SettingsWorkspaceShellPage.xaml",
    }
    context_failures: list[str] = []
    for workspace_name, relative_path in context_shell_pages.items():
        text = read_required_text(repo_root, target_roots, relative_path)
        missing_tokens = require_all_tokens(text, ["WorkspaceShellContextStripControl"])
        if missing_tokens:
            context_failures.append(f"{workspace_name}: missing {', '.join(missing_tokens)}")
    results.append(
        CheckResult(
            name="Top-level WPF shells expose shared context strip",
            passed=not context_failures,
            detail="Trading, Portfolio, Accounting, Reporting, Strategy, Data, and Settings shells include the shared context strip."
            if not context_failures
            else "; ".join(context_failures),
        )
    )

    command_shell_pages = {
        "Research": "src/Meridian.Wpf/Views/ResearchWorkspaceShellPage.xaml",
        "Trading": "src/Meridian.Wpf/Features/Trading/Shell/TradingWorkspaceShellPage.xaml",
        "Accounting": "src/Meridian.Wpf/Views/GovernanceWorkspaceShellPage.xaml",
        "Data": "src/Meridian.Wpf/Features/Data/Shell/DataWorkspaceShellPage.xaml",
        "Settings": "src/Meridian.Wpf/Features/Settings/Shell/SettingsWorkspaceShellPage.xaml",
    }
    command_failures: list[str] = []
    for workspace_name, relative_path in command_shell_pages.items():
        text = read_required_text(repo_root, target_roots, relative_path)
        missing_tokens = require_all_tokens(text, ["WorkspaceCommandBarControl"])
        if missing_tokens:
            command_failures.append(f"{workspace_name}: missing {', '.join(missing_tokens)}")
    results.append(
        CheckResult(
            name="Action-oriented WPF shells expose shared command bar",
            passed=not command_failures,
            detail="Research, Trading, Accounting, Data, and Settings shells include the shared command bar."
            if not command_failures
            else "; ".join(command_failures),
        )
    )

    accounting_route_check = build_accounting_workflow_route_check(repo_root, target_roots)
    if accounting_route_check is not None:
        results.append(accounting_route_check)

    trading_route_check = build_trading_workflow_route_check(repo_root, target_roots)
    if trading_route_check is not None:
        results.append(trading_route_check)

    report_pack_route_check = build_report_pack_workflow_route_check(repo_root, target_roots)
    if report_pack_route_check is not None:
        results.append(report_pack_route_check)

    signifier_check = build_workstation_signifier_check(repo_root, target_roots)
    if signifier_check is not None:
        results.append(signifier_check)

    return results


def build_workstation_signifier_check(repo_root: Path, target_roots: list[Path]) -> CheckResult | None:
    required_paths = [
        "src/Meridian.Wpf/Workstation/Models/WorkstationPresentationModels.cs",
        "src/Meridian.Wpf/Workstation/Controls/WorkstationStatePanelControl.xaml",
        "src/Meridian.Wpf/Workstation/Controls/WorkstationCommandBarControl.xaml",
        "src/Meridian.Wpf/Views/FundLedgerPage.xaml",
    ]
    if not all(is_required_path_in_scope(repo_root, target_roots, path) for path in required_paths):
        return None

    models = read_required_text(repo_root, target_roots, required_paths[0])
    state_panel = read_required_text(repo_root, target_roots, required_paths[1])
    command_bar = read_required_text(repo_root, target_roots, required_paths[2])
    fund_ledger = read_required_text(repo_root, target_roots, required_paths[3])

    missing_tokens: list[str] = []
    missing_tokens.extend(
        f"models: {token}"
        for token in require_all_tokens(
            models,
            [
                "WorkstationReadinessTone",
                "WorkstationActionPostureModel",
                "WorkstationEvidenceLinkModel",
                "WorkstationRecoveryActionModel",
                "WorkstationSignoffRequirementModel",
            ],
        )
    )
    missing_tokens.extend(
        f"state panel: {token}"
        for token in require_all_tokens(
            state_panel,
            [
                "VisibleEvidenceLinks",
                "VisibleRecoveryActions",
                "SignoffRequirement",
                "ActionPosture",
            ],
        )
    )
    missing_tokens.extend(
        f"command bar: {token}"
        for token in require_all_tokens(
            command_bar,
            [
                "EvidenceSourceText",
                "TargetText",
                "DisabledReason",
            ],
        )
    )
    missing_tokens.extend(
        f"fund ledger: {token}"
        for token in require_all_tokens(
            fund_ledger,
            [
                "FundReconciliationGovernanceSignifier",
                "ReconciliationSection.GovernanceSignifierState",
                "FundReportPackReadinessSignifier",
                "ReportPackReadinessState",
            ],
        )
    )

    return CheckResult(
        name="W4 desktop signifiers use shared workstation affordance primitives",
        passed=not missing_tokens,
        detail="Shared action posture, evidence, recovery, sign-off, and W4 Fund Ledger/Report Pack signifier wiring are present."
        if not missing_tokens
        else f"Missing signifier tokens: {', '.join(missing_tokens)}",
    )


def build_accounting_workflow_route_check(repo_root: Path, target_roots: list[Path]) -> CheckResult | None:
    required_paths = [
        "src/Meridian.Ui.Shared/Workflows/BuiltInWorkflowDefinitionProvider.cs",
        "src/Meridian.Wpf/Services/NavigationService.cs",
        "src/Meridian.Ui/dashboard/src/lib/workspace.ts",
    ]
    if not all(is_required_path_in_scope(repo_root, target_roots, path) for path in required_paths):
        return None

    shared_workflows = read_required_text(repo_root, target_roots, required_paths[0])
    wpf_navigation = read_required_text(repo_root, target_roots, required_paths[1])
    browser_routes = read_required_text(repo_root, target_roots, required_paths[2])

    missing_tokens: list[str] = []
    missing_tokens.extend(
        f"shared workflow: {token}"
        for token in require_all_tokens(
            shared_workflows,
            [
                "WorkflowActionIds.AccountingReviewReconciliation",
                '"FundReconciliation"',
                "UiApiRoutes.ReconciliationBreakQueue",
                "WorkflowActionIds.AccountingReviewLedgerContinuity",
                '"FundTrialBalance"',
                "WorkflowActionIds.AccountingReviewAuditTrail",
                '"FundAuditTrail"',
            ],
        )
    )
    missing_tokens.extend(
        f"WPF navigation: {token}"
        for token in require_all_tokens(
            wpf_navigation,
            [
                '"FundReconciliation" => new FundOperationsNavigationContext(Tab: FundOperationsTab.Reconciliation)',
                '"FundTrialBalance" => new FundOperationsNavigationContext(Tab: FundOperationsTab.TrialBalance)',
                '"FundAuditTrail" => new FundOperationsNavigationContext(Tab: FundOperationsTab.AuditTrail)',
            ],
        )
    )
    missing_tokens.extend(
        f"browser route: {token}"
        for token in require_all_tokens(
            browser_routes,
            [
                "FundReconciliation: WORKSTATION_ROUTE_CATALOG.accountingReconciliation",
                "FundTrialBalance: WORKSTATION_ROUTE_CATALOG.accountingLedger",
                "FundAuditTrail: WORKSTATION_ROUTE_CATALOG.accounting",
            ],
        )
    )

    return CheckResult(
        name="Accounting workflow routes stay shared across WPF and browser",
        passed=not missing_tokens,
        detail=(
            "Shared accounting workflow actions route to WPF FundOperations tabs and browser accounting routes for reconciliation, ledger continuity, and audit trail."
            if not missing_tokens
            else "Missing route tokens: " + ", ".join(missing_tokens)
        ),
    )


def build_trading_workflow_route_check(repo_root: Path, target_roots: list[Path]) -> CheckResult | None:
    required_paths = [
        "src/Meridian.Ui.Shared/Workflows/BuiltInWorkflowDefinitionProvider.cs",
        "src/Meridian.Wpf/Services/TradingWorkspaceShellPresentationService.cs",
        "src/Meridian.Wpf/ViewModels/MainPageViewModel.cs",
        "src/Meridian.Ui/dashboard/src/lib/workspace.ts",
    ]
    if not all(is_required_path_in_scope(repo_root, target_roots, path) for path in required_paths):
        return None

    shared_workflows = read_required_text(repo_root, target_roots, required_paths[0])
    wpf_trading = read_required_text(repo_root, target_roots, required_paths[1])
    wpf_inbox = read_required_text(repo_root, target_roots, required_paths[2])
    browser_routes = read_required_text(repo_root, target_roots, required_paths[3])

    missing_tokens: list[str] = []
    missing_tokens.extend(
        f"shared workflow: {token}"
        for token in require_all_tokens(
            shared_workflows,
            [
                "WorkflowActionIds.TradingReviewPaperCandidate",
                '"TradingShell"',
                "UiApiRoutes.WorkstationTradingReadiness",
                "WorkflowActionIds.TradingOpenCockpit",
                "UiApiRoutes.ExecutionSessions",
                "WorkflowActionIds.TradingReviewExecutionControls",
                '"RunRisk"',
                "OperatorWorkItemKindDto.ExecutionControl",
                "UiApiRoutes.ExecutionControls",
            ],
        )
    )
    missing_tokens.extend(
        f"WPF trading shell: {token}"
        for token in require_all_tokens(
            wpf_trading,
            [
                '"RunRisk" => new(actionId, "RunRisk", PaneDropAction.SplitRight',
                'string.Equals(action.TargetPageTag, "TradingShell", StringComparison.OrdinalIgnoreCase)',
            ],
        )
    )
    missing_tokens.extend(
        f"WPF inbox: {token}"
        for token in require_all_tokens(
            wpf_inbox,
            [
                "RouteEqualsOrStartsWith(normalizedRoute, UiApiRoutes.WorkstationTradingReadiness)",
                "RouteEqualsOrStartsWith(normalizedRoute, UiApiRoutes.ExecutionSessions)",
                "RouteEqualsOrStartsWith(normalizedRoute, UiApiRoutes.ExecutionControls)",
                'return "TradingShell";',
                "OperatorWorkItemKindDto.ExecutionControl => \"RunRisk\"",
            ],
        )
    )
    missing_tokens.extend(
        f"browser route: {token}"
        for token in require_all_tokens(
            browser_routes,
            [
                "TradingReadiness: WORKSTATION_ROUTE_CATALOG.tradingReadiness",
                "TradingReadinessConsole: WORKSTATION_ROUTE_CATALOG.tradingReadiness",
                "RunRisk: WORKSTATION_ROUTE_CATALOG.tradingReadiness",
                "TradingShell: WORKSTATION_ROUTE_CATALOG.trading",
            ],
        )
    )

    return CheckResult(
        name="Trading workflow routes stay shared across WPF and browser",
        passed=not missing_tokens,
        detail=(
            "Shared paper-trading workflow actions and inbox API routes resolve to WPF TradingShell/RunRisk targets and browser Trading readiness routes."
            if not missing_tokens
            else "Missing route tokens: " + ", ".join(missing_tokens)
        ),
    )


def build_report_pack_workflow_route_check(repo_root: Path, target_roots: list[Path]) -> CheckResult | None:
    required_paths = [
        "src/Meridian.Ui.Shared/Workflows/BuiltInWorkflowDefinitionProvider.cs",
        "src/Meridian.Wpf/Services/NavigationService.cs",
        "src/Meridian.Wpf/ViewModels/MainPageViewModel.cs",
        "src/Meridian.Ui/dashboard/src/lib/workspace.ts",
        "src/Meridian.Ui/dashboard/src/app-shell.view-model.ts",
    ]
    if not all(is_required_path_in_scope(repo_root, target_roots, path) for path in required_paths):
        return None

    shared_workflows = read_required_text(repo_root, target_roots, required_paths[0])
    wpf_navigation = read_required_text(repo_root, target_roots, required_paths[1])
    wpf_inbox = read_required_text(repo_root, target_roots, required_paths[2])
    browser_routes = read_required_text(repo_root, target_roots, required_paths[3])
    browser_shell = read_required_text(repo_root, target_roots, required_paths[4])

    missing_tokens: list[str] = []
    missing_tokens.extend(
        f"shared workflow: {token}"
        for token in require_all_tokens(
            shared_workflows,
            [
                "WorkflowActionIds.ReportingApproveReportPack",
                '"FundReportPack"',
                "OperatorWorkItemKindDto.ReportPackApproval",
            ],
        )
    )
    missing_tokens.extend(
        f"WPF navigation: {token}"
        for token in require_all_tokens(
            wpf_navigation,
            [
                '"FundReportPack" => new FundOperationsNavigationContext(Tab: FundOperationsTab.ReportPack)',
            ],
        )
    )
    missing_tokens.extend(
        f"WPF inbox: {token}"
        for token in require_all_tokens(
            wpf_inbox,
            [
                "workItem.Kind == OperatorWorkItemKindDto.ReportPackApproval",
                'return "FundReportPack";',
            ],
        )
    )
    missing_tokens.extend(
        f"browser route: {token}"
        for token in require_all_tokens(
            browser_routes,
            [
                "FundReportPack: WORKSTATION_ROUTE_CATALOG.reportingReportPacks",
            ],
        )
    )
    missing_tokens.extend(
        f"browser shell: {token}"
        for token in require_all_tokens(
            browser_shell,
            [
                'case "ReportPackApproval":',
                "return WORKSTATION_ROUTE_CATALOG.reportingReportPacks;",
            ],
        )
    )

    return CheckResult(
        name="Report-pack workflow routes stay shared across WPF and browser",
        passed=not missing_tokens,
        detail=(
            "Shared report-pack approval actions and work items resolve to WPF FundReportPack and browser Reporting report-pack routes."
            if not missing_tokens
            else "Missing route tokens: " + ", ".join(missing_tokens)
        ),
    )


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
