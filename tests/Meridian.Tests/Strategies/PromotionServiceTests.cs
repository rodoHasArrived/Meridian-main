using FluentAssertions;
using Meridian.Backtesting.Sdk;
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

        var result = await service.ApproveAsync(new PromotionApprovalRequest(run.RunId));

        result.Success.Should().BeTrue();
        result.NewRunId.Should().NotBeNullOrWhiteSpace();
        result.PromotionId.Should().NotBeNullOrWhiteSpace();
        service.GetPromotionHistory().Should().HaveCount(1);
    }

    [Fact]
    public async Task ApproveAsync_WhenRunNotFound_ReturnsFailure()
    {
        var service = BuildService(out _);

        var result = await service.ApproveAsync(new PromotionApprovalRequest("missing-run"));

        result.Success.Should().BeFalse();
        result.NewRunId.Should().BeNull();
    }

    // ---- RejectAsync ----

    [Fact]
    public async Task RejectAsync_AlwaysReturnsSuccess()
    {
        var service = BuildService(out _, CreateTempRoot());

        var result = await service.RejectAsync(new PromotionRejectionRequest("any-run", "Not ready"));

        result.Success.Should().BeTrue();
        result.Reason.Should().Contain("Not ready");
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
        await service.ApproveAsync(new PromotionApprovalRequest(run.RunId));

        var history = service.GetPromotionHistory();

        history.Should().HaveCount(1);
        history[0].StrategyId.Should().Be("s1");
        history[0].TargetRunType.Should().Be(RunType.Paper);
        history[0].Decision.Should().Be("Approved");
        history[0].SourceRunId.Should().Be(run.RunId);
        history[0].TargetRunId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetPromotionHistory_SurvivesServiceReinstantiation_WithApprovedAndRejectedDecisions()
    {
        var rootPath = CreateTempRoot();
        var service = BuildService(out var store, rootPath);
        var run = StrategyRunEntry.Start("s1", "Strategy One", RunType.Backtest) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult()
        };
        await store.RecordRunAsync(run);
        await service.ApproveAsync(new PromotionApprovalRequest(
            RunId: run.RunId,
            ReviewNotes: "Initial review complete.",
            ApprovedBy: "ops",
            ApprovalReason: "All policy checks passed.",
            ManualOverrideId: "override-1"));
        await service.RejectAsync(new PromotionRejectionRequest(
            RunId: run.RunId,
            Reason: "Holding promotion pending additional controls.",
            ReviewNotes: "Rejected during governance review.",
            RejectedBy: "risk"));

        var restartedService = BuildService(out _, rootPath);
        var history = restartedService.GetPromotionHistory();

        history.Should().HaveCount(2);
        history.Should().Contain(record =>
            record.Decision == "Approved" &&
            record.SourceRunId == run.RunId &&
            !string.IsNullOrWhiteSpace(record.TargetRunId) &&
            record.ApprovedBy == "ops" &&
            record.ApprovalReason == "All policy checks passed." &&
            record.ReviewNotes == "Initial review complete." &&
            record.ManualOverrideId == "override-1" &&
            !string.IsNullOrWhiteSpace(record.AuditReference));
        history.Should().Contain(record =>
            record.Decision == "Rejected" &&
            record.SourceRunId == run.RunId &&
            record.TargetRunId is null &&
            record.ApprovedBy == "risk" &&
            record.ApprovalReason == "Holding promotion pending additional controls." &&
            record.ReviewNotes == "Rejected during governance review." &&
            !string.IsNullOrWhiteSpace(record.AuditReference));
    }

    // ---- Helpers ----

    private static PromotionService BuildService(out StrategyRunStore store, string? rootPath = null)
    {
        store = new StrategyRunStore();
        var promoter = new BacktestToLivePromoter();
        var promotionStore = new JsonlPromotionRecordStore(
            Path.Combine(rootPath ?? Path.GetTempPath(), "promotion-history"),
            NullLogger<JsonlPromotionRecordStore>.Instance);
        return new PromotionService(store, promoter, promotionStore, NullLogger<PromotionService>.Instance);
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "meridian-tests", Guid.NewGuid().ToString("N"));
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
