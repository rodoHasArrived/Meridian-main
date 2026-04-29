using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Workflows;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Builds a shell-facing workflow summary that keeps next-action ordering shared across hosts.
/// </summary>
public sealed class WorkstationWorkflowSummaryService
{
    private readonly StrategyRunReadService _runReadService;
    private readonly StrategyRunContinuityService? _continuityService;
    private readonly IReconciliationRunService? _reconciliationRunService;
    private readonly Meridian.Application.UI.ConfigStore? _configStore;
    private readonly IWorkflowActionCatalog? _actionCatalog;

    public WorkstationWorkflowSummaryService(
        StrategyRunReadService runReadService,
        StrategyRunContinuityService? continuityService = null,
        IReconciliationRunService? reconciliationRunService = null,
        Meridian.Application.UI.ConfigStore? configStore = null,
        IWorkflowActionCatalog? actionCatalog = null)
    {
        _runReadService = runReadService ?? throw new ArgumentNullException(nameof(runReadService));
        _continuityService = continuityService;
        _reconciliationRunService = reconciliationRunService;
        _configStore = configStore;
        _actionCatalog = actionCatalog;
    }

    public async Task<OperatorWorkflowHomeSummary> GetAsync(
        bool hasOperatingContext = false,
        string? operatingContextDisplayName = null,
        string? fundProfileId = null,
        string? fundDisplayName = null,
        CancellationToken ct = default)
    {
        var contextSelected = hasOperatingContext
            || !string.IsNullOrWhiteSpace(operatingContextDisplayName)
            || !string.IsNullOrWhiteSpace(fundProfileId)
            || !string.IsNullOrWhiteSpace(fundDisplayName);

        var runs = await _runReadService.GetRunsAsync(ct: ct).ConfigureAwait(false);
        var scopedRuns = string.IsNullOrWhiteSpace(fundProfileId)
            ? runs
            : runs.Where(run => string.Equals(run.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase)).ToArray();

        var relevantRuns = string.IsNullOrWhiteSpace(fundProfileId) ? runs : scopedRuns;
        var researchRuns = relevantRuns;
        var governedRuns = relevantRuns
            .Where(static run => run.Mode is StrategyRunMode.Paper or StrategyRunMode.Live)
            .ToArray();

        var candidateForPaper = researchRuns.FirstOrDefault(static run =>
            run.Mode == StrategyRunMode.Backtest &&
            run.Promotion?.State == StrategyRunPromotionState.CandidateForPaper);
        var activeResearchRun = researchRuns.FirstOrDefault(static run =>
            run.Mode == StrategyRunMode.Backtest &&
            run.Status is StrategyRunStatus.Running or StrategyRunStatus.Paused);
        var activeTradingRun = governedRuns.FirstOrDefault(static run =>
            run.Status is StrategyRunStatus.Running or StrategyRunStatus.Paused);
        var candidateForLive = governedRuns.FirstOrDefault(static run =>
            run.Promotion?.State == StrategyRunPromotionState.CandidateForLive);
        var latestGovernedRun = governedRuns.FirstOrDefault();

        var researchCandidateSnapshotTask = LoadRunSnapshotAsync(candidateForPaper, ct);
        var tradingActiveSnapshotTask = LoadRunSnapshotAsync(activeTradingRun, ct);
        var governanceCandidateSnapshotTask = LoadRunSnapshotAsync(candidateForLive ?? activeTradingRun ?? latestGovernedRun, ct);

        await Task.WhenAll(researchCandidateSnapshotTask, tradingActiveSnapshotTask, governanceCandidateSnapshotTask)
            .ConfigureAwait(false);

        var researchCandidateSnapshot = await researchCandidateSnapshotTask.ConfigureAwait(false);
        var tradingActiveSnapshot = await tradingActiveSnapshotTask.ConfigureAwait(false);
        var governanceSnapshot = await governanceCandidateSnapshotTask.ConfigureAwait(false);

        var workspaces = new WorkspaceWorkflowSummary[]
        {
            BuildTradingSummary(
                contextSelected,
                candidateForPaper,
                activeTradingRun,
                candidateForLive,
                tradingActiveSnapshot,
                researchCandidateSnapshot,
                relevantRuns),
            BuildPortfolioSummary(contextSelected),
            BuildAccountingSummary(contextSelected, candidateForLive, latestGovernedRun, governanceSnapshot, governedRuns),
            BuildReportingSummary(contextSelected),
            BuildStrategySummary(candidateForPaper, activeResearchRun, researchCandidateSnapshot, researchRuns),
            BuildDataSummary(),
            BuildSettingsSummary()
        };

        return new OperatorWorkflowHomeSummary(
            GeneratedAt: DateTimeOffset.UtcNow,
            HasOperatingContext: contextSelected,
            OperatingContextLabel: contextSelected
                ? operatingContextDisplayName?.Trim() ?? fundDisplayName?.Trim() ?? fundProfileId?.Trim() ?? "Context selected"
                : "No operating context selected",
            FundDisplayName: fundDisplayName?.Trim()
                ?? fundProfileId?.Trim()
                ?? (contextSelected ? "Fund-linked scope selected" : "No fund scope selected"),
            Workspaces: workspaces);
    }

    private WorkspaceWorkflowSummary BuildStrategySummary(
        StrategyRunSummary? candidateForPaper,
        StrategyRunSummary? activeResearchRun,
        WorkflowRunSnapshot? candidateSnapshot,
        IReadOnlyList<StrategyRunSummary> researchRuns)
    {
        if (candidateForPaper is not null)
        {
            return new WorkspaceWorkflowSummary(
                WorkspaceId: "strategy",
                WorkspaceTitle: "Strategy",
                StatusLabel: "Candidate for paper review",
                StatusDetail: $"{candidateForPaper.StrategyName} is promotion-ready and should hand off into Trading review.",
                StatusTone: "Warning",
                NextAction: new WorkflowNextAction(
                    Label: "Send to Trading Review",
                    Detail: "Open the trading workspace with the strategy handoff in view.",
                    TargetPageTag: ResolveTargetPageTag(WorkflowActionIds.StrategySendToTradingReview, "TradingShell"),
                    Tone: "Primary"),
                PrimaryBlocker: CreateBlocker(
                    code: "promotion-handoff",
                    label: "Trading review pending",
                    detail: candidateForPaper.Promotion?.Reason ?? "Completed strategy runs still need an operator paper-review decision.",
                    tone: "Warning",
                    isBlocking: true),
                Evidence: BuildRunEvidence(candidateForPaper, candidateSnapshot));
        }

        if (activeResearchRun is not null)
        {
            return new WorkspaceWorkflowSummary(
                WorkspaceId: "strategy",
                WorkspaceTitle: "Strategy",
                StatusLabel: "Review active strategy run",
                StatusDetail: $"{activeResearchRun.StrategyName} is still in motion and needs operator review before promotion can begin.",
                StatusTone: "Info",
                NextAction: new WorkflowNextAction(
                    Label: "Review Run",
                    Detail: "Inspect active strategy evidence, metrics, and continuity.",
                    TargetPageTag: ResolveTargetPageTag(WorkflowActionIds.StrategyReviewRuns, "StrategyRuns"),
                    Tone: "Primary"),
                PrimaryBlocker: CreateBlocker(
                    code: "run-in-progress",
                    label: activeResearchRun.Status == StrategyRunStatus.Paused ? "Run paused" : "Run still executing",
                    detail: "Promotion review waits until the current strategy run is inspected or completed.",
                    tone: "Info",
                    isBlocking: false),
                Evidence:
                [
                    new WorkflowEvidenceBadge("Run", activeResearchRun.Status.ToString(), "Info"),
                    new WorkflowEvidenceBadge("Mode", activeResearchRun.Mode.ToString(), "Neutral"),
                    new WorkflowEvidenceBadge("Promotion", activeResearchRun.Promotion?.State.ToString() ?? "Pending", "Neutral")
                ]);
        }

        return new WorkspaceWorkflowSummary(
            WorkspaceId: "strategy",
            WorkspaceTitle: "Strategy",
            StatusLabel: researchRuns.Count == 0 ? "Ready for a new strategy cycle" : "No review queue",
            StatusDetail: researchRuns.Count == 0
                ? "No recorded backtests are available yet."
                : "Recorded runs are available, but none currently require strategy handoff review.",
            StatusTone: "Success",
            NextAction: new WorkflowNextAction(
                Label: "Start Backtest",
                Detail: "Launch a new simulation from the strategy workspace.",
                TargetPageTag: ResolveTargetPageTag(WorkflowActionIds.StrategyStartBacktest, "Backtest"),
                Tone: "Primary"),
            PrimaryBlocker: CreateBlocker(
                code: researchRuns.Count == 0 ? "no-runs" : "no-strategy-review",
                label: researchRuns.Count == 0 ? "No strategy runs recorded" : "No active strategy blocker",
                detail: researchRuns.Count == 0
                    ? "Record the first backtest to create a review and promotion queue."
                    : "Strategy can start a fresh run without a blocking handoff.",
                tone: researchRuns.Count == 0 ? "Neutral" : "Success",
                isBlocking: researchRuns.Count == 0),
            Evidence:
            [
                new WorkflowEvidenceBadge("Runs", researchRuns.Count.ToString(), "Neutral")
            ]);
    }

    private WorkspaceWorkflowSummary BuildTradingSummary(
        bool hasOperatingContext,
        StrategyRunSummary? candidateForPaper,
        StrategyRunSummary? activeTradingRun,
        StrategyRunSummary? candidateForLive,
        WorkflowRunSnapshot? activeTradingSnapshot,
        WorkflowRunSnapshot? candidateResearchSnapshot,
        IReadOnlyList<StrategyRunSummary> runs)
    {
        if (!hasOperatingContext)
        {
            return new WorkspaceWorkflowSummary(
                WorkspaceId: "trading",
                WorkspaceTitle: "Trading",
                StatusLabel: "Context required",
                StatusDetail: "Trading review cannot start until a fund-linked operating context is selected.",
                StatusTone: "Warning",
                NextAction: new WorkflowNextAction(
                    Label: "Choose Context",
                    Detail: "Open the trading shell and select the active operating context.",
                    TargetPageTag: ResolveTargetPageTag(WorkflowActionIds.TradingChooseContext, "TradingShell"),
                    Tone: "Primary"),
                PrimaryBlocker: CreateBlocker(
                    code: "choose-context",
                    label: "No operating context selected",
                    detail: "Paper review, live posture, and accounting consequences scope to the active operating context.",
                    tone: "Warning",
                    isBlocking: true),
                Evidence:
                [
                    new WorkflowEvidenceBadge("Candidates", CountPromotionState(runs, StrategyRunPromotionState.CandidateForPaper).ToString(), "Warning"),
                    new WorkflowEvidenceBadge("Active runs", CountActiveRuns(runs).ToString(), "Info")
                ]);
        }

        if (candidateForPaper is not null)
        {
            return new WorkspaceWorkflowSummary(
                WorkspaceId: "trading",
                WorkspaceTitle: "Trading",
                StatusLabel: "Candidate awaiting paper review",
                StatusDetail: $"{candidateForPaper.StrategyName} has cleared Strategy and is ready for a paper-trading review decision.",
                StatusTone: "Warning",
                NextAction: new WorkflowNextAction(
                    Label: "Review Candidate for Paper",
                    Detail: "Open the trading cockpit to continue the Strategy to Trading handoff.",
                    TargetPageTag: ResolveTargetPageTag(WorkflowActionIds.TradingReviewPaperCandidate, "TradingShell"),
                    Tone: "Primary"),
                PrimaryBlocker: BuildTradingBlocker(candidateForPaper, candidateResearchSnapshot),
                Evidence: BuildRunEvidence(candidateForPaper, candidateResearchSnapshot));
        }

        if (activeTradingRun is not null)
        {
            return new WorkspaceWorkflowSummary(
                WorkspaceId: "trading",
                WorkspaceTitle: "Trading",
                StatusLabel: activeTradingRun.Mode == StrategyRunMode.Live ? "Active live cockpit" : "Active paper cockpit",
                StatusDetail: $"{activeTradingRun.StrategyName} is active. Continue execution review and keep the accounting handoff visible.",
                StatusTone: activeTradingRun.Mode == StrategyRunMode.Live ? "Danger" : "Info",
                NextAction: new WorkflowNextAction(
                    Label: "Open Active Cockpit",
                    Detail: "Continue the active paper or live execution workflow.",
                    TargetPageTag: ResolveTargetPageTag(WorkflowActionIds.TradingOpenCockpit, "TradingShell"),
                    Tone: "Primary"),
                PrimaryBlocker: BuildTradingContinuationBlocker(activeTradingSnapshot),
                Evidence: BuildRunEvidence(activeTradingRun, activeTradingSnapshot));
        }

        if (candidateForLive is not null)
        {
            return new WorkspaceWorkflowSummary(
                WorkspaceId: "trading",
                WorkspaceTitle: "Trading",
                StatusLabel: "Candidate awaiting accounting/live review",
                StatusDetail: $"{candidateForLive.StrategyName} has completed paper trading and is waiting for accounting review before live escalation.",
                StatusTone: "Warning",
                NextAction: new WorkflowNextAction(
                    Label: "Open Accounting Review",
                    Detail: "Move the handoff forward into Accounting.",
                    TargetPageTag: ResolveTargetPageTag(WorkflowActionIds.AccountingReviewLiveHandoff, "AccountingShell"),
                    Tone: "Primary"),
                PrimaryBlocker: CreateBlocker(
                    code: "accounting-review",
                    label: "Accounting review pending",
                    detail: candidateForLive.Promotion?.Reason ?? "Paper runs still require accounting review before a live decision.",
                    tone: "Warning",
                    isBlocking: true),
                Evidence:
                [
                    new WorkflowEvidenceBadge("Mode", candidateForLive.Mode.ToString(), "Info"),
                    new WorkflowEvidenceBadge("Promotion", candidateForLive.Promotion?.State.ToString() ?? "Pending", "Warning"),
                    new WorkflowEvidenceBadge("Audit", string.IsNullOrWhiteSpace(candidateForLive.AuditReference) ? "Pending" : "Linked", string.IsNullOrWhiteSpace(candidateForLive.AuditReference) ? "Warning" : "Success")
                ]);
        }

        return new WorkspaceWorkflowSummary(
            WorkspaceId: "trading",
            WorkspaceTitle: "Trading",
            StatusLabel: "No active trading workflow",
            StatusDetail: "Trading is open, but there is no paper or live cockpit currently in progress.",
            StatusTone: "Neutral",
            NextAction: new WorkflowNextAction(
                Label: "Open Strategy Runs",
                Detail: "Review recorded runs and bring one into the trading lane.",
                TargetPageTag: ResolveTargetPageTag(WorkflowActionIds.StrategyReviewRuns, "StrategyRuns"),
                Tone: "Primary"),
            PrimaryBlocker: CreateBlocker(
                code: "no-trading-run",
                label: "No active paper or live run",
                detail: "Promote a completed backtest or reopen a recorded trading run to continue the workflow.",
                tone: "Neutral",
                isBlocking: false),
            Evidence:
            [
                new WorkflowEvidenceBadge("Paper/live runs", runs.Count(static run => run.Mode is StrategyRunMode.Paper or StrategyRunMode.Live).ToString(), "Neutral")
            ]);
    }

    private WorkspaceWorkflowSummary BuildPortfolioSummary(bool hasOperatingContext)
    {
        return new WorkspaceWorkflowSummary(
            WorkspaceId: "portfolio",
            WorkspaceTitle: "Portfolio",
            StatusLabel: hasOperatingContext ? "Portfolio review available" : "Context recommended",
            StatusDetail: hasOperatingContext
                ? "Portfolio, account, fund, and import workflows are available for the active context."
                : "Select an operating context before treating portfolio exposure and account review as complete.",
            StatusTone: hasOperatingContext ? "Success" : "Neutral",
            NextAction: new WorkflowNextAction(
                Label: hasOperatingContext ? "Open Portfolio" : "Choose Context",
                Detail: "Open portfolio review, accounts, and fund exposure workflows.",
                TargetPageTag: ResolveTargetPageTag(WorkflowActionIds.PortfolioOpen, "PortfolioShell"),
                Tone: "Primary"),
            PrimaryBlocker: CreateBlocker(
                code: hasOperatingContext ? "portfolio-ready" : "portfolio-context",
                label: hasOperatingContext ? "No active portfolio blocker" : "No operating context selected",
                detail: hasOperatingContext
                    ? "Portfolio workflows are ready for review."
                    : "Portfolio review is more useful once a fund or account context is selected.",
                tone: hasOperatingContext ? "Success" : "Neutral",
                isBlocking: false),
            Evidence:
            [
                new WorkflowEvidenceBadge("Context", hasOperatingContext ? "Selected" : "Optional", hasOperatingContext ? "Success" : "Neutral")
            ]);
    }

    private WorkspaceWorkflowSummary BuildDataSummary()
    {
        var providerMetrics = _configStore?.TryLoadProviderMetrics();
        if (providerMetrics is { Providers.Length: > 0 })
        {
            var degradedProviders = providerMetrics.Providers.Count(static provider => !provider.IsConnected);
            if (degradedProviders > 0)
            {
                return new WorkspaceWorkflowSummary(
                    WorkspaceId: "data",
                    WorkspaceTitle: "Data",
                    StatusLabel: "Provider degradation detected",
                    StatusDetail: $"{degradedProviders} provider connection(s) are degraded and should be reviewed before downstream workflows rely on the feed.",
                    StatusTone: "Warning",
                    NextAction: new WorkflowNextAction(
                        Label: "Open Provider Health",
                        Detail: "Inspect provider posture and reconnect degraded feeds.",
                        TargetPageTag: ResolveTargetPageTag(WorkflowActionIds.DataOpenProviderHealth, "ProviderHealth"),
                        Tone: "Primary"),
                    PrimaryBlocker: CreateBlocker(
                        code: "provider-degradation",
                        label: "Provider connectivity is degraded",
                        detail: "At least one configured provider is disconnected.",
                        tone: "Warning",
                        isBlocking: true),
                    Evidence:
                    [
                        new WorkflowEvidenceBadge("Healthy providers", providerMetrics.HealthyProviders.ToString(), "Success"),
                        new WorkflowEvidenceBadge("Degraded providers", degradedProviders.ToString(), "Warning")
                    ]);
            }
        }

        var backfill = _configStore?.TryLoadBackfillStatus();
        if (backfill is not null && !backfill.Success)
        {
            return new WorkspaceWorkflowSummary(
                WorkspaceId: "data",
                WorkspaceTitle: "Data",
                StatusLabel: "Backfill queue requires review",
                StatusDetail: $"The latest backfill job for {backfill.Provider} did not complete successfully.",
                StatusTone: "Warning",
                NextAction: new WorkflowNextAction(
                    Label: "Open Backfill Queue",
                    Detail: "Inspect failed or incomplete queue work.",
                    TargetPageTag: ResolveTargetPageTag(WorkflowActionIds.DataOpenBackfillQueue, "Backfill"),
                    Tone: "Primary"),
                PrimaryBlocker: CreateBlocker(
                    code: "backfill-review",
                    label: "Backfill failure recorded",
                    detail: "Resolve the last backfill issue before treating storage as healthy.",
                    tone: "Warning",
                    isBlocking: true),
                Evidence:
                [
                    new WorkflowEvidenceBadge("Provider", backfill.Provider, "Info"),
                    new WorkflowEvidenceBadge("Status", "Failed", "Warning")
                ]);
        }

        return new WorkspaceWorkflowSummary(
            WorkspaceId: "data",
            WorkspaceTitle: "Data",
            StatusLabel: "Healthy queue overview",
            StatusDetail: "Providers and queue surfaces are available for normal operations review.",
            StatusTone: "Success",
            NextAction: new WorkflowNextAction(
                Label: "Open Queue Overview",
                Detail: "Inspect providers, storage, and backfill posture from the workspace home.",
                TargetPageTag: ResolveTargetPageTag(WorkflowActionIds.DataOpenQueueOverview, "DataShell"),
                Tone: "Primary"),
            PrimaryBlocker: CreateBlocker(
                code: "no-data-blocker",
                label: "No active data blocker",
                detail: "The workstation does not currently show provider or backfill pressure.",
                tone: "Success",
                isBlocking: false),
            Evidence:
            [
                new WorkflowEvidenceBadge("Providers", providerMetrics?.Providers.Length.ToString() ?? "0", "Neutral")
            ]);
    }

    private WorkspaceWorkflowSummary BuildAccountingSummary(
        bool hasOperatingContext,
        StrategyRunSummary? candidateForLive,
        StrategyRunSummary? latestGovernedRun,
        WorkflowRunSnapshot? governanceSnapshot,
        IReadOnlyList<StrategyRunSummary> governedRuns)
    {
        if (!hasOperatingContext)
        {
            return new WorkspaceWorkflowSummary(
                WorkspaceId: "accounting",
                WorkspaceTitle: "Accounting",
                StatusLabel: "Context required",
                StatusDetail: "Accounting queues stay locked until a fund-linked operating context is selected.",
                StatusTone: "Warning",
                NextAction: new WorkflowNextAction(
                    Label: "Choose Context",
                    Detail: "Open the accounting shell and unlock the lane summary.",
                    TargetPageTag: ResolveTargetPageTag(WorkflowActionIds.AccountingChooseContext, "AccountingShell"),
                    Tone: "Primary"),
                PrimaryBlocker: CreateBlocker(
                    code: "choose-context",
                    label: "No operating context selected",
                    detail: "Accounting, reconciliation, cash, banking, and audit review all scope to the active context.",
                    tone: "Warning",
                    isBlocking: true),
                Evidence:
                [
                    new WorkflowEvidenceBadge("Accounting runs", governedRuns.Count.ToString(), "Neutral")
                ]);
        }

        var governanceRun = candidateForLive ?? latestGovernedRun;
        if (governanceRun is not null && governanceSnapshot?.OpenBreakCount > 0)
        {
            return new WorkspaceWorkflowSummary(
                WorkspaceId: "accounting",
                WorkspaceTitle: "Accounting",
                StatusLabel: "Reconciliation breaks require review",
                StatusDetail: $"{governanceRun.StrategyName} has open reconciliation exceptions that block the accounting handoff.",
                StatusTone: "Warning",
                NextAction: new WorkflowNextAction(
                    Label: "Review Reconciliation Breaks",
                    Detail: "Open the reconciliation lane and work the break queue.",
                    TargetPageTag: ResolveTargetPageTag(WorkflowActionIds.AccountingReviewReconciliation, "FundReconciliation"),
                    Tone: "Primary"),
                PrimaryBlocker: CreateBlocker(
                    code: "reconciliation-breaks",
                    label: $"{governanceSnapshot.OpenBreakCount} open reconciliation break(s)",
                    detail: "Accounting cannot clear the workflow until breaks are reviewed.",
                    tone: "Warning",
                    isBlocking: true),
                Evidence: BuildAccountingEvidence(governanceRun, governanceSnapshot));
        }

        if (governanceRun is not null && HasAccountingContinuityGap(governanceSnapshot))
        {
            return new WorkspaceWorkflowSummary(
                WorkspaceId: "accounting",
                WorkspaceTitle: "Accounting",
                StatusLabel: "Ledger continuity needs review",
                StatusDetail: $"{governanceRun.StrategyName} needs a continuity check before accounting can treat the handoff as review-ready.",
                StatusTone: "Info",
                NextAction: new WorkflowNextAction(
                    Label: "Review Ledger Continuity",
                    Detail: "Open trial-balance and continuity surfaces for the selected context.",
                    TargetPageTag: ResolveTargetPageTag(WorkflowActionIds.AccountingReviewLedgerContinuity, "FundTrialBalance"),
                    Tone: "Primary"),
                PrimaryBlocker: BuildAccountingContinuityBlocker(governanceSnapshot),
                Evidence: BuildAccountingEvidence(governanceRun, governanceSnapshot));
        }

        if (governanceRun is not null)
        {
            return new WorkspaceWorkflowSummary(
                WorkspaceId: "accounting",
                WorkspaceTitle: "Accounting",
                StatusLabel: "Accounting review ready",
                StatusDetail: $"{governanceRun.StrategyName} has reached the accounting lane with ledger and reconciliation posture available for review.",
                StatusTone: "Success",
                NextAction: new WorkflowNextAction(
                    Label: "Open Accounting Shell",
                    Detail: "Continue with ledger, reconciliation, cash, banking, and audit review.",
                    TargetPageTag: ResolveTargetPageTag(WorkflowActionIds.AccountingOpen, "AccountingShell"),
                    Tone: "Primary"),
                PrimaryBlocker: CreateBlocker(
                    code: "accounting-ready",
                    label: "No active accounting blocker",
                    detail: "The current governed run is review-ready inside the shell.",
                    tone: "Success",
                    isBlocking: false),
                Evidence: BuildAccountingEvidence(governanceRun, governanceSnapshot));
        }

        return new WorkspaceWorkflowSummary(
            WorkspaceId: "accounting",
            WorkspaceTitle: "Accounting",
            StatusLabel: "Context selected",
            StatusDetail: "Accounting is unlocked, but no paper or live run has entered the review lane yet.",
            StatusTone: "Neutral",
            NextAction: new WorkflowNextAction(
                Label: "Open Accounting Shell",
                Detail: "Review accounting lanes for the active context.",
                TargetPageTag: ResolveTargetPageTag(WorkflowActionIds.AccountingOpen, "AccountingShell"),
                Tone: "Primary"),
            PrimaryBlocker: CreateBlocker(
                code: "no-governed-run",
                label: "No governed run available",
                detail: "Move a run through Trading before expecting reconciliation, accounting, and audit review.",
                tone: "Neutral",
                isBlocking: false),
            Evidence:
            [
                    new WorkflowEvidenceBadge("Accounting runs", governedRuns.Count.ToString(), "Neutral")
            ]);
    }

    private WorkspaceWorkflowSummary BuildReportingSummary(bool hasOperatingContext)
    {
        return new WorkspaceWorkflowSummary(
            WorkspaceId: "reporting",
            WorkspaceTitle: "Reporting",
            StatusLabel: hasOperatingContext ? "Reporting ready" : "Context recommended",
            StatusDetail: hasOperatingContext
                ? "Report packs, dashboards, exports, and presets are ready for the active context."
                : "Select a context before treating fund report packs and exports as complete.",
            StatusTone: hasOperatingContext ? "Success" : "Neutral",
            NextAction: new WorkflowNextAction(
                Label: "Open Reporting",
                Detail: "Open report packs, dashboards, export, and preset workflows.",
                TargetPageTag: ResolveTargetPageTag(WorkflowActionIds.ReportingOpen, "ReportingShell"),
                Tone: "Primary"),
            PrimaryBlocker: CreateBlocker(
                code: hasOperatingContext ? "reporting-ready" : "reporting-context",
                label: hasOperatingContext ? "No active reporting blocker" : "No operating context selected",
                detail: hasOperatingContext
                    ? "Reporting workflows are available for output review."
                    : "Reporting workflows can open now, but fund-specific output needs an active context.",
                tone: hasOperatingContext ? "Success" : "Neutral",
                isBlocking: false),
            Evidence:
            [
                new WorkflowEvidenceBadge("Context", hasOperatingContext ? "Selected" : "Optional", hasOperatingContext ? "Success" : "Neutral")
            ]);
    }

    private WorkspaceWorkflowSummary BuildSettingsSummary()
    {
        return new WorkspaceWorkflowSummary(
            WorkspaceId: "settings",
            WorkspaceTitle: "Settings",
            StatusLabel: "Workstation controls available",
            StatusDetail: "Preferences, credentials, diagnostics, services, notifications, help, and setup are available.",
            StatusTone: "Success",
            NextAction: new WorkflowNextAction(
                Label: "Open Settings",
                Detail: "Open workstation configuration and support surfaces.",
                TargetPageTag: ResolveTargetPageTag(WorkflowActionIds.SettingsOpen, "SettingsShell"),
                Tone: "Primary"),
            PrimaryBlocker: CreateBlocker(
                code: "settings-ready",
                label: "No active settings blocker",
                detail: "Configuration, diagnostics, support, and setup surfaces are available.",
                tone: "Success",
                isBlocking: false),
            Evidence:
            [
                new WorkflowEvidenceBadge("Controls", "Available", "Success")
            ]);
    }

    private async Task<WorkflowRunSnapshot?> LoadRunSnapshotAsync(StrategyRunSummary? run, CancellationToken ct)
    {
        if (run is null)
        {
            return null;
        }

        var detailTask = _runReadService.GetRunDetailAsync(run.RunId, ct);
        var continuityTask = _continuityService is null
            ? Task.FromResult<StrategyRunContinuityDetail?>(null)
            : _continuityService.GetRunContinuityAsync(run.RunId, ct);
        var reconciliationTask = _reconciliationRunService is null
            ? Task.FromResult<ReconciliationRunDetail?>(null)
            : _reconciliationRunService.GetLatestForRunAsync(run.RunId, ct);

        await Task.WhenAll(detailTask, continuityTask, reconciliationTask).ConfigureAwait(false);

        var detail = await detailTask.ConfigureAwait(false);
        var continuity = await continuityTask.ConfigureAwait(false);
        var reconciliation = await reconciliationTask.ConfigureAwait(false);
        var continuityStatus = continuity?.ContinuityStatus;
        var reconciliationSummary = reconciliation?.Summary ?? continuity?.Reconciliation;

        return new WorkflowRunSnapshot(
            HasPortfolio: detail?.Portfolio is not null || continuityStatus?.HasPortfolio == true,
            HasLedger: detail?.Ledger is not null || continuityStatus?.HasLedger == true,
            OpenBreakCount: reconciliationSummary?.OpenBreakCount ?? continuityStatus?.OpenReconciliationBreaks ?? 0,
            HasReconciliation: reconciliationSummary is not null || continuityStatus?.HasReconciliation == true,
            HasAuditTrail: !string.IsNullOrWhiteSpace(run.AuditReference),
            PromotionState: run.Promotion?.State ?? StrategyRunPromotionState.None,
            SecurityCoverageIssues: continuityStatus?.SecurityCoverageIssueCount ?? reconciliationSummary?.SecurityIssueCount ?? 0,
            AsOfDriftMinutes: continuityStatus?.AsOfDriftMinutes ?? 0);
    }

    private static WorkflowBlockerSummary BuildTradingBlocker(StrategyRunSummary run, WorkflowRunSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return CreateBlocker(
                code: "strategy-handoff",
                label: "Strategy handoff pending",
                detail: "Open the trading shell to continue operator review.",
                tone: "Warning",
                isBlocking: true);
        }

        if (!snapshot.HasPortfolio || !snapshot.HasLedger)
        {
            return CreateBlocker(
                code: "coverage-gap",
                label: "Coverage gap before paper review",
                detail: $"{run.StrategyName} is missing {(snapshot.HasPortfolio ? "ledger" : snapshot.HasLedger ? "portfolio" : "portfolio and ledger")} coverage for a smooth trading handoff.",
                tone: "Warning",
                isBlocking: true);
        }

        return CreateBlocker(
            code: "paper-review",
            label: "Operator paper-review required",
            detail: "Trading still needs an explicit review decision before the run becomes an active cockpit.",
            tone: "Warning",
            isBlocking: true);
    }

    private static WorkflowBlockerSummary BuildTradingContinuationBlocker(WorkflowRunSnapshot? snapshot)
    {
        if (snapshot?.OpenBreakCount > 0)
        {
            return CreateBlocker(
                code: "accounting-escalation",
                label: "Accounting review is now blocking",
                detail: $"{snapshot.OpenBreakCount} reconciliation break(s) need accounting attention.",
                tone: "Warning",
                isBlocking: true);
        }

        return CreateBlocker(
            code: "continue-cockpit",
            label: "Continue cockpit review",
            detail: "No blocking reconciliation issue is currently preventing cockpit continuation.",
            tone: "Success",
            isBlocking: false);
    }

    private static WorkflowBlockerSummary BuildAccountingContinuityBlocker(WorkflowRunSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return CreateBlocker(
                code: "continuity-pending",
                label: "Continuity detail unavailable",
                detail: "Accounting still needs a ledger or reconciliation snapshot before continuing.",
                tone: "Info",
                isBlocking: true);
        }

        if (!snapshot.HasLedger)
        {
            return CreateBlocker(
                code: "missing-ledger",
                label: "Ledger coverage missing",
                detail: "Open the accounting lane and confirm trial-balance coverage before sign-off.",
                tone: "Warning",
                isBlocking: true);
        }

        if (!snapshot.HasReconciliation)
        {
            return CreateBlocker(
                code: "missing-reconciliation",
                label: "Reconciliation run missing",
                detail: "Accounting should review a reconciliation result before treating the handoff as complete.",
                tone: "Info",
                isBlocking: true);
        }

        if (snapshot.AsOfDriftMinutes > 5)
        {
            return CreateBlocker(
                code: "as-of-drift",
                label: "Ledger timing drift detected",
                detail: $"Portfolio and ledger timestamps drift by {snapshot.AsOfDriftMinutes} minute(s).",
                tone: "Warning",
                isBlocking: true);
        }

        return CreateBlocker(
            code: "continuity-review",
            label: "Continuity review recommended",
            detail: "Review trial-balance posture before the accounting handoff is treated as complete.",
            tone: "Info",
            isBlocking: false);
    }

    private static bool HasAccountingContinuityGap(WorkflowRunSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return false;
        }

        return !snapshot.HasLedger || !snapshot.HasReconciliation || snapshot.AsOfDriftMinutes > 5;
    }

    private static IReadOnlyList<WorkflowEvidenceBadge> BuildRunEvidence(
        StrategyRunSummary run,
        WorkflowRunSnapshot? snapshot)
    {
        return
        [
            new WorkflowEvidenceBadge("Run", run.Status.ToString(), run.Status == StrategyRunStatus.Completed ? "Success" : "Info"),
            new WorkflowEvidenceBadge("Promotion", run.Promotion?.State.ToString() ?? "None", run.Promotion?.RequiresReview == true ? "Warning" : "Neutral"),
            new WorkflowEvidenceBadge("Portfolio", snapshot?.HasPortfolio == true ? "Available" : "Missing", snapshot?.HasPortfolio == true ? "Success" : "Warning"),
            new WorkflowEvidenceBadge("Ledger", snapshot?.HasLedger == true ? "Available" : "Missing", snapshot?.HasLedger == true ? "Success" : "Warning")
        ];
    }

    private static IReadOnlyList<WorkflowEvidenceBadge> BuildAccountingEvidence(
        StrategyRunSummary run,
        WorkflowRunSnapshot? snapshot)
    {
        return
        [
            new WorkflowEvidenceBadge("Mode", run.Mode.ToString(), run.Mode == StrategyRunMode.Live ? "Danger" : "Info"),
            new WorkflowEvidenceBadge("Ledger", snapshot?.HasLedger == true ? "Ready" : "Missing", snapshot?.HasLedger == true ? "Success" : "Warning"),
            new WorkflowEvidenceBadge("Reconciliation", snapshot?.HasReconciliation == true ? "Linked" : "Missing", snapshot?.HasReconciliation == true ? "Success" : "Warning"),
            new WorkflowEvidenceBadge("Breaks", (snapshot?.OpenBreakCount ?? 0).ToString(), (snapshot?.OpenBreakCount ?? 0) > 0 ? "Warning" : "Success"),
            new WorkflowEvidenceBadge("Audit", snapshot?.HasAuditTrail == true ? "Linked" : "Pending", snapshot?.HasAuditTrail == true ? "Success" : "Info")
        ];
    }

    private static int CountPromotionState(IReadOnlyList<StrategyRunSummary> runs, StrategyRunPromotionState state)
        => runs.Count(run => run.Promotion?.State == state);

    private static int CountActiveRuns(IReadOnlyList<StrategyRunSummary> runs)
        => runs.Count(static run => run.Status is StrategyRunStatus.Running or StrategyRunStatus.Paused);

    private string ResolveTargetPageTag(string actionId, string fallbackPageTag)
        => _actionCatalog?.ResolveTargetPageTag(actionId, fallbackPageTag) ?? fallbackPageTag;

    private static WorkflowBlockerSummary CreateBlocker(
        string code,
        string label,
        string detail,
        string tone,
        bool isBlocking)
        => new(
            Code: code,
            Label: label,
            Detail: detail,
            Tone: tone,
            IsBlocking: isBlocking);

    private sealed record WorkflowRunSnapshot(
        bool HasPortfolio,
        bool HasLedger,
        int OpenBreakCount,
        bool HasReconciliation,
        bool HasAuditTrail,
        StrategyRunPromotionState PromotionState,
        int SecurityCoverageIssues,
        int AsOfDriftMinutes);
}
