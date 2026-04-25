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
            ApprovalReason: "Metrics cleared for paper."));

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
            ApprovalReason: "Metrics cleared for paper."));

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
            ApprovalReason: "Metrics cleared"));

        var restarted = BuildService(out _, durableStore);
        var history = await restarted.GetPromotionHistoryAsync();

        history.Should().ContainSingle();
        history[0].SourceRunId.Should().Be(run.RunId);
        history[0].TargetRunId.Should().NotBeNullOrWhiteSpace();
        history[0].Decision.Should().Be(PromotionDecisionKinds.Approved);
        history[0].ApprovedBy.Should().Be("ops");
        history[0].ApprovalReason.Should().Be("Metrics cleared");
        history[0].ReviewNotes.Should().Be("Ready for paper");
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
}
