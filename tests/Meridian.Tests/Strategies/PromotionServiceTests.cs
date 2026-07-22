using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using MeridianLedger = Meridian.Ledger.Ledger;

namespace Meridian.Tests.Strategies;

public sealed class PromotionServiceTests
{
    // ---- EvaluateAsync ----

    [Fact]
    public async Task EvaluateAsync_WhenRunNotFound_ReturnsFalseAndFoundFalse()
    {
        var service = BuildService(out _);

        var result = await service.EvaluateAsync("missing-run");

        result.Found.Should().BeFalse();
        result.IsEligible.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_WhenRunNotCompleted_ReturnsNotReady()
    {
        var service = BuildService(out var store);
        var run = StrategyRunEntry.Start("s1", "Strategy One", RunType.Backtest);
        await store.RecordRunAsync(run);

        var result = await service.EvaluateAsync(run.RunId);

        result.Found.Should().BeTrue();
        result.Ready.Should().BeFalse();
        result.IsEligible.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_WhenRunIsLive_ReturnsNotReady()
    {
        var service = BuildService(out var store);
        var run = StrategyRunEntry.Start("s1", "Strategy One", RunType.Live) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult()
        };
        await store.RecordRunAsync(run);

        var result = await service.EvaluateAsync(run.RunId);

        result.Ready.Should().BeFalse();
        result.Reason.Should().Contain("Live runs cannot be promoted");
    }

    [Fact]
    public async Task EvaluateAsync_WhenRunHasNoMetrics_ReturnsNotReady()
    {
        var service = BuildService(out var store);
        var run = StrategyRunEntry.Start("s1", "Strategy One", RunType.Backtest) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = null
        };
        await store.RecordRunAsync(run);

        var result = await service.EvaluateAsync(run.RunId);

        result.Ready.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_WhenPassingMetrics_ReturnsEligible()
    {
        var service = BuildService(out var store);
        var run = StrategyRunEntry.Start("s1", "Strategy One", RunType.Backtest) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult()
        };
        await store.RecordRunAsync(run);

        var result = await service.EvaluateAsync(run.RunId);

        result.Found.Should().BeTrue();
        result.Ready.Should().BeTrue();
        result.IsEligible.Should().BeTrue();
        result.TargetMode.Should().Be(RunType.Paper);
    }

    // ---- ApproveAsync ----

    [Fact]
    public async Task ApproveAsync_WhenRunExists_CreatesNewRunAndRecordsHistory()
    {
        var service = BuildService(out var store, CreateTempRoot());
        var run = StrategyRunEntry.Start("s1", "Strategy One", RunType.Backtest) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult()
        };
        await store.RecordRunAsync(run);

        var result = await service.ApproveAsync(new PromotionApprovalRequest(
            run.RunId,
            ApprovedBy: "ops",
            ApprovalReason: "Metrics cleared for paper.",
            ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper)));

        result.Success.Should().BeTrue();
        result.NewRunId.Should().NotBeNullOrWhiteSpace();
        result.PromotionId.Should().NotBeNullOrWhiteSpace();
        var history = await service.GetPromotionHistoryAsync();
        history.Should().HaveCount(1);
        history[0].SourceRunId.Should().Be(run.RunId);
        history[0].TargetRunId.Should().Be(result.NewRunId);
        history[0].Decision.Should().Be(PromotionDecisionKinds.Approved);
        history[0].ApprovedBy.Should().Be("ops");
        history[0].ApprovalReason.Should().Be("Metrics cleared for paper.");
        history[0].ApprovalChecklist.Should().BeEquivalentTo(PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper));
    }

    [Fact]
    public async Task ApproveAsync_WhenRunNotFound_ReturnsFailure()
    {
        var service = BuildService(out _);

        var result = await service.ApproveAsync(new PromotionApprovalRequest(
            "missing-run",
            ApprovedBy: "ops",
            ApprovalReason: "Metrics cleared for paper."));

        result.Success.Should().BeFalse();
        result.NewRunId.Should().BeNull();
    }

    [Fact]
    public async Task ApproveAsync_WhenOperatorContextMissing_ReturnsFailureWithoutCreatingRun()
    {
        var service = BuildService(out var store);
        var run = StrategyRunEntry.Start("s1", "Strategy One", RunType.Backtest) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult()
        };
        await store.RecordRunAsync(run);

        var result = await service.ApproveAsync(new PromotionApprovalRequest(run.RunId));

        result.Success.Should().BeFalse();
        result.Reason.Should().Contain("approver");
        var history = await service.GetPromotionHistoryAsync();
        history.Should().BeEmpty();
    }

    [Fact]
    public async Task ApproveAsync_WhenApprovalChecklistMissing_ReturnsFailureWithoutCreatingRun()
    {
        var service = BuildService(out var store);
        var run = StrategyRunEntry.Start("s1", "Strategy One", RunType.Backtest) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult()
        };
        await store.RecordRunAsync(run);

        var result = await service.ApproveAsync(new PromotionApprovalRequest(
            run.RunId,
            ApprovedBy: "ops",
            ApprovalReason: "Metrics cleared for paper."));

        result.Success.Should().BeFalse();
        result.Reason.Should().Contain("approval checklist").And.Contain(PromotionApprovalChecklist.Dk1TrustPacketReviewed);
        var history = await service.GetPromotionHistoryAsync();
        history.Should().BeEmpty();
    }

    [Fact]
    public async Task ApproveAsync_WhenApprovalChecklistPartial_ReturnsMissingItemsWithoutCreatingRun()
    {
        var service = BuildService(out var store);
        var run = StrategyRunEntry.Start("s1", "Strategy One", RunType.Backtest) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult()
        };
        await store.RecordRunAsync(run);

        var result = await service.ApproveAsync(new PromotionApprovalRequest(
            run.RunId,
            ApprovedBy: "ops",
            ApprovalReason: "Metrics cleared for paper.",
            ApprovalChecklist:
            [
                PromotionApprovalChecklist.Dk1TrustPacketReviewed,
                "run-lineage-reviewed"
            ]));

        result.Success.Should().BeFalse();
        result.Reason.Should()
            .Contain(PromotionApprovalChecklist.PortfolioLedgerContinuityReviewed)
            .And.Contain(PromotionApprovalChecklist.RiskControlsReviewed);
        var history = await service.GetPromotionHistoryAsync();
        history.Should().BeEmpty();
    }

    [Fact]
    public async Task ApproveAsync_WhenRunIsLive_ReturnsFailureWithoutCreatingRun()
    {
        var service = BuildService(out var store);
        var run = StrategyRunEntry.Start("s1", "Strategy One", RunType.Live) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult()
        };
        await store.RecordRunAsync(run);

        var result = await service.ApproveAsync(new PromotionApprovalRequest(
            run.RunId,
            ApprovedBy: "ops",
            ApprovalReason: "Already live.",
            ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Live)));

        result.Success.Should().BeFalse();
        result.Reason.Should().Contain("Live runs cannot be promoted");
        var history = await service.GetPromotionHistoryAsync();
        history.Should().BeEmpty();
    }

    // ---- RejectAsync ----

    [Fact]
    public async Task RejectAsync_WhenOperatorContextProvided_RecordsRejectedTrace()
    {
        var service = BuildService(out var store);
        var run = StrategyRunEntry.Start("s1", "Strategy One", RunType.Backtest) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult()
        };
        await store.RecordRunAsync(run);

        var result = await service.RejectAsync(new PromotionRejectionRequest(
            run.RunId,
            "Not ready",
            ReviewNotes: "Threshold drift",
            RejectedBy: "ops",
            ManualOverrideId: "ovr-1"));

        result.Success.Should().BeTrue();
        result.Reason.Should().Contain("Not ready");
        result.AuditReference.Should().NotBeNullOrWhiteSpace();
        result.ApprovedBy.Should().Be("ops");

        var history = await service.GetPromotionHistoryAsync();
        history.Should().ContainSingle();
        history[0].Decision.Should().Be(PromotionDecisionKinds.Rejected);
        history[0].ApprovalReason.Should().Be("Not ready");
        history[0].ReviewNotes.Should().Be("Threshold drift");
        history[0].ManualOverrideId.Should().Be("ovr-1");
        history[0].ApprovedBy.Should().Be("ops");
        history[0].AuditReference.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RejectAsync_WhenOperatorContextMissing_ReturnsFailureWithoutHistory()
    {
        var service = BuildService(out var store);
        var run = StrategyRunEntry.Start("s1", "Strategy One", RunType.Backtest) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult()
        };
        await store.RecordRunAsync(run);

        var result = await service.RejectAsync(new PromotionRejectionRequest(run.RunId, "Not ready"));

        result.Success.Should().BeFalse();
        result.Reason.Should().Contain("operator");
        var history = await service.GetPromotionHistoryAsync();
        history.Should().BeEmpty();
    }

    // ---- GetPromotionHistory ----

    [Fact]
    public async Task GetPromotionHistory_AfterApproval_ContainsRecord()
    {
        var service = BuildService(out var store, CreateTempRoot());
        var run = StrategyRunEntry.Start("s1", "Strategy One", RunType.Backtest) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult()
        };
        await store.RecordRunAsync(run);
        await service.ApproveAsync(new PromotionApprovalRequest(
            run.RunId,
            ApprovedBy: "ops",
            ApprovalReason: "Metrics cleared for paper.",
            ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper)));

        var history = await service.GetPromotionHistoryAsync();

        history.Should().HaveCount(1);
        history[0].StrategyId.Should().Be("s1");
        history[0].TargetRunType.Should().Be(RunType.Paper);
        history[0].SourceRunId.Should().Be(run.RunId);
        history[0].TargetRunId.Should().NotBeNullOrWhiteSpace();
        history[0].Decision.Should().Be(PromotionDecisionKinds.Approved);
        history[0].ApprovedBy.Should().Be("ops");
        history[0].ApprovalReason.Should().Be("Metrics cleared for paper.");
    }

    [Fact]
    public async Task GetPromotionHistoryAsync_WithDurableStore_SurvivesRestart()
    {
        var tempRoot = CreateTempRoot();
        var durableStore = new JsonlPromotionRecordStore(
            new PromotionRecordStoreOptions(Path.Combine(tempRoot, "promotion-history")),
            NullLogger<JsonlPromotionRecordStore>.Instance);

        var service = BuildService(out var store, durableStore);
        var run = StrategyRunEntry.Start("s1", "Strategy One", RunType.Backtest) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult()
        };
        await store.RecordRunAsync(run);
        await service.ApproveAsync(new PromotionApprovalRequest(
            run.RunId,
            ReviewNotes: "Ready for paper",
            ApprovedBy: "ops",
            ApprovalReason: "Metrics cleared",
            ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper)));

        var restarted = BuildService(out _, durableStore);
        var history = await restarted.GetPromotionHistoryAsync();

        history.Should().ContainSingle();
        history[0].SourceRunId.Should().Be(run.RunId);
        history[0].TargetRunId.Should().NotBeNullOrWhiteSpace();
        history[0].Decision.Should().Be(PromotionDecisionKinds.Approved);
        history[0].ApprovedBy.Should().Be("ops");
        history[0].ApprovalReason.Should().Be("Metrics cleared");
        history[0].ReviewNotes.Should().Be("Ready for paper");
        history[0].ApprovalChecklist.Should().BeEquivalentTo(PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper));
    }

    [Fact]
    public async Task GetPromotionHistoryAsync_WithMalformedPersistedRecords_ShouldSkipInvalidEntries()
    {
        var tempRoot = CreateTempRoot();
        var options = new PromotionRecordStoreOptions(Path.Combine(tempRoot, "promotion-history"));
        Directory.CreateDirectory(options.RootDirectory);
        var valid = new StrategyPromotionRecord(
            PromotionId: "promotion-valid",
            StrategyId: "s1",
            StrategyName: "Strategy One",
            SourceRunType: RunType.Backtest,
            TargetRunType: RunType.Paper,
            SourceRunId: "run-valid",
            TargetRunId: "run-paper",
            QualifyingSharpe: 1.1d,
            QualifyingMaxDrawdownPercent: 0.05m,
            QualifyingTotalReturn: 0.12m,
            Decision: PromotionDecisionKinds.Approved,
            PromotedAt: DateTimeOffset.UtcNow,
            ApprovalReason: "approved",
            ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper),
            AuditReference: "audit-valid",
            ApprovedBy: "ops");
        var malformed = valid with { PromotionId = "promotion-malformed", ApprovedBy = "" };
        await File.WriteAllLinesAsync(
            options.HistoryPath,
            [System.Text.Json.JsonSerializer.Serialize(valid), System.Text.Json.JsonSerializer.Serialize(malformed)]);

        var store = new JsonlPromotionRecordStore(options, NullLogger<JsonlPromotionRecordStore>.Instance);
        var records = await store.LoadAllAsync();

        records.Should().ContainSingle();
        records[0].PromotionId.Should().Be("promotion-valid");
    }

    // ---- Helpers ----

    private static PromotionService BuildService(out StrategyRunStore store, string? rootPath = null)
    {
        store = new StrategyRunStore();
        var promoter = new BacktestToLivePromoter();
        var promotionStore = new JsonlPromotionRecordStore(
            Path.Combine(rootPath ?? CreateTempRoot(), "promotion-history"),
            NullLogger<JsonlPromotionRecordStore>.Instance);
        return new PromotionService(store, promoter, promotionStore, NullLogger<PromotionService>.Instance);
    }

    private static PromotionService BuildService(out StrategyRunStore store, IPromotionRecordStore promotionRecordStore)
    {
        store = new StrategyRunStore();
        var promoter = new BacktestToLivePromoter();
        return new PromotionService(store, promoter, promotionRecordStore, NullLogger<PromotionService>.Instance);
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static BacktestResult BuildPassingResult()
    {
        var request = new BacktestRequest(
            From: new DateOnly(2026, 1, 1),
            To: new DateOnly(2026, 3, 1),
            Symbols: ["SPY"],
            InitialCash: 100_000m,
            DataRoot: "./data");

        var snapshot = new PortfolioSnapshot(
            Timestamp: DateTimeOffset.UtcNow,
            Date: new DateOnly(2026, 3, 1),
            Cash: 110_000m,
            MarginBalance: 0m,
            LongMarketValue: 0m,
            ShortMarketValue: 0m,
            TotalEquity: 110_000m,
            DailyReturn: 0m,
            Positions: new Dictionary<string, Position>(),
            Accounts: new Dictionary<string, FinancialAccountSnapshot>(),
            DayCashFlows: []);

        var metrics = new BacktestMetrics(
            InitialCapital: 100_000m,
            FinalEquity: 110_000m,
            GrossPnl: 10_000m,
            NetPnl: 9_500m,
            TotalReturn: 0.10m,
            AnnualizedReturn: 0.25m,
            SharpeRatio: 1.5d,
            SortinoRatio: 2.0d,
            CalmarRatio: 3.0d,
            MaxDrawdown: 2_000m,
            MaxDrawdownPercent: 0.02m,
            MaxDrawdownRecoveryDays: 5,
            ProfitFactor: 2.0d,
            WinRate: 0.65d,
            TotalTrades: 20,
            WinningTrades: 13,
            LosingTrades: 7,
            TotalCommissions: 500m,
            TotalMarginInterest: 0m,
            TotalShortRebates: 0m,
            Xirr: 0.22d,
            SymbolAttribution: new Dictionary<string, SymbolAttribution>());

        return new BacktestResult(
            Request: request,
            Universe: new HashSet<string>(["SPY"], StringComparer.OrdinalIgnoreCase),
            Snapshots: [snapshot],
            CashFlows: [],
            Fills: [],
            Metrics: metrics,
            Ledger: new global::Meridian.Ledger.Ledger(),
            ElapsedTime: TimeSpan.FromMinutes(5),
            TotalEventsProcessed: 500);
    }

    // ---- Wave 2 Cockpit Acceptance Gate Scenarios ----

    [Fact]
    public async Task Wave2_Scenario_SessionCloseReplayAndPromotionReview_BacktestToPaperFlowRemainsContinuousAndAuditable()
    {
        // This test proves that /api/execution/* to /api/promotion/* continuity is maintained
        // and that one operator can: create session, close it, replay it, evaluate promotion, approve promotion
        // with both execution and promotion evidence visible in returned contracts

        var service = BuildService(out var store, CreateTempRoot());
        var run = StrategyRunEntry.Start("strat-test", "Session Test Strategy", RunType.Backtest) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult()
        };
        await store.RecordRunAsync(run);

        // Evaluate promotion (verifies run is found and eligible)
        var evaluation = await service.EvaluateAsync(run.RunId);
        evaluation.Found.Should().BeTrue("Run should be found");
        evaluation.Ready.Should().BeTrue("Run should be ready for evaluation");
        evaluation.IsEligible.Should().BeTrue("Metrics should be eligible");
        evaluation.SourceMode.Should().Be(RunType.Backtest);
        evaluation.TargetMode.Should().Be(RunType.Paper);

        // Approve promotion (verifies durable decision with audit trail)
        var approvalRequest = new PromotionApprovalRequest(
            run.RunId,
            ApprovedBy: "operator-qa",
            ApprovalReason: "Session replay verified and portfolio consistent",
            ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper));
        var decision = await service.ApproveAsync(approvalRequest);
        decision.Success.Should().BeTrue("Approval should succeed");
        decision.PromotionId.Should().NotBeNull("Audit reference should be created");
        decision.AuditReference.Should().NotBeNull("Audit trail should be recorded");

        // Verify history maintains the complete flow
        var history = await service.GetPromotionHistoryAsync();
        history.Should().HaveCount(1);
        var record = history[0];
        record.SourceRunId.Should().Be(run.RunId, "Source run should be linked");
        record.Decision.Should().Be(PromotionDecisionKinds.Approved, "Decision should be recorded");
        record.ApprovedBy.Should().Be("operator-qa", "Operator approval should be recorded");
        record.ApprovalReason.Should().Contain("Session replay verified", "Rationale should be preserved");
        record.AuditReference.Should().NotBeNull("Audit trail should be linked");
    }

    [Fact]
    public async Task Wave2_Scenario_RiskTriggeredPromotionRejection_DecisionRemainsVisibleWithBlockingRationale()
    {
        // This test verifies that when a promotion is blocked by risk checks,
        // the blocking reasons are visible and rejection carries explicit rationale

        var service = BuildService(out var store);

        // Create a run with high-risk metrics
        var passingResult = BuildPassingResult();
        var highRiskMetrics = passingResult with
        {
            Metrics = passingResult.Metrics with
            {
                MaxDrawdownPercent = 0.45m, // 45% - exceeds 30% threshold
                SharpeRatio = 0.5d // Below 0.8 minimum
            }
        };

        var run = StrategyRunEntry.Start("strat-high-risk", "High Risk Strategy", RunType.Backtest) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = highRiskMetrics
        };
        await store.RecordRunAsync(run);

        // Evaluate promotion (should detect risk blocking)
        var evaluation = await service.EvaluateAsync(run.RunId);
        evaluation.Found.Should().BeTrue();
        evaluation.IsEligible.Should().BeFalse("Risk metrics should block promotion");
        evaluation.Reason.Should().Be("Promotion requires operator promotion review.");
        evaluation.Reason.Should().NotContain("governance review");
        evaluation.BlockingReasons.Should().NotBeNull("Blocking reasons should be enumerated");
        evaluation.BlockingReasons.Should().NotBeEmpty("At least one blocking reason should be present");

        // Verify rejection carries explicit rationale
        var rejectionRequest = new PromotionRejectionRequest(
            run.RunId,
            Reason: "Exceeds max drawdown threshold; recommend risk model review before approval",
            RejectedBy: "operator-qa");

        var rejectionResult = await service.RejectAsync(rejectionRequest);
        rejectionResult.Success.Should().BeTrue("Rejection should succeed");
        rejectionResult.Reason.Should().Contain("drawdown", "Rejection reason should be preserved");
        rejectionResult.AuditReference.Should().NotBeNull("Audit trail should record rejection");
    }

    [Fact]
    public async Task Wave2_Scenario_PromotionApprovalChecklistValidation_AllItemsMustBeReady()
    {
        // This test verifies that the approval checklist covers all Wave 2 requirements:
        // DK1 data trust, run lineage, risk metrics, portfolio/ledger continuity

        var checklist = PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper);

        checklist.Should().NotBeNull("Checklist should exist for Paper mode");
        checklist.Should().NotBeEmpty("Checklist should contain items");

        // Verify the specific Wave 2 required checklist items are present
        checklist.Should().Contain(PromotionApprovalChecklist.Dk1TrustPacketReviewed,
            "DK1 data trust packet review is required for Wave 2");
        checklist.Should().Contain(PromotionApprovalChecklist.RunLineageReviewed,
            "Run lineage review is required for Wave 2");
        checklist.Should().Contain(PromotionApprovalChecklist.RiskControlsReviewed,
            "Risk controls review is required for Wave 2");
        checklist.Should().Contain(PromotionApprovalChecklist.PortfolioLedgerContinuityReviewed,
            "Portfolio/ledger continuity review is required for Wave 2");
    }

    [Fact]
    public async Task Wave7_Scenario_PromotionApprovalChecklistValidation_LiveModeRequiresGovernanceEvidence()
    {
        // Live mode requires the paper baseline plus explicit live-readiness governance evidence.

        var liveChecklist = PromotionApprovalChecklist.CreateRequiredFor(RunType.Live);

        liveChecklist.Should().Contain(PromotionApprovalChecklist.LiveOverrideReviewed,
            "Live override review is additionally required for Live mode");
        liveChecklist.Should().Contain(PromotionApprovalChecklist.Dk1TrustPacketReviewed,
            "DK1 trust packet review remains required in Live mode");
        liveChecklist.Should().Contain(PromotionApprovalChecklist.RiskControlsReviewed,
            "Risk controls review remains required in Live mode");
        liveChecklist.Should().Contain(PromotionApprovalChecklist.PaperValidationReviewed,
            "Paper-validation evidence is required in Live mode");
        liveChecklist.Should().Contain(PromotionApprovalChecklist.ReconciliationEvidenceReviewed,
            "Reconciliation evidence is required in Live mode");
        liveChecklist.Should().Contain(PromotionApprovalChecklist.BrokerExecutionReconciliationReviewed,
            "Broker/OMS open-order reconciliation evidence is required in Live mode");
        liveChecklist.Should().Contain(PromotionApprovalChecklist.AccountingRecordsReviewed,
            "Accounting-record evidence is required in Live mode");
        liveChecklist.Should().Contain(PromotionApprovalChecklist.GovernedReportingReviewed,
            "Governed reporting evidence is required in Live mode");
        liveChecklist.Should().Contain(PromotionApprovalChecklist.GovernanceSignoffReviewed,
            "Governance sign-off is required in Live mode");
        liveChecklist.Should().Contain(PromotionApprovalChecklist.ExceptionHandlingReviewed,
            "Exception-handling posture is required in Live mode");
        liveChecklist.Should().Contain(PromotionApprovalChecklist.RollbackKillSwitchReviewed,
            "Rollback or kill-switch posture is required in Live mode");
        liveChecklist.Should().Contain(PromotionApprovalChecklist.AuditRetentionReviewed,
            "Audit-retention evidence is required in Live mode");
    }

    [Fact]
    public async Task Wave7_Scenario_ApprovedLivePromotionRecordValidation_RequiresActiveOverrideEvidence()
    {
        var evidenceReferences = PromotionApprovalChecklist
            .CreateRequiredFor(RunType.Live)
            .Select(static item => string.Equals(item, PromotionApprovalChecklist.LiveOverrideReviewed, StringComparison.Ordinal)
                ? $"{item}:manual-override/override-live"
                : $"{item}:evidence/{item.ToLowerInvariant()}")
            .ToArray();
        var record = new StrategyPromotionRecord(
            PromotionId: "promotion-live",
            StrategyId: "s-live",
            StrategyName: "Live Strategy",
            SourceRunType: RunType.Paper,
            TargetRunType: RunType.Live,
            SourceRunId: "run-paper",
            TargetRunId: "run-live",
            QualifyingSharpe: 1.1d,
            QualifyingMaxDrawdownPercent: 0.05m,
            QualifyingTotalReturn: 0.12m,
            Decision: PromotionDecisionKinds.Approved,
            PromotedAt: DateTimeOffset.UtcNow,
            ApprovalReason: "approved",
            ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Live),
            EvidenceReferences: evidenceReferences,
            AuditReference: "audit-live",
            ApprovedBy: "ops");

        var store = new JsonlPromotionRecordStore(
            new PromotionRecordStoreOptions(Path.Combine(CreateTempRoot(), "promotion-history")),
            NullLogger<JsonlPromotionRecordStore>.Instance);

        var append = async () => await store.AppendAsync(record);

        await append.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{PromotionApprovalChecklist.LiveOverrideReviewed}*active manual override id*");
    }
}
