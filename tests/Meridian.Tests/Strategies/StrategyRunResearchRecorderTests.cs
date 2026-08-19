using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Strategies;

/// <summary>
/// Covers recording of research-originated backtests into the shared strategy-run store.
/// </summary>
/// <remarks>
/// Backtests run from a script previously left no trace in the run store, so Quant Lab research was
/// invisible to the promotion path and could not be compared against Studio runs. These tests pin
/// the lineage that fixes it, and the fail-open/fail-closed split that keeps a recording problem
/// from either destroying a researcher's work or manufacturing promotion evidence.
/// </remarks>
public sealed class StrategyRunResearchRecorderTests
{
    private static StrategyRunResearchRecorder CreateRecorder(out StrategyRunStore store)
    {
        store = new StrategyRunStore();
        return new StrategyRunResearchRecorder(store, NullLogger<StrategyRunResearchRecorder>.Instance);
    }

    private static BacktestResult Result()
    {
        var metrics = new BacktestMetrics(
            InitialCapital: 100_000m, FinalEquity: 110_000m, GrossPnl: 10_500m, NetPnl: 10_000m,
            TotalReturn: 0.10m, AnnualizedReturn: 0.21m, SharpeRatio: 1.4, SortinoRatio: 1.8,
            CalmarRatio: 1.1, MaxDrawdown: 2_000m, MaxDrawdownPercent: 0.02m, MaxDrawdownRecoveryDays: 9,
            ProfitFactor: 2.1, WinRate: 0.6, TotalTrades: 20, WinningTrades: 12, LosingTrades: 8,
            TotalCommissions: 50m, TotalMarginInterest: 0m, TotalShortRebates: 0m, Xirr: 0.2,
            SymbolAttribution: new Dictionary<string, SymbolAttribution>());

        return new BacktestResult(
            Request: new BacktestRequest(
                From: new DateOnly(2026, 1, 1),
                To: new DateOnly(2026, 6, 30),
                Symbols: ["SPY"],
                InitialCash: 100_000m),
            Universe: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SPY" },
            Snapshots: [],
            CashFlows: [],
            Fills: [],
            Metrics: metrics,
            Ledger: new Meridian.Ledger.Ledger(),
            ElapsedTime: TimeSpan.FromSeconds(1),
            TotalEventsProcessed: 100);
    }

    private static ExecutionRealismDescriptor Realism(
        FillConservatism conservatism = FillConservatism.Conservative) => new(
            ExecutionModel.Auto,
            FillTiming.NextBar,
            conservatism,
            DelistingPolicy.LiquidateAtLastPrice,
            0m,
            5,
            BacktestCommissionKind.PerShare,
            0.005m,
            1.00m,
            decimal.MaxValue,
            5m,
            0m,
            0.1m,
            0m,
            true,
            0.04);

    // ── Lineage ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task RecordAsync_PersistsARetrievableRun()
    {
        var recorder = CreateRecorder(out var store);

        var runId = await recorder.RecordAsync(
            new ResearchRunDescriptor("strat-1", "Momentum"),
            Result());

        runId.Should().NotBeNullOrWhiteSpace();
        var stored = await store.GetRunByIdAsync(runId!);
        stored.Should().NotBeNull();
        stored!.StrategyId.Should().Be("strat-1");
        stored.RunType.Should().Be(RunType.Backtest);
    }

    [Fact]
    public async Task RecordAsync_PreservesTheCorrelationBackToTheSourceArtifact()
    {
        // This is the whole point of recording: a run must be traceable to the notebook cell that
        // produced it, otherwise it is just another anonymous row.
        var recorder = CreateRecorder(out var store);

        var runId = await recorder.RecordAsync(
            new ResearchRunDescriptor("strat-1", "Momentum", CorrelationId: "notebook:abc#cell:3"),
            Result());

        var stored = await store.GetRunByIdAsync(runId!);
        stored!.CorrelationId.Should().Be("notebook:abc#cell:3");
    }

    [Fact]
    public async Task RecordAsync_MarksTheRunCompletedWithItsMetrics()
    {
        var recorder = CreateRecorder(out var store);
        var result = Result();

        var runId = await recorder.RecordAsync(new ResearchRunDescriptor("strat-1", "Momentum"), result);

        var stored = await store.GetRunByIdAsync(runId!);
        stored!.EndedAt.Should().NotBeNull();
        stored.LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.Completed);
        stored.Metrics.Should().BeSameAs(result);
    }

    [Fact]
    public async Task RecordAsync_FallsBackToTheStrategyIdWhenNoNameIsGiven()
    {
        var recorder = CreateRecorder(out var store);

        var runId = await recorder.RecordAsync(new ResearchRunDescriptor("strat-1", "strat-1"), Result());

        (await store.GetRunByIdAsync(runId!))!.StrategyName.Should().Be("strat-1");
    }

    // ── Realism participates in recorded identity ────────────────────────────

    [Fact]
    public async Task RecordAsync_RealismChangesTheRecordedInputHash()
    {
        var recorder = CreateRecorder(out var store);

        var conservative = await recorder.RecordAsync(
            new ResearchRunDescriptor("strat-1", "Momentum", ExecutionRealism: Realism()),
            Result());
        var optimistic = await recorder.RecordAsync(
            new ResearchRunDescriptor(
                "strat-1",
                "Momentum",
                ExecutionRealism: Realism(FillConservatism.Optimistic)),
            Result());

        var a = await store.GetRunByIdAsync(conservative!);
        var b = await store.GetRunByIdAsync(optimistic!);

        a!.InputHashSha256.Should().NotBeNullOrWhiteSpace();
        b!.InputHashSha256.Should().NotBe(a.InputHashSha256);
    }

    [Fact]
    public async Task RecordAsync_IdenticalInputsProduceTheSameInputHash()
    {
        var recorder = CreateRecorder(out var store);

        var first = await recorder.RecordAsync(
            new ResearchRunDescriptor("strat-1", "Momentum", ExecutionRealism: Realism()), Result());
        var second = await recorder.RecordAsync(
            new ResearchRunDescriptor("strat-1", "Momentum", ExecutionRealism: Realism()), Result());

        var a = await store.GetRunByIdAsync(first!);
        var b = await store.GetRunByIdAsync(second!);

        b!.InputHashSha256.Should().Be(a!.InputHashSha256);
        b.RunId.Should().NotBe(a.RunId, "each execution is its own run even when the inputs match");
    }

    // ── Guards ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task RecordAsync_RejectsAMissingStrategyIdentity()
    {
        var recorder = CreateRecorder(out _);

        var act = async () => await recorder.RecordAsync(
            new ResearchRunDescriptor("  ", "Momentum"), Result());

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RecordAsync_RejectsANullResult()
    {
        var recorder = CreateRecorder(out _);

        var act = async () => await recorder.RecordAsync(
            new ResearchRunDescriptor("strat-1", "Momentum"), null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RecordAsync_HonoursCancellation()
    {
        var recorder = CreateRecorder(out _);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await recorder.RecordAsync(
            new ResearchRunDescriptor("strat-1", "Momentum"), Result(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
