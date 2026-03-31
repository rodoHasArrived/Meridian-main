using FluentAssertions;
using Meridian.Contracts.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.YahooFinance;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Meridian.Tests.Integration;

/// <summary>
/// Integration tests that pull real historical data from Yahoo Finance
/// for Pacific Gas &amp; Electric (PG&amp;E) preferred share series.
///
/// Yahoo Finance tickers for PG&amp;E preferred shares:
///   PCG-PA  6.00% First Preferred Stock
///   PCG-PB  5.50% First Preferred Stock
///   PCG-PC  5.00% First Preferred Stock
///   PCG-PD  4.80% First Preferred Stock
///   PCG-PE  4.50% First Preferred Stock
///
/// These tests hit the live Yahoo Finance API and are marked with
/// Trait("Category", "Integration") so they can be filtered during CI.
/// Run with: dotnet test --filter "Category=Integration"
/// When the Yahoo Finance API is unreachable (e.g. in offline CI sandboxes)
/// each test is dynamically skipped rather than failing.
/// </summary>
[Trait("Category", "Integration")]
public sealed class YahooFinancePcgPreferredIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly YahooFinanceHistoricalDataProvider _provider;

    // Shared availability check: performed at most once per test-runner process.
    // Caching avoids a repeated round-trip for every [Theory] row.
    private static readonly Lazy<Task<bool>> _yahooAvailable =
        new(() => new YahooFinanceHistoricalDataProvider().IsAvailableAsync(),
            System.Threading.LazyThreadSafetyMode.PublicationOnly);

    /// <summary>
    /// All known PG&amp;E preferred share series on Yahoo Finance.
    /// </summary>
    private static readonly string[] PcgPreferredSymbols =
    [
        "PCG-PA", // 6.00% First Preferred
        "PCG-PB", // 5.50% First Preferred
        "PCG-PC", // 5.00% First Preferred
        "PCG-PD", // 4.80% First Preferred
        "PCG-PE", // 4.50% First Preferred
    ];

    public YahooFinancePcgPreferredIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _provider = new YahooFinanceHistoricalDataProvider();
    }

    /// <summary>
    /// Throws <see cref="SkipException"/> when Yahoo Finance is not reachable so that
    /// tests appear as Skipped rather than Failed in offline environments (e.g. CI sandboxes).
    /// </summary>
    private static async Task SkipWhenOfflineAsync()
    {
        if (!await _yahooAvailable.Value.ConfigureAwait(false))
            throw SkipException.ForSkip(
                "Yahoo Finance API is not reachable in this environment. " +
                "Run with live network access to execute these tests.");
    }

    [Fact]
    public async Task YahooFinance_IsAvailable_ReturnsTrue()
    {
        await SkipWhenOfflineAsync();

        // Verify Yahoo Finance API is reachable before running data tests
        var available = await _provider.IsAvailableAsync();

        _output.WriteLine($"Yahoo Finance available: {available}");
        available.Should().BeTrue("Yahoo Finance API should be reachable");
    }

    [Theory]
    [InlineData("PCG-PA", "6.00% First Preferred")]
    [InlineData("PCG-PB", "5.50% First Preferred")]
    [InlineData("PCG-PC", "5.00% First Preferred")]
    [InlineData("PCG-PD", "4.80% First Preferred")]
    [InlineData("PCG-PE", "4.50% First Preferred")]
    public async Task YahooFinance_GetAllHistoricalBars_ForPcgPreferredSeries(
        string symbol, string seriesDescription)
    {
        await SkipWhenOfflineAsync();

        // Act - Pull all available historical data (no date range limit)
        var bars = await _provider.GetDailyBarsAsync(symbol, from: null, to: null);

        // Log results
        _output.WriteLine($"--- {symbol} ({seriesDescription}) ---");
        _output.WriteLine($"Total bars returned: {bars.Count}");

        if (bars.Count > 0)
        {
            var earliest = bars.First();
            var latest = bars.Last();
            _output.WriteLine($"Date range: {earliest.SessionDate} to {latest.SessionDate}");
            _output.WriteLine($"First bar: O={earliest.Open} H={earliest.High} L={earliest.Low} C={earliest.Close} V={earliest.Volume}");
            _output.WriteLine($"Last bar:  O={latest.Open} H={latest.High} L={latest.Low} C={latest.Close} V={latest.Volume}");
            _output.WriteLine($"Price range across all bars: Low={bars.Min(b => b.Low):F2} to High={bars.Max(b => b.High):F2}");
        }

        // Assert
        bars.Should().NotBeNull();
        // Preferred shares may have limited data; just verify the provider returns without error
        if (bars.Count > 0)
        {
            bars.Should().AllSatisfy(bar =>
            {
                bar.Symbol.Should().Be(symbol);
                bar.Open.Should().BeGreaterThan(0);
                bar.High.Should().BeGreaterThanOrEqualTo(bar.Low);
                bar.Close.Should().BeGreaterThan(0);
                bar.Volume.Should().BeGreaterThanOrEqualTo(0);
                bar.Source.Should().Be("yahoo");
            });

            bars.Should().BeInAscendingOrder(b => b.SessionDate,
                "bars should be sorted chronologically");
        }
        else
        {
            _output.WriteLine($"WARNING: No data returned for {symbol}. " +
                              "The symbol may be delisted or not available on Yahoo Finance.");
        }
    }

    [Theory]
    [InlineData("PCG-PA", "6.00% First Preferred")]
    [InlineData("PCG-PB", "5.50% First Preferred")]
    [InlineData("PCG-PC", "5.00% First Preferred")]
    [InlineData("PCG-PD", "4.80% First Preferred")]
    [InlineData("PCG-PE", "4.50% First Preferred")]
    public async Task YahooFinance_GetAdjustedBars_IncludesDividendData_ForPcgPreferredSeries(
        string symbol, string seriesDescription)
    {
        await SkipWhenOfflineAsync();

        // Act - Pull adjusted bars which include dividend and split factor data
        var bars = await _provider.GetAdjustedDailyBarsAsync(symbol, from: null, to: null);

        // Log results
        _output.WriteLine($"--- {symbol} ({seriesDescription}) Adjusted Bars ---");
        _output.WriteLine($"Total adjusted bars: {bars.Count}");

        if (bars.Count > 0)
        {
            var withDividends = bars.Where(b => b.DividendAmount.HasValue && b.DividendAmount > 0).ToList();
            var withSplitFactor = bars.Where(b => b.SplitFactor.HasValue).ToList();
            var withAdjustedClose = bars.Where(b => b.AdjustedClose.HasValue).ToList();

            _output.WriteLine($"Bars with dividend events: {withDividends.Count}");
            _output.WriteLine($"Bars with split factor:    {withSplitFactor.Count}");
            _output.WriteLine($"Bars with adjusted close:  {withAdjustedClose.Count}");

            foreach (var div in withDividends.Take(10))
            {
                _output.WriteLine($"  Dividend on {div.SessionDate}: ${div.DividendAmount:F4}");
            }

            if (withDividends.Count > 10)
            {
                _output.WriteLine($"  ... and {withDividends.Count - 10} more dividend events");
            }

            // Preferred stocks are known for regular dividend payments
            if (withDividends.Count > 0)
            {
                _output.WriteLine($"Total dividend amount: ${withDividends.Sum(d => d.DividendAmount ?? 0):F4}");
            }
        }

        // Assert
        bars.Should().NotBeNull();
        if (bars.Count > 0)
        {
            bars.Should().AllSatisfy(bar =>
            {
                bar.Symbol.Should().Be(symbol);
                bar.Open.Should().BeGreaterThan(0);
                bar.High.Should().BeGreaterThanOrEqualTo(bar.Low);
                bar.Close.Should().BeGreaterThan(0);
            });

            bars.Should().BeInAscendingOrder(b => b.SessionDate);
        }
    }

    [Fact]
    public async Task YahooFinance_GetAllPcgPreferredSeries_SummaryReport()
    {
        await SkipWhenOfflineAsync();

        // Pull data for all preferred series and produce a summary comparison
        _output.WriteLine("=== PG&E Preferred Shares - Full Summary ===");
        _output.WriteLine("");

        var allResults = new Dictionary<string, IReadOnlyList<HistoricalBar>>();

        foreach (var symbol in PcgPreferredSymbols)
        {
            try
            {
                var bars = await _provider.GetDailyBarsAsync(symbol, from: null, to: null);
                allResults[symbol] = bars;
            }
            catch (Exception ex)
            {
                _output.WriteLine($"{symbol}: FAILED - {ex.Message}");
                allResults[symbol] = Array.Empty<HistoricalBar>();
            }
        }

        // Summary table
        _output.WriteLine($"{"Symbol",-10} {"Bars",8} {"From",12} {"To",12} {"Low",10} {"High",10} {"Last Close",12}");
        _output.WriteLine(new string('-', 76));

        foreach (var (symbol, bars) in allResults)
        {
            if (bars.Count > 0)
            {
                _output.WriteLine(
                    $"{symbol,-10} {bars.Count,8} {bars.First().SessionDate,12} {bars.Last().SessionDate,12} " +
                    $"{bars.Min(b => b.Low),10:F2} {bars.Max(b => b.High),10:F2} {bars.Last().Close,12:F2}");
            }
            else
            {
                _output.WriteLine($"{symbol,-10} {"N/A",8} {"—",12} {"—",12} {"—",10} {"—",10} {"—",12}");
            }
        }

        _output.WriteLine("");
        _output.WriteLine($"Total symbols queried: {PcgPreferredSymbols.Length}");
        _output.WriteLine($"Symbols with data:     {allResults.Count(kv => kv.Value.Count > 0)}");
        _output.WriteLine($"Total bars across all:  {allResults.Values.Sum(b => b.Count)}");

        // At least one series should return data
        allResults.Values.Sum(b => b.Count).Should().BeGreaterThan(0,
            "at least one PG&E preferred share series should have historical data on Yahoo Finance");
    }

    public void Dispose()
    {
        _provider.Dispose();
    }
}
