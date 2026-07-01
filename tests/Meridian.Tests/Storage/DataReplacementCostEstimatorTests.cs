using FluentAssertions;
using Meridian.Contracts.Catalog;
using Meridian.Storage.Services;
using Xunit;

namespace Meridian.Tests.Storage;

public sealed class DataReplacementCostEstimatorTests
{
    private static SymbolCatalogEntry MakeSymbol(
        string symbol,
        int tradingDays,
        string[] eventTypes,
        long eventCount = 1_000) => new()
    {
        Symbol = symbol,
        EventCount = eventCount,
        EventTypes = eventTypes,
        DateRange = new CatalogDateRange
        {
            Earliest = new DateTime(2026, 1, 1),
            Latest = new DateTime(2026, 3, 31),
            CalendarDays = 90,
            TradingDays = tradingDays
        }
    };

    private static StorageCatalog MakeCatalog(
        long totalBytesRaw = 0,
        params SymbolCatalogEntry[] symbols)
    {
        var catalog = new StorageCatalog
        {
            Statistics = new CatalogStatistics { TotalBytesRaw = totalBytesRaw }
        };
        foreach (var symbol in symbols)
            catalog.Symbols[symbol.Symbol] = symbol;
        return catalog;
    }

    [Fact]
    public void Estimate_EmptyCatalog_ReturnsZeroedEstimate()
    {
        var estimate = DataReplacementCostEstimator.Estimate(MakeCatalog(), new DataReplacementCostOptions());

        estimate.ConservativeEstimateUsd.Should().Be(0m);
        estimate.TotalSymbols.Should().Be(0);
        estimate.EventTypeBreakdown.Should().BeEmpty();
    }

    [Fact]
    public void Estimate_Disabled_ReturnsZeroedEstimate()
    {
        var catalog = MakeCatalog(
            totalBytesRaw: 5_000_000_000,
            MakeSymbol("SPY", tradingDays: 60, eventTypes: ["Trade"]));

        var estimate = DataReplacementCostEstimator.Estimate(
            catalog,
            new DataReplacementCostOptions { Enabled = false });

        estimate.ConservativeEstimateUsd.Should().Be(0m);
    }

    [Fact]
    public void Estimate_PricesSymbolDaysPerEventType()
    {
        var options = new DataReplacementCostOptions
        {
            HistoricalRatePerSymbolDayUsd = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["Trade"] = 0.10m,
                ["Quote"] = 0.20m
            },
            // Large per-GB rate so the symbol-day basis is the conservative one.
            RatePerGigabyteUsd = 1_000m
        };
        var catalog = MakeCatalog(
            totalBytesRaw: 10_000_000_000,
            MakeSymbol("SPY", tradingDays: 60, eventTypes: ["Trade", "Quote"]));

        var estimate = DataReplacementCostEstimator.Estimate(catalog, options);

        // 60 days × (0.10 + 0.20) = 18.00
        estimate.SymbolDayBasisUsd.Should().Be(18.00m);
        estimate.ConservativeEstimateUsd.Should().Be(18.00m);
        estimate.TotalSymbolDays.Should().Be(120);
        estimate.EventTypeBreakdown.Should().HaveCount(2);
        estimate.EventTypeBreakdown[0].EventType.Should().Be("Quote");
        estimate.EventTypeBreakdown[0].EstimatedUsd.Should().Be(12.00m);
    }

    [Fact]
    public void Estimate_TakesLowerOfTheTwoBases()
    {
        var options = new DataReplacementCostOptions
        {
            HistoricalRatePerSymbolDayUsd = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["Trade"] = 1.00m
            },
            RatePerGigabyteUsd = 5.00m
        };
        // Symbol-day basis: 100 days × $1 = $100. Volume basis: 2 GB × $5 = $10.
        var catalog = MakeCatalog(
            totalBytesRaw: 2_000_000_000,
            MakeSymbol("SPY", tradingDays: 100, eventTypes: ["Trade"]));

        var estimate = DataReplacementCostEstimator.Estimate(catalog, options);

        estimate.SymbolDayBasisUsd.Should().Be(100.00m);
        estimate.VolumeBasisUsd.Should().Be(10.00m);
        estimate.ConservativeEstimateUsd.Should().Be(10.00m);
    }

    [Fact]
    public void Estimate_FallsBackToDefaultRateForUnknownEventTypes()
    {
        var options = new DataReplacementCostOptions
        {
            HistoricalRatePerSymbolDayUsd = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase),
            DefaultRatePerSymbolDayUsd = 0.50m,
            RatePerGigabyteUsd = 1_000m
        };
        var catalog = MakeCatalog(
            totalBytesRaw: 1_000_000_000,
            MakeSymbol("ES", tradingDays: 10, eventTypes: ["FuturesSettlement"]));

        var estimate = DataReplacementCostEstimator.Estimate(catalog, options);

        estimate.SymbolDayBasisUsd.Should().Be(5.00m);
    }

    [Fact]
    public void Estimate_UsesCalendarDaysWhenTradingDaysMissing()
    {
        var symbol = MakeSymbol("BTC-USD", tradingDays: 0, eventTypes: ["Trade"]);
        symbol.DateRange!.CalendarDays = 30;
        var catalog = MakeCatalog(totalBytesRaw: 0, symbol);

        var estimate = DataReplacementCostEstimator.Estimate(catalog, new DataReplacementCostOptions());

        estimate.TotalSymbolDays.Should().Be(30);
    }

    [Fact]
    public void Estimate_CountsSingleDayForSymbolsWithoutDateRange()
    {
        var symbol = new SymbolCatalogEntry
        {
            Symbol = "AAPL",
            EventCount = 500,
            EventTypes = ["Trade"]
        };
        var catalog = MakeCatalog(totalBytesRaw: 0, symbol);

        var estimate = DataReplacementCostEstimator.Estimate(catalog, new DataReplacementCostOptions());

        estimate.TotalSymbolDays.Should().Be(1);
    }

    [Fact]
    public void Estimate_FallsBackToCompressedBytesForVolumeBasis()
    {
        var catalog = MakeCatalog(
            totalBytesRaw: 0,
            MakeSymbol("SPY", tradingDays: 10, eventTypes: ["Trade"]));
        catalog.Statistics.TotalBytesCompressed = 3_000_000_000;

        var estimate = DataReplacementCostEstimator.Estimate(
            catalog,
            new DataReplacementCostOptions { RatePerGigabyteUsd = 5.00m });

        estimate.TotalGigabytes.Should().Be(3.00m);
        estimate.VolumeBasisUsd.Should().Be(15.00m);
    }
}
