using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services;
using Meridian.Ui.Shared.Services;
using Meridian.Wpf.Models;
using WpfLoggingService = Meridian.Wpf.Services.LoggingService;

namespace Meridian.Wpf.Services;

public sealed class TradingWorkspaceShellPresentationService
{
    private readonly StrategyRunWorkspaceService _runService;
    private readonly FundContextService _fundContextService;
    private readonly WorkstationOperatingContextService? _operatingContextService;
    private readonly CashFinancingReadService _cashFinancingReadService;
    private readonly WorkspaceShellContextService _shellContextService;
    private readonly WorkstationWorkflowSummaryService? _workflowSummaryService;
    private readonly TradingOperatorReadinessService? _operatorReadinessService;
    private bool _started;

    public TradingWorkspaceShellPresentationService(
        StrategyRunWorkspaceService runService,
        FundContextService fundContextService,
        WorkstationOperatingContextService? operatingContextService,
        CashFinancingReadService cashFinancingReadService,
        WorkspaceShellContextService shellContextService,
        WorkstationWorkflowSummaryService? workflowSummaryService = null,
        TradingOperatorReadinessService? operatorReadinessService = null)
    {
        _runService = runService ?? throw new ArgumentNullException(nameof(runService));
        _fundContextService = fundContextService ?? throw new ArgumentNullException(nameof(fundContextService));
        _operatingContextService = operatingContextService;
        _cashFinancingReadService = cashFinancingReadService ?? throw new ArgumentNullException(nameof(cashFinancingReadService));
        _shellContextService = shellContextService ?? throw new ArgumentNullException(nameof(shellContextService));
        _workflowSummaryService = workflowSummaryService;
        _operatorReadinessService = operatorReadinessService;
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
        _shellContextService.SignalsChanged -= OnSignalsChanged;
        if (_operatingContextService is not null)
        {
            _operatingContextService.ActiveContextChanged -= OnOperatingContextChanged;
            _operatingContextService.WindowModeChanged -= OnSignalsChanged;
        }
    }

    internal async Task<TradingWorkspaceShellPresentationState> BuildAsync(CancellationToken ct = default)
    {
        var summaryTask = _runService.GetTradingSummaryAsync(ct);
        var workflowTask = GetTradingWorkflowSummaryAsync(ct);
        var readinessTask = GetTradingOperatorReadinessAsync(ct);
        await Task.WhenAll(summaryTask, workflowTask, readinessTask).ConfigureAwait(false);

        var summary = await summaryTask.ConfigureAwait(false);
        var workflow = await workflowTask.ConfigureAwait(false);
        var readiness = await readinessTask.ConfigureAwait(false);
        var operatingContext = _operatingContextService?.CurrentContext;
        var profile = _fundContextService.CurrentFundProfile;
        var hasOperatingContext = operatingContext is not null || profile is not null;
        var displayName = operatingContext?.DisplayName ?? profile?.DisplayName;
        var activeFund = BuildActiveFundPresentation(operatingContext, profile);
        var activeRun = BuildActiveRunPresentation(summary.ActiveRunContext, summary);
        var capital = await BuildCapitalPresentationAsync(profile, operatingContext, ct).ConfigureAwait(false);
        var statusCard = readiness is null
            ? BuildWorkflowStatusCardPresentation(workflow)
            : BuildOperatorReadinessStatusCardPresentation(readiness);
        var riskRailText = readiness is null
            ? activeRun.RiskRailText
            : BuildReadinessRiskRailText(readiness);
        var deskActionStatusText = readiness is null
            ? activeRun.DeskActionStatusText
            : BuildReadinessDeskActionStatusText(readiness);

        return new TradingWorkspaceShellPresentationState
        {
            ActiveFundText = activeFund.Title,
            ActiveFundDetailText = activeFund.Detail,
            PaperRunsText = summary.PaperRunCount.ToString(),
            LiveRunsText = summary.LiveRunCount.ToString(),
            TotalEquityText = summary.TotalEquityFormatted,
            DrawdownText = summary.MaxDrawdownFormatted,
            PositionLimitText = summary.PositionLimitLabel,
            OrderRateText = summary.OrderRateLabel,
            CapitalCashText = capital.CashText,
            CapitalGrossExposureText = capital.GrossExposureText,
            CapitalNetExposureText = capital.NetExposureText,
            CapitalFinancingText = capital.FinancingText,
            CapitalControlsDetailText = capital.DetailText,
            TradingActiveRunText = activeRun.Title,
            TradingActiveRunMetaText = activeRun.Meta,
            WatchlistStatusText = activeRun.WatchlistStatusText,
            MarketCoreText = activeRun.MarketCoreText,
            RiskRailText = riskRailText,
            DeskActionStatusText = deskActionStatusText,
            ActivePositions = summary.ActivePositions,
            ShellContext = await BuildShellContextAsync(summary, ct).ConfigureAwait(false),
            CommandGroup = BuildCommandGroup(),
            StatusCard = statusCard,
            DeskHero = BuildDeskHeroState(
                summary.ActiveRunContext,
                workflow,
                readiness,
                hasOperatingContext,
                displayName),
            WorkflowNextAction = BuildEffectiveWorkflow(workflow, hasOperatingContext).NextAction,
            ActiveRunContext = summary.ActiveRunContext
        };
    }

    internal TradingWorkspaceShellPresentationState BuildDegradedState()
    {
        return new TradingWorkspaceShellPresentationState
        {
            StatusCard = BuildDegradedStatusCardPresentation(),
            DeskHero = BuildDegradedDeskHeroState(),
            RiskRailText = "Cockpit refresh degraded. Trading posture and broker validation details may be stale until the shell can refresh again.",
            DeskActionStatusText = "Cockpit refresh failed. Recheck desktop API connectivity, run-state services, and broker validation before relying on this shell state.",
            CommandGroup = BuildCommandGroup()
        };
    }

    internal static TradingWorkspaceShellActionRequest CreateActionRequest(
        string? actionId,
        ActiveRunContext? activeRun = null)
    {
        if (string.IsNullOrWhiteSpace(actionId))
        {
            return default;
        }

        return actionId switch
        {
            "SwitchContext" => new(actionId, null, PaneDropAction.Replace, null, false, true, null),
            "Pause" => new(actionId, "PositionBlotter", PaneDropAction.SplitRight, null, false, false, "Pause queued. Review blotter and risk rail before resuming."),
            "Stop" => new(actionId, "RunRisk", PaneDropAction.SplitBelow, null, false, false, "Stop requested. Existing positions remain visible for review."),
            "Flatten" => new(actionId, "OrderBook", PaneDropAction.FloatWindow, null, false, false, "Flatten review opened. Use the blotter and order book to verify exit posture."),
            "CancelAll" => new(actionId, "PositionBlotter", PaneDropAction.FloatWindow, null, false, false, "Cancel-all review opened. Confirm open orders in the blotter."),
            "AcknowledgeRisk" => new(actionId, "RunRisk", PaneDropAction.SplitRight, null, false, false, "Risk acknowledgement captured locally for this workstation session."),
            "LiveData" => new(actionId, "LiveData", PaneDropAction.Replace, null, true, false, null),
            "PositionBlotter" => new(actionId, "PositionBlotter", PaneDropAction.SplitRight, null, false, false, null),
            "RunPortfolio" => CreatePortfolioActionRequest(activeRun),
            "PortfolioImport" => new(actionId, "PortfolioImport", PaneDropAction.Replace, null, true, false, null),
            "OrderBook" => new(actionId, "OrderBook", PaneDropAction.FloatWindow, null, false, false, null),
            "RunRisk" => new(actionId, "RunRisk", PaneDropAction.SplitRight, null, false, false, null),
            "NotificationCenter" => new(actionId, "NotificationCenter", PaneDropAction.SplitBelow, null, false, false, null),
            "FundTrialBalance" => new(actionId, "FundTrialBalance", PaneDropAction.OpenTab, null, false, false, null),
            "FundReconciliation" => new(actionId, "FundReconciliation", PaneDropAction.SplitBelow, null, false, false, null),
            "FundAuditTrail" => new(actionId, "FundAuditTrail", PaneDropAction.OpenTab, null, false, false, null),
            "TradingHours" => new(actionId, "TradingHours", PaneDropAction.Replace, null, true, false, null),
            _ => new(actionId, actionId, PaneDropAction.Replace, null, true, false, null)
        };
    }

    internal static TradingWorkspaceShellActionRequest CreateWorkflowActionRequest(
        WorkflowNextAction? action,
        ActiveRunContext? activeRun)
    {
        if (action is null)
        {
            return default;
        }

        if (string.Equals(action.TargetPageTag, "TradingShell", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(action.Label, "Choose Context", StringComparison.OrdinalIgnoreCase))
        {
            return CreateActionRequest("SwitchContext", activeRun);
        }

        if (string.Equals(action.TargetPageTag, "TradingShell", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(action.Label, "Open Active Cockpit", StringComparison.OrdinalIgnoreCase))
        {
            return CreateActionRequest("RunPortfolio", activeRun);
        }

        return action.TargetPageTag switch
        {
            "StrategyRuns" => new(action.TargetPageTag, "StrategyRuns", PaneDropAction.Replace, null, true, false, null),
            "GovernanceShell" => new(action.TargetPageTag, "GovernanceShell", PaneDropAction.Replace, null, true, false, null),
            "FundTrialBalance" => CreateActionRequest("FundTrialBalance", activeRun),
            "FundReconciliation" => CreateActionRequest("FundReconciliation", activeRun),
            _ => new(action.TargetPageTag, action.TargetPageTag, PaneDropAction.Replace, null, true, false, null)
        };
    }

    internal static TradingPortfolioNavigationTarget ResolvePortfolioNavigationTarget(ActiveRunContext? activeRun)
        => activeRun is null
            ? new TradingPortfolioNavigationTarget("AccountPortfolio", PaneDropAction.Replace, null)
            : new TradingPortfolioNavigationTarget("RunPortfolio", PaneDropAction.SplitLeft, activeRun.RunId);

    internal static Guid? ResolveFundAccountId(WorkstationOperatingContext? context)
    {
        if (context is null)
        {
            return null;
        }

        var accountId = context.AccountId;
        if (string.IsNullOrWhiteSpace(accountId) &&
            context.ScopeKind == OperatingContextScopeKind.Account)
        {
            accountId = context.ScopeId;
        }

        return Guid.TryParse(accountId, out var parsed)
            ? parsed
            : null;
    }

    internal static TradingStatusCardPresentation BuildStatusCardPresentation(TradingWorkspaceSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var promotion = summary.ActiveRunContext?.PromotionStatus ?? summary.PromotionStatus;
        var audit = summary.ActiveRunContext?.AuditStatus ?? summary.AuditStatus;
        var validation = summary.ActiveRunContext?.ValidationStatus ?? summary.ValidationStatus;
        var cardTone = ResolveCardTone(promotion, audit, validation);
        var summaryText = summary.ActiveRunContext is null
            ? "Workspace-level promotion handoff, audit traceability, and control coverage across recorded runs."
            : $"{summary.ActiveRunContext.StrategyName} promotion handoff, audit traceability, and control coverage.";

        return new TradingStatusCardPresentation(
            summaryText,
            GetToneBadgeText(cardTone),
            cardTone,
            promotion,
            audit,
            validation);
    }

    internal static TradingWorkspaceStatusItem BuildReplayStatusItem(TradingOperatorReadinessDto readiness)
    {
        ArgumentNullException.ThrowIfNull(readiness);

        var replayGate = readiness.AcceptanceGates.FirstOrDefault(static gate =>
            string.Equals(gate.GateId, "replay", StringComparison.OrdinalIgnoreCase));

        if (readiness.Replay is null)
        {
            return new TradingWorkspaceStatusItem
            {
                Label = "Replay evidence unavailable",
                Detail = replayGate?.Detail ?? "Paper replay verification has not produced shared readiness evidence yet.",
                Tone = TradingWorkspaceStatusTone.Warning
            };
        }

        var countDetail = BuildReplayCountDetail(readiness.ActiveSession, readiness.Replay);
        if (!readiness.Replay.IsConsistent)
        {
            return new TradingWorkspaceStatusItem
            {
                Label = "Replay mismatch",
                Detail = JoinStatusDetails(
                    readiness.Replay.MismatchReasons.FirstOrDefault() ?? "Paper replay verification recorded a mismatch.",
                    countDetail),
                Tone = TradingWorkspaceStatusTone.Warning
            };
        }

        if (replayGate is { Status: not TradingAcceptanceGateStatusDto.Ready })
        {
            return new TradingWorkspaceStatusItem
            {
                Label = replayGate.Detail.Contains("stale", StringComparison.OrdinalIgnoreCase)
                    ? "Replay stale"
                    : "Replay review required",
                Detail = JoinStatusDetails(replayGate.Detail, countDetail),
                Tone = TradingWorkspaceStatusTone.Warning
            };
        }

        return new TradingWorkspaceStatusItem
        {
            Label = "Replay verified",
            Detail = countDetail,
            Tone = TradingWorkspaceStatusTone.Success
        };
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

    internal static TradingDeskHeroState BuildDeskHeroState(
        ActiveRunContext? activeRun,
        WorkspaceWorkflowSummary? workflow,
        TradingOperatorReadinessDto? readiness,
        bool hasOperatingContext,
        string? operatingContextDisplayName)
    {
        var scopeDisplayName = string.IsNullOrWhiteSpace(operatingContextDisplayName)
            ? "the current trading scope"
            : operatingContextDisplayName;
        var effectiveWorkflow = BuildEffectiveWorkflow(workflow, hasOperatingContext);

        if (!hasOperatingContext)
        {
            return new TradingDeskHeroState(
                FocusLabel: "Context handoff",
                Summary: "Trading review is waiting for an operating context.",
                Detail: effectiveWorkflow.PrimaryBlocker.Detail,
                BadgeText: effectiveWorkflow.StatusLabel,
                BadgeTone: ParseTone(effectiveWorkflow.StatusTone),
                HandoffTitle: "Choose context before desk review",
                HandoffDetail: effectiveWorkflow.NextAction.Detail,
                PrimaryActionId: "SwitchContext",
                PrimaryActionLabel: "Switch Context",
                SecondaryActionId: "StrategyRuns",
                SecondaryActionLabel: "Run Browser",
                TargetLabel: "Target page: Context selector");
        }

        if (readiness is not null)
        {
            if (readiness.Controls.CircuitBreakerOpen)
            {
                return new TradingDeskHeroState(
                    FocusLabel: "Controls",
                    Summary: string.IsNullOrWhiteSpace(readiness.Controls.CircuitBreakerReason)
                        ? "Trading controls are blocking new desk actions."
                        : readiness.Controls.CircuitBreakerReason,
                    Detail: $"{readiness.Controls.ManualOverrideCount} manual override(s) and {readiness.Controls.SymbolLimitCount} symbol limit(s) remain visible for {scopeDisplayName}.",
                    BadgeText: "Attention",
                    BadgeTone: TradingWorkspaceStatusTone.Warning,
                    HandoffTitle: "Review controls before next order flow",
                    HandoffDetail: "Keep the risk rail and audit trail visible until the circuit breaker and override posture are understood.",
                    PrimaryActionId: "RunRisk",
                    PrimaryActionLabel: "Open Risk Rail",
                    SecondaryActionId: "FundAuditTrail",
                    SecondaryActionLabel: "Audit Trail",
                    TargetLabel: "Target page: RunRisk");
            }

            if (readiness.Controls.UnexplainedEvidenceCount > 0)
            {
                return new TradingDeskHeroState(
                    FocusLabel: "Controls",
                    Summary: readiness.Controls.ExplainabilityWarnings.FirstOrDefault()
                        ?? "Risk/control audit evidence is missing actor, scope, or rationale.",
                    Detail: $"{readiness.Controls.UnexplainedEvidenceCount} evidence item(s) need review before {scopeDisplayName} can be treated as explainable.",
                    BadgeText: "Attention",
                    BadgeTone: TradingWorkspaceStatusTone.Warning,
                    HandoffTitle: "Complete risk evidence",
                    HandoffDetail: "Open the audit trail first, then confirm the risk rail matches the recorded actor, scope, and rationale.",
                    PrimaryActionId: "FundAuditTrail",
                    PrimaryActionLabel: "Audit Trail",
                    SecondaryActionId: "RunRisk",
                    SecondaryActionLabel: "Open Risk Rail",
                    TargetLabel: "Target page: FundAuditTrail");
            }

            if (readiness.Replay is { IsConsistent: false } replay)
            {
                return new TradingDeskHeroState(
                    FocusLabel: "Replay",
                    Summary: replay.MismatchReasons.FirstOrDefault() ?? "Paper replay verification recorded a mismatch.",
                    Detail: $"{replay.ComparedOrderCount} order(s), {replay.ComparedFillCount} fill(s), and {replay.ComparedLedgerEntryCount} ledger entry(s) were compared before the mismatch was recorded.",
                    BadgeText: "Attention",
                    BadgeTone: TradingWorkspaceStatusTone.Warning,
                    HandoffTitle: "Verify replay evidence before promotion or live handling",
                    HandoffDetail: "Use the audit trail first, then clear supporting alerts before treating the desk as operator-ready.",
                    PrimaryActionId: "FundAuditTrail",
                    PrimaryActionLabel: "Audit Trail",
                    SecondaryActionId: "NotificationCenter",
                    SecondaryActionLabel: "Open Alerts",
                    TargetLabel: "Target page: FundAuditTrail");
            }

            if (readiness.Warnings.Count > 0)
            {
                return new TradingDeskHeroState(
                    FocusLabel: "Operator attention",
                    Summary: readiness.Warnings[0],
                    Detail: readiness.ActiveSession is null
                        ? "Shared readiness evidence is still reporting warnings for this desk scope."
                        : $"Paper session {readiness.ActiveSession.SessionId} still needs operator review before the desk can be treated as ready.",
                    BadgeText: "Attention",
                    BadgeTone: TradingWorkspaceStatusTone.Warning,
                    HandoffTitle: "Clear readiness warnings",
                    HandoffDetail: "Open alerts first, then confirm audit evidence and controls before moving deeper into the desk.",
                    PrimaryActionId: "NotificationCenter",
                    PrimaryActionLabel: "Open Alerts",
                    SecondaryActionId: "FundAuditTrail",
                    SecondaryActionLabel: "Audit Trail",
                    TargetLabel: "Target page: NotificationCenter");
            }

            if (GetPrimaryAttentionWorkItem(readiness.WorkItems) is { } workItem)
            {
                return BuildWorkItemReviewDeskHeroState(workItem, scopeDisplayName);
            }

            if (!IsOperatorReady(readiness))
            {
                return BuildReadinessReviewDeskHeroState(readiness, scopeDisplayName);
            }
        }

        if (activeRun is null)
        {
            var primaryActionId = ResolveWorkflowHeroActionId(effectiveWorkflow.NextAction, hasActiveRun: false);
            return new TradingDeskHeroState(
                FocusLabel: "Promotion handoff",
                Summary: $"{scopeDisplayName} is ready for a trading run selection.",
                Detail: effectiveWorkflow.StatusDetail,
                BadgeText: effectiveWorkflow.StatusLabel,
                BadgeTone: ParseTone(effectiveWorkflow.StatusTone),
                HandoffTitle: effectiveWorkflow.NextAction.Label,
                HandoffDetail: effectiveWorkflow.NextAction.Detail,
                PrimaryActionId: primaryActionId,
                PrimaryActionLabel: ResolveHeroActionLabel(primaryActionId, effectiveWorkflow.NextAction.Label),
                SecondaryActionId: "LiveData",
                SecondaryActionLabel: "Open Live Data",
                TargetLabel: $"Target page: {ResolveHeroTargetLabel(primaryActionId, effectiveWorkflow.NextAction.TargetPageTag)}");
        }

        var isLiveMode = IsLiveMode(activeRun);
        var hasOperatorReadyLane = readiness is not null && IsOperatorReady(readiness);
        var badgeTone = hasOperatorReadyLane
            ? TradingWorkspaceStatusTone.Success
            : ResolveCardTone(activeRun.PromotionStatus, activeRun.AuditStatus, activeRun.ValidationStatus);
        var detail = hasOperatorReadyLane
            ? BuildReadyDeskHeroDetail(readiness!, activeRun.ValidationStatus.Detail)
            : activeRun.ValidationStatus.Detail;
        var secondaryActionId = activeRun.AuditStatus.Tone == TradingWorkspaceStatusTone.Warning
            ? "FundAuditTrail"
            : "RunRisk";

        return new TradingDeskHeroState(
            FocusLabel: isLiveMode ? "Live oversight" : "Paper review",
            Summary: $"{activeRun.StrategyName} is the active {(isLiveMode ? "live" : "paper")} handoff for {scopeDisplayName}.",
            Detail: detail,
            BadgeText: GetToneBadgeText(badgeTone),
            BadgeTone: badgeTone,
            HandoffTitle: isLiveMode ? "Open active desk" : "Open review desk",
            HandoffDetail: isLiveMode
                ? "Use the blotter first, then keep the risk rail docked beside live positions and alerts."
                : "Review portfolio, blotter, and risk posture together before moving the run forward.",
            PrimaryActionId: isLiveMode ? "PositionBlotter" : "RunPortfolio",
            PrimaryActionLabel: isLiveMode ? "Open Blotter" : "Open Portfolio",
            SecondaryActionId: secondaryActionId,
            SecondaryActionLabel: secondaryActionId == "FundAuditTrail" ? "Audit Trail" : "Open Risk Rail",
            TargetLabel: $"Target page: {(isLiveMode ? "PositionBlotter" : "RunPortfolio")}");
    }

    internal static string ResolveOperatorWorkItemActionId(OperatorWorkItemDto workItem)
    {
        var routeActionId = ResolveOperatorWorkItemRouteActionId(workItem.TargetRoute);
        if (!string.IsNullOrWhiteSpace(routeActionId))
        {
            return routeActionId;
        }

        if (!string.IsNullOrWhiteSpace(workItem.TargetPageTag) &&
            !IsWorkspaceShellTag(workItem.TargetPageTag))
        {
            return workItem.TargetPageTag;
        }

        return workItem.Kind switch
        {
            OperatorWorkItemKindDto.PaperReplay => "FundAuditTrail",
            OperatorWorkItemKindDto.PromotionReview => "StrategyRuns",
            OperatorWorkItemKindDto.BrokerageSync => "AccountPortfolio",
            OperatorWorkItemKindDto.SecurityMasterCoverage => "SecurityMaster",
            OperatorWorkItemKindDto.ReconciliationBreak => "FundReconciliation",
            OperatorWorkItemKindDto.ReportPackApproval => "FundReportPack",
            OperatorWorkItemKindDto.ProviderTrustGate => "FundAuditTrail",
            OperatorWorkItemKindDto.ExecutionControl => "RunRisk",
            _ => "NotificationCenter"
        };
    }

    internal static TradingDeskHeroState BuildDegradedDeskHeroState() =>
        new(
            FocusLabel: "Desk briefing degraded",
            Summary: "Trading cockpit refresh is degraded.",
            Detail: "Shared workflow, readiness, and active-run posture may be stale until the shell refresh succeeds.",
            BadgeText: "Attention",
            BadgeTone: TradingWorkspaceStatusTone.Warning,
            HandoffTitle: "Reopen a stable trading surface",
            HandoffDetail: "Use the run browser, blotter, or risk rail to verify state until the cockpit refresh recovers.",
            PrimaryActionId: "StrategyRuns",
            PrimaryActionLabel: "Run Browser",
            SecondaryActionId: "RunRisk",
            SecondaryActionLabel: "Open Risk Rail",
            TargetLabel: "Target page: StrategyRuns");

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

    private async Task<TradingOperatorReadinessDto?> GetTradingOperatorReadinessAsync(CancellationToken ct)
    {
        if (_operatorReadinessService is null)
        {
            return null;
        }

        try
        {
            return await _operatorReadinessService
                .GetAsync(ResolveFundAccountId(_operatingContextService?.CurrentContext), ct)
                .ConfigureAwait(false);
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

    private async Task<WorkspaceWorkflowSummary?> GetTradingWorkflowSummaryAsync(CancellationToken ct)
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
                string.Equals(workspace.WorkspaceId, "trading", StringComparison.OrdinalIgnoreCase));
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
        TradingWorkspaceSummary summary,
        CancellationToken ct)
        => await _shellContextService.CreateAsync(new WorkspaceShellContextInput
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
        }, ct).ConfigureAwait(false);

    private async Task<CapitalPresentation> BuildCapitalPresentationAsync(
        FundProfileDetail? profile,
        WorkstationOperatingContext? operatingContext,
        CancellationToken ct)
    {
        if (profile is null)
        {
            return new CapitalPresentation(
                "-",
                "-",
                "-",
                "-",
                operatingContext is { } context
                    ? $"Switch to a fund-linked accounting view to unlock capital and reconciliation posture for {context.DisplayName}."
                    : "Select an operating context to unlock capital, financing, and reconciliation posture.");
        }

        var capitalSummary = await _cashFinancingReadService
            .GetAsync(profile.FundProfileId, profile.BaseCurrency, ct)
            .ConfigureAwait(false);

        return new CapitalPresentation(
            capitalSummary.TotalCash.ToString("C0"),
            capitalSummary.GrossExposure.ToString("C0"),
            capitalSummary.NetExposure.ToString("C0"),
            capitalSummary.FinancingCost.ToString("C0"),
            capitalSummary.Highlights.FirstOrDefault()
                ?? "Capital and financing posture is available for the active fund.");
    }

    private static ActiveFundPresentation BuildActiveFundPresentation(
        WorkstationOperatingContext? operatingContext,
        FundProfileDetail? profile)
    {
        if (operatingContext is not null)
        {
            return new ActiveFundPresentation(operatingContext.DisplayName, operatingContext.Subtitle);
        }

        if (profile is null)
        {
            return new ActiveFundPresentation(
                "No operating context selected",
                "Runs, allocations, and accounting posture scope to the active operating context.");
        }

        return new ActiveFundPresentation(profile.DisplayName, $"{profile.LegalEntityName} · {profile.BaseCurrency}");
    }

    private static ActiveRunPresentation BuildActiveRunPresentation(
        ActiveRunContext? activeRun,
        TradingWorkspaceSummary? summary)
    {
        if (activeRun is null)
        {
            return new ActiveRunPresentation(
                "No active trading run",
                "Use Research to promote a run, or open a live/paper panel below.",
                "Watchlists and active strategies populate once paper or live runs are started.",
                "Live data, order book, portfolio, and accounting consequences are ready to dock below.",
                summary is null
                    ? "Risk, reconciliation, and audit surfaces become specific once an active run is selected."
                    : $"{summary.ValidationStatus.Label}: {summary.ValidationStatus.Detail}",
                summary?.ValidationStatus.Detail
                    ?? "Broker validation and promotion readiness appear here once a run is active.");
        }

        return new ActiveRunPresentation(
            activeRun.StrategyName,
            $"{activeRun.ModeLabel} · {activeRun.StatusLabel} · {activeRun.FundScopeLabel}",
            activeRun.PortfolioPreview,
            $"{activeRun.LedgerPreview} {activeRun.AuditStatus.Detail}",
            $"{activeRun.RiskSummary} {activeRun.ValidationStatus.Label}: {activeRun.ValidationStatus.Detail}",
            activeRun.ValidationStatus.Detail);
    }

    private static TradingStatusCardPresentation BuildWorkflowStatusCardPresentation(WorkspaceWorkflowSummary? workflow)
    {
        var effectiveWorkflow = BuildEffectiveWorkflow(workflow, hasOperatingContext: true);
        return new TradingStatusCardPresentation(
            effectiveWorkflow.StatusDetail,
            effectiveWorkflow.StatusLabel,
            ParseTone(effectiveWorkflow.StatusTone),
            new TradingWorkspaceStatusItem
            {
                Label = effectiveWorkflow.StatusLabel,
                Detail = effectiveWorkflow.StatusDetail,
                Tone = ParseTone(effectiveWorkflow.StatusTone)
            },
            new TradingWorkspaceStatusItem
            {
                Label = effectiveWorkflow.PrimaryBlocker.Label,
                Detail = effectiveWorkflow.PrimaryBlocker.Detail,
                Tone = ParseTone(effectiveWorkflow.PrimaryBlocker.Tone)
            },
            new TradingWorkspaceStatusItem
            {
                Label = effectiveWorkflow.NextAction.Label,
                Detail = effectiveWorkflow.NextAction.Detail,
                Tone = ParseTone(effectiveWorkflow.NextAction.Tone)
            });
    }

    private static TradingStatusCardPresentation BuildOperatorReadinessStatusCardPresentation(
        TradingOperatorReadinessDto readiness)
    {
        var isOperatorReady = IsOperatorReady(readiness)
            && readiness.Warnings.Count == 0
            && readiness.WorkItems.All(static item => item.Tone is not OperatorWorkItemToneDto.Warning and not OperatorWorkItemToneDto.Critical);
        var badgeTone = isOperatorReady ? TradingWorkspaceStatusTone.Success : TradingWorkspaceStatusTone.Warning;

        return new TradingStatusCardPresentation(
            BuildSessionReadinessSummary(readiness),
            isOperatorReady ? "Ready" : "Attention",
            badgeTone,
            new TradingWorkspaceStatusItem
            {
                Label = readiness.Promotion?.State ?? "Promotion decision required",
                Detail = readiness.Promotion?.Reason ?? "Promotion posture has not been projected into shared readiness yet.",
                Tone = readiness.Promotion?.RequiresReview == true ? TradingWorkspaceStatusTone.Warning : TradingWorkspaceStatusTone.Info
            },
            new TradingWorkspaceStatusItem
            {
                Label = readiness.Controls.CircuitBreakerOpen
                    ? "Controls blocked"
                    : readiness.Controls.UnexplainedEvidenceCount > 0 ? "Evidence incomplete" : "Controls ready",
                Detail = BuildControlReadinessDetail(readiness.Controls),
                Tone = readiness.Controls.CircuitBreakerOpen || readiness.Controls.UnexplainedEvidenceCount > 0
                    ? TradingWorkspaceStatusTone.Warning
                    : TradingWorkspaceStatusTone.Success
            },
            BuildReplayStatusItem(readiness));
    }

    private static string BuildReadinessRiskRailText(TradingOperatorReadinessDto readiness)
    {
        if (readiness.Warnings.Count > 0)
        {
            return readiness.Warnings[0];
        }

        if (readiness.Controls.RecentEvidence.Count > 0)
        {
            return $"Latest risk/control evidence: {BuildControlEvidenceSummary(readiness.Controls.RecentEvidence[0])}";
        }

        return readiness.TrustGate.ReadyForOperatorReview
            ? $"DK1 trust gate {readiness.TrustGate.Status}; {readiness.TrustGate.Detail}"
            : "Paper session, controls, brokerage sync, and Security Master coverage are aligned for operator review.";
    }

    private static string BuildReadinessDeskActionStatusText(TradingOperatorReadinessDto readiness)
    {
        if (readiness.BrokerageSync is null)
        {
            return "Brokerage sync evidence is unavailable. Portfolio and cash continuity are based on local paper and ledger state only.";
        }

        var syncAsOf = readiness.BrokerageSync.LastSuccessfulSyncAt is { } successfulSync
            ? successfulSync.ToLocalTime().ToString("MMM dd HH:mm")
            : "never";
        return $"Brokerage sync {readiness.BrokerageSync.Health.ToString().ToLowerInvariant()} as of {syncAsOf}; {readiness.BrokerageSync.PositionCount} position(s), {readiness.BrokerageSync.OpenOrderCount} open order(s).";
    }

    private static WorkspaceWorkflowSummary BuildEffectiveWorkflow(
        WorkspaceWorkflowSummary? workflow,
        bool hasOperatingContext)
        => workflow ?? new WorkspaceWorkflowSummary(
            WorkspaceId: "trading",
            WorkspaceTitle: "Trading",
            StatusLabel: hasOperatingContext ? "Fallback trading guidance" : "Context required",
            StatusDetail: hasOperatingContext
                ? "Open the cockpit or review recorded runs from the trading workspace."
                : "Choose the active context before relying on trading posture.",
            StatusTone: hasOperatingContext ? "Info" : "Warning",
            NextAction: new WorkflowNextAction(
                Label: hasOperatingContext ? "Open Strategy Runs" : "Choose Context",
                Detail: hasOperatingContext
                    ? "Review recorded runs and select the desk handoff that should stay active."
                    : "Select the active operating context before opening trading reviews.",
                TargetPageTag: hasOperatingContext ? "StrategyRuns" : "TradingShell",
                Tone: "Primary"),
            PrimaryBlocker: new WorkflowBlockerSummary(
                Code: hasOperatingContext ? "fallback" : "choose-context",
                Label: hasOperatingContext ? "Workflow summary unavailable" : "No operating context selected",
                Detail: hasOperatingContext
                    ? "Fallback guidance keeps one stable desk action visible while shared workflow data refreshes."
                    : "Paper review, live posture, and governance-linked trading actions scope to the active operating context.",
                Tone: hasOperatingContext ? "Info" : "Warning",
                IsBlocking: !hasOperatingContext),
            Evidence: []);

    private static TradingWorkspaceShellActionRequest CreatePortfolioActionRequest(ActiveRunContext? activeRun)
    {
        var target = ResolvePortfolioNavigationTarget(activeRun);
        return new TradingWorkspaceShellActionRequest(
            "RunPortfolio",
            target.PageTag,
            target.Action,
            target.RunId,
            false,
            false,
            null);
    }

    private static TradingDeskHeroState BuildWorkItemReviewDeskHeroState(
        OperatorWorkItemDto workItem,
        string scopeDisplayName)
    {
        var primaryActionId = ResolveOperatorWorkItemActionId(workItem);
        var secondaryActionId = primaryActionId == "FundAuditTrail"
            ? "NotificationCenter"
            : "FundAuditTrail";

        return new TradingDeskHeroState(
            FocusLabel: workItem.Tone == OperatorWorkItemToneDto.Critical
                ? "Readiness blocked"
                : "Operator queue",
            Summary: workItem.Detail,
            Detail: $"{workItem.Label} is still open for {scopeDisplayName}; resolve it before the desk can be shown as ready.",
            BadgeText: "Attention",
            BadgeTone: TradingWorkspaceStatusTone.Warning,
            HandoffTitle: workItem.Label,
            HandoffDetail: "Open the routed queue item, then return to Trading once shared readiness has no warning or critical work items.",
            PrimaryActionId: primaryActionId,
            PrimaryActionLabel: ResolveHeroActionLabel(primaryActionId, workItem.Label),
            SecondaryActionId: secondaryActionId,
            SecondaryActionLabel: ResolveHeroActionLabel(secondaryActionId, "Audit Trail"),
            TargetLabel: $"Target page: {ResolveHeroTargetLabel(primaryActionId, primaryActionId)}");
    }

    private static TradingDeskHeroState BuildReadinessReviewDeskHeroState(
        TradingOperatorReadinessDto readiness,
        string scopeDisplayName)
    {
        var reviewGate = readiness.AcceptanceGates.FirstOrDefault(static gate =>
            gate.Status != TradingAcceptanceGateStatusDto.Ready);
        var summary = reviewGate?.Detail
            ?? readiness.Warnings.FirstOrDefault()
            ?? "Shared trading readiness is still under operator review.";
        var gateLabel = reviewGate?.Label ?? "Operator readiness";
        var primaryActionId = ResolveReadinessReviewActionId(reviewGate?.GateId);
        var secondaryActionId = primaryActionId == "FundAuditTrail"
            ? "NotificationCenter"
            : "FundAuditTrail";

        return new TradingDeskHeroState(
            FocusLabel: readiness.OverallStatus == TradingAcceptanceGateStatusDto.Blocked
                ? "Readiness blocked"
                : "Operator review",
            Summary: summary,
            Detail: $"{gateLabel} must be accepted before {scopeDisplayName} can be shown as ready.",
            BadgeText: "Attention",
            BadgeTone: TradingWorkspaceStatusTone.Warning,
            HandoffTitle: $"Complete {gateLabel.ToLowerInvariant()}",
            HandoffDetail: "Use the shared readiness lane first, then return to the desk once the acceptance gate is green.",
            PrimaryActionId: primaryActionId,
            PrimaryActionLabel: ResolveHeroActionLabel(primaryActionId, "Open Review"),
            SecondaryActionId: secondaryActionId,
            SecondaryActionLabel: ResolveHeroActionLabel(secondaryActionId, "Audit Trail"),
            TargetLabel: $"Target page: {ResolveHeroTargetLabel(primaryActionId, primaryActionId)}");
    }

    private static string ResolveReadinessReviewActionId(string? gateId) => gateId switch
    {
        "session" => "StrategyRuns",
        "promotion" => "StrategyRuns",
        "audit-controls" => "RunRisk",
        "replay" => "FundAuditTrail",
        "dk1-trust" => "FundAuditTrail",
        _ => "NotificationCenter"
    };

    private static OperatorWorkItemDto? GetPrimaryAttentionWorkItem(IReadOnlyList<OperatorWorkItemDto> workItems)
        => workItems
            .Where(static item => item.Tone is OperatorWorkItemToneDto.Critical or OperatorWorkItemToneDto.Warning)
            .OrderByDescending(static item => item.Tone)
            .ThenByDescending(static item => item.CreatedAt)
            .ThenBy(static item => item.WorkItemId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

    private static string? ResolveOperatorWorkItemRouteActionId(string? targetRoute)
    {
        if (string.IsNullOrWhiteSpace(targetRoute))
        {
            return null;
        }

        var normalizedRoute = targetRoute.Split('?', 2)[0].TrimEnd('/');
        if (RouteEqualsOrStartsWith(normalizedRoute, UiApiRoutes.ReconciliationBreakQueue))
        {
            return "FundReconciliation";
        }

        if (RouteEqualsOrStartsWith(normalizedRoute, UiApiRoutes.WorkstationSecurityMasterSearch))
        {
            return "SecurityMaster";
        }

        if (RouteEqualsOrStartsWith(normalizedRoute, UiApiRoutes.FundAccountBrokerageSyncAccounts) ||
            normalizedRoute.Contains("/brokerage-sync", StringComparison.OrdinalIgnoreCase))
        {
            return "AccountPortfolio";
        }

        return null;
    }

    private static bool RouteEqualsOrStartsWith(string route, string knownRoute)
    {
        var normalizedKnownRoute = knownRoute.TrimEnd('/');
        return string.Equals(route, normalizedKnownRoute, StringComparison.OrdinalIgnoreCase) ||
               route.StartsWith($"{normalizedKnownRoute}/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWorkspaceShellTag(string pageTag)
        => string.Equals(pageTag, "ResearchShell", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(pageTag, "TradingShell", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(pageTag, "DataOperationsShell", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(pageTag, "GovernanceShell", StringComparison.OrdinalIgnoreCase);

    private static string BuildReadyDeskHeroDetail(TradingOperatorReadinessDto readiness, string fallbackDetail)
    {
        var controlEvidence = readiness.Controls.RecentEvidence.Count > 0
            ? $" Latest risk/control evidence: {BuildControlEvidenceSummary(readiness.Controls.RecentEvidence[0])}"
            : string.Empty;

        if (readiness.BrokerageSync is null)
        {
            return string.IsNullOrWhiteSpace(readiness.TrustGate.Detail)
                ? $"{fallbackDetail}{controlEvidence}"
                : $"{readiness.TrustGate.Detail} Brokerage sync evidence is unavailable; verify the desk state before relying on local posture alone.{controlEvidence}";
        }

        var syncAsOf = readiness.BrokerageSync.LastSuccessfulSyncAt is { } successfulSync
            ? successfulSync.ToLocalTime().ToString("MMM dd HH:mm")
            : "never";
        var trustDetail = string.IsNullOrWhiteSpace(readiness.TrustGate.Detail)
            ? fallbackDetail
            : readiness.TrustGate.Detail;
        return $"{trustDetail} Brokerage sync {readiness.BrokerageSync.Health.ToString().ToLowerInvariant()} as of {syncAsOf} with {readiness.BrokerageSync.PositionCount} position(s) and {readiness.BrokerageSync.OpenOrderCount} open order(s).{controlEvidence}";
    }

    private static string BuildSessionReadinessSummary(TradingOperatorReadinessDto readiness)
    {
        if (readiness.ActiveSession is null)
        {
            return "No active paper session is ready for operator acceptance.";
        }

        var session = readiness.ActiveSession;
        var activeCounts = $"{session.OrderCount} order(s), {session.FillCount} fill(s), and {session.LedgerEntryCount} ledger entry(s)";
        if (readiness.Replay is null)
        {
            return $"Paper session {session.SessionId} has {activeCounts}; replay verification is still missing.";
        }

        return $"Paper session {session.SessionId} has {activeCounts}; replay verified {readiness.Replay.ComparedOrderCount} order(s), {readiness.Replay.ComparedFillCount} fill(s), and {readiness.Replay.ComparedLedgerEntryCount} ledger entry(s).";
    }

    private static string BuildReplayCountDetail(
        TradingPaperSessionReadinessDto? activeSession,
        TradingReplayReadinessDto replay)
    {
        if (activeSession is null)
        {
            return $"Replay verified {replay.ComparedOrderCount} order(s), {replay.ComparedFillCount} fill(s), and {replay.ComparedLedgerEntryCount} ledger entry(s).";
        }

        return $"Active session has {activeSession.OrderCount} order(s), {activeSession.FillCount} fill(s), and {activeSession.LedgerEntryCount} ledger entry(s); replay verified {replay.ComparedOrderCount} order(s), {replay.ComparedFillCount} fill(s), and {replay.ComparedLedgerEntryCount} ledger entry(s).";
    }

    private static string JoinStatusDetails(string first, string second)
    {
        if (string.IsNullOrWhiteSpace(first))
        {
            return second;
        }

        if (string.IsNullOrWhiteSpace(second))
        {
            return first;
        }

        return $"{first.Trim()} {second.Trim()}";
    }

    private static string BuildControlReadinessDetail(TradingControlReadinessDto controls)
    {
        if (controls.CircuitBreakerOpen)
        {
            return controls.CircuitBreakerReason ?? "Execution is blocked by an operator control.";
        }

        if (controls.ExplainabilityWarnings.Count > 0)
        {
            return controls.ExplainabilityWarnings[0];
        }

        if (controls.RecentEvidence.Count > 0)
        {
            return BuildControlEvidenceSummary(controls.RecentEvidence[0]);
        }

        return $"{controls.SymbolLimitCount} symbol limit(s), {controls.ManualOverrideCount} manual override(s).";
    }

    private static string BuildControlEvidenceSummary(TradingControlEvidenceDto evidence)
    {
        var actor = string.IsNullOrWhiteSpace(evidence.Actor) ? "unknown actor" : evidence.Actor;
        return $"{evidence.Action} by {actor} on {evidence.Scope}: {evidence.Reason}";
    }

    private static string ResolveWorkflowHeroActionId(WorkflowNextAction nextAction, bool hasActiveRun)
    {
        if (string.Equals(nextAction.TargetPageTag, "TradingShell", StringComparison.OrdinalIgnoreCase))
        {
            return hasActiveRun ? "PositionBlotter" : "StrategyRuns";
        }

        return nextAction.TargetPageTag;
    }

    private static string ResolveHeroActionLabel(string actionId, string fallbackLabel) => actionId switch
    {
        "SwitchContext" => "Switch Context",
        "StrategyRuns" => "Run Browser",
        "LiveData" => "Open Live Data",
        "PositionBlotter" => "Open Blotter",
        "RunPortfolio" => "Open Portfolio",
        "RunRisk" => "Open Risk Rail",
        "FundAuditTrail" => "Audit Trail",
        "NotificationCenter" => "Open Alerts",
        "AccountPortfolio" => "Open Portfolio",
        "SecurityMaster" => "Security Master",
        "FundReconciliation" => "Review Breaks",
        "FundReportPack" => "Report Pack",
        "FundTrialBalance" => "Open Accounting",
        _ => string.IsNullOrWhiteSpace(fallbackLabel) ? "Open" : fallbackLabel
    };

    private static string ResolveHeroTargetLabel(string actionId, string fallbackTargetPageTag) => actionId switch
    {
        "SwitchContext" => "Context selector",
        "StrategyRuns" => "StrategyRuns",
        "LiveData" => "LiveData",
        "PositionBlotter" => "PositionBlotter",
        "RunPortfolio" => "RunPortfolio",
        "RunRisk" => "RunRisk",
        "FundAuditTrail" => "FundAuditTrail",
        "NotificationCenter" => "NotificationCenter",
        "AccountPortfolio" => "AccountPortfolio",
        "SecurityMaster" => "SecurityMaster",
        "FundReconciliation" => "FundReconciliation",
        "FundReportPack" => "FundReportPack",
        "FundTrialBalance" => "FundTrialBalance",
        _ => string.IsNullOrWhiteSpace(fallbackTargetPageTag) ? "TradingShell" : fallbackTargetPageTag
    };

    private static bool IsLiveMode(ActiveRunContext activeRun)
        => activeRun.ModeLabel.Contains("live", StringComparison.OrdinalIgnoreCase);

    private static bool IsOperatorReady(TradingOperatorReadinessDto readiness) =>
        readiness.ReadyForPaperOperation &&
        readiness.OverallStatus == TradingAcceptanceGateStatusDto.Ready &&
        GetPrimaryAttentionWorkItem(readiness.WorkItems) is null;

    private static string GetToneBadgeText(TradingWorkspaceStatusTone tone) => tone switch
    {
        TradingWorkspaceStatusTone.Success => "Ready",
        TradingWorkspaceStatusTone.Warning => "Attention",
        _ => "Info"
    };

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

    private static TradingWorkspaceStatusTone ParseTone(string? tone) => tone?.ToLowerInvariant() switch
    {
        "success" => TradingWorkspaceStatusTone.Success,
        "warning" => TradingWorkspaceStatusTone.Warning,
        "danger" => TradingWorkspaceStatusTone.Warning,
        _ => TradingWorkspaceStatusTone.Info
    };

    private void OnActiveFundProfileChanged(object? sender, FundProfileChangedEventArgs e)
        => PresentationInvalidated?.Invoke(this, EventArgs.Empty);

    private void OnActiveRunContextChanged(object? sender, ActiveRunContext? e)
        => PresentationInvalidated?.Invoke(this, EventArgs.Empty);

    private void OnSignalsChanged(object? sender, EventArgs e)
        => PresentationInvalidated?.Invoke(this, EventArgs.Empty);

    private void OnOperatingContextChanged(object? sender, WorkstationOperatingContextChangedEventArgs e)
        => PresentationInvalidated?.Invoke(this, EventArgs.Empty);

    private readonly record struct ActiveFundPresentation(string Title, string Detail);

    private readonly record struct ActiveRunPresentation(
        string Title,
        string Meta,
        string WatchlistStatusText,
        string MarketCoreText,
        string RiskRailText,
        string DeskActionStatusText);

    private readonly record struct CapitalPresentation(
        string CashText,
        string GrossExposureText,
        string NetExposureText,
        string FinancingText,
        string DetailText);
}
