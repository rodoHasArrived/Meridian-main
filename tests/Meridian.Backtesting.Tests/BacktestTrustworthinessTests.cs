using System.Text.Json;
using FluentAssertions;
using Meridian.Backtesting.Engine;
using Meridian.Core.Serialization;
using Meridian.Domain.Events;
using Meridian.Storage;
using Meridian.Storage.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Backtesting.Tests;

/// <summary>
/// Engine-level coverage for the trustworthiness features: next-bar fill timing, delisting
/// handling, and the bias-disclosure report attached to every result.
/// </summary>
public sealed class BacktestTrustworthinessTests : IDisposable
{
    private readonly string _dataRoot;
    private readonly BacktestEngine _engine;

    public BacktestTrustworthinessTests()
    {
        _dataRoot = Path.Combine(Path.GetTempPath(), $"meridian-trust-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dataRoot);
        var catalog = new StorageCatalogService(_dataRoot, new StorageOptions());
        _engine = new BacktestEngine(NullLogger<BacktestEngine>.Instance, catalog);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Fill timing
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NextBarTiming_OrderPlacedOnBar_DoesNotFillUntilNextBar()
    {
        WriteBars("AAPL", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 3), basePrice: 100m);

        var strategy = new BuyOnFirstBarStrategy("AAPL", quantity: 10);
        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 3),
            DataRoot: _dataRoot);

        var result = await _engine.RunAsync(request, strategy);

        result.Fills.Should().HaveCount(1);
        DateOnly.FromDateTime(result.Fills[0].FilledAt.UtcDateTime).Should().Be(new DateOnly(2024, 1, 3),
            "an order signalled on the Jan 2 bar must not fill until the Jan 3 bar under next-bar timing");
    }

    [Fact]
    public async Task NextBarTiming_SingleBar_OrderNeverFills()
    {
        WriteBars("AAPL", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 2), basePrice: 100m);

        var strategy = new BuyOnFirstBarStrategy("AAPL", quantity: 10);
        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 2),
            DataRoot: _dataRoot);

        var result = await _engine.RunAsync(request, strategy);

        result.Fills.Should().BeEmpty(
            "there is no later bar for the order to fill against, so same-bar look-ahead must not occur");
    }

    [Fact]
    public async Task SameBarTiming_OptIn_FillsOnSignalBar()
    {
        WriteBars("AAPL", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 2), basePrice: 100m);

        var strategy = new BuyOnFirstBarStrategy("AAPL", quantity: 10);
        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 2),
            DataRoot: _dataRoot,
            FillTiming: FillTiming.SameBar);

        var result = await _engine.RunAsync(request, strategy);

        result.Fills.Should().HaveCount(1, "same-bar mode preserves the legacy fill behaviour");
        result.BiasDisclosure.Should().NotBeNull();
        result.BiasDisclosure!.Items.Should().Contain(item =>
            item.Code == "fill-timing" && item.Severity == BiasSeverity.Warning,
            "same-bar execution must be flagged as look-ahead risk");
    }

    [Fact]
    public async Task NextBarTiming_OrderPlacedAtDayEnd_FillsOnNextDaysBar()
    {
        WriteBars("AAPL", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 3), basePrice: 100m);

        var strategy = new BuyAtDayEndStrategy("AAPL", quantity: 10);
        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 3),
            DataRoot: _dataRoot);

        var result = await _engine.RunAsync(request, strategy);

        result.Fills.Should().HaveCount(1);
        DateOnly.FromDateTime(result.Fills[0].FilledAt.UtcDateTime).Should().Be(new DateOnly(2024, 1, 3),
            "an order placed at Jan 2 day-end becomes eligible on the first Jan 3 event");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Delisting handling
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DelistedSymbol_PositionIsForceLiquidatedAfterGracePeriod()
    {
        // Data ends Jan 5 but the backtest runs to Jan 20 — a delisting proxy.
        WriteBars("GONE", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 5), basePrice: 100m);

        var strategy = new BuyOnFirstBarStrategy("GONE", quantity: 10);
        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 20),
            DataRoot: _dataRoot);

        var result = await _engine.RunAsync(request, strategy);

        var liquidation = result.Fills.Should().ContainSingle(fill => fill.OrderId == Guid.Empty).Subject;
        liquidation.FilledQuantity.Should().Be(-10L, "the open long must be fully closed");
        liquidation.FillPrice.Should().Be(100m, "liquidation uses the last observed price when no haircut is configured");
        DateOnly.FromDateTime(liquidation.FilledAt.UtcDateTime).Should().Be(new DateOnly(2024, 1, 11),
            "liquidation triggers at the first day-end more than the 5-day grace period past the last data date (Jan 5)");

        result.Snapshots.Last().Positions.Should().NotContainKey("GONE");

        result.BiasDisclosure.Should().NotBeNull();
        var recordedLiquidation = result.BiasDisclosure!.DelistingLiquidations.Should().ContainSingle().Subject;
        recordedLiquidation.Symbol.Should().Be("GONE");
        recordedLiquidation.LastDataDate.Should().Be(new DateOnly(2024, 1, 5));
        recordedLiquidation.Quantity.Should().Be(10L);
        result.BiasDisclosure.Items.Should().Contain(item => item.Code == "delisting-liquidations");
    }

    [Fact]
    public async Task DelistedSymbol_HaircutIsAppliedAgainstThePosition()
    {
        WriteBars("GONE", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 5), basePrice: 100m);

        var strategy = new BuyOnFirstBarStrategy("GONE", quantity: 10);
        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 20),
            DataRoot: _dataRoot,
            DelistingHaircutPercent: 0.25m);

        var result = await _engine.RunAsync(request, strategy);

        var liquidation = result.Fills.Should().ContainSingle(fill => fill.OrderId == Guid.Empty).Subject;
        liquidation.FillPrice.Should().Be(75m, "a 25% haircut against a long position reduces the 100 last price to 75");
    }

    [Fact]
    public async Task DelistedSymbol_HoldPolicy_KeepsPositionAndWarnsInDisclosure()
    {
        WriteBars("GONE", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 5), basePrice: 100m);

        var strategy = new BuyOnFirstBarStrategy("GONE", quantity: 10);
        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 20),
            DataRoot: _dataRoot,
            DelistingPolicy: DelistingPolicy.Hold);

        var result = await _engine.RunAsync(request, strategy);

        result.Fills.Should().NotContain(fill => fill.OrderId == Guid.Empty, "the Hold policy never force-liquidates");
        result.Snapshots.Last().Positions.Should().ContainKey("GONE");
        result.BiasDisclosure!.Items.Should().Contain(item =>
            item.Code == "delisting-policy" && item.Severity == BiasSeverity.Warning,
            "holding delisted names at stale marks must be disclosed as a warning");
    }

    [Fact]
    public async Task DataGapShorterThanGracePeriod_DoesNotTriggerLiquidation()
    {
        // Data ends Jan 5, backtest ends Jan 8 — a 3-day tail, inside the 5-day grace period.
        WriteBars("GAPPY", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 5), basePrice: 100m);

        var strategy = new BuyOnFirstBarStrategy("GAPPY", quantity: 10);
        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 8),
            DataRoot: _dataRoot);

        var result = await _engine.RunAsync(request, strategy);

        result.Fills.Should().NotContain(fill => fill.OrderId == Guid.Empty,
            "weekend-sized gaps must not be treated as delistings");
        result.Snapshots.Last().Positions.Should().ContainKey("GAPPY");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Bias disclosure content
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DefaultRun_DisclosureDocumentsTrustworthyDefaults()
    {
        WriteBars("SPY", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 3), basePrice: 470m);

        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 3),
            DataRoot: _dataRoot);

        var result = await _engine.RunAsync(request, new NoTradeStrategy());

        var disclosure = result.BiasDisclosure;
        disclosure.Should().NotBeNull();
        disclosure!.FillTiming.Should().Be(FillTiming.NextBar);
        disclosure.FillConservatism.Should().Be(FillConservatism.Conservative);
        disclosure.DelistingPolicy.Should().Be(DelistingPolicy.LiquidateAtLastPrice);
        disclosure.UniverseSource.Should().Be(BiasDisclosureReport.UniverseSourceDiscovered,
            "no explicit symbol list was supplied");

        disclosure.Items.Should().Contain(item => item.Code == "fill-timing" && item.Severity == BiasSeverity.Info);
        disclosure.Items.Should().Contain(item => item.Code == "fill-conservatism" && item.Severity == BiasSeverity.Info);
        disclosure.Items.Should().Contain(item => item.Code == "universe" && item.Severity == BiasSeverity.Warning,
            "a disk-discovered universe is survivorship-prone and must be flagged");
        disclosure.Items.Should().Contain(item => item.Code == "corporate-actions" && item.Severity == BiasSeverity.Warning,
            "adjustment was requested but no adjustment service is configured on this engine");
        disclosure.Items.Should().Contain(item => item.Code == "in-sample");

        disclosure.Items.Should().BeInDescendingOrder(item => item.Severity, "most severe items lead the panel");
    }

    [Fact]
    public async Task ExplicitSymbolList_DisclosureMarksUniverseAsCallerFixed()
    {
        WriteBars("SPY", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 3), basePrice: 470m);

        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 3),
            Symbols: ["SPY"],
            DataRoot: _dataRoot);

        var result = await _engine.RunAsync(request, new NoTradeStrategy());

        result.BiasDisclosure!.UniverseSource.Should().Be(BiasDisclosureReport.UniverseSourceExplicit);
        result.BiasDisclosure.Items.Should().Contain(item =>
            item.Code == "universe" && item.Severity == BiasSeverity.Caution);
    }

    [Fact]
    public async Task EmptyUniverse_ResultStillCarriesDisclosure()
    {
        var request = new BacktestRequest(
            From: new DateOnly(2024, 1, 2),
            To: new DateOnly(2024, 1, 3),
            DataRoot: _dataRoot);

        var result = await _engine.RunAsync(request, new NoTradeStrategy());

        result.TotalEventsProcessed.Should().Be(0);
        result.BiasDisclosure.Should().NotBeNull("even an empty run must disclose its assumptions");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Fixture helpers
    // ────────────────────────────────────────────────────────────────────────

    private void WriteBars(string symbol, DateOnly from, DateOnly to, decimal basePrice)
    {
        var symbolDir = Path.Combine(_dataRoot, symbol.ToUpperInvariant());
        Directory.CreateDirectory(symbolDir);
        var filePath = Path.Combine(symbolDir, $"{symbol}_bars_{from:yyyy-MM-dd}.jsonl");

        using var writer = new StreamWriter(filePath);
        var seq = 1L;
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var bar = new HistoricalBar(
                Symbol: symbol,
                SessionDate: date,
                Open: basePrice,
                High: basePrice + 5m,
                Low: basePrice - 5m,
                Close: basePrice,
                Volume: 1_000_000L,
                Source: "test",
                SequenceNumber: seq++);

            var evt = MarketEvent.HistoricalBar(bar.ToTimestampUtc(), symbol, bar, seq, "test");
            writer.WriteLine(JsonSerializer.Serialize(evt, MarketDataJsonContext.HighPerformanceOptions));
        }
    }
}

file sealed class BuyOnFirstBarStrategy(string symbol, long quantity) : IBacktestStrategy
{
    private bool _bought;

    public string Name => "BuyOnFirstBar";
    public void Initialize(IBacktestContext ctx) { }
    public void OnTrade(Trade trade, IBacktestContext ctx) { }
    public void OnQuote(BboQuotePayload quote, IBacktestContext ctx) { }

    public void OnBar(HistoricalBar bar, IBacktestContext ctx)
    {
        if (!_bought && bar.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
        {
            ctx.PlaceOrder(new OrderRequest(symbol, quantity, OrderType.Market, TimeInForce: TimeInForce.GoodTilCancelled));
            _bought = true;
        }
    }

    public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx) { }
    public void OnOrderFill(FillEvent fill, IBacktestContext ctx) { }
    public void OnDayEnd(DateOnly date, IBacktestContext ctx) { }
    public void OnFinished(IBacktestContext ctx) { }
}

file sealed class BuyAtDayEndStrategy(string symbol, long quantity) : IBacktestStrategy
{
    private bool _bought;

    public string Name => "BuyAtDayEnd";
    public void Initialize(IBacktestContext ctx) { }
    public void OnTrade(Trade trade, IBacktestContext ctx) { }
    public void OnQuote(BboQuotePayload quote, IBacktestContext ctx) { }
    public void OnBar(HistoricalBar bar, IBacktestContext ctx) { }
    public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx) { }
    public void OnOrderFill(FillEvent fill, IBacktestContext ctx) { }

    public void OnDayEnd(DateOnly date, IBacktestContext ctx)
    {
        if (!_bought)
        {
            ctx.PlaceOrder(new OrderRequest(symbol, quantity, OrderType.Market, TimeInForce: TimeInForce.GoodTilCancelled));
            _bought = true;
        }
    }

    public void OnFinished(IBacktestContext ctx) { }
}

file sealed class NoTradeStrategy : IBacktestStrategy
{
    public string Name => "NoTrade";
    public void Initialize(IBacktestContext ctx) { }
    public void OnTrade(Trade trade, IBacktestContext ctx) { }
    public void OnQuote(BboQuotePayload quote, IBacktestContext ctx) { }
    public void OnBar(HistoricalBar bar, IBacktestContext ctx) { }
    public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx) { }
    public void OnOrderFill(FillEvent fill, IBacktestContext ctx) { }
    public void OnDayEnd(DateOnly date, IBacktestContext ctx) { }
    public void OnFinished(IBacktestContext ctx) { }
}
