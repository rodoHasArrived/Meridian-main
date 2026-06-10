using Meridian.Contracts.Api;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;
using Meridian.Wpf.Workstation.Commands;
using Meridian.Wpf.Workstation.ViewModels.Base;

namespace Meridian.Wpf.ViewModels;

public abstract class WorkspaceShellViewModelBase : WorkspaceViewModelBase
{
    private WorkspaceCommandGroup _commandGroup = new();

    protected WorkspaceShellViewModelBase(WorkspaceShellDefinition workspaceDefinition)
    {
        WorkspaceDefinition = workspaceDefinition ?? throw new ArgumentNullException(nameof(workspaceDefinition));
    }

    public WorkspaceShellDefinition WorkspaceDefinition { get; }

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
