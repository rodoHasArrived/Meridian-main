using Meridian.Contracts.Api;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services;
using Meridian.Ui.Shared.Services;
using Meridian.Wpf.Features.Reporting;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
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
    private readonly FundContextService? _fundContextService;
    private readonly FundOperationsWorkspaceReadService? _workspaceReadService;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private bool _isMonitoringFundContext;
    private IReadOnlyList<WorkspaceQueueItem> _cockpitDecisionItems = ReportingWorkspaceShellPresentationService.BuildFallbackDecisionItems();
    private string _writerSummaryText = "Locked";
    private string _approvalSummaryText = "Context";
    private string _deliverySummaryText = "Awaiting fund";
    private string _summarySnapshotDetailText = "Select a fund-linked context to load report-writer, schedule, export, branding, access, and audit telemetry.";

    public ReportingWorkspaceShellViewModel(
        FundContextService? fundContextService = null,
        FundOperationsWorkspaceReadService? workspaceReadService = null,
        ReportingGovernanceWorkbenchViewModel? governance = null)
        : base(ShellNavigationCatalog.GetWorkspaceShell("reporting")!)
    {
        _fundContextService = fundContextService;
        _workspaceReadService = workspaceReadService;
        Governance = governance;
        CommandGroup = ReportingWorkspaceShellPresentationService.BuildCommandGroup(hasFund: false);
    }

    public event EventHandler? RefreshRequested;

    /// <summary>
    /// Canonical reporting lifecycle workbench. It remains a thin client projection over the
    /// shared reporting API and is optional only for compatibility with isolated shell tests.
    /// </summary>
    public ReportingGovernanceWorkbenchViewModel? Governance { get; }

    public IReadOnlyList<WorkspaceQueueItem> CockpitDecisionItems
    {
        get => _cockpitDecisionItems;
        private set => SetProperty(ref _cockpitDecisionItems, value);
    }

    public string WriterSummaryText
    {
        get => _writerSummaryText;
        private set => SetProperty(ref _writerSummaryText, value);
    }

    public string ApprovalSummaryText
    {
        get => _approvalSummaryText;
        private set => SetProperty(ref _approvalSummaryText, value);
    }

    public string DeliverySummaryText
    {
        get => _deliverySummaryText;
        private set => SetProperty(ref _deliverySummaryText, value);
    }

    public string SummarySnapshotDetailText
    {
        get => _summarySnapshotDetailText;
        private set => SetProperty(ref _summarySnapshotDetailText, value);
    }

    public void Start()
    {
        if (_fundContextService is null || _isMonitoringFundContext)
        {
            return;
        }

        _fundContextService.ActiveFundProfileChanged += OnFundContextChanged;
        _isMonitoringFundContext = true;
    }

    public void Stop()
    {
        if (_fundContextService is null || !_isMonitoringFundContext)
        {
            return;
        }

        _fundContextService.ActiveFundProfileChanged -= OnFundContextChanged;
        _isMonitoringFundContext = false;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_fundContextService?.CurrentFundProfile is not { } profile)
        {
            Governance?.SetFundContext(null, null);
            ApplyReporting(null);
            return;
        }

        Governance?.SetFundContext(profile.FundProfileId, profile.BaseCurrency);
        if (_workspaceReadService is null)
        {
            ApplyReporting(null);
            return;
        }

        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            var workspace = await _workspaceReadService.GetWorkspaceAsync(
                    new FundOperationsWorkspaceQuery(profile.FundProfileId, Currency: profile.BaseCurrency),
                    cancellationToken)
                .ConfigureAwait(true);

            ApplyReporting(workspace.Reporting);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Meridian.Wpf.Services.LoggingService.Instance.LogError("[ReportingWorkspaceShell] Refresh failed", ex);
            ApplyReporting(null);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private void ApplyReporting(FundReportingSummaryDto? reporting)
    {
        Governance?.ApplyScheduleRecords(reporting?.Schedules);
        CockpitDecisionItems = ReportingWorkspaceShellPresentationService.BuildDecisionItems(reporting);
        CommandGroup = ReportingWorkspaceShellPresentationService.BuildCommandGroup(reporting is not null);

        if (reporting is null)
        {
            WriterSummaryText = "Locked";
            ApprovalSummaryText = "Context";
            DeliverySummaryText = "Awaiting fund";
            SummarySnapshotDetailText = "Select a fund-linked context to load report-writer, schedule, export, branding, access, and audit telemetry.";
            return;
        }

        var datasetSources = reporting.ReportWriterDatasetSources?.Count ?? 0;
        var generatedGrids = reporting.ScheduleDeliveryPlans?.Sum(static plan => plan.LastDeliveryGeneratedReportWriterGridCount) ?? 0;
        var schedulePlans = reporting.ScheduleDeliveryPlans?.Count ?? 0;
        var readyPlans = reporting.ScheduleDeliveryPlans?.Count(static plan => plan.IsReady) ?? 0;
        var deliveryAttempts = reporting.DeliveryAttempts?.Count ?? 0;
        var workflowRecords = reporting.WorkflowRecords?.Count ?? 0;

        WriterSummaryText = generatedGrids > 0 ? $"{generatedGrids} grids" : $"{datasetSources} sources";
        ApprovalSummaryText = workflowRecords > 0 ? $"{workflowRecords} records" : "No records";
        DeliverySummaryText = schedulePlans > 0 ? $"{readyPlans}/{schedulePlans} ready" : $"{deliveryAttempts} attempts";
        SummarySnapshotDetailText = $"{reporting.Summary} Report writer, scheduled delivery, portfolio views, exports, branding, access, and audit lineage are refreshed from the shared fund-operations read model.";
    }

    private void OnFundContextChanged(object? sender, FundProfileChangedEventArgs e)
        => RefreshRequested?.Invoke(this, EventArgs.Empty);
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
