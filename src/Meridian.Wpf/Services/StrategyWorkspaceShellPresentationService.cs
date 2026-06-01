using Meridian.Contracts.Workstation;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Services;
using Meridian.Ui.Services;
using Meridian.Ui.Shared.Services;
using Meridian.Wpf.Copy;
using Meridian.Wpf.Models;
using WpfLoggingService = Meridian.Wpf.Services.LoggingService;

namespace Meridian.Wpf.Services;

public sealed class StrategyWorkspaceShellPresentationService : IWorkspaceScopedService
{
    private readonly StrategyRunWorkspaceService _runService;
    private readonly IStrategyBriefingWorkspaceService _briefingService;
    private readonly WatchlistService _watchlistService;
    private readonly FundContextService _fundContextService;
    private readonly WorkstationOperatingContextService? _operatingContextService;
    private readonly WorkspaceShellContextService _shellContextService;
    private readonly WorkstationWorkflowSummaryService? _workflowSummaryService;
    private readonly PromotionService? _promotionService;
    private bool _started;

    public StrategyWorkspaceShellPresentationService(
        StrategyRunWorkspaceService runService,
        IStrategyBriefingWorkspaceService briefingService,
        WatchlistService watchlistService,
        FundContextService fundContextService,
        WorkstationOperatingContextService? operatingContextService,
        WorkspaceShellContextService shellContextService,
        WorkstationWorkflowSummaryService? workflowSummaryService = null,
        PromotionService? promotionService = null)
    {
        _runService = runService ?? throw new ArgumentNullException(nameof(runService));
        _briefingService = briefingService ?? throw new ArgumentNullException(nameof(briefingService));
        _watchlistService = watchlistService ?? throw new ArgumentNullException(nameof(watchlistService));
        _fundContextService = fundContextService ?? throw new ArgumentNullException(nameof(fundContextService));
        _operatingContextService = operatingContextService;
        _shellContextService = shellContextService ?? throw new ArgumentNullException(nameof(shellContextService));
        _workflowSummaryService = workflowSummaryService;
        _promotionService = promotionService;
    }

    public event EventHandler? PresentationInvalidated;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _fundContextService.ActiveFundProfileChanged += OnActiveFundProfileChanged;
        _runService.ActiveRunContextChanged += OnActiveRunContextChanged;
        _watchlistService.WatchlistsChanged += OnWatchlistsChanged;
        _shellContextService.SignalsChanged += OnSignalsChanged;
        if (_operatingContextService is not null)
        {
            _operatingContextService.ActiveContextChanged += OnOperatingContextChanged;
            _operatingContextService.WindowModeChanged += OnSignalsChanged;
        }
    }

    public void Stop()
    {
        if (!_started)
        {
            return;
        }

        _started = false;
        _fundContextService.ActiveFundProfileChanged -= OnActiveFundProfileChanged;
        _runService.ActiveRunContextChanged -= OnActiveRunContextChanged;
        _watchlistService.WatchlistsChanged -= OnWatchlistsChanged;
        _shellContextService.SignalsChanged -= OnSignalsChanged;
        if (_operatingContextService is not null)
        {
            _operatingContextService.ActiveContextChanged -= OnOperatingContextChanged;
            _operatingContextService.WindowModeChanged -= OnSignalsChanged;
        }
    }

    internal async Task<StrategyWorkspaceShellPresentationState> BuildAsync(CancellationToken ct = default)
    {
        var summaryTask = _runService.GetStrategySummaryAsync(ct);
        var activeRunTask = _runService.GetActiveRunContextAsync(ct);
        var briefingTask = _briefingService.GetBriefingAsync(ct);
        var workflowTask = GetStrategyWorkflowSummaryAsync(ct);
        await Task.WhenAll(summaryTask, activeRunTask, briefingTask, workflowTask).ConfigureAwait(false);

        var summary = await summaryTask.ConfigureAwait(false);
        var activeRun = await activeRunTask.ConfigureAwait(false);
        var briefing = await briefingTask.ConfigureAwait(false);
        var workflow = await workflowTask.ConfigureAwait(false)
            ?? StrategyWorkspaceShellPresentationDefaults.Workflow;
        var activeRunPresentation = BuildActiveRunPresentation(activeRun);

        return new StrategyWorkspaceShellPresentationState
        {
            TotalRunsText = summary.TotalRuns.ToString(),
            PromotedText = summary.PromotedCount.ToString(),
            PendingReviewText = summary.PendingReviewCount.ToString(),
            PromotionCountBadgeText = summary.PendingReviewCount.ToString(),
            RecentRuns = summary.RecentRuns,
            PromotionCandidates = summary.PromotionCandidates,
            ActiveRunNameText = activeRunPresentation.Name,
            ActiveRunMetaText = activeRunPresentation.Meta,
            ScenarioStrategyText = activeRunPresentation.ScenarioStrategy,
            ScenarioCoverageText = activeRunPresentation.ScenarioCoverage,
            RunStatusText = activeRunPresentation.RunStatus,
            RunPerformanceText = activeRunPresentation.RunPerformance,
            RunCompareText = activeRunPresentation.RunCompare,
            PortfolioPreviewText = activeRunPresentation.PortfolioPreview,
            LedgerPreviewText = activeRunPresentation.LedgerPreview,
            RiskPreviewText = activeRunPresentation.RiskPreview,
            BriefingSummaryText = briefing.Workspace.Summary,
            BriefingGeneratedText = $"Updated {FormatBriefingTimestamp(briefing.InsightFeed.GeneratedAt)}",
            BriefingInsights = briefing.InsightFeed.Widgets,
            BriefingWatchlists = briefing.Watchlists,
            BriefingWhatChanged = briefing.WhatChanged,
            BriefingAlerts = briefing.Alerts,
            BriefingComparisons = briefing.SavedComparisons,
            ShellContext = await BuildShellContextAsync(summary, activeRun, briefing, ct).ConfigureAwait(false),
            CommandGroup = BuildCommandGroup(activeRun?.CanPromoteToPaper == true, activeRun is not null),
            Workflow = workflow,
            DeskHero = BuildDeskHeroState(summary, activeRun, workflow),
            ActiveRunContext = activeRun
        };
    }

    internal StrategyWorkspaceShellPresentationState BuildDegradedState()
        => new()
        {
            CommandGroup = BuildCommandGroup(canPromoteActiveRun: false, canOpenTradingCockpit: false),
            DeskHero = new StrategyDeskHeroState(
                FocusLabel: "Desk briefing degraded",
                Summary: "Strategy workspace refresh is degraded.",
                Detail: "Run, briefing, workflow, or shell-context state may be stale until the shell refresh succeeds.",
                BadgeText: "Attention",
                BadgeTone: StrategyDeskHeroTone.Warning,
                HandoffTitle: "Reopen a stable strategy surface",
                HandoffDetail: "Use the run browser or backtest page to verify state while the shell briefing recovers.",
                PrimaryActionId: "StrategyRuns",
                PrimaryActionLabel: "Run Browser",
                SecondaryActionId: "Backtest",
                SecondaryActionLabel: "Start Backtest",
                TargetLabel: "Target page: StrategyRuns")
        };

    internal async Task SetActiveRunContextAsync(string? runId, CancellationToken ct = default)
        => await _runService.SetActiveRunContextAsync(runId, ct).ConfigureAwait(false);

    internal async Task<StrategyWorkspaceShellActionRequest> PromoteActiveRunAsync(
        ActiveRunContext? activeRun,
        CancellationToken ct = default)
    {
        if (activeRun is null)
        {
            return default;
        }

        if (_promotionService is null)
        {
            return CreateActionRequest("RunDetail", activeRun);
        }

        try
        {
            var result = await _promotionService.ApproveAsync(new PromotionApprovalRequest(
                    RunId: activeRun.RunId,
                    ReviewNotes: "Promoted from strategy workspace shell.",
                    ApprovedBy: Environment.UserName,
                    ApprovalReason: "Strategy workstation promotion",
                    ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper)),
                ct).ConfigureAwait(false);

            if (result.Success && !string.IsNullOrWhiteSpace(result.NewRunId))
            {
                await _runService.SetActiveRunContextAsync(result.NewRunId, ct).ConfigureAwait(false);
                return new StrategyWorkspaceShellActionRequest(
                    "PromoteToPaper",
                    StrategyWorkspaceShellActionKind.Navigate,
                    "TradingShell",
                    PaneDropAction.Replace,
                    result.NewRunId);
            }

            return CreateActionRequest("RunDetail", activeRun);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            WpfLoggingService.Instance.LogError($"[StrategyWorkspaceShell] Failed to promote active run: {ex.Message}");
            return CreateActionRequest("RunDetail", activeRun);
        }
    }

    internal static StrategyWorkspaceShellActionRequest CreateActionRequest(
        string? actionId,
        ActiveRunContext? activeRun = null)
    {
        if (string.IsNullOrWhiteSpace(actionId))
        {
            return default;
        }

        return actionId switch
        {
            "ResetStudio" => new(actionId, StrategyWorkspaceShellActionKind.ResetLayout, null, PaneDropAction.Replace, null),
            "PromoteToPaper" => activeRun is null
                ? default
                : new(actionId, StrategyWorkspaceShellActionKind.Dock, "RunDetail", PaneDropAction.SplitRight, activeRun.RunId),
            "OpenTradingCockpit" or "TradingShell" => activeRun is null
                ? default
                : new(actionId, StrategyWorkspaceShellActionKind.Navigate, "TradingShell", PaneDropAction.Replace, activeRun.RunId),
            "Backtest" => new(actionId, StrategyWorkspaceShellActionKind.Navigate, "Backtest", PaneDropAction.Replace, null),
            "RunMat" => new(actionId, StrategyWorkspaceShellActionKind.Navigate, "RunMat", PaneDropAction.Replace, null),
            "Charts" => new(actionId, StrategyWorkspaceShellActionKind.Navigate, "Charts", PaneDropAction.Replace, null),
            "StrategyRuns" => new(actionId, StrategyWorkspaceShellActionKind.Navigate, "StrategyRuns", PaneDropAction.Replace, null),
            "Watchlist" => new(actionId, StrategyWorkspaceShellActionKind.Navigate, "Watchlist", PaneDropAction.Replace, null),
            "LeanIntegration" => new(actionId, StrategyWorkspaceShellActionKind.Navigate, "LeanIntegration", PaneDropAction.Replace, null),
            "RunDetail" => new(actionId, StrategyWorkspaceShellActionKind.Dock, "RunDetail", PaneDropAction.SplitRight, activeRun?.RunId),
            "RunPortfolio" => new(actionId, StrategyWorkspaceShellActionKind.Dock, "RunPortfolio", PaneDropAction.SplitBelow, activeRun?.RunId),
            "RunLedger" => new(actionId, StrategyWorkspaceShellActionKind.Dock, "RunLedger", PaneDropAction.OpenTab, activeRun?.RunId),
            "FundTrialBalance" => new(actionId, StrategyWorkspaceShellActionKind.Dock, "FundTrialBalance", PaneDropAction.OpenTab, null),
            "FundReconciliation" => new(actionId, StrategyWorkspaceShellActionKind.Dock, "FundReconciliation", PaneDropAction.SplitBelow, null),
            "FundAuditTrail" => new(actionId, StrategyWorkspaceShellActionKind.Dock, "FundAuditTrail", PaneDropAction.OpenTab, null),
            _ => new(actionId, StrategyWorkspaceShellActionKind.Navigate, actionId, PaneDropAction.Replace, null)
        };
    }

    internal static StrategyWorkspaceShellActionRequest CreateOpenRunStudioActionRequest(string? runId)
        => string.IsNullOrWhiteSpace(runId)
            ? default
            : new StrategyWorkspaceShellActionRequest(
                "OpenRunStudio",
                StrategyWorkspaceShellActionKind.OpenRunStudio,
                null,
                PaneDropAction.Replace,
                runId);

    internal static StrategyWorkspaceShellActionRequest CreateRunReviewActionRequest(string? runId)
        => string.IsNullOrWhiteSpace(runId)
            ? default
            : new StrategyWorkspaceShellActionRequest(
                "ReviewPromotion",
                StrategyWorkspaceShellActionKind.Navigate,
                "RunDetail",
                PaneDropAction.Replace,
                runId);

    internal static StrategyWorkspaceShellActionRequest CreateComparisonActionRequest(string? runId)
        => new(
            "OpenComparison",
            StrategyWorkspaceShellActionKind.Navigate,
            "StrategyRuns",
            PaneDropAction.Replace,
            string.IsNullOrWhiteSpace(runId) ? null : runId);

    internal static WorkspaceCommandGroup BuildCommandGroup(
        bool canPromoteActiveRun = false,
        bool canOpenTradingCockpit = false) =>
        new()
        {
            PrimaryCommands =
            [
                new WorkspaceCommandItem
                {
                    Id = "ResetStudio",
                    Label = "Reset Studio",
                    Description = "Reset the strategy studio layout",
                    ShortcutHint = "Ctrl+R",
                    Glyph = "\uE9D9",
                    Tone = WorkspaceTone.Primary
                },
                new WorkspaceCommandItem
                {
                    Id = "PromoteToPaper",
                    Label = "Promote to Paper",
                    Description = "Promote the selected run",
                    ShortcutHint = "Review",
                    Glyph = "\uE8FB",
                    IsEnabled = canPromoteActiveRun
                },
                new WorkspaceCommandItem
                {
                    Id = "OpenTradingCockpit",
                    Label = "Open Trading Cockpit",
                    Description = "Open the selected run in trading",
                    ShortcutHint = "Handoff",
                    Glyph = "\uE9F5",
                    IsEnabled = canOpenTradingCockpit
                }
            ],
            SecondaryCommands =
            [
                new WorkspaceCommandItem { Id = "Watchlist", Label = "Watchlists", Description = "Open symbol staging watchlists", Glyph = "\uE8D4" },
                new WorkspaceCommandItem { Id = "StrategyRuns", Label = "Run Browser", Description = "Open run browser", Glyph = "\uE8FD" },
                new WorkspaceCommandItem { Id = "RunDetail", Label = "Run Detail", Description = "Open run detail", Glyph = "\uE7C3" },
                new WorkspaceCommandItem { Id = "RunPortfolio", Label = "Portfolio Inspector", Description = "Open portfolio inspector", Glyph = "\uE8B5" },
                new WorkspaceCommandItem { Id = "RunLedger", Label = "Ledger Inspector", Description = "Open ledger inspector", Glyph = "\uEE94" },
                new WorkspaceCommandItem { Id = "FundTrialBalance", Label = "Open Accounting Impact", Description = "Open trial-balance impact view", Glyph = "\uE9D9" },
                new WorkspaceCommandItem { Id = "FundReconciliation", Label = "Review Recon Breaks", Description = "Review reconciliation breaks", Glyph = "\uE895" },
                new WorkspaceCommandItem { Id = "FundAuditTrail", Label = "Open Audit Trail", Description = "Open Accounting audit trail", Glyph = "\uE7BA" },
                new WorkspaceCommandItem { Id = "LeanIntegration", Label = "Lean Integration", Description = "Open Lean integration", Glyph = "\uE943" }
            ]
        };

    internal static StrategyDeskHeroState BuildDeskHeroState(
        StrategyWorkspaceSummary summary,
        ActiveRunContext? activeRun,
        WorkspaceWorkflowSummary? workflow)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var effectiveWorkflow = workflow ?? StrategyWorkspaceShellPresentationDefaults.Workflow;
        if (activeRun is not null && activeRun.CanPromoteToPaper)
        {
            return new StrategyDeskHeroState(
                FocusLabel: "Promotion review",
                Summary: $"{activeRun.StrategyName} is ready for paper handoff.",
                Detail: BuildActiveRunHeroDetail(activeRun),
                BadgeText: "Ready",
                BadgeTone: StrategyDeskHeroTone.Success,
                HandoffTitle: "Carry the run into trading review",
                HandoffDetail: "Open the trading shell with the selected run still attached, then keep portfolio, ledger, and audit evidence visible before approving the next mode.",
                PrimaryActionId: "TradingShell",
                PrimaryActionLabel: "Open Trading Review",
                SecondaryActionId: "PromoteToPaper",
                SecondaryActionLabel: "Promote to Paper",
                TargetLabel: "Target page: TradingShell");
        }

        if (activeRun is not null)
        {
            return new StrategyDeskHeroState(
                FocusLabel: "Selected run",
                Summary: $"{activeRun.StrategyName} is the active Strategy run.",
                Detail: BuildActiveRunHeroDetail(activeRun),
                BadgeText: effectiveWorkflow.PrimaryBlocker.IsBlocking ? "Attention" : "In review",
                BadgeTone: effectiveWorkflow.PrimaryBlocker.IsBlocking ? StrategyDeskHeroTone.Warning : StrategyDeskHeroTone.Info,
                HandoffTitle: "Continue run review",
                HandoffDetail: "Keep run detail, portfolio, and ledger inspectors docked beside the active run before handing it forward.",
                PrimaryActionId: "RunDetail",
                PrimaryActionLabel: "Open Run Detail",
                SecondaryActionId: "RunPortfolio",
                SecondaryActionLabel: "Open Portfolio",
                TargetLabel: "Target page: RunDetail");
        }

        if (summary.PendingReviewCount > 0 ||
            string.Equals(effectiveWorkflow.NextAction.TargetPageTag, "TradingShell", StringComparison.OrdinalIgnoreCase))
        {
            var queueCountLabel = summary.PendingReviewCount == 1
                ? "1 run is waiting for trading review."
                : $"{summary.PendingReviewCount} run(s) are waiting for trading review.";

            return new StrategyDeskHeroState(
                FocusLabel: "Promotion queue",
                Summary: summary.PendingReviewCount > 0 ? queueCountLabel : effectiveWorkflow.StatusLabel,
                Detail: effectiveWorkflow.StatusDetail,
                BadgeText: "Attention",
                BadgeTone: StrategyDeskHeroTone.Warning,
                HandoffTitle: "Stage the next promotion candidate",
                HandoffDetail: "Open the run browser first, choose the candidate with complete evidence attached, then carry it into the trading shell.",
                PrimaryActionId: "StrategyRuns",
                PrimaryActionLabel: "Run Browser",
                SecondaryActionId: "Watchlist",
                SecondaryActionLabel: "Open Watchlists",
                TargetLabel: "Target page: StrategyRuns");
        }

        if (summary.TotalRuns == 0)
        {
            return StrategyWorkspaceShellPresentationDefaults.DeskHero;
        }

        var primaryActionId = ResolveHeroActionId(effectiveWorkflow.NextAction, hasActiveRun: false);
        var secondaryActionId = primaryActionId == "StrategyRuns" ? "RunMat" : "StrategyRuns";

        return new StrategyDeskHeroState(
            FocusLabel: "Strategy cycle",
            Summary: effectiveWorkflow.StatusLabel,
            Detail: effectiveWorkflow.StatusDetail,
            BadgeText: ParseHeroTone(effectiveWorkflow.StatusTone) switch
            {
                StrategyDeskHeroTone.Success => "Ready",
                StrategyDeskHeroTone.Warning => "Attention",
                _ => "Focus"
            },
            BadgeTone: ParseHeroTone(effectiveWorkflow.StatusTone),
            HandoffTitle: "Keep the next action docked",
            HandoffDetail: effectiveWorkflow.NextAction.Detail,
            PrimaryActionId: primaryActionId,
            PrimaryActionLabel: ResolveHeroActionLabel(primaryActionId, effectiveWorkflow.NextAction.Label),
            SecondaryActionId: secondaryActionId,
            SecondaryActionLabel: ResolveHeroActionLabel(secondaryActionId, secondaryActionId),
            TargetLabel: $"Target page: {ResolveHeroTargetLabel(primaryActionId, effectiveWorkflow.NextAction.TargetPageTag)}");
    }

    internal static StrategyDeskHeroTone ParseHeroTone(string? tone) => tone?.ToLowerInvariant() switch
    {
        "success" => StrategyDeskHeroTone.Success,
        "warning" => StrategyDeskHeroTone.Warning,
        "danger" => StrategyDeskHeroTone.Warning,
        _ => StrategyDeskHeroTone.Info
    };

    internal static string ResolveHeroActionLabel(string actionId, string fallbackLabel) => actionId switch
    {
        "Backtest" => "Start Backtest",
        "RunMat" => "Open RunMat",
        "StrategyRuns" => "Run Browser",
        "RunDetail" => "Open Run Detail",
        "RunPortfolio" => "Open Portfolio",
        "Watchlist" => "Open Watchlists",
        "TradingShell" => "Open Trading Review",
        "PromoteToPaper" => "Promote to Paper",
        _ => string.IsNullOrWhiteSpace(fallbackLabel) ? "Open" : fallbackLabel
    };

    internal static string ResolveHeroTargetLabel(string actionId, string fallbackTargetPageTag) => actionId switch
    {
        "Backtest" => "Backtest",
        "RunMat" => "RunMat",
        "StrategyRuns" => "StrategyRuns",
        "RunDetail" => "RunDetail",
        "RunPortfolio" => "RunPortfolio",
        "Watchlist" => "Watchlist",
        "TradingShell" => "TradingShell",
        "PromoteToPaper" => "Promotion approval",
        _ => string.IsNullOrWhiteSpace(fallbackTargetPageTag) ? "Strategy" : fallbackTargetPageTag
    };

    private async Task<WorkspaceWorkflowSummary?> GetStrategyWorkflowSummaryAsync(CancellationToken ct)
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
                    fundDisplayName: _fundContextService.CurrentFundProfile?.DisplayName,
                    ct: ct)
                .ConfigureAwait(false);

            return summary.Workspaces.FirstOrDefault(static workspace =>
                string.Equals(workspace.WorkspaceId, "strategy", StringComparison.OrdinalIgnoreCase)
                || string.Equals(workspace.WorkspaceId, "research", StringComparison.OrdinalIgnoreCase));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private async Task<WorkspaceShellContext> BuildShellContextAsync(
        StrategyWorkspaceSummary summary,
        ActiveRunContext? activeRun,
        StrategyBriefingDto briefing,
        CancellationToken ct)
        => await _shellContextService.CreateAsync(
            BuildShellContextInput(summary, activeRun, briefing),
            ct).ConfigureAwait(false);

    private static WorkspaceShellContextInput BuildShellContextInput(
        StrategyWorkspaceSummary summary,
        ActiveRunContext? activeRun,
        StrategyBriefingDto briefing)
    {
        var promotionCandidates = briefing.Workspace.PromotionCandidates;
        var alertCount = briefing.Alerts.Count;
        var activeSessions = briefing.Workspace.ActiveRuns;

        return new WorkspaceShellContextInput
        {
            WorkspaceTitle = WorkspaceCopyCatalog.Strategy.ShellTitle,
            WorkspaceSubtitle = WorkspaceCopyCatalog.Strategy.ShellSubtitle,
            PrimaryScopeLabel = WorkspaceCopyCatalog.Strategy.PrimaryScopeLabel,
            PrimaryScopeValue = activeRun?.StrategyName
                ?? briefing.Workspace.LatestStrategyName
                ?? "No active Strategy run",
            AsOfValue = briefing.InsightFeed.GeneratedAt.ToString("MMM dd yyyy HH:mm"),
            FreshnessValue = activeRun is null
                ? $"{activeSessions} active session(s)"
                : $"{activeRun.ModeLabel} · {activeRun.StatusLabel}",
            ReviewStateLabel = "Promotion",
            ReviewStateValue = promotionCandidates > 0
                ? $"{promotionCandidates} candidate(s)"
                : "No promotion queue",
            ReviewStateTone = promotionCandidates > 0 ? WorkspaceTone.Warning : WorkspaceTone.Success,
            CriticalLabel = "Alerts",
            CriticalValue = alertCount > 0 ? $"{alertCount} alert(s)" : "No blocking alerts",
            CriticalTone = alertCount > 0 ? WorkspaceTone.Warning : WorkspaceTone.Info,
            AdditionalBadges =
            [
                new WorkspaceShellBadge
                {
                    Label = "Watchlists",
                    Value = briefing.Watchlists.Count > 0 ? $"{briefing.Watchlists.Count} staged" : string.Empty,
                    Glyph = "\uE8D4",
                    Tone = WorkspaceTone.Info
                },
                new WorkspaceShellBadge
                {
                    Label = "Comparisons",
                    Value = briefing.SavedComparisons.Count > 0 ? $"{briefing.SavedComparisons.Count} saved" : string.Empty,
                    Glyph = "\uE9D9",
                    Tone = WorkspaceTone.Neutral
                },
                new WorkspaceShellBadge
                {
                    Label = "Runs",
                    Value = summary.TotalRuns > 0 ? $"{summary.TotalRuns} tracked" : string.Empty,
                    Glyph = "\uE8FD",
                    Tone = WorkspaceTone.Neutral
                }
            ]
        };
    }

    private static StrategyActiveRunPresentation BuildActiveRunPresentation(ActiveRunContext? activeContext)
    {
        if (activeContext is null)
        {
            return StrategyActiveRunPresentation.Empty;
        }

        return new StrategyActiveRunPresentation(
            Name: activeContext.StrategyName,
            Meta: $"{activeContext.ModeLabel} · {activeContext.StatusLabel} · {activeContext.FundScopeLabel}",
            ScenarioStrategy: $"{activeContext.StrategyName} ({activeContext.RunId})",
            ScenarioCoverage: $"Session scope: {activeContext.FundScopeLabel}",
            RunStatus: $"{activeContext.ModeLabel} run selected",
            RunPerformance: activeContext.PortfolioPreview,
            RunCompare: activeContext.RiskSummary,
            PortfolioPreview: activeContext.PortfolioPreview,
            LedgerPreview: $"{activeContext.LedgerPreview} Open accounting impact to verify trial-balance continuity before promotion.",
            RiskPreview: $"{activeContext.RiskSummary} Audit and reconciliation drill-ins stay one action away from the same shell.");
    }

    private static string BuildActiveRunHeroDetail(ActiveRunContext activeRun)
    {
        var validationDetail = string.IsNullOrWhiteSpace(activeRun.ValidationStatus.Detail)
            || string.Equals(activeRun.ValidationStatus.Detail, "Status detail unavailable.", StringComparison.Ordinal)
                ? activeRun.RiskSummary
                : activeRun.ValidationStatus.Detail;
        return $"{activeRun.ModeLabel} · {activeRun.StatusLabel} · {activeRun.FundScopeLabel}. {validationDetail}";
    }

    private static string ResolveHeroActionId(WorkflowNextAction nextAction, bool hasActiveRun)
    {
        if (string.Equals(nextAction.TargetPageTag, "TradingShell", StringComparison.OrdinalIgnoreCase))
        {
            return hasActiveRun ? "TradingShell" : "StrategyRuns";
        }

        return nextAction.TargetPageTag;
    }

    private static string FormatBriefingTimestamp(DateTimeOffset timestamp)
    {
        var span = DateTimeOffset.UtcNow - timestamp;
        if (span.TotalMinutes < 1)
        {
            return "just now";
        }

        if (span.TotalHours < 1)
        {
            return $"{Math.Round(span.TotalMinutes)}m ago";
        }

        if (span.TotalDays < 1)
        {
            return $"{Math.Round(span.TotalHours)}h ago";
        }

        return $"{Math.Round(span.TotalDays)}d ago";
    }

    private void OnActiveFundProfileChanged(object? sender, FundProfileChangedEventArgs e)
        => PresentationInvalidated?.Invoke(this, EventArgs.Empty);

    private void OnSignalsChanged(object? sender, EventArgs e)
        => PresentationInvalidated?.Invoke(this, EventArgs.Empty);

    private void OnWatchlistsChanged(object? sender, WatchlistsChangedEventArgs e)
        => PresentationInvalidated?.Invoke(this, EventArgs.Empty);

    private void OnOperatingContextChanged(object? sender, WorkstationOperatingContextChangedEventArgs e)
        => PresentationInvalidated?.Invoke(this, EventArgs.Empty);

    private void OnActiveRunContextChanged(object? sender, ActiveRunContext? e)
        => PresentationInvalidated?.Invoke(this, EventArgs.Empty);

    private readonly record struct StrategyActiveRunPresentation(
        string Name,
        string Meta,
        string ScenarioStrategy,
        string ScenarioCoverage,
        string RunStatus,
        string RunPerformance,
        string RunCompare,
        string PortfolioPreview,
        string LedgerPreview,
        string RiskPreview)
    {
        public static StrategyActiveRunPresentation Empty { get; } = new(
            Name: "No selected run",
            Meta: "Start a backtest or choose a run from history.",
            ScenarioStrategy: "No strategy selected",
            ScenarioCoverage: "No strategy session restored.",
            RunStatus: "Awaiting run selection",
            RunPerformance: "Compare runs, equity, and fills from a selected strategy run.",
            RunCompare: "Use the bottom history rail to select a run and load detail panels.",
            PortfolioPreview: "Portfolio inspector opens here once a run is selected.",
            LedgerPreview: "Accounting impact preview opens here once a run is selected.",
            RiskPreview: "Risk and audit preview becomes available after a completed run is selected.");
    }
}
