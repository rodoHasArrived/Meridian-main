using FluentAssertions;
using Meridian.Application.Backfill;
using Meridian.Infrastructure.Adapters.Stooq;
using Meridian.Infrastructure.Adapters.Core;

namespace Meridian.Tests.Application.Backfill;

/// <summary>
/// Unit tests for <see cref="BackfillCostEstimator"/> covering weekday counting,
/// provider-cost estimation, and warning generation.
/// </summary>
public sealed class BackfillCostEstimatorTests
{
    // ------------------------------------------------------------------ //
    //  EstimateTradingDays — weekday counting accuracy                    //
    // ------------------------------------------------------------------ //

    [Theory]
    [InlineData("2024-01-01", "2024-01-01", 0)]  // same day → 0
    [InlineData("2024-01-08", "2024-01-01", 0)]  // to < from → 0
    [InlineData("2024-01-01", "2024-01-08", 5)]  // Mon–Sun (7 days) → 5 weekdays
    [InlineData("2024-01-01", "2024-01-06", 5)]  // Mon–Sat (5 days) → 5 weekdays
    [InlineData("2024-01-01", "2024-01-05", 4)]  // Mon–Fri (4 days, excludes Fri) → 4
    [InlineData("2024-01-06", "2024-01-07", 0)]  // Sat–Sun → 0 weekdays
    [InlineData("2024-01-06", "2024-01-08", 0)]  // Sat–Mon (Sat/Sun only) → 0
    [InlineData("2024-01-07", "2024-01-08", 0)]  // Sun–Mon → 0
    [InlineData("2024-01-08", "2024-01-12", 4)]  // Mon–Fri (4 days, to is exclusive) → 4
    [InlineData("2024-01-08", "2024-01-13", 5)]  // Mon–Sat inclusive range → 5 weekdays
    [InlineData("2024-01-01", "2025-01-01", 262)] // ~1 year (2024 leap year: 366 days → 52 full weeks + 2 extra days Mon/Tue)
    public void EstimateTradingDays_VariousRanges_ReturnsCorrectWeekdayCount(
        string fromStr, string toStr, int expected)
    {
        var from = DateOnly.Parse(fromStr);
        var to = DateOnly.Parse(toStr);
        var estimator = new BackfillCostEstimator(Array.Empty<IHistoricalDataProvider>());

        var result = estimator.Estimate(new BackfillCostRequest(
            Symbols: ["SPY"],
            From: from,
            To: to));

        result.TradingDays.Should().Be(expected,
            because: $"range [{fromStr}, {toStr}) should contain {expected} weekdays");
    }

    // ------------------------------------------------------------------ //
    //  Estimate — basic flow                                              //
    // ------------------------------------------------------------------ //

    [Fact]
    public void Estimate_NoSymbols_ReturnsEmpty()
    {
        var estimator = new BackfillCostEstimator(Array.Empty<IHistoricalDataProvider>());

        var result = estimator.Estimate(new BackfillCostRequest(Symbols: null));

        result.Symbols.Should().BeEmpty();
        result.ProviderEstimates.Should().BeEmpty();
        result.Warnings.Should().ContainSingle(w => w.Contains("No symbols"));
    }

    [Fact]
    public void Estimate_EmptySymbols_ReturnsEmpty()
    {
        var estimator = new BackfillCostEstimator(Array.Empty<IHistoricalDataProvider>());

        var result = estimator.Estimate(new BackfillCostRequest(Symbols: ["", "  "]));

        result.Symbols.Should().BeEmpty();
    }

    [Fact]
    public void Estimate_UnknownProvider_ReturnsWarning()
    {
        var estimator = new BackfillCostEstimator(Array.Empty<IHistoricalDataProvider>());

        var result = estimator.Estimate(new BackfillCostRequest(
            Symbols: ["AAPL"],
            Provider: "nonexistent"));

        result.Warnings.Should().ContainSingle(w => w.Contains("Unknown provider"));
    }

    [Fact]
    public void Estimate_WithStooqProvider_PopulatesProviderEstimates()
    {
        using var provider = new StooqHistoricalDataProvider();
        var estimator = new BackfillCostEstimator([provider]);

        var result = estimator.Estimate(new BackfillCostRequest(
            Symbols: ["AAPL", "MSFT"],
            From: new DateOnly(2024, 1, 1),
            To: new DateOnly(2024, 1, 31)));

        result.ProviderEstimates.Should().HaveCount(1);
        result.ProviderEstimates[0].ProviderName.Should().Be("stooq");
        result.EstimatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Estimate_SingleProvider_SetsRecommendedProvider()
    {
        using var provider = new StooqHistoricalDataProvider();
        var estimator = new BackfillCostEstimator([provider]);

        var result = estimator.Estimate(new BackfillCostRequest(
            Symbols: ["SPY"],
            From: new DateOnly(2024, 1, 1),
            To: new DateOnly(2024, 3, 31)));

        result.RecommendedProvider.Should().Be("stooq");
    }

    [Fact]
    public void Estimate_NullRequest_Throws()
    {
        var estimator = new BackfillCostEstimator(Array.Empty<IHistoricalDataProvider>());

        var act = () => estimator.Estimate(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ------------------------------------------------------------------ //
    //  Warnings                                                           //
    // ------------------------------------------------------------------ //

    [Fact]
    public void Estimate_LargeDateRange_IncludesWarning()
    {
        using var provider = new StooqHistoricalDataProvider();
        var estimator = new BackfillCostEstimator([provider]);

        // 2+ years → well over 365 trading days
        var result = estimator.Estimate(new BackfillCostRequest(
            Symbols: ["SPY"],
            From: new DateOnly(2020, 1, 1),
            To: new DateOnly(2023, 1, 1)));

        result.Warnings.Should().Contain(w => w.Contains("Large date range"));
    }

    [Fact]
    public void Estimate_LargeSymbolList_IncludesWarning()
    {
        using var provider = new StooqHistoricalDataProvider();
        var estimator = new BackfillCostEstimator([provider]);

        var symbols = Enumerable.Range(1, 51).Select(i => $"SYM{i}").ToArray();
        var result = estimator.Estimate(new BackfillCostRequest(
            Symbols: symbols,
            From: new DateOnly(2024, 1, 1),
            To: new DateOnly(2024, 1, 31)));

        result.Warnings.Should().Contain(w => w.Contains("Large symbol list"));
    }
}
