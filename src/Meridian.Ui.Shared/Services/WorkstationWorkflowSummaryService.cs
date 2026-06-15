using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;
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
    private readonly IOperationsContinuityWorkflowService? _operationsContinuityWorkflowService;

    public WorkstationWorkflowSummaryService(
        StrategyRunReadService runReadService,
        StrategyRunContinuityService? continuityService = null,
        IReconciliationRunService? reconciliationRunService = null,
        Meridian.Application.UI.ConfigStore? configStore = null,
        IWorkflowActionCatalog? actionCatalog = null,
        IOperationsContinuityWorkflowService? operationsContinuityWorkflowService = null)
    {
        _runReadService = runReadService ?? throw new ArgumentNullException(nameof(runReadService));
        _continuityService = continuityService;
        _reconciliationRunService = reconciliationRunService;
        _configStore = configStore;
        _actionCatalog = actionCatalog;
        _operationsContinuityWorkflowService = operationsContinuityWorkflowService;
    }

    public async Task<OperatorWorkflowHomeSummary> GetAsync(
        bool hasOperatingContext = false,
        string? operatingContextDisplayName = null,
        string? fundProfileId = null,
        string? fundAccountId = null,
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
        var strategyRuns = relevantRuns;
        var governedRuns = relevantRuns
            .Where(static run => run.Mode is StrategyRunMode.Paper or StrategyRunMode.Live)
            .ToArray();

        var candidateForPaper = strategyRuns.FirstOrDefault(static run =>
            run.Mode == StrategyRunMode.Backtest &&
            run.Promotion?.State == StrategyRunPromotionState.CandidateForPaper);
        var activeStrategyRun = strategyRuns.FirstOrDefault(static run =>
            run.Mode == StrategyRunMode.Backtest &&
            run.Status is StrategyRunStatus.Running or StrategyRunStatus.Paused);
        var activeTradingRun = governedRuns.FirstOrDefault(static run =>
            run.Status is StrategyRunStatus.Running or StrategyRunStatus.Paused);
        var candidateForLive = governedRuns.FirstOrDefault(static run =>
            run.Promotion?.State == StrategyRunPromotionState.CandidateForLive);
        var latestGovernedRun = governedRuns.FirstOrDefault();

        var strategyCandidateSnapshotTask = LoadRunSnapshotAsync(candidateForPaper, ct);
        var tradingActiveSnapshotTask = LoadRunSnapshotAsync(activeTradingRun, ct);
        var accountingCandidateSnapshotTask = LoadRunSnapshotAsync(candidateForLive ?? activeTradingRun ?? latestGovernedRun, ct);
        var financialOperationsSnapshotTask = LoadFinancialOperationsSnapshotAsync(contextSelected, fundAccountId, fundProfileId, ct);

        await Task.WhenAll(strategyCandidateSnapshotTask, tradingActiveSnapshotTask, accountingCandidateSnapshotTask, financialOperationsSnapshotTask)
            .ConfigureAwait(false);

        var strategyCandidateSnapshot = await strategyCandidateSnapshotTask.ConfigureAwait(false);
        var tradingActiveSnapshot = await tradingActiveSnapshotTask.ConfigureAwait(false);
        var accountingSnapshot = await accountingCandidateSnapshotTask.ConfigureAwait(false);
        var financialOperationsSnapshot = await financialOperationsSnapshotTask.ConfigureAwait(false);

        var workspaces = new WorkspaceWorkflowSummary[]
        {
            BuildTradingSummary(
                contextSelected,
                candidateForPaper,
                activeTradingRun,
                candidateForLive,
                tradingActiveSnapshot,
                strategyCandidateSnapshot,
                relevantRuns),
            BuildPortfolioSummary(contextSelected),
            BuildAccountingSummary(contextSelected, candidateForLive, latestGovernedRun, accountingSnapshot, governedRuns, financialOperationsSnapshot),
            BuildReportingSummary(contextSelected),
            BuildStrategySummary(candidateForPaper, activeStrategyRun, strategyCandidateSnapshot, strategyRuns),
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
            Workspaces: workspaces)
        {
            AssuranceScore = BuildAssuranceScore(workspaces)
        };
    }

    private static MeridianAssuranceScoreDto BuildAssuranceScore(IReadOnlyList<WorkspaceWorkflowSummary> workspaces)
    {
        var components = workspaces
            .Select(BuildAssuranceComponent)
            .ToArray();
        var score = components.Length == 0
            ? 100
            : (int)Math.Round(components.Average(static component => component.Score), MidpointRounding.AwayFromZero);
        var status =
            components.Any(static component => component.Status == EvidenceStatusDto.Blocked) ? EvidenceStatusDto.Blocked :
            components.Any(static component => component.Status == EvidenceStatusDto.Stale) ? EvidenceStatusDto.Stale :
            components.Any(static component => component.Status == EvidenceStatusDto.ReviewRequired) ? EvidenceStatusDto.ReviewRequired :
            components.All(static component => component.Status == EvidenceStatusDto.Ready) ? EvidenceStatusDto.Ready :
            EvidenceStatusDto.ReviewRequired;

        return new MeridianAssuranceScoreDto(score, status, components, []);
    }

    private static EvidenceAssuranceComponentDto BuildAssuranceComponent(WorkspaceWorkflowSummary workspace)
    {
        var isBlocking = workspace.PrimaryBlocker.IsBlocking;
        var tone = workspace.StatusTone;
        var score =
            isBlocking && IsCriticalTone(tone) ? 0 :
            isBlocking ? 60 :
            string.Equals(tone, "Success", StringComparison.OrdinalIgnoreCase) ? 100 :
            string.Equals(tone, "Info", StringComparison.OrdinalIgnoreCase) ? 85 :
            75;
        var status =
            isBlocking && IsCriticalTone(tone) ? EvidenceStatusDto.Blocked :
            isBlocking ? EvidenceStatusDto.ReviewRequired :
            string.Equals(tone, "Success", StringComparison.OrdinalIgnoreCase) ? EvidenceStatusDto.Ready :
            EvidenceStatusDto.ReviewRequired;

        return new EvidenceAssuranceComponentDto(
            ComponentId: workspace.WorkspaceId,
            Label: workspace.WorkspaceTitle,
            Score: score,
            Status: status,
            Detail: $"{workspace.StatusLabel}: {workspace.StatusDetail}");
    }

    private static bool IsCriticalTone(string tone) =>
        string.Equals(tone, "Critical", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(tone, "Danger", StringComparison.OrdinalIgnoreCase);

    private WorkspaceWorkflowSummary BuildStrategySummary(
        StrategyRunSummary? candidateForPaper,
        StrategyRunSummary? activeStrategyRun,
        WorkflowRunSnapshot? candidateSnapshot,
        IReadOnlyList<StrategyRunSummary> strategyRuns)
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

        if (activeStrategyRun is not null)
        {
            return new WorkspaceWorkflowSummary(
                WorkspaceId: "strategy",
                WorkspaceTitle: "Strategy",
                StatusLabel: "Review active strategy run",
                StatusDetail: $"{activeStrategyRun.StrategyName} is still in motion and needs operator review before promotion can begin.",
                StatusTone: "Info",
                NextAction: new WorkflowNextAction(
                    Label: "Review Run",
                    Detail: "Inspect active strategy evidence, metrics, and continuity.",
                    TargetPageTag: ResolveTargetPageTag(WorkflowActionIds.StrategyReviewRuns, "StrategyRuns"),
                    Tone: "Primary"),
                PrimaryBlocker: CreateBlocker(
                    code: "run-in-progress",
                    label: activeStrategyRun.Status == StrategyRunStatus.Paused ? "Run paused" : "Run still executing",
                    detail: "Promotion review waits until the current strategy run is inspected or completed.",
                    tone: "Info",
                    isBlocking: false),
                Evidence:
                [
                    new WorkflowEvidenceBadge("Run", activeStrategyRun.Status.ToString(), "Info"),
                    new WorkflowEvidenceBadge("Mode", activeStrategyRun.Mode.ToString(), "Neutral"),
                    new WorkflowEvidenceBadge("Promotion", activeStrategyRun.Promotion?.State.ToString() ?? "Pending", "Neutral")
                ]);
        }

        return new WorkspaceWorkflowSummary(
            WorkspaceId: "strategy",
            WorkspaceTitle: "Strategy",
            StatusLabel: strategyRuns.Count == 0 ? "Ready for a new strategy cycle" : "No review queue",
            StatusDetail: strategyRuns.Count == 0
                ? "No recorded backtests are available yet."
                : "Recorded runs are available, but none currently require strategy handoff review.",
            StatusTone: "Success",
            NextAction: new WorkflowNextAction(
                Label: "Start Backtest",
                Detail: "Launch a new simulation from the strategy workspace.",
                TargetPageTag: ResolveTargetPageTag(WorkflowActionIds.StrategyStartBacktest, "Backtest"),
                Tone: "Primary"),
            PrimaryBlocker: CreateBlocker(
                code: strategyRuns.Count == 0 ? "no-runs" : "no-strategy-review",
                label: strategyRuns.Count == 0 ? "No strategy runs recorded" : "No active strategy blocker",
                detail: strategyRuns.Count == 0
                    ? "Record the first backtest to create a review and promotion queue."
                    : "Strategy can start a fresh run without a blocking handoff.",
                tone: strategyRuns.Count == 0 ? "Neutral" : "Success",
                isBlocking: strategyRuns.Count == 0),
            Evidence:
            [
                new WorkflowEvidenceBadge("Runs", strategyRuns.Count.ToString(), "Neutral")
            ]);
    }

    private WorkspaceWorkflowSummary BuildTradingSummary(
        bool hasOperatingContext,
        StrategyRunSummary? candidateForPaper,
        StrategyRunSummary? activeTradingRun,
        StrategyRunSummary? candidateForLive,
        WorkflowRunSnapshot? activeTradingSnapshot,
        WorkflowRunSnapshot? candidateStrategySnapshot,
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
                PrimaryBlocker: BuildTradingBlocker(candidateForPaper, candidateStrategySnapshot),
                Evidence: BuildRunEvidence(candidateForPaper, candidateStrategySnapshot));
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
        WorkflowRunSnapshot? accountingSnapshot,
        IReadOnlyList<StrategyRunSummary> governedRuns,
        FinancialOperationsControlSnapshot? financialOperationsSnapshot)
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

        if (financialOperationsSnapshot is not null)
        {
            return BuildFinancialOperationsAccountingSummary(financialOperationsSnapshot);
        }

        var accountingRun = candidateForLive ?? latestGovernedRun;
        if (accountingRun is not null && accountingSnapshot?.OpenBreakCount > 0)
        {
            return new WorkspaceWorkflowSummary(
                WorkspaceId: "accounting",
                WorkspaceTitle: "Accounting",
                StatusLabel: "Reconciliation breaks require review",
                StatusDetail: $"{accountingRun.StrategyName} has open reconciliation exceptions that block the accounting handoff.",
                StatusTone: "Warning",
                NextAction: new WorkflowNextAction(
                    Label: "Review Reconciliation Breaks",
                    Detail: "Open the reconciliation lane and work the break queue.",
                    TargetPageTag: ResolveTargetPageTag(WorkflowActionIds.AccountingReviewReconciliation, "FundReconciliation"),
                    Tone: "Primary"),
                PrimaryBlocker: CreateBlocker(
                    code: "reconciliation-breaks",
                    label: $"{accountingSnapshot.OpenBreakCount} open reconciliation break(s)",
                    detail: "Accounting cannot clear the workflow until breaks are reviewed.",
                    tone: "Warning",
                    isBlocking: true),
                Evidence: BuildAccountingEvidence(accountingRun, accountingSnapshot));
        }

        if (accountingRun is not null && HasAccountingContinuityGap(accountingSnapshot))
        {
            return new WorkspaceWorkflowSummary(
                WorkspaceId: "accounting",
                WorkspaceTitle: "Accounting",
                StatusLabel: "Ledger continuity needs review",
                StatusDetail: $"{accountingRun.StrategyName} needs a continuity check before accounting can treat the handoff as review-ready.",
                StatusTone: "Info",
                NextAction: new WorkflowNextAction(
                    Label: "Review Ledger Continuity",
                    Detail: "Open trial-balance and continuity surfaces for the selected context.",
                    TargetPageTag: ResolveTargetPageTag(WorkflowActionIds.AccountingReviewLedgerContinuity, "FundTrialBalance"),
                    Tone: "Primary"),
                PrimaryBlocker: BuildAccountingContinuityBlocker(accountingSnapshot),
                Evidence: BuildAccountingEvidence(accountingRun, accountingSnapshot));
        }

        if (accountingRun is not null)
        {
            return new WorkspaceWorkflowSummary(
                WorkspaceId: "accounting",
                WorkspaceTitle: "Accounting",
                StatusLabel: "Accounting review ready",
                StatusDetail: $"{accountingRun.StrategyName} has reached the accounting lane with ledger and reconciliation posture available for review.",
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
                Evidence: BuildAccountingEvidence(accountingRun, accountingSnapshot));
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

    private WorkspaceWorkflowSummary BuildFinancialOperationsAccountingSummary(FinancialOperationsControlSnapshot snapshot)
    {
        var workflow = snapshot.ActiveWorkflow;
        if (workflow is null)
        {
            return new WorkspaceWorkflowSummary(
                WorkspaceId: "accounting",
                WorkspaceTitle: "Accounting",
                StatusLabel: "Financial operations workflow not started",
                StatusDetail: $"No operations continuity workflow is recorded for fund account {snapshot.FundAccountId:D}.",
                StatusTone: "Warning",
                NextAction: new WorkflowNextAction(
                    Label: "Receive Activity",
                    Detail: "Start the governed operations flow before matching, exception resolution, approval, or evidence production.",
                    TargetPageTag: ResolveTargetPageTag(WorkflowActionIds.AccountingReviewOperationsContinuity, "OperationsContinuity"),
                    Tone: "Primary"),
                PrimaryBlocker: CreateBlocker(
                    code: "financial-operations-not-started",
                    label: "No source-backed operations workflow",
                    detail: "Financial operations status is blocked until source activity is received through an operations continuity workflow.",
                    tone: "Warning",
                    isBlocking: true),
                Evidence:
                [
                    new WorkflowEvidenceBadge("Fund account", "Scoped", "Success"),
                    new WorkflowEvidenceBadge("Workflows", snapshot.WorkflowCount.ToString(), "Warning"),
                    new WorkflowEvidenceBadge("Core flow", "Receive Activity", "Warning"),
                    new WorkflowEvidenceBadge("Reviewed automation", "No suggestions without intake", "Warning")
                ]);
        }

        var unresolvedBreakCount = CountUnresolvedBreaks(workflow);
        var evidenceCount = CountEvidenceLinks(workflow);
        var stage = ResolveFinancialOperationsStage(workflow, unresolvedBreakCount);
        var evidencePackageBlocker = ResolveFinancialOperationsEvidencePackageBlocker(workflow);

        if (unresolvedBreakCount > 0)
        {
            return new WorkspaceWorkflowSummary(
                WorkspaceId: "accounting",
                WorkspaceTitle: "Accounting",
                StatusLabel: "Financial operations exceptions require review",
                StatusDetail: $"Period {workflow.PeriodId} has {unresolvedBreakCount} unresolved exception(s) before approval and evidence production can complete.",
                StatusTone: "Warning",
                NextAction: new WorkflowNextAction(
                    Label: "Resolve Exceptions",
                    Detail: "Open reconciliation casework, assign breaks, and retain resolution evidence.",
                    TargetPageTag: ResolveTargetPageTag(WorkflowActionIds.AccountingReviewReconciliation, "FundReconciliation"),
                    Tone: "Primary"),
                PrimaryBlocker: CreateBlocker(
                    code: "financial-operations-exceptions",
                    label: $"{unresolvedBreakCount} unresolved exception(s)",
                    detail: "Approval and close evidence remain blocked until every break is matched, resolved, or explicitly closed.",
                    tone: "Warning",
                    isBlocking: true),
                Evidence: BuildFinancialOperationsEvidence(snapshot, workflow, stage, unresolvedBreakCount, evidenceCount));
        }

        if (workflow.ApprovalState is OperationsApprovalStateDto.Submitted or OperationsApprovalStateDto.ReviewerAssigned ||
            workflow.Status == OperationsWorkflowStatusDto.ApprovalPending)
        {
            var pendingApprovals = workflow.Approvals.Count(static approval =>
                approval.Status is OperationsApprovalStateDto.Submitted or OperationsApprovalStateDto.ReviewerAssigned);
            return new WorkspaceWorkflowSummary(
                WorkspaceId: "accounting",
                WorkspaceTitle: "Accounting",
                StatusLabel: "Financial operations approval pending",
                StatusDetail: $"Period {workflow.PeriodId} has matched records and is waiting on {Math.Max(1, pendingApprovals)} approval review(s).",
                StatusTone: "Warning",
                NextAction: new WorkflowNextAction(
                    Label: "Approve Results",
                    Detail: "Review workflow approvals, rationale, checklist controls, and retained evidence.",
                    TargetPageTag: ResolveTargetPageTag(WorkflowActionIds.AccountingReviewOperationsContinuity, "OperationsContinuity"),
                    Tone: "Primary"),
                PrimaryBlocker: CreateBlocker(
                    code: "financial-operations-approval-pending",
                    label: "Approval history is pending",
                    detail: "The financial operations flow cannot produce final close evidence until approval review is complete.",
                    tone: "Warning",
                    isBlocking: true),
                Evidence: BuildFinancialOperationsEvidence(snapshot, workflow, stage, unresolvedBreakCount, evidenceCount));
        }

        if ((workflow.Status == OperationsWorkflowStatusDto.Closed || workflow.ClosePackage is not null) &&
            evidencePackageBlocker is not null)
        {
            return new WorkspaceWorkflowSummary(
                WorkspaceId: "accounting",
                WorkspaceTitle: "Accounting",
                StatusLabel: "Financial operations evidence package review required",
                StatusDetail: $"Period {workflow.PeriodId} has close evidence but {evidencePackageBlocker.Label.ToLowerInvariant()} is {evidencePackageBlocker.Status}.",
                StatusTone: "Warning",
                NextAction: new WorkflowNextAction(
                    Label: "Review Evidence Package",
                    Detail: evidencePackageBlocker.RequiredActions.FirstOrDefault()
                        ?? "Review retained evidence-package posture before treating the workflow as close-ready evidence.",
                    TargetPageTag: ResolveTargetPageTag(WorkflowActionIds.AccountingReviewOperationsContinuity, "OperationsContinuity"),
                    Tone: "Primary"),
                PrimaryBlocker: CreateBlocker(
                    code: "financial-operations-evidence-package",
                    label: evidencePackageBlocker.Label,
                    detail: evidencePackageBlocker.Summary,
                    tone: "Warning",
                    isBlocking: true),
                Evidence: BuildFinancialOperationsEvidence(snapshot, workflow, stage, unresolvedBreakCount, evidenceCount));
        }

        if (workflow.Status == OperationsWorkflowStatusDto.Closed || workflow.ClosePackage is not null)
        {
            return new WorkspaceWorkflowSummary(
                WorkspaceId: "accounting",
                WorkspaceTitle: "Accounting",
                StatusLabel: "Financial operations evidence produced",
                StatusDetail: workflow.ClosePackage is null
                    ? $"Period {workflow.PeriodId} is closed with retained approval and workflow history."
                    : $"Period {workflow.PeriodId} produced close package {workflow.ClosePackage.ClosePackageId} with retained manifest evidence.",
                StatusTone: "Success",
                NextAction: new WorkflowNextAction(
                    Label: "Open Evidence Packet",
                    Detail: "Inspect retained source, reconciliation, approval, close, and reporting evidence.",
                    TargetPageTag: ResolveTargetPageTag(WorkflowActionIds.EvidenceOpenPacket, "EvidenceWorkbench"),
                    Tone: "Primary"),
                PrimaryBlocker: CreateBlocker(
                    code: "financial-operations-evidence-produced",
                    label: "No active financial operations blocker",
                    detail: "The active operations workflow has produced retained approval and close evidence.",
                    tone: "Success",
                    isBlocking: false),
                Evidence: BuildFinancialOperationsEvidence(snapshot, workflow, stage, unresolvedBreakCount, evidenceCount));
        }

        if ((workflow.Status == OperationsWorkflowStatusDto.ReadyForClose ||
             workflow.ApprovalState == OperationsApprovalStateDto.Approved) &&
            workflow.CloseReadiness is { IsReadyToClose: false })
        {
            return new WorkspaceWorkflowSummary(
                WorkspaceId: "accounting",
                WorkspaceTitle: "Accounting",
                StatusLabel: "Financial operations close readiness blocked",
                StatusDetail: $"Period {workflow.PeriodId} close readiness score is {workflow.CloseReadiness.Score}; review blockers before producing the evidence package.",
                StatusTone: "Warning",
                NextAction: new WorkflowNextAction(
                    Label: "Review Close Readiness",
                    Detail: "Inspect close checklist controls, report readiness, and recovery actions.",
                    TargetPageTag: ResolveTargetPageTag(WorkflowActionIds.AccountingReviewCloseReadiness, "OperationsClose"),
                    Tone: "Primary"),
                PrimaryBlocker: CreateBlocker(
                    code: "financial-operations-close-readiness",
                    label: "Close evidence is incomplete",
                    detail: workflow.CloseReadiness.Blockers.FirstOrDefault()?.Message
                        ?? "Close readiness blockers remain before the workflow can produce final evidence.",
                    tone: "Warning",
                    isBlocking: true),
                Evidence: BuildFinancialOperationsEvidence(snapshot, workflow, stage, unresolvedBreakCount, evidenceCount));
        }

        return new WorkspaceWorkflowSummary(
            WorkspaceId: "accounting",
            WorkspaceTitle: "Accounting",
            StatusLabel: "Financial operations control flow active",
            StatusDetail: $"Period {workflow.PeriodId} is in the {stage} stage with source-backed workflow gates, checklist, and audit trail.",
            StatusTone: "Info",
            NextAction: new WorkflowNextAction(
                Label: stage,
                Detail: "Open the governed operations continuity workflow and continue the next source-backed control step.",
                TargetPageTag: ResolveTargetPageTag(WorkflowActionIds.AccountingReviewOperationsContinuity, "OperationsContinuity"),
                Tone: "Primary"),
            PrimaryBlocker: CreateBlocker(
                code: "financial-operations-in-progress",
                label: "Financial operations flow in progress",
                detail: "Continue the source-backed receive, match, resolve, approve, and evidence workflow before close.",
                tone: "Info",
                isBlocking: false),
            Evidence: BuildFinancialOperationsEvidence(snapshot, workflow, stage, unresolvedBreakCount, evidenceCount));
    }

    private static IReadOnlyList<WorkflowEvidenceBadge> BuildFinancialOperationsEvidence(
        FinancialOperationsControlSnapshot snapshot,
        OperationsContinuityWorkflowDto workflow,
        string stage,
        int unresolvedBreakCount,
        int evidenceCount)
    {
        var closeValue = workflow.ClosePackage is not null
            ? "Package retained"
            : workflow.CloseReadiness is null
                ? "Pending"
                : $"{workflow.CloseReadiness.Score}";

        return
        [
            new WorkflowEvidenceBadge("Core flow", stage, ToneForStage(workflow, unresolvedBreakCount)),
            new WorkflowEvidenceBadge("Workflows", snapshot.WorkflowCount.ToString(), "Neutral"),
            new WorkflowEvidenceBadge("Breaks", unresolvedBreakCount.ToString(), unresolvedBreakCount > 0 ? "Warning" : "Success"),
            new WorkflowEvidenceBadge("Approval", workflow.ApprovalState.ToString(), workflow.ApprovalState == OperationsApprovalStateDto.Approved ? "Success" : "Warning"),
            new WorkflowEvidenceBadge("Evidence", evidenceCount.ToString(), evidenceCount > 0 ? "Success" : "Warning"),
            new WorkflowEvidenceBadge("Close", closeValue, workflow.ClosePackage is not null ? "Success" : "Info"),
            BuildEvidencePackageReadinessEvidence(workflow),
            BuildPeriodLockEvidence(workflow),
            BuildReviewedAutomationEvidence(workflow)
        ];
    }

    private static WorkflowEvidenceBadge BuildEvidencePackageReadinessEvidence(OperationsContinuityWorkflowDto workflow)
    {
        if (workflow.EvidencePackages.Count == 0)
        {
            return new WorkflowEvidenceBadge("Evidence packages", "Not projected", "Info");
        }

        var readyCount = workflow.EvidencePackages.Count(static package => IsEvidencePackageReady(package));
        var totalCount = workflow.EvidencePackages.Count;

        return new WorkflowEvidenceBadge(
            "Evidence packages",
            $"{readyCount}/{totalCount}",
            readyCount == totalCount ? "Success" : "Warning");
    }

    private static WorkflowEvidenceBadge BuildPeriodLockEvidence(OperationsContinuityWorkflowDto workflow)
    {
        var periodLockPackage = FindPeriodLockEvidencePackage(workflow);
        if (periodLockPackage is null)
        {
            return new WorkflowEvidenceBadge("Period lock", "Not projected", "Info");
        }

        return new WorkflowEvidenceBadge(
            "Period lock",
            periodLockPackage.Status.ToString(),
            ToneForEvidenceStatus(periodLockPackage.Status));
    }

    private static WorkflowEvidenceBadge BuildReviewedAutomationEvidence(OperationsContinuityWorkflowDto workflow)
    {
        if (workflow.ReviewedAutomation is { } reviewedAutomation)
        {
            return new WorkflowEvidenceBadge(
                "Reviewed automation",
                reviewedAutomation.Stage,
                ToneForReviewedAutomation(reviewedAutomation));
        }

        if (workflow.BrokerIntakeState is OperationsBrokerIntakeStateDto.Imported or OperationsBrokerIntakeStateDto.Normalized)
        {
            return new WorkflowEvidenceBadge("Reviewed automation", "Extraction review", "Warning");
        }

        if (workflow.ReconciliationState == OperationsReconciliationStateDto.AutoMatched)
        {
            return new WorkflowEvidenceBadge("Reviewed automation", "Suggested matches require review", "Warning");
        }

        if (workflow.LedgerPostingState is OperationsLedgerPostingStateDto.Drafted or OperationsLedgerPostingStateDto.Validated)
        {
            return new WorkflowEvidenceBadge("Reviewed automation", "Journal draft review", "Warning");
        }

        if (workflow.ApprovalState is OperationsApprovalStateDto.Pending or OperationsApprovalStateDto.Submitted or OperationsApprovalStateDto.ReviewerAssigned)
        {
            return new WorkflowEvidenceBadge("Reviewed automation", "Reviewer approval required", "Warning");
        }

        if (workflow.Status == OperationsWorkflowStatusDto.Closed || workflow.ClosePackage is not null)
        {
            return new WorkflowEvidenceBadge("Reviewed automation", "Reviewed evidence retained", "Success");
        }

        return new WorkflowEvidenceBadge("Reviewed automation", "Suggestions only", "Info");
    }

    private static string ToneForReviewedAutomation(OperationsReviewedAutomationSummaryDto reviewedAutomation)
    {
        if (reviewedAutomation.RequiresHumanReview || reviewedAutomation.Status == EvidenceStatusDto.ReviewRequired)
        {
            return "Warning";
        }

        return reviewedAutomation.Status == EvidenceStatusDto.Ready
            ? "Success"
            : "Info";
    }

    private async Task<FinancialOperationsControlSnapshot?> LoadFinancialOperationsSnapshotAsync(
        bool hasOperatingContext,
        string? fundAccountId,
        string? fundProfileId,
        CancellationToken ct)
    {
        if (!hasOperatingContext || _operationsContinuityWorkflowService is null)
        {
            return null;
        }

        if (!TryResolveFinancialOperationsFundAccountId(fundAccountId, fundProfileId, out var parsedFundAccountId))
        {
            return null;
        }

        var workflows = await _operationsContinuityWorkflowService
            .ListAsync(parsedFundAccountId, periodId: null, status: null, ct)
            .ConfigureAwait(false);
        var active = workflows
            .OrderBy(static workflow => RankFinancialOperationsWorkflow(workflow.Status))
            .ThenByDescending(static workflow => workflow.UpdatedAtUtc)
            .FirstOrDefault();
        var detail = active is null
            ? null
            : await _operationsContinuityWorkflowService.GetAsync(active.WorkflowId, ct).ConfigureAwait(false);

        return new FinancialOperationsControlSnapshot(parsedFundAccountId, workflows.Count, detail);
    }

    private static bool TryResolveFinancialOperationsFundAccountId(
        string? fundAccountId,
        string? fundProfileId,
        out Guid parsedFundAccountId)
    {
        if (Guid.TryParse(fundAccountId, out parsedFundAccountId))
        {
            return true;
        }

        return Guid.TryParse(fundProfileId, out parsedFundAccountId);
    }

    private static int RankFinancialOperationsWorkflow(OperationsWorkflowStatusDto status) => status switch
    {
        OperationsWorkflowStatusDto.Blocked => 0,
        OperationsWorkflowStatusDto.ReconciliationActive => 1,
        OperationsWorkflowStatusDto.ApprovalPending => 2,
        OperationsWorkflowStatusDto.ReadyForClose => 3,
        OperationsWorkflowStatusDto.CollectingBrokerData => 4,
        OperationsWorkflowStatusDto.SecurityMasterValidation => 5,
        OperationsWorkflowStatusDto.LedgerPostingDraft => 6,
        OperationsWorkflowStatusDto.NotStarted => 7,
        OperationsWorkflowStatusDto.Closed => 8,
        _ => 9
    };

    private static string ResolveFinancialOperationsStage(OperationsContinuityWorkflowDto workflow, int unresolvedBreakCount)
    {
        if (workflow.BrokerIntakeState is OperationsBrokerIntakeStateDto.Pending or OperationsBrokerIntakeStateDto.Imported or OperationsBrokerIntakeStateDto.Normalized)
        {
            return "Receive Activity";
        }

        if (workflow.ReconciliationState is OperationsReconciliationStateDto.Pending or OperationsReconciliationStateDto.AutoMatched)
        {
            return "Match Records";
        }

        if (unresolvedBreakCount > 0 ||
            workflow.ReconciliationState is OperationsReconciliationStateDto.ExceptionsOpen or OperationsReconciliationStateDto.InReview)
        {
            return "Resolve Exceptions";
        }

        if (workflow.ApprovalState != OperationsApprovalStateDto.Approved)
        {
            return "Approve Results";
        }

        return "Produce Evidence";
    }

    private static int CountUnresolvedBreaks(OperationsContinuityWorkflowDto workflow)
        => workflow.BreakCases.Count(static breakCase => !IsResolvedBreakStatus(breakCase.Status));

    private static bool IsResolvedBreakStatus(string? status)
        => string.Equals(status, "closed", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(status, "resolved", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(status, "matched", StringComparison.OrdinalIgnoreCase);

    private static int CountEvidenceLinks(OperationsContinuityWorkflowDto workflow)
        => workflow.EvidenceLinks
            .Concat(workflow.ReportPackReadiness.EvidenceLinks)
            .Concat(workflow.ClosePackage?.EvidenceLinks ?? [])
            .Concat(workflow.EvidencePackages.SelectMany(static package => package.EvidenceLinks))
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .Count();

    private static OperationsEvidencePackageSummaryDto? ResolveFinancialOperationsEvidencePackageBlocker(
        OperationsContinuityWorkflowDto workflow)
        => workflow.EvidencePackages
            .Where(static package => !IsEvidencePackageReady(package))
            .OrderBy(static package => RankEvidencePackageStatus(package.Status))
            .ThenBy(static package => string.Equals(
                package.PackageId,
                "period-lock-reopen",
                StringComparison.OrdinalIgnoreCase)
                || package.PackageId.StartsWith("period-lock-reopen:", StringComparison.OrdinalIgnoreCase)
                    ? 0
                    : 1)
            .ThenBy(static package => package.Label, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

    private static OperationsEvidencePackageSummaryDto? FindPeriodLockEvidencePackage(
        OperationsContinuityWorkflowDto workflow)
        => workflow.EvidencePackages.FirstOrDefault(static package =>
            string.Equals(package.PackageId, "period-lock-reopen", StringComparison.OrdinalIgnoreCase) ||
            package.PackageId.StartsWith("period-lock-reopen:", StringComparison.OrdinalIgnoreCase) ||
            package.Label.Contains("period lock", StringComparison.OrdinalIgnoreCase));

    private static bool IsEvidencePackageReady(OperationsEvidencePackageSummaryDto package)
        => package.IsReady && package.Status == EvidenceStatusDto.Ready;

    private static int RankEvidencePackageStatus(EvidenceStatusDto status) => status switch
    {
        EvidenceStatusDto.Blocked => 0,
        EvidenceStatusDto.Missing => 1,
        EvidenceStatusDto.Stale => 2,
        EvidenceStatusDto.ReviewRequired => 3,
        EvidenceStatusDto.Unknown => 4,
        EvidenceStatusDto.Ready => 5,
        _ => 6
    };

    private static string ToneForEvidenceStatus(EvidenceStatusDto status) => status switch
    {
        EvidenceStatusDto.Ready => "Success",
        EvidenceStatusDto.Unknown => "Info",
        _ => "Warning"
    };

    private static string ToneForStage(OperationsContinuityWorkflowDto workflow, int unresolvedBreakCount)
    {
        if (unresolvedBreakCount > 0 || workflow.Status == OperationsWorkflowStatusDto.Blocked)
        {
            return "Warning";
        }

        return workflow.Status == OperationsWorkflowStatusDto.Closed ? "Success" : "Info";
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

    private sealed record FinancialOperationsControlSnapshot(
        Guid FundAccountId,
        int WorkflowCount,
        OperationsContinuityWorkflowDto? ActiveWorkflow);
}
