using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Serialization;
using Meridian.Backtesting.Engine;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Adapters.YahooFinance;
using Meridian.Storage;
using Meridian.Storage.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Meridian.Backtesting.Tests;

/// <summary>
/// Development integration tests that wire the Yahoo Finance historical data provider
/// directly to the backtest engine. These tests verify the full pipeline:
///   1. Fetch daily bars from Yahoo Finance
///   2. Seed them to local JSONL in the format the engine expects
///   3. Run a simple strategy through the engine
///   4. Assert basic metric sanity
///
/// Marked with <c>[Trait("Category", "Integration")]</c> — they hit the live Yahoo API
/// and should not run in CI by default.
/// Run individually during development with:
///   dotnet test --filter "Category=Integration&amp;FullyQualifiedName~YahooFinanceBacktest"
/// </summary>
[Trait("Category", "Integration")]
public sealed class YahooFinanceBacktestIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _dataRoot;
    private readonly YahooFinanceHistoricalDataProvider _provider;
    private readonly BacktestEngine _engine;

    public YahooFinanceBacktestIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _dataRoot = Path.Combine(Path.GetTempPath(), $"meridian-yahoo-backtest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dataRoot);

        _provider = new YahooFinanceHistoricalDataProvider();
        var catalog = new StorageCatalogService(_dataRoot, new StorageOptions());
        _engine = new BacktestEngine(NullLogger<BacktestEngine>.Instance, catalog);
    }

    public void Dispose()
    {
        _provider.Dispose();
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    // ------------------------------------------------------------------ //
    //  Full pipeline: Yahoo → JSONL → BacktestEngine                      //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Yahoo_SpyBuyAndHold_BacktestReturnsPositiveEquityOnUptrendMonth()
    {
        // Arrange: January 2024 was a broadly positive month for SPY.
        var from = new DateOnly(2024, 1, 2);
        var to   = new DateOnly(2024, 1, 31);

        var bars = await _provider.GetDailyBarsAsync("SPY", from, to);

        if (!HasData("SPY", bars.Count)) return;

        YahooBacktestSeedHelper.WriteToJsonl(_dataRoot, "SPY", bars);
        _output.WriteLine($"Seeded {bars.Count} SPY bars ({from} – {to}) to {_dataRoot}");

        // Act
        var strategy = new YahooBuyOnFirstBarStrategy("SPY", quantity: 100);
        var request  = new BacktestRequest(From: from, To: to, InitialCash: 50_000m, DataRoot: _dataRoot);

        var result = await _engine.RunAsync(request, strategy);

        // Assert
        LogResult(result);

        result.Universe.Should().ContainSingle("SPY bars were seeded for exactly one symbol");
        result.TotalEventsProcessed.Should().BeGreaterThan(0);
        result.Fills.Should().NotBeEmpty("the strategy should place a buy on the first bar");
        result.Metrics.FinalEquity.Should().BeGreaterThan(0, "equity should never be zero after a buy");

        // SPY rose ~5% in January 2024; verify the backtest captured a gain.
        result.Metrics.FinalEquity.Should().BeGreaterThan(50_000m,
            "SPY trended upward in January 2024, so a 100-share long should return above initial cash");
    }

    [Fact]
    public async Task Yahoo_MultiSymbol_BacktestDiscoversBothSymbols()
    {
        // Arrange: seed two symbols over the same short window.
        var from = new DateOnly(2024, 2, 1);
        var to   = new DateOnly(2024, 2, 29);

        var spyBars  = await _provider.GetDailyBarsAsync("SPY",  from, to);
        var aaplBars = await _provider.GetDailyBarsAsync("AAPL", from, to);

        if (!HasData("SPY",  spyBars.Count))  return;
        if (!HasData("AAPL", aaplBars.Count)) return;

        YahooBacktestSeedHelper.WriteToJsonl(_dataRoot, "SPY",  spyBars);
        YahooBacktestSeedHelper.WriteToJsonl(_dataRoot, "AAPL", aaplBars);

        _output.WriteLine($"Seeded {spyBars.Count} SPY + {aaplBars.Count} AAPL bars ({from} – {to})");

        // Act
        var request = new BacktestRequest(From: from, To: to, DataRoot: _dataRoot);
        var result  = await _engine.RunAsync(request, new YahooNoOpStrategy());

        // Assert
        LogResult(result);

        result.Universe.Should().HaveCount(2, "two symbols were seeded");
        result.Universe.Should().Contain("SPY",  "SPY bars were written");
        result.Universe.Should().Contain("AAPL", "AAPL bars were written");
        result.TotalEventsProcessed.Should().BeGreaterThanOrEqualTo(2,
            "at least one bar per symbol must have been replayed");
    }

    [Fact]
    public async Task Yahoo_BarsAreInChronologicalOrder_AfterRoundTrip()
    {
        // Arrange: a longer window to make ordering more meaningful.
        var from = new DateOnly(2023, 10, 1);
        var to   = new DateOnly(2023, 12, 29);

        var bars = await _provider.GetDailyBarsAsync("QQQ", from, to);
        if (!HasData("QQQ", bars.Count)) return;

        YahooBacktestSeedHelper.WriteToJsonl(_dataRoot, "QQQ", bars);

        // Act
        var received = new List<DateOnly>();
        var strategy = new YahooDateCapturingStrategy(received);
        var request  = new BacktestRequest(From: from, To: to, DataRoot: _dataRoot);

        await _engine.RunAsync(request, strategy);

        // Assert — engine must deliver bars in ascending date order.
        received.Should().NotBeEmpty("QQQ should have data for Q4 2023");
        received.Should().BeInAscendingOrder(d => d,
            "the multi-symbol merge enumerator must preserve chronological order");
    }

    [Fact]
    public async Task Yahoo_Provider_IsAvailable_ReturnsTrue()
    {
        // Verify the Yahoo Finance API is reachable before running the heavier tests.
        var available = await _provider.IsAvailableAsync();

        _output.WriteLine($"Yahoo Finance available: {available}");
        available.Should().BeTrue("Yahoo Finance API should be reachable in the integration environment");
    }

    // ------------------------------------------------------------------ //
    //  Helpers                                                             //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Returns <c>true</c> when there is data to process. Logs a notice and
    /// returns <c>false</c> when the provider returned nothing (the caller
    /// should do an early return so the test body is skipped gracefully).
    /// </summary>
    private bool HasData(string symbol, int count)
    {
        if (count > 0) return true;
        _output.WriteLine($"INCONCLUSIVE: Yahoo Finance returned no data for {symbol}. " +
                          "The symbol may be unavailable or the API is unreachable. " +
                          "Re-run with network access to exercise this test.");
        return false;
    }

    private void LogResult(BacktestResult result)
    {
        _output.WriteLine($"Universe:     {string.Join(", ", result.Universe)}");
        _output.WriteLine($"Events:       {result.TotalEventsProcessed}");
        _output.WriteLine($"Fills:        {result.Fills.Count}");
        _output.WriteLine($"Final equity: {result.Metrics.FinalEquity:C}");
        _output.WriteLine($"Total return: {result.Metrics.TotalReturn:F2}%");
    }
}

// ------------------------------------------------------------------ //
//  JSONL seed helper                                                  //
// ------------------------------------------------------------------ //

/// <summary>
/// Writes a list of <see cref="HistoricalBar"/> records to a JSONL file
/// in the directory layout and serialisation format that <c>BacktestEngine</c>
/// and <c>UniverseDiscovery</c> expect.
///
/// Layout: <c>{dataRoot}/{SYMBOL}/{SYMBOL}_bars_{firstDate:yyyy-MM-dd}.jsonl</c>
/// Each line is a JSON-serialised <see cref="MarketEvent"/> (HistoricalBar payload).
/// </summary>
internal static class YahooBacktestSeedHelper
{
    /// <summary>
    /// Writes <paramref name="bars"/> for <paramref name="symbol"/> under
    /// <paramref name="dataRoot"/>, replacing any existing file for the same symbol.
    /// </summary>
    public static void WriteToJsonl(string dataRoot, string symbol, IReadOnlyList<HistoricalBar> bars)
    {
        if (bars.Count == 0) return;

        var upperSymbol = symbol.ToUpperInvariant();
        var symbolDir   = Path.Combine(dataRoot, upperSymbol);
        Directory.CreateDirectory(symbolDir);

        var firstDate = bars.Min(b => b.SessionDate);
        var filePath  = Path.Combine(symbolDir, $"{upperSymbol}_bars_{firstDate:yyyy-MM-dd}.jsonl");

        using var writer = new StreamWriter(filePath);
        var seq = 1L;
        foreach (var bar in bars.OrderBy(b => b.SessionDate))
        {
            var ts  = bar.ToTimestampUtc();
            var evt = MarketEvent.HistoricalBar(ts, bar.Symbol, bar, seq++, bar.Source);
            writer.WriteLine(JsonSerializer.Serialize(evt, MarketDataJsonContext.HighPerformanceOptions));
        }
    }
}

// ------------------------------------------------------------------ //
//  Minimal strategy stubs used by the tests above                    //
// ------------------------------------------------------------------ //

file sealed class YahooNoOpStrategy : IBacktestStrategy
{
    public string Name => "YahooNoOp";
    public void Initialize(IBacktestContext ctx) { }
    public void OnTrade(Trade trade, IBacktestContext ctx) { }
    public void OnQuote(BboQuotePayload quote, IBacktestContext ctx) { }
    public void OnBar(HistoricalBar bar, IBacktestContext ctx) { }
    public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx) { }
    public void OnOrderFill(FillEvent fill, IBacktestContext ctx) { }
    public void OnDayEnd(DateOnly date, IBacktestContext ctx) { }
    public void OnFinished(IBacktestContext ctx) { }
}

/// <summary>Places a single market-buy on the very first bar for the target symbol.</summary>
file sealed class YahooBuyOnFirstBarStrategy(string symbol, long quantity) : IBacktestStrategy
{
    private bool _bought;

    public string Name => "YahooBuyOnFirstBar";
    public void Initialize(IBacktestContext ctx) { }
    public void OnTrade(Trade trade, IBacktestContext ctx) { }
    public void OnQuote(BboQuotePayload quote, IBacktestContext ctx) { }

    public void OnBar(HistoricalBar bar, IBacktestContext ctx)
    {
        if (_bought || !bar.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
            return;

        ctx.PlaceMarketOrder(symbol, quantity);
        _bought = true;
    }

    public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx) { }
    public void OnOrderFill(FillEvent fill, IBacktestContext ctx) { }
    public void OnDayEnd(DateOnly date, IBacktestContext ctx) { }
    public void OnFinished(IBacktestContext ctx) { }
}

/// <summary>Records every bar date in the order the engine delivered it.</summary>
file sealed class YahooDateCapturingStrategy(List<DateOnly> received) : IBacktestStrategy
{
    public string Name => "YahooDateCapture";
    public void Initialize(IBacktestContext ctx) { }
    public void OnTrade(Trade trade, IBacktestContext ctx) { }
    public void OnQuote(BboQuotePayload quote, IBacktestContext ctx) { }
    public void OnBar(HistoricalBar bar, IBacktestContext ctx) => received.Add(bar.SessionDate);
    public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx) { }
    public void OnOrderFill(FillEvent fill, IBacktestContext ctx) { }
    public void OnDayEnd(DateOnly date, IBacktestContext ctx) { }
    public void OnFinished(IBacktestContext ctx) { }
}
