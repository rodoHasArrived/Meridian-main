using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using WpfLoggingService = Meridian.Wpf.Services.LoggingService;

namespace Meridian.Wpf.Views;

/// <summary>
/// Trading cockpit shell - landing page for the Trading workspace.
/// Surfaces active paper/live run counts, total equity, open positions, and the risk rail.
/// Embeds a <see cref="MeridianDockingManager"/> for IDE-style floating panes.
/// </summary>
public partial class TradingWorkspaceShellPage : TradingWorkspaceShellPageBase
{
    internal readonly record struct TradingPortfolioNavigationTarget(string PageTag, PaneDropAction Action, string? RunId);
    internal readonly record struct TradingStatusCardPresentation(
        string SummaryText,
        string BadgeText,
        TradingWorkspaceStatusTone BadgeTone,
        TradingWorkspaceStatusItem PromotionStatus,
        TradingWorkspaceStatusItem AuditStatus,
        TradingWorkspaceStatusItem ValidationStatus);

    private readonly StrategyRunWorkspaceService _runService;
    private readonly FundContextService _fundContextService;
    private readonly WorkstationOperatingContextService? _operatingContextService;
    private readonly CashFinancingReadService _cashFinancingReadService;
    private readonly WorkspaceShellContextService _shellContextService;
    private readonly WorkstationWorkflowSummaryService? _workflowSummaryService;
    private readonly TradingOperatorReadinessService? _operatorReadinessService;
    private WorkflowNextAction? _currentWorkflowAction;

    public TradingWorkspaceShellPage(
        NavigationService navigationService,
        TradingWorkspaceShellStateProvider stateProvider,
        TradingWorkspaceShellViewModel viewModel,
        StrategyRunWorkspaceService runService,
        FundContextService fundContextService,
        WorkstationOperatingContextService? operatingContextService,
        CashFinancingReadService cashFinancingReadService,
        WorkspaceShellContextService shellContextService,
        WorkstationWorkflowSummaryService? workflowSummaryService = null,
        TradingOperatorReadinessService? operatorReadinessService = null)
        : base(navigationService, stateProvider, viewModel)
    {
        InitializeComponent();
        _runService = runService;
        _fundContextService = fundContextService;
        _operatingContextService = operatingContextService;
        _cashFinancingReadService = cashFinancingReadService;
        _shellContextService = shellContextService;
        _workflowSummaryService = workflowSummaryService;
        _operatorReadinessService = operatorReadinessService;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _fundContextService.ActiveFundProfileChanged += OnActiveFundProfileChanged;
        _runService.ActiveRunContextChanged += OnActiveRunContextChanged;
        _shellContextService.SignalsChanged += OnSignalsChanged;
        if (_operatingContextService is not null)
        {
            _operatingContextService.ActiveContextChanged += OnOperatingContextChanged;
            _operatingContextService.WindowModeChanged += OnSignalsChanged;
        }

        UpdateActiveFundText();
        await RefreshAsync();
        await RestoreDockLayoutAsync(TradingDockManager);
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _fundContextService.ActiveFundProfileChanged -= OnActiveFundProfileChanged;
        _runService.ActiveRunContextChanged -= OnActiveRunContextChanged;
        _shellContextService.SignalsChanged -= OnSignalsChanged;
        if (_operatingContextService is not null)
        {
            _operatingContextService.ActiveContextChanged -= OnOperatingContextChanged;
            _operatingContextService.WindowModeChanged -= OnSignalsChanged;
        }

        _ = SaveDockLayoutAsync(TradingDockManager);
    }

    private async System.Threading.Tasks.Task RefreshAsync()
    {
        try
        {
            var summaryTask = _runService.GetTradingSummaryAsync();
            var workflowTask = GetTradingWorkflowSummaryAsync();
            var readinessTask = GetTradingOperatorReadinessAsync();
            await System.Threading.Tasks.Task.WhenAll(summaryTask, workflowTask, readinessTask).ConfigureAwait(true);

            var summary = await summaryTask.ConfigureAwait(true);
            var workflow = await workflowTask.ConfigureAwait(true);
            var readiness = await readinessTask.ConfigureAwait(true);

            PaperRunsText.Text = summary.PaperRunCount.ToString();
            LiveRunsText.Text = summary.LiveRunCount.ToString();
            TotalEquityText.Text = summary.TotalEquityFormatted;

            DrawdownText.Text = summary.MaxDrawdownFormatted;
            PositionLimitText.Text = summary.PositionLimitLabel;
            OrderRateText.Text = summary.OrderRateLabel;

            if (summary.ActivePositions.Count > 0)
            {
                ActivePositionsList.ItemsSource = summary.ActivePositions;
                NoPositionsText.Visibility = Visibility.Collapsed;
            }
            else
            {
                ActivePositionsList.ItemsSource = null;
                NoPositionsText.Visibility = Visibility.Visible;
            }

            var profile = _fundContextService.CurrentFundProfile;
            if (profile is not null)
            {
                var capitalSummary = await _cashFinancingReadService.GetAsync(profile.FundProfileId, profile.BaseCurrency);
                CapitalCashText.Text = capitalSummary.TotalCash.ToString("C0");
                CapitalGrossExposureText.Text = capitalSummary.GrossExposure.ToString("C0");
                CapitalNetExposureText.Text = capitalSummary.NetExposure.ToString("C0");
                CapitalFinancingText.Text = capitalSummary.FinancingCost.ToString("C0");
                CapitalControlsDetailText.Text = capitalSummary.Highlights.FirstOrDefault()
                    ?? "Capital and financing posture is available for the active fund.";
            }
            else
            {
                CapitalCashText.Text = "-";
                CapitalGrossExposureText.Text = "-";
                CapitalNetExposureText.Text = "-";
                CapitalFinancingText.Text = "-";
                CapitalControlsDetailText.Text = _operatingContextService?.CurrentContext is { } operatingContext
                    ? $"Switch to a fund-linked accounting view to unlock capital and reconciliation posture for {operatingContext.DisplayName}."
                    : "Select an operating context to unlock capital, financing, and reconciliation posture.";
            }

            ContextStrip.ShellContext = await _shellContextService.CreateAsync(new WorkspaceShellContextInput
            {
                WorkspaceTitle = "Trading Cockpit",
                WorkspaceSubtitle = "Risk-aware trading shell for live posture, blotter review, safe staging, and docked execution detail.",
                PrimaryScopeLabel = "Desk",
                PrimaryScopeValue = summary.ActiveRunContext?.StrategyName ?? (_fundContextService.CurrentFundProfile?.DisplayName ?? "No active trading run"),
                AsOfValue = DateTimeOffset.Now.ToString("MMM dd yyyy HH:mm"),
                FreshnessValue = summary.ActiveRunContext is null ? "Awaiting active run" : $"{summary.ActiveRunContext.ModeLabel} · {summary.ActiveRunContext.StatusLabel}",
                ReviewStateLabel = "Risk",
                ReviewStateValue = summary.ActivePositions.Count > 0 ? $"{summary.ActivePositions.Count} active position(s)" : "No live positions",
                ReviewStateTone = summary.ActivePositions.Count > 0 ? WorkspaceTone.Warning : WorkspaceTone.Success,
                CriticalLabel = "Critical",
                CriticalValue = summary.LiveRunCount > 0 ? $"{summary.LiveRunCount} live run(s)" : "No live runs",
                CriticalTone = summary.LiveRunCount > 0 ? WorkspaceTone.Warning : WorkspaceTone.Info,
                AdditionalBadges =
                [
                    new WorkspaceShellBadge
                    {
                        Label = "Equity",
                        Value = summary.TotalEquityFormatted,
                        Glyph = "\uE9F5",
                        Tone = summary.LiveRunCount > 0 ? WorkspaceTone.Info : WorkspaceTone.Neutral
                    }
                ]
            });

            UpdateActiveRun(summary.ActiveRunContext, summary);
            UpdateStatusCard(summary);
            ApplyWorkflowGuidance(workflow);
            ApplyOperatorReadiness(readiness);
            ViewModel.CommandGroup = BuildCommandGroup();
            CommandBar.CommandGroup = ViewModel.CommandGroup;
        }
        catch (Exception ex)
        {
            WpfLoggingService.Instance.LogError($"[TradingWorkspaceShell] Refresh failed: {ex.Message}");
            ApplyStatusCardPresentation(BuildDegradedStatusCardPresentation());
            ApplyWorkflowGuidance(null);
            RiskRailText.Text = "Cockpit refresh degraded. Trading posture and broker validation details may be stale until the shell can refresh again.";
            DeskActionStatusText.Text = "Cockpit refresh failed. Recheck desktop API connectivity, run-state services, and broker validation before relying on this shell state.";
        }
    }

    private async Task<TradingOperatorReadinessDto?> GetTradingOperatorReadinessAsync()
    {
        if (_operatorReadinessService is null)
        {
            return null;
        }

        try
        {
            return await _operatorReadinessService.GetAsync().ConfigureAwait(true);
        }
        catch
        {
            return null;
        }
    }

    private async Task<WorkspaceWorkflowSummary?> GetTradingWorkflowSummaryAsync()
    {
        if (_workflowSummaryService is null)
        {
            return null;
        }

        try
        {
            var summary = await _workflowSummaryService
                .GetAsync(
                    hasOperatingContext: _operatingContextService?.CurrentContext is not null || _fundContextService.CurrentFundProfile is not null,
                    operatingContextDisplayName: _operatingContextService?.CurrentContext?.DisplayName,
                    fundProfileId: _fundContextService.CurrentFundProfile?.FundProfileId,
                    fundDisplayName: _fundContextService.CurrentFundProfile?.DisplayName)
                .ConfigureAwait(true);

            return summary.Workspaces.FirstOrDefault(static workspace =>
                string.Equals(workspace.WorkspaceId, "trading", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    private void ApplyWorkflowGuidance(WorkspaceWorkflowSummary? workflow)
    {
        var effectiveWorkflow = workflow ?? new WorkspaceWorkflowSummary(
            WorkspaceId: "trading",
            WorkspaceTitle: "Trading",
            StatusLabel: "Fallback trading guidance",
            StatusDetail: "The desk is using deterministic fallback text until the shared workflow summary refresh succeeds.",
            StatusTone: "Info",
            NextAction: new WorkflowNextAction(
                Label: "Open Strategy Runs",
                Detail: "Review recorded runs and bring one into the trading lane.",
                TargetPageTag: "StrategyRuns",
                Tone: "Primary"),
            PrimaryBlocker: new WorkflowBlockerSummary(
                Code: "fallback",
                Label: "Workflow summary unavailable",
                Detail: "Fallback guidance keeps the trading shell actionable without depending on null-sensitive state.",
                Tone: "Info",
                IsBlocking: false),
            Evidence: []);

        _currentWorkflowAction = effectiveWorkflow.NextAction;
        TradingStatusSummaryText.Text = effectiveWorkflow.StatusDetail;
        TradingStatusBadgeText.Text = effectiveWorkflow.StatusLabel;
        ApplyTone(TradingStatusBadgeBorder, TradingStatusBadgeText, ParseTone(effectiveWorkflow.StatusTone));
        ApplyStatusItem(
            PromotionStatusPill,
            PromotionStatusLabelText,
            PromotionStatusDetailText,
            new TradingWorkspaceStatusItem
            {
                Label = effectiveWorkflow.StatusLabel,
                Detail = effectiveWorkflow.StatusDetail,
                Tone = ParseTone(effectiveWorkflow.StatusTone)
            });
        ApplyStatusItem(
            AuditStatusPill,
            AuditStatusLabelText,
            AuditStatusDetailText,
            new TradingWorkspaceStatusItem
            {
                Label = effectiveWorkflow.PrimaryBlocker.Label,
                Detail = effectiveWorkflow.PrimaryBlocker.Detail,
                Tone = ParseTone(effectiveWorkflow.PrimaryBlocker.Tone)
            });
        ApplyStatusItem(
            ValidationStatusPill,
            ValidationStatusLabelText,
            ValidationStatusDetailText,
            new TradingWorkspaceStatusItem
            {
                Label = effectiveWorkflow.NextAction.Label,
                Detail = effectiveWorkflow.NextAction.Detail,
                Tone = ParseTone(effectiveWorkflow.NextAction.Tone)
            });

        TradingWorkflowTargetText.Text = $"Target page: {effectiveWorkflow.NextAction.TargetPageTag}";
        TradingWorkflowPrimaryButton.Content = effectiveWorkflow.NextAction.Label;
    }

    private void ApplyOperatorReadiness(TradingOperatorReadinessDto? readiness)
    {
        if (readiness is null)
        {
            ValidationStatusDetailText.Text = "Operator readiness is unavailable. Replay evidence, controls, and brokerage sync freshness may be stale.";
            DeskActionStatusText.Text = "Shared readiness evidence is unavailable. Recheck local host services before accepting this cockpit state.";
            return;
        }

        TradingStatusSummaryText.Text = readiness.Summary;
        TradingStatusBadgeText.Text = readiness.IsOperatorReady ? "Ready" : "Attention";
        ApplyTone(
            TradingStatusBadgeBorder,
            TradingStatusBadgeText,
            readiness.IsOperatorReady ? TradingWorkspaceStatusTone.Success : TradingWorkspaceStatusTone.Warning);

        ApplyStatusItem(
            PromotionStatusPill,
            PromotionStatusLabelText,
            PromotionStatusDetailText,
            new TradingWorkspaceStatusItem
            {
                Label = readiness.Promotion?.StatusLabel ?? "Promotion evidence unavailable",
                Detail = readiness.Promotion?.Detail ?? "Promotion posture has not been projected into shared readiness yet.",
                Tone = readiness.Promotion?.NeedsReview == true ? TradingWorkspaceStatusTone.Warning : TradingWorkspaceStatusTone.Info
            });

        ApplyStatusItem(
            AuditStatusPill,
            AuditStatusLabelText,
            AuditStatusDetailText,
            new TradingWorkspaceStatusItem
            {
                Label = readiness.Controls?.ControlsReady == true ? "Controls ready" : "Controls need review",
                Detail = readiness.Controls?.Summary ?? "Execution controls have not been projected into shared readiness yet.",
                Tone = readiness.Controls?.ControlsReady == true ? TradingWorkspaceStatusTone.Success : TradingWorkspaceStatusTone.Warning
            });

        ApplyStatusItem(
            ValidationStatusPill,
            ValidationStatusLabelText,
            ValidationStatusDetailText,
            new TradingWorkspaceStatusItem
            {
                Label = readiness.Replay?.StatusLabel ?? "Replay evidence unavailable",
                Detail = readiness.Replay?.Detail ?? "Paper replay verification has not produced shared readiness evidence yet.",
                Tone = readiness.Replay?.HasMismatch == true ? TradingWorkspaceStatusTone.Warning : TradingWorkspaceStatusTone.Info
            });

        if (readiness.Warnings.Count > 0)
        {
            RiskRailText.Text = readiness.Warnings[0];
        }
        else
        {
            RiskRailText.Text = "Paper session, controls, brokerage sync, and Security Master coverage are aligned for operator review.";
        }

        DeskActionStatusText.Text = readiness.BrokerageSync is null
            ? "Brokerage sync evidence is unavailable. Portfolio and cash continuity are based on local paper and ledger state only."
            : $"Brokerage sync {readiness.BrokerageSync.Health.ToString().ToLowerInvariant()} as of {readiness.BrokerageSync.LastSuccessfulSyncUtc?.ToLocalTime():MMM dd HH:mm}; {readiness.BrokerageSync.PositionCount} position(s), {readiness.BrokerageSync.OpenOrderCount} open order(s).";
    }

    private void UpdateActiveRun(ActiveRunContext? activeRun, TradingWorkspaceSummary? summary = null)
    {
        if (activeRun is null)
        {
            TradingActiveRunText.Text = "No active trading run";
            TradingActiveRunMetaText.Text = "Use Research to promote a run, or open a live/paper panel below.";
            WatchlistStatusText.Text = "Watchlists and active strategies populate once paper or live runs are started.";
            MarketCoreText.Text = "Live data, order book, portfolio, and accounting consequences are ready to dock below.";
            RiskRailText.Text = summary is null
                ? "Risk, reconciliation, and audit surfaces become specific once an active run is selected."
                : $"{summary.ValidationStatus.Label}: {summary.ValidationStatus.Detail}";
            DeskActionStatusText.Text = summary?.ValidationStatus.Detail
                ?? "Broker validation and promotion readiness appear here once a run is active.";
            return;
        }

        TradingActiveRunText.Text = activeRun.StrategyName;
        TradingActiveRunMetaText.Text = $"{activeRun.ModeLabel} · {activeRun.StatusLabel} · {activeRun.FundScopeLabel}";
        WatchlistStatusText.Text = activeRun.PortfolioPreview;
        MarketCoreText.Text = $"{activeRun.LedgerPreview} {activeRun.AuditStatus.Detail}";
        RiskRailText.Text = $"{activeRun.RiskSummary} {activeRun.ValidationStatus.Label}: {activeRun.ValidationStatus.Detail}";
        DeskActionStatusText.Text = activeRun.ValidationStatus.Detail;
    }

    private void UpdateStatusCard(TradingWorkspaceSummary summary)
        => ApplyStatusCardPresentation(BuildStatusCardPresentation(summary));

    private void ApplyStatusCardPresentation(TradingStatusCardPresentation presentation)
    {
        TradingStatusSummaryText.Text = presentation.SummaryText;
        TradingStatusBadgeText.Text = presentation.BadgeText;
        ApplyTone(TradingStatusBadgeBorder, TradingStatusBadgeText, presentation.BadgeTone);
        ApplyStatusItem(PromotionStatusPill, PromotionStatusLabelText, PromotionStatusDetailText, presentation.PromotionStatus);
        ApplyStatusItem(AuditStatusPill, AuditStatusLabelText, AuditStatusDetailText, presentation.AuditStatus);
        ApplyStatusItem(ValidationStatusPill, ValidationStatusLabelText, ValidationStatusDetailText, presentation.ValidationStatus);
    }

    internal static TradingStatusCardPresentation BuildStatusCardPresentation(TradingWorkspaceSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var promotion = summary.ActiveRunContext?.PromotionStatus ?? summary.PromotionStatus;
        var audit = summary.ActiveRunContext?.AuditStatus ?? summary.AuditStatus;
        var validation = summary.ActiveRunContext?.ValidationStatus ?? summary.ValidationStatus;
        var cardTone = ResolveCardTone(promotion, audit, validation);
        var badgeText = cardTone switch
        {
            TradingWorkspaceStatusTone.Warning => "Attention",
            TradingWorkspaceStatusTone.Success => "Ready",
            _ => "Info"
        };
        var summaryText = summary.ActiveRunContext is null
            ? "Workspace-level promotion handoff, audit traceability, and control coverage across recorded runs."
            : $"{summary.ActiveRunContext.StrategyName} promotion handoff, audit traceability, and control coverage.";

        return new TradingStatusCardPresentation(
            summaryText,
            badgeText,
            cardTone,
            promotion,
            audit,
            validation);
    }

    internal static TradingStatusCardPresentation BuildDegradedStatusCardPresentation() =>
        new(
            "Cockpit refresh degraded. Promotion, audit, and broker validation details may be stale.",
            "Attention",
            TradingWorkspaceStatusTone.Warning,
            new TradingWorkspaceStatusItem
            {
                Label = "Promotion refresh degraded",
                Detail = "Promotion posture may be stale until the shell can refresh again.",
                Tone = TradingWorkspaceStatusTone.Warning
            },
            new TradingWorkspaceStatusItem
            {
                Label = "Audit refresh degraded",
                Detail = "Audit linkage and ledger review posture may be stale until the shell can refresh again.",
                Tone = TradingWorkspaceStatusTone.Warning
            },
            new TradingWorkspaceStatusItem
            {
                Label = "Validation refresh degraded",
                Detail = "Trading posture and broker validation details may be stale until the shell can refresh again.",
                Tone = TradingWorkspaceStatusTone.Warning
            });

    private static TradingWorkspaceStatusTone ResolveCardTone(params TradingWorkspaceStatusItem[] items)
    {
        if (items.Any(static item => item.Tone == TradingWorkspaceStatusTone.Warning))
        {
            return TradingWorkspaceStatusTone.Warning;
        }

        return items.Any(static item => item.Tone == TradingWorkspaceStatusTone.Success)
            ? TradingWorkspaceStatusTone.Success
            : TradingWorkspaceStatusTone.Info;
    }

    private void ApplyStatusItem(
        Border pill,
        TextBlock labelText,
        TextBlock detailText,
        TradingWorkspaceStatusItem item)
    {
        labelText.Text = item.Label;
        detailText.Text = item.Detail;
        ApplyTone(pill, labelText, item.Tone);
    }

    private void ApplyTone(Border border, TextBlock textBlock, TradingWorkspaceStatusTone tone)
    {
        var (backgroundKey, borderKey) = tone switch
        {
            TradingWorkspaceStatusTone.Success => ("ConsoleAccentGreenAlpha10Brush", "SuccessColorBrush"),
            TradingWorkspaceStatusTone.Warning => ("ConsoleAccentOrangeAlpha10Brush", "WarningColorBrush"),
            _ => ("ConsoleAccentBlueAlpha10Brush", "InfoColorBrush")
        };

        border.Background = GetBrush(backgroundKey);
        border.BorderBrush = GetBrush(borderKey);
        textBlock.Foreground = GetBrush(borderKey);
    }

    private Brush GetBrush(string resourceKey)
        => TryFindResource(resourceKey) as Brush ?? Brushes.Transparent;

    private static TradingWorkspaceStatusTone ParseTone(string? tone) => tone?.ToLowerInvariant() switch
    {
        "success" => TradingWorkspaceStatusTone.Success,
        "warning" => TradingWorkspaceStatusTone.Warning,
        "danger" => TradingWorkspaceStatusTone.Warning,
        _ => TradingWorkspaceStatusTone.Info
    };

    private void OnPaneDropRequested(object? sender, PaneDropEventArgs e)
        => OpenWorkspacePage(TradingDockManager, e.PageTag, e.Action);

    private void OnCommandBarCommandInvoked(object sender, WorkspaceCommandInvokedEventArgs e)
    {
        switch (e.Command.Id)
        {
            case "Pause":
                PauseTrading_Click(sender, new RoutedEventArgs());
                break;
            case "Stop":
                StopTrading_Click(sender, new RoutedEventArgs());
                break;
            case "Flatten":
                FlattenPositions_Click(sender, new RoutedEventArgs());
                break;
            case "CancelAll":
                CancelAll_Click(sender, new RoutedEventArgs());
                break;
            case "AcknowledgeRisk":
                AcknowledgeRisk_Click(sender, new RoutedEventArgs());
                break;
            case "LiveData":
                OpenLiveData_Click(sender, new RoutedEventArgs());
                break;
            case "PositionBlotter":
                OpenBlotter_Click(sender, new RoutedEventArgs());
                break;
            case "RunPortfolio":
                OpenPortfolio_Click(sender, new RoutedEventArgs());
                break;
            case "RunRisk":
                OpenRiskRail_Click(sender, new RoutedEventArgs());
                break;
            case "NotificationCenter":
                OpenAlerts_Click(sender, new RoutedEventArgs());
                break;
            case "FundTrialBalance":
                OpenAccountingConsequences_Click(sender, new RoutedEventArgs());
                break;
            case "FundReconciliation":
                OpenReconciliationReview_Click(sender, new RoutedEventArgs());
                break;
            case "FundAuditTrail":
                OpenAuditTrail_Click(sender, new RoutedEventArgs());
                break;
            case "TradingHours":
                NavigationService.NavigateTo("TradingHours");
                break;
        }
    }

    private void PauseTrading_Click(object sender, RoutedEventArgs e)
    {
        DeskActionStatusText.Text = "Pause queued. Review blotter and risk rail before resuming.";
        OpenWorkspacePage(TradingDockManager, "PositionBlotter", PaneDropAction.SplitRight);
    }

    private void StopTrading_Click(object sender, RoutedEventArgs e)
    {
        DeskActionStatusText.Text = "Stop requested. Existing positions remain visible for review.";
        OpenWorkspacePage(TradingDockManager, "RunRisk", PaneDropAction.SplitBelow);
    }

    private void FlattenPositions_Click(object sender, RoutedEventArgs e)
    {
        DeskActionStatusText.Text = "Flatten review opened. Use the blotter and order book to verify exit posture.";
        OpenWorkspacePage(TradingDockManager, "OrderBook", PaneDropAction.FloatWindow);
    }

    private void CancelAll_Click(object sender, RoutedEventArgs e)
    {
        DeskActionStatusText.Text = "Cancel-all review opened. Confirm open orders in the blotter.";
        OpenWorkspacePage(TradingDockManager, "PositionBlotter", PaneDropAction.FloatWindow);
    }

    private void AcknowledgeRisk_Click(object sender, RoutedEventArgs e)
    {
        DeskActionStatusText.Text = "Risk acknowledgement captured locally for this workstation session.";
        OpenWorkspacePage(TradingDockManager, "RunRisk", PaneDropAction.SplitRight);
    }

    private void OpenLiveData_Click(object sender, RoutedEventArgs e)
        => NavigationService.NavigateTo("LiveData");

    private void OpenBlotter_Click(object sender, RoutedEventArgs e)
        => OpenWorkspacePage(TradingDockManager, "PositionBlotter", PaneDropAction.SplitRight);

    private async void OpenPortfolio_Click(object sender, RoutedEventArgs e)
    {
        var target = ResolvePortfolioNavigationTarget(await _runService.GetActiveRunContextAsync().ConfigureAwait(true));
        OpenWorkspacePage(TradingDockManager, target.PageTag, target.Action, target.RunId);
    }

    private void ImportPositions_Click(object sender, RoutedEventArgs e)
        => NavigationService.NavigateTo("PortfolioImport");

    private void OpenOrderBook_Click(object sender, RoutedEventArgs e)
        => OpenWorkspacePage(TradingDockManager, "OrderBook", PaneDropAction.FloatWindow);

    private void OpenRiskRail_Click(object sender, RoutedEventArgs e)
        => OpenWorkspacePage(TradingDockManager, "RunRisk", PaneDropAction.SplitRight);

    private void OpenAlerts_Click(object sender, RoutedEventArgs e)
        => OpenWorkspacePage(TradingDockManager, "NotificationCenter", PaneDropAction.SplitBelow);

    private void OpenWorkflowNextAction_Click(object sender, RoutedEventArgs e)
    {
        var action = _currentWorkflowAction;
        if (action is null)
        {
            return;
        }

        switch (action.TargetPageTag)
        {
            case "TradingShell" when string.Equals(action.Label, "Choose Context", StringComparison.OrdinalIgnoreCase):
                RequestContextSelection(_fundContextService, _operatingContextService);
                return;
            case "TradingShell" when string.Equals(action.Label, "Open Active Cockpit", StringComparison.OrdinalIgnoreCase):
                OpenPortfolio_Click(sender, e);
                return;
            case "TradingShell":
                NavigationService.NavigateTo(action.TargetPageTag);
                return;
            case "StrategyRuns":
                NavigationService.NavigateTo("StrategyRuns");
                return;
            case "GovernanceShell":
                NavigationService.NavigateTo("GovernanceShell");
                return;
            case "FundTrialBalance":
                OpenAccountingConsequences_Click(sender, e);
                return;
            case "FundReconciliation":
                OpenReconciliationReview_Click(sender, e);
                return;
            default:
                NavigationService.NavigateTo(action.TargetPageTag);
                return;
        }
    }

    private void OpenAccountingConsequences_Click(object sender, RoutedEventArgs e)
        => OpenWorkspacePage(TradingDockManager, "FundTrialBalance", PaneDropAction.OpenTab);

    private void OpenReconciliationReview_Click(object sender, RoutedEventArgs e)
        => OpenWorkspacePage(TradingDockManager, "FundReconciliation", PaneDropAction.SplitBelow);

    private void OpenAuditTrail_Click(object sender, RoutedEventArgs e)
        => OpenWorkspacePage(TradingDockManager, "FundAuditTrail", PaneDropAction.OpenTab);

    private async void OnActiveFundProfileChanged(object? sender, FundProfileChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() => OnActiveFundProfileChanged(sender, e));
            return;
        }

        UpdateActiveFundText();
        await RefreshAsync();
    }

    private void OnActiveRunContextChanged(object? sender, ActiveRunContext? e)
        => DispatchRefresh(RefreshAsync);

    private void OnSignalsChanged(object? sender, EventArgs e)
        => DispatchRefresh(RefreshAsync);

    private void OnOperatingContextChanged(object? sender, WorkstationOperatingContextChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() => OnOperatingContextChanged(sender, e));
            return;
        }

        UpdateActiveFundText();
        _ = RefreshAsync();
    }

    private void UpdateActiveFundText()
    {
        if (_operatingContextService?.CurrentContext is { } operatingContext)
        {
            ActiveFundText.Text = operatingContext.DisplayName;
            ActiveFundDetailText.Text = operatingContext.Subtitle;
            return;
        }

        var profile = _fundContextService.CurrentFundProfile;
        if (profile is null)
        {
            ActiveFundText.Text = "No operating context selected";
            ActiveFundDetailText.Text = "Runs, allocations, and accounting posture scope to the active operating context.";
            return;
        }

        ActiveFundText.Text = profile.DisplayName;
        ActiveFundDetailText.Text = $"{profile.LegalEntityName} · {profile.BaseCurrency}";
    }

    internal static WorkspaceCommandGroup BuildCommandGroup() =>
        new()
        {
            PrimaryCommands =
            [
                new WorkspaceCommandItem { Id = "Pause", Label = "Pause", Description = "Pause trading", ShortcutHint = "Desk", Glyph = "\uE769", Tone = WorkspaceTone.Primary },
                new WorkspaceCommandItem { Id = "Stop", Label = "Stop", Description = "Stop trading", ShortcutHint = "Desk", Glyph = "\uE71A", Tone = WorkspaceTone.Secondary },
                new WorkspaceCommandItem { Id = "Flatten", Label = "Flatten", Description = "Flatten positions", ShortcutHint = "Risk", Glyph = "\uE9F5", Tone = WorkspaceTone.Danger }
            ],
            SecondaryCommands =
            [
                new WorkspaceCommandItem { Id = "CancelAll", Label = "Cancel All", Description = "Cancel staged orders", Glyph = "\uE711" },
                new WorkspaceCommandItem { Id = "AcknowledgeRisk", Label = "Acknowledge Risk", Description = "Acknowledge current risk posture", Glyph = "\uE73E" },
                new WorkspaceCommandItem { Id = "LiveData", Label = "Live Data", Description = "Open live data", Glyph = "\uE9D2" },
                new WorkspaceCommandItem { Id = "RunPortfolio", Label = "Portfolio", Description = "Open run or account portfolio", Glyph = "\uE8B5" },
                new WorkspaceCommandItem { Id = "PositionBlotter", Label = "Blotter", Description = "Open position blotter", Glyph = "\uE8A5" },
                new WorkspaceCommandItem { Id = "RunRisk", Label = "Risk Rail", Description = "Open risk rail", Glyph = "\uE7BA" },
                new WorkspaceCommandItem { Id = "FundTrialBalance", Label = "Accounting", Description = "Open accounting consequences", Glyph = "\uE9D9" },
                new WorkspaceCommandItem { Id = "FundReconciliation", Label = "Reconciliation", Description = "Open reconciliation review", Glyph = "\uE895" },
                new WorkspaceCommandItem { Id = "FundAuditTrail", Label = "Audit", Description = "Open audit trail", Glyph = "\uE7BA" },
                new WorkspaceCommandItem { Id = "NotificationCenter", Label = "Alerts", Description = "Open alerts", Glyph = "\uE7F4" },
                new WorkspaceCommandItem { Id = "TradingHours", Label = "Trading Hours", Description = "Open trading hours", Glyph = "\uE823" }
            ]
        };

    internal static TradingPortfolioNavigationTarget ResolvePortfolioNavigationTarget(ActiveRunContext? activeRun)
        => activeRun is null
            ? new TradingPortfolioNavigationTarget("AccountPortfolio", PaneDropAction.Replace, null)
            : new TradingPortfolioNavigationTarget("RunPortfolio", PaneDropAction.SplitLeft, activeRun.RunId);
}
