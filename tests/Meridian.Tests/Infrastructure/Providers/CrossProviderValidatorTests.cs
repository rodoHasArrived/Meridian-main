using FluentAssertions;
using Meridian.Contracts.Domain.Models;
using Meridian.Core.Config;
using Meridian.Infrastructure.Adapters.Core;
using Moq;
using Serilog;
using Xunit;

namespace Meridian.Tests.Providers;

/// <summary>
/// The backfill cross-provider validator uses the configured bar count and close tolerance
/// instead of the historical fixed "first 5 bars, 1%" check.
/// </summary>
public sealed class CrossProviderValidatorTests
{
    private const int Sessions = 10;

    private static IReadOnlyList<HistoricalBar> Bars(string source, decimal closeMultiplier)
        => Enumerable.Range(0, Sessions)
            .Select(i => new HistoricalBar(
                "SPY",
                new DateOnly(2026, 10, 1).AddDays(i),
                100m + i, 105m + i, 95m + i, Math.Round((102m + i) * closeMultiplier, 4),
                1000L * (i + 1),
                source,
                i))
            .ToArray();

    private static CrossProviderValidator Create(CrossProviderTolerancePolicy? policy)
    {
        var source = new Mock<IHistoricalDataProvider>();
        source.Setup(p => p.Name).Returns("stooq");
        var comparison = new Mock<IHistoricalDataProvider>();
        comparison.Setup(p => p.Name).Returns("tiingo");
        comparison.Setup(p => p.GetDailyBarsAsync("SPY", It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Bars("tiingo", 1.003m)); // every close 0.3% higher
        return new CrossProviderValidator(
            [source.Object, comparison.Object],
            (symbol, _, _) => Task.FromResult(symbol),
            Log.Logger,
            policy is null ? null : () => policy);
    }

    private static Task<int> Validate(CrossProviderValidator validator)
        => validator.ValidateAsync(Bars("stooq", 1m), "SPY", null, null, "stooq", CancellationToken.None);

    [Fact]
    public async Task DefaultPolicy_KeepsHistoricalBehaviour()
    {
        (await Validate(Create(null))).Should().Be(0, "0.3% is inside the default 1% close tolerance");
    }

    [Fact]
    public async Task StrictTolerance_ChecksOnlyConfiguredNumberOfBars()
    {
        var policy = new CrossProviderTolerancePolicy(BackfillCloseTolerancePercent: 0.2);

        (await Validate(Create(policy))).Should().Be(5, "the default still compares the first five bars");
    }

    [Fact]
    public async Task NullBarLimit_ChecksEveryBar()
    {
        var policy = new CrossProviderTolerancePolicy(BackfillCloseTolerancePercent: 0.2, BackfillBarsToCompare: null);

        (await Validate(Create(policy))).Should().Be(Sessions);
    }
}
