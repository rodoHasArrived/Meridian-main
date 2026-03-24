using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Serialization;
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
            DataRoot: _dataRoot);

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
    //  JSONL fixture helpers                                              //
    // ------------------------------------------------------------------ //

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
            var evt = MarketEvent.HistoricalBar(ts, symbol, bar, seq, "test");

            writer.WriteLine(JsonSerializer.Serialize(evt, MarketDataJsonContext.HighPerformanceOptions));
            date = date.AddDays(1);
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
