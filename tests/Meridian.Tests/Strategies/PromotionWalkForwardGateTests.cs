using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Execution.Sdk;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Strategies;

/// <summary>
/// Covers the overfitting-protection promotion gate: paper -> live eligibility must not be
/// judged on a single full-period backtest — walk-forward/out-of-sample evidence is required
/// and its out-of-sample metrics are enforced.
/// </summary>
public sealed class PromotionWalkForwardGateTests
{
    [Fact]
    public async Task EvaluateAsync_PaperRunTargetingLive_WithoutWalkForwardEvidence_IsNotEligible()
    {
        var service = BuildService(out var store);
        var run = StrategyRunEntry.Start("s-wf", "Strategy WF", RunType.Paper) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult()
        };
        await store.RecordRunAsync(run);

        var result = await service.EvaluateAsync(run.RunId);

        result.TargetMode.Should().Be(RunType.Live);
        result.IsEligible.Should().BeFalse();
        result.RequiresHumanApproval.Should().BeTrue();
        result.BlockingReasons.Should().NotBeNull();
        result.BlockingReasons!.Should().Contain(reason =>
            reason.Contains("walk-forward", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task EvaluateAsync_PaperRunTargetingLive_WithWeakOutOfSampleSharpe_IsNotEligible()
    {
        var service = BuildService(out var store);
        var run = StrategyRunEntry.Start("s-wf", "Strategy WF", RunType.Paper) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult(),
            WalkForwardEvidence = new StrategyRunWalkForwardEvidence(
                OutOfSampleSharpeRatio: -0.4,
                OutOfSampleMaxDrawdownPercent: 0.10m,
                DegradationRatio: 0.9,
                WindowCount: 6,
                RecordedAt: DateTimeOffset.UtcNow)
        };
        await store.RecordRunAsync(run);

        var result = await service.EvaluateAsync(run.RunId);

        result.IsEligible.Should().BeFalse();
        result.BlockingReasons.Should().NotBeNull();
        result.BlockingReasons!.Should().Contain(reason =>
            reason.Contains("Out-of-sample Sharpe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task EvaluateAsync_PaperRunTargetingLive_WithExcessiveOutOfSampleDrawdown_IsNotEligible()
    {
        var service = BuildService(out var store);
        var run = StrategyRunEntry.Start("s-wf", "Strategy WF", RunType.Paper) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult(),
            WalkForwardEvidence = new StrategyRunWalkForwardEvidence(
                OutOfSampleSharpeRatio: 0.9,
                OutOfSampleMaxDrawdownPercent: 0.45m,
                DegradationRatio: 0.8,
                WindowCount: 6,
                RecordedAt: DateTimeOffset.UtcNow)
        };
        await store.RecordRunAsync(run);

        var result = await service.EvaluateAsync(run.RunId);

        result.IsEligible.Should().BeFalse();
        result.BlockingReasons.Should().NotBeNull();
        result.BlockingReasons!.Should().Contain(reason =>
            reason.Contains("Out-of-sample max drawdown", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task EvaluateAsync_PaperRunTargetingLive_WithExcessiveDegradation_IsNotEligible()
    {
        var service = BuildService(out var store);
        var run = StrategyRunEntry.Start("s-wf", "Strategy WF", RunType.Paper) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult(),
            WalkForwardEvidence = new StrategyRunWalkForwardEvidence(
                OutOfSampleSharpeRatio: 0.6,
                OutOfSampleMaxDrawdownPercent: 0.10m,
                DegradationRatio: 0.2,
                WindowCount: 6,
                RecordedAt: DateTimeOffset.UtcNow)
        };
        await store.RecordRunAsync(run);

        var result = await service.EvaluateAsync(run.RunId);

        result.IsEligible.Should().BeFalse();
        result.BlockingReasons.Should().NotBeNull();
        result.BlockingReasons!.Should().Contain(reason =>
            reason.Contains("degradation ratio", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task EvaluateAsync_BacktestRunTargetingPaper_WithoutWalkForwardEvidence_StaysEligible()
    {
        // The research -> paper step stays reachable on full-period metrics alone;
        // the walk-forward requirement applies to the paper -> live step.
        var service = BuildService(out var store);
        var run = StrategyRunEntry.Start("s-wf", "Strategy WF", RunType.Backtest) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult()
        };
        await store.RecordRunAsync(run);

        var result = await service.EvaluateAsync(run.RunId);

        result.TargetMode.Should().Be(RunType.Paper);
        result.IsEligible.Should().BeTrue(result.Reason);
    }

    [Fact]
    public async Task EvaluateAsync_BacktestRunTargetingPaper_WithWeakRecordedEvidence_EnforcesOutOfSampleGates()
    {
        // Evidence is optional for paper targets, but once recorded its gates are enforced
        // so a run that demonstrably overfits cannot advance anywhere.
        var service = BuildService(out var store);
        var run = StrategyRunEntry.Start("s-wf", "Strategy WF", RunType.Backtest) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult(),
            WalkForwardEvidence = new StrategyRunWalkForwardEvidence(
                OutOfSampleSharpeRatio: -1.0,
                OutOfSampleMaxDrawdownPercent: 0.40m,
                DegradationRatio: 0.1,
                WindowCount: 4,
                RecordedAt: DateTimeOffset.UtcNow)
        };
        await store.RecordRunAsync(run);

        var result = await service.EvaluateAsync(run.RunId);

        result.IsEligible.Should().BeFalse();
    }

    [Fact]
    public async Task RecordWalkForwardEvidenceAsync_AttachesEvidenceAndUnblocksEvaluation()
    {
        var service = BuildService(out var store);
        var run = StrategyRunEntry.Start("s-wf", "Strategy WF", RunType.Paper) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult()
        };
        await store.RecordRunAsync(run);

        var before = await service.EvaluateAsync(run.RunId);
        before.BlockingReasons.Should().NotBeNull();
        before.BlockingReasons!.Should().Contain(reason =>
            reason.Contains("walk-forward", StringComparison.OrdinalIgnoreCase));

        var updated = await service.RecordWalkForwardEvidenceAsync(
            run.RunId,
            new StrategyRunWalkForwardEvidence(
                OutOfSampleSharpeRatio: 1.0,
                OutOfSampleMaxDrawdownPercent: 0.09m,
                DegradationRatio: 0.8,
                WindowCount: 8,
                RecordedAt: DateTimeOffset.UtcNow,
                SourceReference: "reports/wf-run.json"));

        updated.Should().NotBeNull();
        updated!.WalkForwardEvidence.Should().NotBeNull();

        var after = await service.EvaluateAsync(run.RunId);
        after.BlockingReasons?.Should().NotContain(reason =>
            reason.Contains("walk-forward", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task EvaluateAsync_WithEvidenceRequirementDisabledInCriteria_SkipsWalkForwardGate()
    {
        var service = BuildService(out var store);
        var run = StrategyRunEntry.Start("s-wf", "Strategy WF", RunType.Paper) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult()
        };
        await store.RecordRunAsync(run);

        var criteria = PromotionCriteria.Default with { RequireWalkForwardEvidenceForLive = false };
        var result = await service.EvaluateAsync(run.RunId, criteria);

        result.BlockingReasons?.Should().NotContain(reason =>
            reason.Contains("walk-forward", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RecordWalkForwardEvidenceAsync_UnknownRun_ReturnsNull()
    {
        var service = BuildService(out _);

        var updated = await service.RecordWalkForwardEvidenceAsync(
            "missing-run",
            new StrategyRunWalkForwardEvidence(1.0, 0.1m, 0.8, 6, DateTimeOffset.UtcNow));

        updated.Should().BeNull();
    }

    private static PromotionService BuildService(out StrategyRunStore store)
    {
        store = new StrategyRunStore();
        var promotionStore = new JsonlPromotionRecordStore(
            Path.Combine(Path.Combine(Path.GetTempPath(), $"mdc-wf-gate-{Guid.NewGuid():N}"), "promotion-history"),
            NullLogger<JsonlPromotionRecordStore>.Instance);
        return new PromotionService(
            store,
            new BacktestToLivePromoter(),
            promotionStore,
            NullLogger<PromotionService>.Instance);
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
            Ledger: new Meridian.Ledger.Ledger(),
            ElapsedTime: TimeSpan.FromMinutes(5),
            TotalEventsProcessed: 500);
    }
}
