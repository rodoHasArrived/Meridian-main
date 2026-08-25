using System.Text.Json;
using FluentAssertions;
using Meridian.Core.Serialization;
using Meridian.Backtesting.Engine;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Storage;
using Meridian.Storage.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Backtesting.Tests;

/// <summary>
/// Integration tests for <see cref="BacktestEngine"/> using real temporary JSONL data on disk.
/// Exercises the full replay loop without requiring live infrastructure: strategy callbacks,
/// order placement, fill processing, daily snapshots, and result metrics.
/// </summary>
public sealed class BacktestEngineIntegrationTests : IDisposable
{
    private readonly string _dataRoot;
    private readonly BacktestEngine _engine;

    public BacktestEngineIntegrationTests()
    {
        _dataRoot = Path.Combine(Path.GetTempPath(), $"meridian-backtest-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dataRoot);

        var catalog = new StorageCatalogService(_dataRoot, new StorageOptions());
        _engine = new BacktestEngine(NullLogger<BacktestEngine>.Instance, catalog);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    // ------------------------------------------------------------------ //
    //  Empty universe                                                      //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task RunAsync_EmptyDataRoot_ReturnsEmptyResult()
    {
        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 3),
            DataRoot: _dataRoot);

        var result = await _engine.RunAsync(request, new NoOpStrategy());

        result.Should().NotBeNull();
        result.TotalEventsProcessed.Should().Be(0);
        result.Universe.Should().BeEmpty();
        result.Fills.Should().BeEmpty();
    }

    // ------------------------------------------------------------------ //
    //  Single-symbol bar replay                                           //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task RunAsync_SingleSymbolBarData_CallsOnBarForEveryBar()
    {
        WriteBarJsonl("AAPL", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 3), basePrice: 185m);

        var strategy = new BarTrackingStrategy();
        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 3),
            DataRoot: _dataRoot);

        await _engine.RunAsync(request, strategy);

        strategy.BarsReceived.Should().Be(2, "one bar per trading day was written to disk");
        strategy.Symbols.Should().ContainSingle().Which.Should().Be("AAPL");
    }

    [Fact]
    public async Task RunAsync_SingleSymbolBarData_RecordsOneDailySnapshotPerDay()
    {
        WriteBarJsonl("SPY", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 5), basePrice: 470m);

        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 5),
            DataRoot: _dataRoot);

        var result = await _engine.RunAsync(request, new NoOpStrategy());

        result.Snapshots.Should().HaveCount(4, "one snapshot is taken at end of each of the 4 requested days");
    }

    // ------------------------------------------------------------------ //
    //  Buy-and-hold order placement                                       //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task RunAsync_BuyAndHoldStrategy_ProducesPositiveEquity()
    {
        WriteBarJsonl("MSFT", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 5), basePrice: 400m, dailyGain: 1m);

        var strategy = new BuyFirstBarStrategy("MSFT", quantity: 10);
        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 5),
            InitialCash: 100_000m,
            DataRoot: _dataRoot);

        var result = await _engine.RunAsync(request, strategy);

        result.Fills.Should().NotBeEmpty("the buy order should fill on the first bar");
        result.Metrics.FinalEquity.Should().BeGreaterThan(100_000m,
            "a rising stock with a long position increases total equity");
    }

    [Fact]
    public async Task RunAsync_BuyAndHoldStrategy_FillsAtBarMidpoint()
    {
        WriteBarJsonl("TSLA", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 2), basePrice: 200m);

        var strategy = new BuyFirstBarStrategy("TSLA", quantity: 5);
        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 2),
            DataRoot: _dataRoot,
            // Single-bar fixture: only same-bar execution can fill at all, which is exactly the
            // legacy midpoint behaviour this test documents.
            FillTiming: FillTiming.SameBar);

        var result = await _engine.RunAsync(request, strategy);

        result.Fills.Should().HaveCount(1);
        var fill = result.Fills[0];
        fill.Symbol.Should().Be("TSLA");
        fill.FilledQuantity.Should().Be(5);
        fill.FillPrice.Should().BeInRange(190m, 215m, "bar midpoint fill should land within the OHLC range");
    }

    // ------------------------------------------------------------------ //
    //  Multi-symbol universe                                              //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task RunAsync_MultiSymbolData_UniverseContainsAllSymbols()
    {
        WriteBarJsonl("AAPL", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 2), basePrice: 185m);
        WriteBarJsonl("GOOG", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 2), basePrice: 140m);
        WriteBarJsonl("NVDA", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 2), basePrice: 495m);

        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 2),
            DataRoot: _dataRoot);

        var result = await _engine.RunAsync(request, new NoOpStrategy());

        result.Universe.Should().BeEquivalentTo(
            new[] { "AAPL", "GOOG", "NVDA" },
            opts => opts.WithoutStrictOrdering(),
            "all three symbols with JSONL data must be discovered");
    }

    [Fact]
    public async Task RunAsync_SymbolFilter_RestrictsUniverseToRequestedSymbols()
    {
        WriteBarJsonl("AAPL", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 2), basePrice: 185m);
        WriteBarJsonl("GOOG", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 2), basePrice: 140m);

        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 2),
            Symbols: ["AAPL"],
            DataRoot: _dataRoot);

        var result = await _engine.RunAsync(request, new NoOpStrategy());

        result.Universe.Should().ContainSingle().Which.Should().Be("AAPL",
            "symbol filter must restrict universe to only requested symbols");
    }

    [Fact]
    public async Task RunAsync_EqualTimestampEvents_RespectStableSymbolOrder()
    {
        WriteEventJsonl("MSFT", new DateTimeOffset(2024, 1, 2, 14, 30, 0, TimeSpan.Zero));
        WriteEventJsonl("AAPL", new DateTimeOffset(2024, 1, 2, 14, 30, 0, TimeSpan.Zero));

        var strategy = new OrderedSymbolCaptureStrategy();
        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 2),
            Symbols: ["MSFT", "AAPL"],
            DataRoot: _dataRoot);

        await _engine.RunAsync(request, strategy);

        strategy.BarSymbolsInArrivalOrder.Should().Equal(
            ["MSFT", "AAPL"],
            "equal-timestamp events should be ordered by stable stream/symbol order");
    }

    [Fact]
    public async Task RunAsync_EqualTimestampEvents_AreRepeatableAcrossRuns()
    {
        WriteEventJsonl("MSFT", new DateTimeOffset(2024, 1, 2, 14, 30, 0, TimeSpan.Zero));
        WriteEventJsonl("AAPL", new DateTimeOffset(2024, 1, 2, 14, 30, 0, TimeSpan.Zero));

        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 2),
            Symbols: ["MSFT", "AAPL"],
            DataRoot: _dataRoot);

        var firstRunStrategy = new OrderedSymbolCaptureStrategy();
        var secondRunStrategy = new OrderedSymbolCaptureStrategy();

        await _engine.RunAsync(request, firstRunStrategy);
        await _engine.RunAsync(request, secondRunStrategy);

        firstRunStrategy.BarSymbolsInArrivalOrder.Should().Equal(secondRunStrategy.BarSymbolsInArrivalOrder,
            "equal-timestamp merge ordering should be deterministic across repeated runs");
        firstRunStrategy.BarSymbolsInArrivalOrder.Should().Equal(["MSFT", "AAPL"]);
    }

    // ------------------------------------------------------------------ //
    //  UTC date boundary filtering (regression: FilterBySymbolAndDate    //
    //  must use UtcDateTime.Date, not LocalDateTime.Date)                //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Regression test: FilterBySymbolAndDate must use UtcDateTime.Date, not LocalDateTime.Date.
    /// An event whose UTC date is 2024-01-16 (but whose embedded offset would produce a different
    /// LocalDateTime on a non-UTC machine) must be excluded from a backtest requested only through
    /// 2024-01-15.
    /// </summary>
    [Fact]
    public async Task RunAsync_EventTimestampCrossesUtcMidnight_ExcludedByUtcDate()
    {
        // Bar whose UTC timestamp is 2024-01-16T00:00:00Z (i.e. the next day in UTC).
        // If filtering used LocalDateTime the date on this machine would still be 2024-01-16 in UTC
        // (no offset difference), but an offset of -01:00 shifts LocalDateTime to 2024-01-15 23:00
        // while UtcDateTime remains 2024-01-16 00:00.  Prove the correct path is taken.
        var ts = new DateTimeOffset(2024, 1, 16, 0, 0, 0, TimeSpan.FromHours(-1));
        // ts.LocalDateTime = 2024-01-15 23:00:00 (offset -01:00)
        // ts.UtcDateTime   = 2024-01-16 00:00:00

        WriteEventJsonl("SPY", ts);

        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 15),
            To: new DateOnly(2024, 1, 15),   // range is ONLY Jan 15
            DataRoot: _dataRoot);

        var strategy = new BarTrackingStrategy();
        await _engine.RunAsync(request, strategy);

        strategy.BarsReceived.Should().Be(0,
            "the event's UTC date is 2024-01-16, which is outside the requested range ending on 2024-01-15; " +
            "LocalDateTime would incorrectly classify it as 2024-01-15 — the filter must use UtcDateTime");
    }

    /// <summary>
    /// Regression test: an event whose UTC date falls exactly on the last requested day must be
    /// included even when its LocalDateTime (in a positive-offset timezone) would push it past that
    /// boundary.
    /// </summary>
    [Fact]
    public async Task RunAsync_EventTimestampOnLastDay_IncludedByUtcDate()
    {
        // UTC date 2024-01-15 23:00:00Z — local time on a UTC+2 machine would be 2024-01-16 01:00
        // (next day), but the filter must include it because UtcDateTime.Date = 2024-01-15.
        var ts = new DateTimeOffset(2024, 1, 15, 23, 0, 0, TimeSpan.Zero);

        WriteEventJsonl("SPY", ts);

        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 15),
            To: new DateOnly(2024, 1, 15),
            DataRoot: _dataRoot);

        var strategy = new BarTrackingStrategy();
        await _engine.RunAsync(request, strategy);

        strategy.BarsReceived.Should().Be(1,
            "the event's UTC date is 2024-01-15, which is within the requested range; " +
            "LocalDateTime in a positive-offset timezone would wrongly push it to 2024-01-16");
    }

    // ------------------------------------------------------------------ //
    //  Fill rejection does not crash the engine (regression: domain       //
    //  violations from ProcessFill must be caught; backtest continues)   //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Regression test: when a strategy places a short-sell order on an account that has
    /// AllowShortSelling=false, SimulatedPortfolio.ProcessFill throws InvalidOperationException.
    /// The engine must catch this domain violation, log a warning, and continue the replay loop
    /// rather than propagating the exception to the caller.
    /// </summary>
    [Fact]
    public async Task RunAsync_StrategyAttemptsShortSellOnRestrictedAccount_RunCompletesWithoutException()
    {
        WriteBarJsonl("AAPL", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 4), basePrice: 185m);

        var restrictedAccount = new FinancialAccount(
            BacktestDefaults.DefaultBrokerageAccountId,
            "No-Short Brokerage",
            FinancialAccountKind.Brokerage,
            InitialCash: 100_000m,
            Rules: new FinancialAccountRules(AllowMargin: true, AllowShortSelling: false));

        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 4),
            DataRoot: _dataRoot,
            Accounts: [restrictedAccount]);

        var strategy = new ShortFirstBarStrategy("AAPL", quantity: 10);

        // Must NOT throw — the engine catches InvalidOperationException from ProcessFill and
        // discards the fill instead of surfacing it as a backtest failure.
        var result = await _engine.RunAsync(request, strategy);

        result.Should().NotBeNull("RunAsync must complete normally");
        result.Fills.Should().BeEmpty(
            "the short-sell fill was rejected by the account rule; no fills should be recorded");
        strategy.FillCallbacks.Should().BeEmpty(
            "portfolio-rejected fill candidates must never reach strategy callbacks");
    }

    [Fact]
    public async Task RunAsync_RejectedMarketImpactSlices_DoNotRemoveRemainingOrderQuantity()
    {
        WriteCustomBarJsonl(
            "AAPL",
            (new DateOnly(2024, 1, 2), 100m, 100m, 100m, 100m, 40),
            (new DateOnly(2024, 1, 3), 1m, 1m, 1m, 1m, 40));

        var restrictedAccount = new FinancialAccount(
            BacktestDefaults.DefaultBrokerageAccountId,
            "No-Margin Brokerage",
            FinancialAccountKind.Brokerage,
            InitialCash: 500m,
            Rules: new FinancialAccountRules(AllowMargin: false, AllowShortSelling: true));

        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 3),
            DataRoot: _dataRoot,
            Accounts: [restrictedAccount],
            // Same-bar timing keeps the regression scenario intact: the first slices must be
            // accepted/rejected against the expensive first bar, then complete on the cheap second bar.
            FillTiming: FillTiming.SameBar);

        var strategy = new BuyFirstBarWithMarketImpactGtcStrategy("AAPL", quantity: 20);
        var result = await _engine.RunAsync(request, strategy);

        result.Fills.Sum(static fill => fill.FilledQuantity).Should().Be(20,
            "the first accepted slice should keep the order pending so remaining quantity can fill later");
    }

    [Fact]
    public async Task RunAsync_WhenEveryProposedFillIsRejected_GtcOrderRemainsWorking()
    {
        WriteCustomBarJsonl(
            "AAPL",
            (new DateOnly(2024, 1, 2), 100m, 100m, 100m, 100m, 1_000),
            (new DateOnly(2024, 1, 3), 1m, 1m, 1m, 1m, 1_000));

        var cashOnlyAccount = new FinancialAccount(
            BacktestDefaults.DefaultBrokerageAccountId,
            "Cash Brokerage",
            FinancialAccountKind.Brokerage,
            InitialCash: 50m,
            Rules: new FinancialAccountRules(AllowMargin: false, AllowShortSelling: true));

        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 3),
            DataRoot: _dataRoot,
            Accounts: [cashOnlyAccount],
            FillTiming: FillTiming.SameBar);

        var result = await _engine.RunAsync(
            request,
            new SubmitOnceStrategy(new OrderRequest(
                "AAPL",
                1L,
                OrderType.Market,
                TimeInForce: TimeInForce.GoodTilCancelled)));

        result.Fills.Should().ContainSingle();
        result.Fills[0].FilledAt.UtcDateTime.Date.Should().Be(new DateTime(2024, 1, 3));
        result.Fills[0].FilledQuantity.Should().Be(1L,
            "the rejected first attempt must not advance or remove the authoritative working order");
    }

    [Fact]
    public async Task RunAsync_CancelOrder_RemovesAnAlreadyWorkingOrder()
    {
        WriteCustomBarJsonl(
            "AAPL",
            (new DateOnly(2024, 1, 2), 100m, 105m, 95m, 100m, 1_000),
            (new DateOnly(2024, 1, 3), 100m, 105m, 95m, 100m, 1_000),
            (new DateOnly(2024, 1, 4), 10m, 15m, 5m, 10m, 1_000));

        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 4),
            DataRoot: _dataRoot,
            FillTiming: FillTiming.SameBar);

        var result = await _engine.RunAsync(request, new CancelWorkingOrderOnSecondBarStrategy());

        result.Fills.Should().BeEmpty(
            "the GTC limit order was cancelled before the later bar crossed its limit");
    }

    [Fact]
    public async Task RunAsync_CancelContingentOrdersFromFillCallback_RemovesAttachedExits()
    {
        WriteMultiLevelLobJsonl(
            "AAPL",
            (new DateTimeOffset(2024, 1, 2, 14, 30, 0, TimeSpan.Zero),
                [(99m, 1_000L)],
                [(100m, 4L), (101m, 6L)]),
            (new DateTimeOffset(2024, 1, 2, 14, 30, 1, TimeSpan.Zero),
                [(120m, 1_000L)],
                [(121m, 1_000L)]));

        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 2),
            DataRoot: _dataRoot,
            FillTiming: FillTiming.SameBar);

        var result = await _engine.RunAsync(request, new CancelBracketExitsOnEntryFillStrategy());

        result.Fills.Should().HaveCount(2, "the entry walked two order-book levels");
        result.Fills.Should().OnlyContain(static fill => fill.FilledQuantity > 0,
            "the first entry-fill callback cancelled every contingent slice before the next snapshot");
        result.Fills.Sum(static fill => fill.FilledQuantity).Should().Be(10L);
    }

    [Fact]
    public async Task RunAsync_ImmediateOrCancelPartialFill_DoesNotFillRemainderLater()
    {
        WriteLobJsonl("SPY",
            (new DateTimeOffset(2024, 1, 2, 14, 30, 0, TimeSpan.Zero), 100m, 40L),
            (new DateTimeOffset(2024, 1, 2, 14, 30, 1, TimeSpan.Zero), 100m, 100L));

        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 2),
            DataRoot: _dataRoot,
            FillTiming: FillTiming.SameBar);

        var result = await _engine.RunAsync(
            request,
            new SubmitOnceStrategy(new OrderRequest(
                "SPY",
                100L,
                OrderType.Market,
                TimeInForce: TimeInForce.ImmediateOrCancel,
                ExecutionModel: ExecutionModel.OrderBook)));

        result.Fills.Sum(static fill => fill.FilledQuantity).Should().Be(40L);
        result.Fills.Should().ContainSingle(
            "the unfilled IOC remainder is terminal and cannot consume the later snapshot");
    }

    [Fact]
    public async Task RunAsync_FillOrKillWithInsufficientDepth_DoesNotFillOnLaterSnapshot()
    {
        WriteLobJsonl("SPY",
            (new DateTimeOffset(2024, 1, 2, 14, 30, 0, TimeSpan.Zero), 100m, 40L),
            (new DateTimeOffset(2024, 1, 2, 14, 30, 1, TimeSpan.Zero), 100m, 100L));

        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 2),
            DataRoot: _dataRoot,
            FillTiming: FillTiming.SameBar);

        var result = await _engine.RunAsync(
            request,
            new SubmitOnceStrategy(new OrderRequest(
                "SPY",
                100L,
                OrderType.Market,
                TimeInForce: TimeInForce.FillOrKill,
                ExecutionModel: ExecutionModel.OrderBook)));

        result.Fills.Should().BeEmpty(
            "an FOK order cancelled against the first snapshot cannot become eligible later");
    }

    [Fact]
    public async Task RunAsync_FillOrKillBatchRejectedOnLaterSlice_AcceptsNoSlices()
    {
        WriteMultiLevelLobJsonl(
            "SPY",
            (new DateTimeOffset(2024, 1, 2, 14, 30, 0, TimeSpan.Zero),
                [(99m, 1_000L)],
                [(100m, 1L), (101m, 1L)]));

        var cashOnlyAccount = new FinancialAccount(
            BacktestDefaults.DefaultBrokerageAccountId,
            "Cash Brokerage",
            FinancialAccountKind.Brokerage,
            InitialCash: 150m,
            Rules: new FinancialAccountRules(AllowMargin: false, AllowShortSelling: true));
        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 2),
            DataRoot: _dataRoot,
            Accounts: [cashOnlyAccount],
            FillTiming: FillTiming.SameBar);

        var result = await _engine.RunAsync(
            request,
            new SubmitOnceStrategy(new OrderRequest(
                "SPY",
                2L,
                OrderType.Market,
                TimeInForce: TimeInForce.FillOrKill,
                ExecutionModel: ExecutionModel.OrderBook)));

        result.Fills.Should().BeEmpty(
            "the second slice violates the cash rule, so the complete FOK batch must roll back");
        result.Snapshots.Should().ContainSingle();
        result.Snapshots[0].Accounts[BacktestDefaults.DefaultBrokerageAccountId]
            .Cash.Should().Be(150m);
    }

    [Fact]
    public async Task RunAsync_FillOrKillCompleteProposal_WithDefaultPartialFlag_FillsAtomically()
    {
        WriteMultiLevelLobJsonl(
            "SPY",
            (new DateTimeOffset(2024, 1, 2, 14, 30, 0, TimeSpan.Zero),
                [(99m, 1_000L)],
                [(100m, 1L), (101m, 1L)]));

        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 2),
            DataRoot: _dataRoot,
            FillTiming: FillTiming.SameBar);

        var result = await _engine.RunAsync(
            request,
            new SubmitOnceStrategy(new OrderRequest(
                "SPY",
                2L,
                OrderType.Market,
                TimeInForce: TimeInForce.FillOrKill,
                ExecutionModel: ExecutionModel.OrderBook)));

        result.Fills.Should().HaveCount(2);
        result.Fills.Sum(static fill => fill.FilledQuantity).Should().Be(2L,
            "FOK requires atomic handling even when AllowPartialFills retains its default value");
    }

    [Fact]
    public async Task RunAsync_NonPartialBatchRejectedOnLaterSlice_AcceptsNoSlices()
    {
        WriteMultiLevelLobJsonl(
            "SPY",
            (new DateTimeOffset(2024, 1, 2, 14, 30, 0, TimeSpan.Zero),
                [(99m, 1_000L)],
                [(100m, 1L), (101m, 1L)]));

        var cashOnlyAccount = new FinancialAccount(
            BacktestDefaults.DefaultBrokerageAccountId,
            "Cash Brokerage",
            FinancialAccountKind.Brokerage,
            InitialCash: 150m,
            Rules: new FinancialAccountRules(AllowMargin: false, AllowShortSelling: true));
        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 2),
            DataRoot: _dataRoot,
            Accounts: [cashOnlyAccount],
            FillTiming: FillTiming.SameBar);

        var result = await _engine.RunAsync(
            request,
            new SubmitOnceStrategy(new OrderRequest(
                "SPY",
                2L,
                OrderType.Market,
                TimeInForce: TimeInForce.GoodTilCancelled,
                AllowPartialFills: false,
                ExecutionModel: ExecutionModel.OrderBook)));

        result.Fills.Should().BeEmpty(
            "a non-partial order must roll back every slice when any slice violates account rules");
        result.Snapshots.Should().ContainSingle();
        result.Snapshots[0].Accounts[BacktestDefaults.DefaultBrokerageAccountId]
            .Cash.Should().Be(150m);
    }

    [Fact]
    public async Task RunAsync_NonPartialMarketImpactLimitProposalMustBeComplete()
    {
        WriteCustomBarJsonl(
            "AAPL",
            (new DateOnly(2024, 1, 2), 100m, 110m, 90m, 100m, 1_000));

        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 2),
            DataRoot: _dataRoot,
            FillTiming: FillTiming.SameBar);

        var result = await _engine.RunAsync(
            request,
            new SubmitOnceStrategy(new OrderRequest(
                "AAPL",
                500L,
                OrderType.Limit,
                LimitPrice: 104m,
                TimeInForce: TimeInForce.GoodTilCancelled,
                AllowPartialFills: false,
                ExecutionModel: ExecutionModel.MarketImpact)));

        result.Fills.Should().BeEmpty(
            "a non-partial limit order cannot accept only the market-impact slices below its limit");
    }

    [Fact]
    public async Task RunAsync_FillOrKillBarProposalCannotComplete_DiscardsPartialProposal()
    {
        WriteCustomBarJsonl(
            "AAPL",
            (new DateOnly(2024, 1, 2), 100m, 105m, 95m, 100m, 40));

        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 2),
            DataRoot: _dataRoot,
            MaxParticipationRate: 1m,
            FillTiming: FillTiming.SameBar);

        var result = await _engine.RunAsync(
            request,
            new SubmitOnceStrategy(new OrderRequest(
                "AAPL",
                100L,
                OrderType.Market,
                TimeInForce: TimeInForce.FillOrKill,
                ExecutionModel: ExecutionModel.BarMidpoint)));

        result.Fills.Should().BeEmpty(
            "FOK is enforced centrally even when a fill model proposes a partial slice");
    }

    [Fact]
    public async Task RunAsync_ImmediateOrCancelBarPartialFill_CancelsRemainderCentrally()
    {
        WriteCustomBarJsonl(
            "AAPL",
            (new DateOnly(2024, 1, 2), 100m, 105m, 95m, 100m, 40),
            (new DateOnly(2024, 1, 3), 100m, 105m, 95m, 100m, 100));

        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 3),
            DataRoot: _dataRoot,
            MaxParticipationRate: 1m,
            FillTiming: FillTiming.SameBar);

        var result = await _engine.RunAsync(
            request,
            new SubmitOnceStrategy(new OrderRequest(
                "AAPL",
                100L,
                OrderType.Market,
                TimeInForce: TimeInForce.ImmediateOrCancel,
                ExecutionModel: ExecutionModel.BarMidpoint)));

        result.Fills.Should().ContainSingle();
        result.Fills[0].FilledQuantity.Should().Be(40L,
            "the second bar cannot fill the terminal IOC remainder");
    }

    [Fact]
    public async Task RunAsync_DayOrderExpiresBeforeLaterSessionCrossesItsLimit()
    {
        WriteCustomBarJsonl(
            "AAPL",
            (new DateOnly(2024, 1, 2), 100m, 105m, 95m, 100m, 1_000),
            (new DateOnly(2024, 1, 3), 10m, 15m, 5m, 10m, 1_000));

        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 3),
            DataRoot: _dataRoot,
            FillTiming: FillTiming.SameBar);

        var result = await _engine.RunAsync(
            request,
            new SubmitOnceStrategy(new OrderRequest(
                "AAPL",
                10L,
                OrderType.Limit,
                LimitPrice: 50m,
                TimeInForce: TimeInForce.Day)));

        result.Fills.Should().BeEmpty("the Day order expired at the end of its submission day");
    }

    // ------------------------------------------------------------------ //
    //  Progress reporting                                                 //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task RunAsync_WithProgressCallback_ReportsCompletion()
    {
        WriteBarJsonl("SPY", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 3), basePrice: 470m);

        var progressReports = new List<BacktestProgressEvent>();
        var progress = new Progress<BacktestProgressEvent>(e => progressReports.Add(e));

        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 3),
            DataRoot: _dataRoot);

        await _engine.RunAsync(request, new NoOpStrategy(), progress);

        // Allow the progress delegate to fire (it's posted to the thread pool by Progress<T>)
        await Task.Delay(50);

        progressReports.Should().NotBeEmpty("progress must be reported at least once");
        progressReports.Should().Contain(e => e.ProgressFraction >= 1.0,
            "a completion event with FractionComplete=1 must be reported");
    }

    // ------------------------------------------------------------------ //
    //  Cancellation                                                       //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task RunAsync_CancelledBeforeStart_ThrowsOperationCanceledException()
    {
        WriteBarJsonl("AAPL", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 31), basePrice: 185m);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 31),
            DataRoot: _dataRoot);

        var act = async () => await _engine.RunAsync(request, new NoOpStrategy(), ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ------------------------------------------------------------------ //
    //  Corporate action price adjustment                                  //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task RunAsync_WithStockSplitAdjustment_StrategyReceivesAdjustedBarPrices()
    {
        // Write bars with pre-split price of 200
        WriteBarJsonl("AAPL", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 2), basePrice: 200m);

        // Mock adjustment service that halves all prices (simulating a 2:1 split)
        var mockAdj = new StubCorporateActionAdjustmentService(factor: 2m);

        var catalog = new StorageCatalogService(_dataRoot, new StorageOptions());
        var engine = new BacktestEngine(
            NullLogger<BacktestEngine>.Instance,
            catalog,
            securityMasterQueryService: null,
            corporateActionAdjustment: mockAdj);

        var strategy = new PriceCapturingStrategy();
        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 2),
            DataRoot: _dataRoot,
            AdjustForCorporateActions: true);

        await engine.RunAsync(request, strategy);

        strategy.ReceivedBars.Should().ContainSingle("one bar was written to disk");
        var bar = strategy.ReceivedBars[0];
        bar.Open.Should().Be(100m, "price should be halved by the 2:1 split adjustment (200 / 2)");
        bar.Close.Should().Be(100m, "close should also be halved");
        bar.Volume.Should().Be(2_000_000L, "volume should be doubled by the split adjustment");
    }

    [Fact]
    public async Task RunAsync_CorporateActions_PreparesCompleteSeriesOnceAtPinnedAsOfAndAppliesPlan()
    {
        WriteBarJsonl(
            "AAPL",
            new DateOnly(2024, 1, 2),
            new DateOnly(2024, 1, 4),
            basePrice: 200m,
            dailyGain: 4m);

        var adjustment = new StubCorporateActionAdjustmentService(factor: 2m);
        var catalog = new StorageCatalogService(_dataRoot, new StorageOptions());
        var engine = new BacktestEngine(
            NullLogger<BacktestEngine>.Instance,
            catalog,
            securityMasterQueryService: null,
            corporateActionAdjustment: adjustment);
        var strategy = new PriceCapturingStrategy();
        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 4),
            DataRoot: _dataRoot,
            AdjustForCorporateActions: true);

        await engine.RunAsync(request, strategy);

        adjustment.PrepareCallCount.Should().Be(1);
        adjustment.CallCount.Should().Be(1, "the compatibility batch adjustment runs once during preparation");
        adjustment.PreparedTicker.Should().Be("AAPL");
        adjustment.PreparedAsOfUtc.Should().Be(new DateTimeOffset(
            request.To.ToDateTime(TimeOnly.MaxValue),
            TimeSpan.Zero));
        adjustment.PreparedBars.Should().HaveCount(3);
        adjustment.PreparedBars.Select(static bar => bar.SessionDate).Should().Equal(
            new DateOnly(2024, 1, 2),
            new DateOnly(2024, 1, 3),
            new DateOnly(2024, 1, 4));

        strategy.ReceivedBars.Should().HaveCount(3);
        strategy.ReceivedBars.Select(static bar => bar.Open).Should().Equal(100m, 102m, 104m);
        strategy.ReceivedBars.Should().OnlyContain(static bar => bar.Volume == 2_000_000L,
            "the prepared immutable plan is applied to every bar in the execution replay");
    }

    [Fact]
    public async Task RunAsync_CorporateActions_ExecutesFromThePreparedMarketDataSnapshot()
    {
        WriteBarJsonl("AAPL", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 2), basePrice: 200m);
        var sourcePath = Path.Combine(_dataRoot, "AAPL", "AAPL_bars_2024-01-02.jsonl");
        var adjustment = new StubCorporateActionAdjustmentService(
            factor: 2m,
            onPrepare: () => File.WriteAllText(sourcePath, string.Empty));
        var catalog = new StorageCatalogService(_dataRoot, new StorageOptions());
        var engine = new BacktestEngine(
            NullLogger<BacktestEngine>.Instance,
            catalog,
            securityMasterQueryService: null,
            corporateActionAdjustment: adjustment);
        var strategy = new PriceCapturingStrategy();
        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 2),
            DataRoot: _dataRoot,
            AdjustForCorporateActions: true);

        await engine.RunAsync(request, strategy);

        strategy.ReceivedBars.Should().ContainSingle();
        strategy.ReceivedBars[0].Open.Should().Be(100m,
            "execution must use the same captured bar that was supplied during preparation");
    }

    [Fact]
    public async Task RunAsync_WithAdjustForCorporateActionsFalse_StrategyReceivesOriginalPrices()
    {
        // Write bars with pre-split price of 200
        WriteBarJsonl("AAPL", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 2), basePrice: 200m);

        // Mock adjustment service would halve prices, but it should NOT be called when disabled
        var mockAdj = new StubCorporateActionAdjustmentService(factor: 2m);

        var catalog = new StorageCatalogService(_dataRoot, new StorageOptions());
        var engine = new BacktestEngine(
            NullLogger<BacktestEngine>.Instance,
            catalog,
            securityMasterQueryService: null,
            corporateActionAdjustment: mockAdj);

        var strategy = new PriceCapturingStrategy();
        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 2),
            DataRoot: _dataRoot,
            AdjustForCorporateActions: false);

        await engine.RunAsync(request, strategy);

        strategy.ReceivedBars.Should().ContainSingle();
        strategy.ReceivedBars[0].Open.Should().Be(200m, "adjustment disabled — original unadjusted price expected");
        mockAdj.CallCount.Should().Be(0, "adjustment service must not be called when AdjustForCorporateActions is false");
    }

    // ------------------------------------------------------------------ //
    //  JSONL fixture helpers                                              //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Writes a single <see cref="HistoricalBar"/> event with the given <paramref name="timestamp"/>
    /// (preserving its UTC offset) to a JSONL file so the engine can read it back.
    /// Used to test date-boundary filtering with non-UTC-offset timestamps.
    /// </summary>
    private void WriteEventJsonl(string symbol, DateTimeOffset timestamp)
    {
        var utcDate = DateOnly.FromDateTime(timestamp.UtcDateTime);
        var symbolDir = Path.Combine(_dataRoot, symbol.ToUpperInvariant());
        Directory.CreateDirectory(symbolDir);
        var filePath = Path.Combine(symbolDir, $"{symbol}_bars_{utcDate:yyyy-MM-dd}.jsonl");

        var bar = new HistoricalBar(
            Symbol: symbol,
            SessionDate: utcDate,
            Open: 100m, High: 105m, Low: 95m, Close: 100m,
            Volume: 1_000_000L,
            Source: "test",
            SequenceNumber: 1L);

        var evt = MarketEvent.HistoricalBar(timestamp, symbol, bar, "test", 1);
        using var writer = new StreamWriter(filePath);
        writer.WriteLine(JsonSerializer.Serialize(evt, MarketDataJsonContext.HighPerformanceOptions));
    }

    /// <summary>
    /// Writes one <see cref="HistoricalBar"/> per day (from → to inclusive) to a JSONL file
    /// in a per-symbol sub-directory, named in the pattern the UniverseDiscovery scanner expects.
    /// </summary>
    private void WriteBarJsonl(string symbol, DateOnly from, DateOnly to, decimal basePrice, decimal dailyGain = 0m)
    {
        var symbolDir = Path.Combine(_dataRoot, symbol.ToUpperInvariant());
        Directory.CreateDirectory(symbolDir);
        var filePath = Path.Combine(symbolDir, $"{symbol}_bars_{from:yyyy-MM-dd}.jsonl");

        using var writer = new StreamWriter(filePath);
        var date = from;
        var seq = 1L;
        while (date <= to)
        {
            var open = basePrice + (date.DayNumber - from.DayNumber) * dailyGain;
            var high = open + 5m;
            var low = open - 5m;
            var close = open + dailyGain;

            var bar = new HistoricalBar(
                Symbol: symbol,
                SessionDate: date,
                Open: open,
                High: high,
                Low: low,
                Close: close,
                Volume: 1_000_000L,
                Source: "test",
                SequenceNumber: seq++);

            var ts = bar.ToTimestampUtc();
            var evt = MarketEvent.HistoricalBar(ts, symbol, bar, "test", seq);

            writer.WriteLine(JsonSerializer.Serialize(evt, MarketDataJsonContext.HighPerformanceOptions));
            date = date.AddDays(1);
        }
    }

    private void WriteCustomBarJsonl(string symbol, params (DateOnly Date, decimal Open, decimal High, decimal Low, decimal Close, long Volume)[] bars)
    {
        var symbolDir = Path.Combine(_dataRoot, symbol.ToUpperInvariant());
        Directory.CreateDirectory(symbolDir);
        var filePath = Path.Combine(symbolDir, $"{symbol}_bars_{bars[0].Date:yyyy-MM-dd}.jsonl");

        using var writer = new StreamWriter(filePath);
        var seq = 1L;
        foreach (var bar in bars)
        {
            var payload = new HistoricalBar(
                Symbol: symbol,
                SessionDate: bar.Date,
                Open: bar.Open,
                High: bar.High,
                Low: bar.Low,
                Close: bar.Close,
                Volume: bar.Volume,
                Source: "test",
                SequenceNumber: seq++);

            var ts = payload.ToTimestampUtc();
            var evt = MarketEvent.HistoricalBar(ts, symbol, payload, "test", seq);
            writer.WriteLine(JsonSerializer.Serialize(evt, MarketDataJsonContext.HighPerformanceOptions));
        }
    }

    private void WriteLobJsonl(
        string symbol,
        params (DateTimeOffset Timestamp, decimal AskPrice, long AskQuantity)[] snapshots)
    {
        var symbolDir = Path.Combine(_dataRoot, symbol.ToUpperInvariant());
        Directory.CreateDirectory(symbolDir);
        var filePath = Path.Combine(
            symbolDir,
            $"{symbol}_lob_{DateOnly.FromDateTime(snapshots[0].Timestamp.UtcDateTime):yyyy-MM-dd}.jsonl");

        using var writer = new StreamWriter(filePath);
        var sequence = 1L;
        foreach (var snapshot in snapshots)
        {
            var payload = new LOBSnapshot(
                snapshot.Timestamp,
                symbol,
                Bids: [new OrderBookLevel(OrderBookSide.Bid, 0, snapshot.AskPrice - 1m, 1_000m)],
                Asks: [new OrderBookLevel(OrderBookSide.Ask, 0, snapshot.AskPrice, snapshot.AskQuantity)],
                SequenceNumber: sequence);
            var evt = MarketEvent.L2Snapshot(
                snapshot.Timestamp,
                symbol,
                payload,
                source: "test",
                seq: sequence++);
            writer.WriteLine(JsonSerializer.Serialize(evt, MarketDataJsonContext.HighPerformanceOptions));
        }
    }

    private void WriteMultiLevelLobJsonl(
        string symbol,
        params (
            DateTimeOffset Timestamp,
            (decimal Price, long Quantity)[] Bids,
            (decimal Price, long Quantity)[] Asks)[] snapshots)
    {
        var symbolDir = Path.Combine(_dataRoot, symbol.ToUpperInvariant());
        Directory.CreateDirectory(symbolDir);
        var filePath = Path.Combine(
            symbolDir,
            $"{symbol}_lob_{DateOnly.FromDateTime(snapshots[0].Timestamp.UtcDateTime):yyyy-MM-dd}.jsonl");

        using var writer = new StreamWriter(filePath);
        var sequence = 1L;
        foreach (var snapshot in snapshots)
        {
            var payload = new LOBSnapshot(
                snapshot.Timestamp,
                symbol,
                Bids: snapshot.Bids
                    .Select((level, index) => new OrderBookLevel(
                        OrderBookSide.Bid,
                        (ushort)index,
                        level.Price,
                        level.Quantity))
                    .ToArray(),
                Asks: snapshot.Asks
                    .Select((level, index) => new OrderBookLevel(
                        OrderBookSide.Ask,
                        (ushort)index,
                        level.Price,
                        level.Quantity))
                    .ToArray(),
                SequenceNumber: sequence);
            var evt = MarketEvent.L2Snapshot(
                snapshot.Timestamp,
                symbol,
                payload,
                source: "test",
                seq: sequence++);
            writer.WriteLine(JsonSerializer.Serialize(evt, MarketDataJsonContext.HighPerformanceOptions));
        }
    }
}

// ------------------------------------------------------------------ //
//  Minimal strategy implementations used by the tests above          //
// ------------------------------------------------------------------ //

file sealed class NoOpStrategy : IBacktestStrategy
{
    public string Name => "NoOp";
    public void Initialize(IBacktestContext ctx) { }
    public void OnTrade(Trade trade, IBacktestContext ctx) { }
    public void OnQuote(BboQuotePayload quote, IBacktestContext ctx) { }
    public void OnBar(HistoricalBar bar, IBacktestContext ctx) { }
    public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx) { }
    public void OnOrderFill(FillEvent fill, IBacktestContext ctx) { }
    public void OnDayEnd(DateOnly date, IBacktestContext ctx) { }
    public void OnFinished(IBacktestContext ctx) { }
}

file sealed class BarTrackingStrategy : IBacktestStrategy
{
    public string Name => "BarTracker";
    public int BarsReceived { get; private set; }
    public HashSet<string> Symbols { get; } = [];

    public void Initialize(IBacktestContext ctx) { }
    public void OnTrade(Trade trade, IBacktestContext ctx) { }
    public void OnQuote(BboQuotePayload quote, IBacktestContext ctx) { }

    public void OnBar(HistoricalBar bar, IBacktestContext ctx)
    {
        BarsReceived++;
        Symbols.Add(bar.Symbol);
    }

    public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx) { }
    public void OnOrderFill(FillEvent fill, IBacktestContext ctx) { }
    public void OnDayEnd(DateOnly date, IBacktestContext ctx) { }
    public void OnFinished(IBacktestContext ctx) { }
}

file sealed class OrderedSymbolCaptureStrategy : IBacktestStrategy
{
    public string Name => "OrderedSymbolCapture";
    public List<string> BarSymbolsInArrivalOrder { get; } = [];

    public void Initialize(IBacktestContext ctx) { }
    public void OnTrade(Trade trade, IBacktestContext ctx) { }
    public void OnQuote(BboQuotePayload quote, IBacktestContext ctx) { }
    public void OnBar(HistoricalBar bar, IBacktestContext ctx) => BarSymbolsInArrivalOrder.Add(bar.Symbol);
    public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx) { }
    public void OnOrderFill(FillEvent fill, IBacktestContext ctx) { }
    public void OnDayEnd(DateOnly date, IBacktestContext ctx) { }
    public void OnFinished(IBacktestContext ctx) { }
}

/// <summary>Places a single market buy on the very first bar, then does nothing further.</summary>
file sealed class BuyFirstBarStrategy(string symbol, long quantity) : IBacktestStrategy
{
    private bool _bought;

    public string Name => "BuyFirstBar";

    public void Initialize(IBacktestContext ctx) { }
    public void OnTrade(Trade trade, IBacktestContext ctx) { }
    public void OnQuote(BboQuotePayload quote, IBacktestContext ctx) { }

    public void OnBar(HistoricalBar bar, IBacktestContext ctx)
    {
        if (!_bought && bar.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
        {
            ctx.PlaceMarketOrder(symbol, quantity);
            _bought = true;
        }
    }

    public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx) { }
    public void OnOrderFill(FillEvent fill, IBacktestContext ctx) { }
    public void OnDayEnd(DateOnly date, IBacktestContext ctx) { }
    public void OnFinished(IBacktestContext ctx) { }
}

/// <summary>Captures all bars received during the backtest for price assertions.</summary>
file sealed class PriceCapturingStrategy : IBacktestStrategy
{
    public string Name => "PriceCapturing";
    public List<HistoricalBar> ReceivedBars { get; } = [];

    public void Initialize(IBacktestContext ctx) { }
    public void OnTrade(Trade trade, IBacktestContext ctx) { }
    public void OnQuote(BboQuotePayload quote, IBacktestContext ctx) { }
    public void OnBar(HistoricalBar bar, IBacktestContext ctx) => ReceivedBars.Add(bar);
    public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx) { }
    public void OnOrderFill(FillEvent fill, IBacktestContext ctx) { }
    public void OnDayEnd(DateOnly date, IBacktestContext ctx) { }
    public void OnFinished(IBacktestContext ctx) { }
}

/// <summary>
/// Stub <see cref="ICorporateActionAdjustmentService"/> that divides all bar prices by a
/// configurable split <paramref name="factor"/> and multiplies volume by the same factor.
/// </summary>
file sealed class StubCorporateActionAdjustmentService(decimal factor, Action? onPrepare = null) : ICorporateActionAdjustmentService
{
    public int CallCount { get; private set; }
    public int PrepareCallCount { get; private set; }
    public IReadOnlyList<HistoricalBar> PreparedBars { get; private set; } = [];
    public string? PreparedTicker { get; private set; }
    public DateTimeOffset? PreparedAsOfUtc { get; private set; }

    public async Task<CorporateActionAdjustmentPlan> PrepareAsync(
        IReadOnlyList<HistoricalBar> bars,
        string ticker,
        DateTimeOffset asOfUtc,
        CancellationToken ct = default)
    {
        PrepareCallCount++;
        PreparedBars = bars.ToArray();
        PreparedTicker = ticker;
        PreparedAsOfUtc = asOfUtc;
        onPrepare?.Invoke();

        var adjusted = await AdjustAsync(bars, ticker, ct);
        return CorporateActionAdjustmentPlan.FromLegacyAdjustedBars(
            ticker,
            asOfUtc,
            bars,
            adjusted);
    }

    public Task<IReadOnlyList<HistoricalBar>> AdjustAsync(
        IReadOnlyList<HistoricalBar> bars,
        string ticker,
        CancellationToken ct = default)
    {
        CallCount++;
        var adjusted = bars
            .Select(b => new HistoricalBar(
                Symbol: b.Symbol,
                SessionDate: b.SessionDate,
                Open: b.Open / factor,
                High: b.High / factor,
                Low: b.Low / factor,
                Close: b.Close / factor,
                Volume: (long)(b.Volume * factor),
                Source: b.Source,
                SequenceNumber: b.SequenceNumber))
            .ToList();
        return Task.FromResult<IReadOnlyList<HistoricalBar>>(adjusted);
    }
}

/// <summary>
/// Places a single short-sell (negative quantity) market order on the very first bar.
/// Used to exercise the engine's fill-rejection path when AllowShortSelling=false.
/// </summary>
file sealed class ShortFirstBarStrategy(string symbol, long quantity) : IBacktestStrategy
{
    private bool _shorted;

    public string Name => "ShortFirstBar";
    public List<FillEvent> FillCallbacks { get; } = [];

    public void Initialize(IBacktestContext ctx) { }
    public void OnTrade(Trade trade, IBacktestContext ctx) { }
    public void OnQuote(BboQuotePayload quote, IBacktestContext ctx) { }

    public void OnBar(HistoricalBar bar, IBacktestContext ctx)
    {
        if (!_shorted && bar.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
        {
            ctx.PlaceMarketOrder(symbol, -quantity);   // negative quantity = short sell
            _shorted = true;
        }
    }

    public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx) { }
    public void OnOrderFill(FillEvent fill, IBacktestContext ctx) => FillCallbacks.Add(fill);
    public void OnDayEnd(DateOnly date, IBacktestContext ctx) { }
    public void OnFinished(IBacktestContext ctx) { }
}

file sealed class BuyFirstBarWithMarketImpactGtcStrategy(string symbol, long quantity) : IBacktestStrategy
{
    private bool _submitted;

    public string Name => "BuyFirstBarWithMarketImpactGtc";

    public void Initialize(IBacktestContext ctx) { }
    public void OnTrade(Trade trade, IBacktestContext ctx) { }
    public void OnQuote(BboQuotePayload quote, IBacktestContext ctx) { }

    public void OnBar(HistoricalBar bar, IBacktestContext ctx)
    {
        if (_submitted || !bar.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
            return;

        ctx.PlaceOrder(new OrderRequest(
            Symbol: symbol,
            Quantity: quantity,
            Type: OrderType.Market,
            TimeInForce: TimeInForce.GoodTilCancelled,
            ExecutionModel: ExecutionModel.MarketImpact));
        _submitted = true;
    }

    public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx) { }
    public void OnOrderFill(FillEvent fill, IBacktestContext ctx) { }
    public void OnDayEnd(DateOnly date, IBacktestContext ctx) { }
    public void OnFinished(IBacktestContext ctx) { }
}

file sealed class SubmitOnceStrategy(OrderRequest request) : IBacktestStrategy
{
    private bool _submitted;

    public string Name => "SubmitOnce";

    public void Initialize(IBacktestContext ctx) { }
    public void OnTrade(Trade trade, IBacktestContext ctx) => Submit(ctx);
    public void OnQuote(BboQuotePayload quote, IBacktestContext ctx) => Submit(ctx);
    public void OnBar(HistoricalBar bar, IBacktestContext ctx) => Submit(ctx);
    public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx) => Submit(ctx);
    public void OnOrderFill(FillEvent fill, IBacktestContext ctx) { }
    public void OnDayEnd(DateOnly date, IBacktestContext ctx) { }
    public void OnFinished(IBacktestContext ctx) { }

    private void Submit(IBacktestContext context)
    {
        if (_submitted)
            return;

        context.PlaceOrder(request);
        _submitted = true;
    }
}

file sealed class CancelWorkingOrderOnSecondBarStrategy : IBacktestStrategy
{
    private Guid _orderId;
    private int _barCount;

    public string Name => "CancelWorkingOrderOnSecondBar";

    public void Initialize(IBacktestContext ctx) { }
    public void OnTrade(Trade trade, IBacktestContext ctx) { }
    public void OnQuote(BboQuotePayload quote, IBacktestContext ctx) { }

    public void OnBar(HistoricalBar bar, IBacktestContext ctx)
    {
        _barCount++;
        if (_barCount == 1)
        {
            _orderId = ctx.PlaceOrder(new OrderRequest(
                bar.Symbol,
                10L,
                OrderType.Limit,
                LimitPrice: 50m,
                TimeInForce: TimeInForce.GoodTilCancelled));
        }
        else if (_barCount == 2)
        {
            ctx.CancelOrder(_orderId);
        }
    }

    public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx) { }
    public void OnOrderFill(FillEvent fill, IBacktestContext ctx) { }
    public void OnDayEnd(DateOnly date, IBacktestContext ctx) { }
    public void OnFinished(IBacktestContext ctx) { }
}

file sealed class CancelBracketExitsOnEntryFillStrategy : IBacktestStrategy
{
    private Guid _entryOrderId;
    private bool _cancelledContingents;

    public string Name => "CancelBracketExitsOnEntryFill";

    public void Initialize(IBacktestContext ctx) { }
    public void OnTrade(Trade trade, IBacktestContext ctx) { }
    public void OnQuote(BboQuotePayload quote, IBacktestContext ctx) { }

    public void OnBar(HistoricalBar bar, IBacktestContext ctx) => SubmitBracket(bar.Symbol, ctx);

    public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx) => SubmitBracket(snapshot.Symbol, ctx);

    public void OnOrderFill(FillEvent fill, IBacktestContext ctx)
    {
        if (fill.OrderId != _entryOrderId || _cancelledContingents)
            return;

        ctx.CancelContingentOrders(_entryOrderId);
        _cancelledContingents = true;
    }

    public void OnDayEnd(DateOnly date, IBacktestContext ctx) { }
    public void OnFinished(IBacktestContext ctx) { }

    private void SubmitBracket(string symbol, IBacktestContext context)
    {
        if (_entryOrderId != Guid.Empty)
            return;

        _entryOrderId = context.PlaceBracketOrder(new BracketOrderRequest(
            symbol,
            10L,
            OrderType.Market,
            TakeProfitPrice: 110m,
            StopLossPrice: 90m,
            TimeInForce: TimeInForce.GoodTilCancelled,
            ExecutionModel: ExecutionModel.OrderBook));
    }
}
