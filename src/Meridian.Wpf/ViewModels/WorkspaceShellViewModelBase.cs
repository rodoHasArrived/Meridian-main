using Meridian.Contracts.Api;
using Meridian.Contracts.AssetOperations;
using System.Globalization;
using System.Windows.Input;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;
using Meridian.Wpf.Workstation.Commands;
using Meridian.Wpf.Workstation.ViewModels.Base;

namespace Meridian.Wpf.ViewModels;

public abstract class WorkspaceShellViewModelBase : WorkspaceViewModelBase
{
    private WorkspaceCommandGroup _commandGroup = new();
    private WorkspaceAttentionRibbonViewModel _attentionRibbon;

    protected WorkspaceShellViewModelBase(WorkspaceShellDefinition workspaceDefinition)
    {
        WorkspaceDefinition = workspaceDefinition ?? throw new ArgumentNullException(nameof(workspaceDefinition));
        _attentionRibbon = WorkspaceAttentionRibbonViewModel.ForWorkspace(
            workspaceDefinition.WorkspaceId,
            ExecuteAttentionPrimaryAction);
    }

    public event EventHandler<string>? AttentionPrimaryActionRequested;

    public WorkspaceShellDefinition WorkspaceDefinition { get; }

    public WorkspaceAttentionRibbonViewModel AttentionRibbon
    {
        get => _attentionRibbon;
        protected set => SetProperty(ref _attentionRibbon, value);
    }

    public WorkspaceCommandGroup CommandGroup
    {
        get => _commandGroup;
        set
        {
            if (SetProperty(ref _commandGroup, value))
            {
                CommandDescriptors = WorkspaceCommandAdapters.ToCommandDescriptors(value);
            }
        }
    }
    private void ExecuteAttentionPrimaryAction(string actionId)
    {
        if (!string.IsNullOrWhiteSpace(actionId))
        {
            AttentionPrimaryActionRequested?.Invoke(this, actionId);
        }
    }
}

public sealed class WorkspaceAttentionRibbonViewModel : BindableBase
{
    private static readonly DateTimeOffset PlaceholderTimestamp = new(2026, 06, 15, 09, 00, 00, TimeSpan.Zero);
    private readonly Action<string> _executePrimaryAction;

    private WorkspaceAttentionRibbonViewModel(
        string workspaceId,
        string severity,
        string summaryText,
        DateTimeOffset timestamp,
        string primaryActionLabel,
        string primaryActionTarget,
        Action<string> executePrimaryAction)
    {
        WorkspaceId = workspaceId;
        Severity = severity;
        SummaryText = summaryText;
        Timestamp = timestamp;
        PrimaryActionLabel = primaryActionLabel;
        PrimaryActionTarget = primaryActionTarget;
        _executePrimaryAction = executePrimaryAction;
        PrimaryActionCommand = new WorkspaceAttentionRibbonCommand(() => _executePrimaryAction(PrimaryActionTarget), () => !string.IsNullOrWhiteSpace(PrimaryActionTarget));
    }

    public string WorkspaceId { get; }

    public string Severity { get; }

    public string SeverityLabel => Severity switch
    {
        WorkspaceAttentionSeverity.Healthy => "Healthy",
        WorkspaceAttentionSeverity.Warning => "Warning",
        WorkspaceAttentionSeverity.Error => "Error",
        _ => "Info"
    };

    public string Tone => Severity switch
    {
        WorkspaceAttentionSeverity.Healthy => WorkspaceTone.Success,
        WorkspaceAttentionSeverity.Warning => WorkspaceTone.Warning,
        WorkspaceAttentionSeverity.Error => WorkspaceTone.Danger,
        _ => WorkspaceTone.Info
    };

    public string IconGlyph => Severity switch
    {
        WorkspaceAttentionSeverity.Healthy => "\uE930",
        WorkspaceAttentionSeverity.Warning => "\uE7BA",
        WorkspaceAttentionSeverity.Error => "\uEA39",
        _ => "\uE946"
    };

    public string SummaryText { get; }

    public DateTimeOffset Timestamp { get; }

    public string TimestampText => Timestamp.ToLocalTime().ToString("MMM d, HH:mm", CultureInfo.InvariantCulture);

    public string SummaryWithTimestamp => $"{SummaryText} Updated {TimestampText}.";

    public string PrimaryActionLabel { get; }

    public string PrimaryActionTarget { get; }

    public ICommand PrimaryActionCommand { get; }

    public string PanelAutomationId => $"{WorkspaceId}AttentionRibbon";

    public string IconContainerAutomationId => $"{WorkspaceId}AttentionRibbonIconContainer";

    public string IconAutomationId => $"{WorkspaceId}AttentionRibbonIcon";

    public string SeverityAutomationId => $"{WorkspaceId}AttentionRibbonSeverity";

    public string SummaryAutomationId => $"{WorkspaceId}AttentionRibbonSummary";

    public string PrimaryActionAutomationId => $"{WorkspaceId}AttentionRibbonPrimaryAction";

    public static WorkspaceAttentionRibbonViewModel ForWorkspace(string workspaceId, Action<string> executePrimaryAction)
    {
        var normalized = workspaceId.ToLowerInvariant();
        return normalized switch
        {
            "trading" => new(normalized, WorkspaceAttentionSeverity.Warning, "Paper-first execution is available; select an operating context before promoting live desk actions.", PlaceholderTimestamp, "Choose Context", "Settings", executePrimaryAction),
            "portfolio" => new(normalized, WorkspaceAttentionSeverity.Informational, "Portfolio cockpit is reading exposure, import, multi-asset, and close-readiness placeholders until shared coverage refresh completes.", PlaceholderTimestamp, "Review Imports", "PortfolioImport", executePrimaryAction),
            "accounting" => new(normalized, WorkspaceAttentionSeverity.Error, "Accounting attention is focused on unresolved reconciliation, approval, and evidence blockers before close sign-off.", PlaceholderTimestamp, "Open Reconciliation", "FundReconciliation", executePrimaryAction),
            "reporting" => new(normalized, WorkspaceAttentionSeverity.Healthy, "Reporting packs, schedules, and export presets are ready for governed review.", PlaceholderTimestamp, "Open Reports", "FundReportPack", executePrimaryAction),
            "strategy" => new(normalized, WorkspaceAttentionSeverity.Warning, "Strategy runs require promotion review before trading, portfolio, or accounting handoff.", PlaceholderTimestamp, "Review Runs", "StrategyRuns", executePrimaryAction),
            "data" => new(normalized, WorkspaceAttentionSeverity.Informational, "Data workspace is using provider and quality posture adapters while source-backed diagnostics continue to load.", PlaceholderTimestamp, "Open Quality", "DataQuality", executePrimaryAction),
            "settings" => new(normalized, WorkspaceAttentionSeverity.Healthy, "Settings and capability gates are available for operator configuration review.", PlaceholderTimestamp, "Open Settings", "Settings", executePrimaryAction),
            _ => new(normalized, WorkspaceAttentionSeverity.Informational, "Workspace attention posture is available.", PlaceholderTimestamp, "Review", workspaceId, executePrimaryAction)
        };
    }
}

public static class WorkspaceAttentionSeverity
{
    public const string Healthy = "Healthy";
    public const string Warning = "Warning";
    public const string Error = "Error";
    public const string Informational = "Informational";
}

internal sealed class WorkspaceAttentionRibbonCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool> _canExecute;

    public WorkspaceAttentionRibbonCommand(Action execute, Func<bool> canExecute)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute();

    public void Execute(object? parameter) => _execute();

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class PortfolioWorkspaceShellViewModel : WorkspaceShellViewModelBase
{
    private const string MultiAssetCoverageRoute = UiApiRoutes.WorkstationPortfolioMultiAssetCoverage;
    private const string AssetOperationsRoute = UiApiRoutes.WorkstationAssetOperations;

    public IReadOnlyList<WorkspaceQueueItem> CockpitDecisionItems { get; } =
    [
        new()
        {
            Title = "Account exposure",
            Detail = "Open account and aggregate portfolio views.",
            StatusLabel = "Compare accounts",
            CountLabel = "Review",
            Tone = WorkspaceTone.Info,
            PrimaryActionId = "AccountPortfolio",
            PrimaryActionLabel = "Open",
            AutomationName = "Portfolio account exposure decision"
        },
        new()
        {
            Title = "Fund posture",
            Detail = "Inspect fund accounts before reconciliation or reporting handoff.",
            StatusLabel = "Accounts and cash",
            CountLabel = "Ready",
            Tone = WorkspaceTone.Success,
            PrimaryActionId = "FundAccounts",
            PrimaryActionLabel = "Open",
            AutomationName = "Portfolio fund posture decision"
        },
        new()
        {
            Title = "Import exceptions",
            Detail = "Route external snapshots through import and reference-data checks.",
            StatusLabel = "Portfolio import",
            CountLabel = "Queue",
            Tone = WorkspaceTone.Warning,
            PrimaryActionId = "PortfolioImport",
            PrimaryActionLabel = "Open",
            AutomationName = "Portfolio import exceptions decision"
        },
        new()
        {
            Title = "Multi-asset readiness groups",
            Detail = $"Review {nameof(MultiAssetCoverageSummaryDto.AssetClasses)}, {nameof(MultiAssetClassCoverageDto.Status)}, {nameof(MultiAssetClassCoverageDto.Blockers)}, and {nameof(MultiAssetClassCoverageDto.DrillThroughTargets)} from {MultiAssetCoverageRoute}.",
            StatusLabel = "Asset class readiness",
            CountLabel = "Review",
            Tone = WorkspaceTone.Info,
            PrimaryActionId = "FundPortfolio",
            PrimaryActionLabel = "Open Portfolio",
            SecondaryActionId = "FundAccounts",
            SecondaryActionLabel = "Accounts",
            AutomationName = "Portfolio multi-asset readiness grouping decision"
        },
        new()
        {
            Title = "Provider evidence degradation",
            Detail = $"Inspect {nameof(MultiAssetClassCoverageDto.EvidenceRequirements)}, {nameof(MultiAssetEvidenceRequirementDto.EvidenceRoute)}, and {nameof(MultiAssetDrillThroughTargetDto.TargetType)} references from {MultiAssetCoverageRoute}.",
            StatusLabel = "Provider degraded",
            CountLabel = "Check",
            Tone = WorkspaceTone.Warning,
            PrimaryActionId = "PortfolioImport",
            PrimaryActionLabel = "Open Imports",
            SecondaryActionId = "ProviderHealth",
            SecondaryActionLabel = "Providers",
            AutomationName = "Portfolio multi-asset provider degradation decision"
        },
        new()
        {
            Title = "Ledger and reconciliation coverage",
            Detail = $"Compare {nameof(MultiAssetClassCoverageDto.LedgerClassification)}, {nameof(MultiAssetClassCoverageDto.ReconciliationSignals)}, and {nameof(MultiAssetDrillThroughTargetDto.Route)} from {MultiAssetCoverageRoute}.",
            StatusLabel = "Coverage drift",
            CountLabel = "Investigate",
            Tone = WorkspaceTone.Warning,
            PrimaryActionId = "FundReconciliation",
            PrimaryActionLabel = "Reconcile",
            SecondaryActionId = "FundAuditTrail",
            SecondaryActionLabel = "Audit",
            AutomationName = "Portfolio multi-asset reconciliation coverage decision"
        },
        new()
        {
            Title = "Asset operations detail",
            Detail = $"Drill into {nameof(AssetOperationsDetailDto.Subject)}, {nameof(AssetOperationsDetailDto.ProjectedCashFlows)}, {nameof(AssetOperationsDetailDto.ReconciliationResults)}, and {nameof(AssetOperationsDetailDto.LedgerProjections)} through {AssetOperationsRoute}.",
            StatusLabel = "Security operations",
            CountLabel = "Drill-in",
            Tone = WorkspaceTone.Info,
            PrimaryActionId = "FundPortfolio",
            PrimaryActionLabel = "Open Portfolio",
            SecondaryActionId = "FundAuditTrail",
            SecondaryActionLabel = "Evidence",
            AutomationName = "Portfolio asset operations detail decision"
        },
        new()
        {
            Title = "Close readiness blockers",
            Detail = $"Open blocker evidence carried by {nameof(MultiAssetReadinessBlockerDto.EvidenceRoute)}, {nameof(MultiAssetDrillThroughTargetDto.EvidenceLink)}, and {nameof(MultiAssetCoverageSummaryDto.DrillThroughRoutes)} from {MultiAssetCoverageRoute}.",
            StatusLabel = "Close readiness",
            CountLabel = "Blocked",
            Tone = WorkspaceTone.Danger,
            IsBlocked = true,
            PrimaryActionId = "OperationsClose",
            PrimaryActionLabel = "Close Review",
            SecondaryActionId = "FundAuditTrail",
            SecondaryActionLabel = "Evidence",
            AutomationName = "Portfolio multi-asset close blocker decision"
        },
        new()
        {
            Title = "Specialty exposure",
            Detail = "Review lending concentration before accounting close and reports.",
            StatusLabel = "Direct lending",
            CountLabel = "Review",
            Tone = WorkspaceTone.Neutral,
            PrimaryActionId = "DirectLending",
            PrimaryActionLabel = "Open",
            AutomationName = "Portfolio specialty exposure decision"
        }
    ];

    public PortfolioWorkspaceShellViewModel()
        : base(ShellNavigationCatalog.GetWorkspaceShell("portfolio")!)
    {
    }
}

public sealed class AccountingWorkspaceShellViewModel : WorkspaceShellViewModelBase
{
    public AccountingWorkspaceShellViewModel()
        : base(ShellNavigationCatalog.GetWorkspaceShell("accounting")!)
    {
    }
}

public sealed class ReportingWorkspaceShellViewModel : WorkspaceShellViewModelBase
{
    public IReadOnlyList<WorkspaceQueueItem> CockpitDecisionItems { get; } =
    [
        new()
        {
            Title = "Pack assembly",
            Detail = "Review no-code report grids, branded pack output, shadow-NAV evidence, and downstream handoff readiness.",
            StatusLabel = "Report writer",
            CountLabel = "Draft",
            Tone = WorkspaceTone.Info,
            PrimaryActionId = "FundReportPack",
            PrimaryActionLabel = "Open",
            SecondaryActionId = "ExportPresets",
            SecondaryActionLabel = "Presets",
            AutomationName = "Reporting pack assembly decision"
        },
        new()
        {
            Title = "Approval gates",
            Detail = "Review scheduled runs, approval blockers, retry manifests, and immutable audit lineage.",
            StatusLabel = "Schedule controls",
            CountLabel = "Gate",
            Tone = WorkspaceTone.Warning,
            PrimaryActionId = "ReportRunStatus",
            PrimaryActionLabel = "Open",
            SecondaryActionId = "FundAuditTrail",
            SecondaryActionLabel = "Audit",
            AutomationName = "Reporting approval gates decision"
        },
        new()
        {
            Title = "Dashboard exceptions",
            Detail = "Inspect live exposure, cash, P&L, liquidity, Top-N, contribution, and data-quality signals.",
            StatusLabel = "Portfolio views",
            CountLabel = "Review",
            Tone = WorkspaceTone.Neutral,
            PrimaryActionId = "Dashboard",
            PrimaryActionLabel = "Open",
            SecondaryActionId = "DataQuality",
            SecondaryActionLabel = "Quality",
            AutomationName = "Reporting dashboard exceptions decision"
        },
        new()
        {
            Title = "Formula grid validation",
            Detail = $"Check {nameof(ReportWriterGridDefinitionDto.Formulas)}, {nameof(ReportWriterGridDefinitionDto.Metrics)}, {nameof(ReportWriterGridDefinitionDto.RowFields)}, and custom-formula lineage before publishing no-code report writer output.",
            StatusLabel = "Custom formulas",
            CountLabel = "Validate",
            Tone = WorkspaceTone.Info,
            PrimaryActionId = "FundReportPack",
            PrimaryActionLabel = "Open Pack",
            SecondaryActionId = "DataQuality",
            SecondaryActionLabel = "Quality",
            AutomationName = "Reporting formula grid validation decision"
        },
        new()
        {
            Title = "Cross-fund consolidation",
            Detail = $"Review {nameof(FundReportingSummaryDto.CrossFundConsolidations)}, {nameof(CrossFundReportingConsolidationDto.FundCount)}, {nameof(CrossFundReportingConsolidationDto.EntityCount)}, {nameof(CrossFundReportingConsolidationDto.ShadowNav)}, and drill-through routes before allocator or company-wide delivery.",
            StatusLabel = "Consolidation",
            CountLabel = "Roll-up",
            Tone = WorkspaceTone.Warning,
            PrimaryActionId = "Dashboard",
            PrimaryActionLabel = "Open Dashboard",
            SecondaryActionId = "AnalysisExport",
            SecondaryActionLabel = "Exports",
            AutomationName = "Reporting cross-fund consolidation decision"
        },
        new()
        {
            Title = "Export delivery",
            Detail = "Prepare scheduled PDF/XLSX/CSV packages for email-link, secure-portal, regulatory, and warehouse delivery.",
            StatusLabel = "Distribution",
            CountLabel = "Ready",
            Tone = WorkspaceTone.Success,
            PrimaryActionId = "AnalysisExport",
            PrimaryActionLabel = "Open",
            SecondaryActionId = "ExportPresets",
            SecondaryActionLabel = "Presets",
            AutomationName = "Reporting export delivery decision"
        }
    ];

    public ReportingWorkspaceShellViewModel()
        : base(ShellNavigationCatalog.GetWorkspaceShell("reporting")!)
    {
    }
}

public sealed class DataWorkspaceShellViewModel : WorkspaceShellViewModelBase
{
    public DataWorkspaceShellViewModel()
        : base(ShellNavigationCatalog.GetWorkspaceShell("data")!)
    {
    }
}

public sealed class SettingsWorkspaceShellViewModel : WorkspaceShellViewModelBase
{
    public SettingsWorkspaceShellViewModel()
        : base(ShellNavigationCatalog.GetWorkspaceShell("settings")!)
    {
    }
}
