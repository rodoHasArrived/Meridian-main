using FluentAssertions;
using Meridian.Application.Backfill;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.ProviderSdk;
using Xunit;

namespace Meridian.Tests.Application.Backfill;

public sealed class BackfillCostEstimatorTests
{
    // ── Helpers ─────────────────────────────────────────────────────────────

    private static FakeHistoricalDataProvider MakeProvider(
        string name = "testprovider",
        string displayName = "Test Provider",
        int priority = 100,
        TimeSpan? rateLimitDelay = null,
        int maxRequestsPerWindow = int.MaxValue,
        TimeSpan? rateLimitWindow = null,
        bool supportsIntraday = false) =>
        new FakeHistoricalDataProvider(
            name, displayName, priority,
            rateLimitDelay ?? TimeSpan.Zero,
            maxRequestsPerWindow,
            rateLimitWindow ?? TimeSpan.FromHours(1),
            supportsIntraday);

    private static BackfillCostEstimator MakeEstimator(
        params FakeHistoricalDataProvider[] providers) =>
        new(providers);

    // ── Estimate: empty/invalid input ────────────────────────────────────────

    [Fact]
    public void Estimate_NullSymbols_ReturnsEmpty()
    {
        var sut = MakeEstimator(MakeProvider());

        var result = sut.Estimate(new BackfillCostRequest(Symbols: null));

        result.Symbols.Should().BeEmpty();
        result.ProviderEstimates.Should().BeEmpty();
        result.Warnings.Should().ContainSingle();
        result.RecommendedProvider.Should().BeNull();
    }

    [Fact]
    public void Estimate_EmptySymbolList_ReturnsEmpty()
    {
        var sut = MakeEstimator(MakeProvider());

        var result = sut.Estimate(new BackfillCostRequest(Symbols: Array.Empty<string>()));

        result.Symbols.Should().BeEmpty();
        result.ProviderEstimates.Should().BeEmpty();
    }

    [Fact]
    public void Estimate_WhitespaceOnlySymbols_ReturnsEmpty()
    {
        var sut = MakeEstimator(MakeProvider());

        var result = sut.Estimate(new BackfillCostRequest(Symbols: new[] { "  ", "" }));

        result.Symbols.Should().BeEmpty();
    }

    [Fact]
    public void Estimate_UnknownProvider_ReturnsEmptyWithWarning()
    {
        var sut = MakeEstimator(MakeProvider("alpaca", "Alpaca"));

        var result = sut.Estimate(new BackfillCostRequest(
            Symbols: new[] { "AAPL" },
            Provider: "polygon"));

        result.Symbols.Should().BeEmpty();
        result.Warnings.Should().ContainSingle(w => w.Contains("polygon"));
    }

    [Fact]
    public void Estimate_NullRequest_Throws()
    {
        var sut = MakeEstimator(MakeProvider());
        var act = () => sut.Estimate(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── Estimate: single provider ────────────────────────────────────────────

    [Fact]
    public void Estimate_SingleSymbol_SingleProvider_ReturnsOneProviderEstimate()
    {
        var sut = MakeEstimator(MakeProvider("alpaca", "Alpaca"));

        var result = sut.Estimate(new BackfillCostRequest(
            Symbols: new[] { "SPY" },
            From: new DateOnly(2025, 1, 1),
            To: new DateOnly(2025, 1, 31)));

        result.Symbols.Should().ContainSingle("SPY");
        result.ProviderEstimates.Should().ContainSingle();
        result.RecommendedProvider.Should().Be("alpaca");
    }

    [Fact]
    public void Estimate_SpecificProviderRequested_OnlyEstimatesThatProvider()
    {
        var sut = MakeEstimator(
            MakeProvider("alpaca", "Alpaca"),
            MakeProvider("polygon", "Polygon"));

        var result = sut.Estimate(new BackfillCostRequest(
            Symbols: new[] { "AAPL" },
            Provider: "alpaca",
            From: new DateOnly(2025, 1, 1),
            To: new DateOnly(2025, 1, 31)));

        result.ProviderEstimates.Should().ContainSingle();
        result.ProviderEstimates[0].ProviderName.Should().Be("alpaca");
    }

    [Fact]
    public void Estimate_ProviderLookup_IsCaseInsensitive()
    {
        var sut = MakeEstimator(MakeProvider("alpaca", "Alpaca"));

        var result = sut.Estimate(new BackfillCostRequest(
            Symbols: new[] { "AAPL" },
            Provider: "ALPACA",
            From: new DateOnly(2025, 1, 1),
            To: new DateOnly(2025, 1, 31)));

        result.ProviderEstimates.Should().ContainSingle();
        result.ProviderEstimates[0].ProviderName.Should().Be("alpaca");
    }

    // ── Estimate: multi-provider ordering ────────────────────────────────────

    [Fact]
    public void Estimate_MultipleProviders_AllAreEstimated()
    {
        var sut = MakeEstimator(
            MakeProvider("alpaca", "Alpaca", priority: 10),
            MakeProvider("polygon", "Polygon", priority: 20),
            MakeProvider("tiingo", "Tiingo", priority: 30));

        var result = sut.Estimate(new BackfillCostRequest(
            Symbols: new[] { "AAPL" },
            From: new DateOnly(2025, 1, 1),
            To: new DateOnly(2025, 1, 31)));

        result.ProviderEstimates.Should().HaveCount(3);
        result.ProviderEstimates.Select(e => e.ProviderName)
            .Should().BeEquivalentTo(new[] { "alpaca", "polygon", "tiingo" });
    }

    [Fact]
    public void Estimate_RecommendedProvider_PrefersLowestWallClockTime()
    {
        // Fast provider (high rate limit) vs slow (rate-limited)
        var fast = MakeProvider("fast", "Fast Provider", priority: 100,
            maxRequestsPerWindow: int.MaxValue);
        var slow = MakeProvider("slow", "Slow Provider", priority: 50,
            rateLimitDelay: TimeSpan.FromSeconds(1));

        var sut = MakeEstimator(fast, slow);

        var result = sut.Estimate(new BackfillCostRequest(
            Symbols: new[] { "AAPL" },
            From: new DateOnly(2025, 1, 1),
            To: new DateOnly(2025, 3, 31)));

        result.RecommendedProvider.Should().Be("fast");
    }

    // ── Estimate: API call calculations ─────────────────────────────────────

    [Fact]
    public void Estimate_DailyBarsProvider_OneCallPerSymbol()
    {
        // supportsIntraday=false → 1 call per symbol regardless of days
        var sut = MakeEstimator(MakeProvider(supportsIntraday: false));

        var result = sut.Estimate(new BackfillCostRequest(
            Symbols: new[] { "AAPL", "MSFT", "GOOGL" },
            From: new DateOnly(2025, 1, 1),
            To: new DateOnly(2025, 6, 30)));

        result.ProviderEstimates[0].EstimatedApiCalls.Should().Be(3);
    }

    [Fact]
    public void Estimate_IntradayProvider_CallsPerSymbolScaleWithDays()
    {
        // supportsIntraday=true → 1 call per trading-day per symbol
        var sut = MakeEstimator(MakeProvider(supportsIntraday: true));

        var from = new DateOnly(2025, 1, 1);
        var to = new DateOnly(2025, 1, 31); // ~22 trading days
        var result = sut.Estimate(new BackfillCostRequest(
            Symbols: new[] { "AAPL", "MSFT" },
            From: from,
            To: to));

        // Should be 2 symbols × (approx 22 trading days)
        result.ProviderEstimates[0].EstimatedApiCalls.Should().BeGreaterThan(2);
    }

    // ── Estimate: wall-clock time calculations ───────────────────────────────

    [Fact]
    public void Estimate_NoRateLimit_EstimatesBasedOnNetworkOverhead()
    {
        var sut = MakeEstimator(MakeProvider(
            maxRequestsPerWindow: int.MaxValue,
            rateLimitDelay: TimeSpan.Zero));

        var result = sut.Estimate(new BackfillCostRequest(
            Symbols: new[] { "AAPL" },
            From: new DateOnly(2025, 1, 1),
            To: new DateOnly(2025, 1, 31)));

        // With one call (daily bars), estimated 200ms per call
        result.ProviderEstimates[0].EstimatedWallClockTime
            .Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void Estimate_WithRateLimitDelay_WallClockScalesWithCalls()
    {
        var delay = TimeSpan.FromMilliseconds(100);
        var sut = MakeEstimator(MakeProvider(rateLimitDelay: delay, supportsIntraday: false));

        var result = sut.Estimate(new BackfillCostRequest(
            Symbols: new[] { "AAPL", "MSFT" }, // 2 symbols → 2 calls
            From: new DateOnly(2025, 1, 1),
            To: new DateOnly(2025, 1, 31)));

        var estimate = result.ProviderEstimates[0];
        estimate.EstimatedWallClockTime.Should().Be(delay * 2);
    }

    [Fact]
    public void Estimate_WithWindowRateLimit_WallClockBasedOnWindows()
    {
        // 5 max requests per window, 10 calls needed → 2 windows
        var window = TimeSpan.FromMinutes(1);
        var sut = MakeEstimator(MakeProvider(
            maxRequestsPerWindow: 5,
            rateLimitWindow: window,
            supportsIntraday: false));

        var result = sut.Estimate(new BackfillCostRequest(
            Symbols: Enumerable.Range(0, 10).Select(i => $"SYM{i}").ToArray(),
            From: new DateOnly(2025, 1, 1),
            To: new DateOnly(2025, 1, 31)));

        var estimate = result.ProviderEstimates[0];
        estimate.EstimatedWallClockTime.Should().BeGreaterThanOrEqualTo(window);
    }

    // ── Estimate: quota check ─────────────────────────────────────────────────

    [Fact]
    public void Estimate_CallsWithinQuota_WouldExceedFreeQuotaIsFalse()
    {
        var sut = MakeEstimator(MakeProvider(
            maxRequestsPerWindow: 1000,
            supportsIntraday: false));

        var result = sut.Estimate(new BackfillCostRequest(
            Symbols: new[] { "AAPL" },
            From: new DateOnly(2025, 1, 1),
            To: new DateOnly(2025, 1, 31)));

        result.ProviderEstimates[0].WouldExceedFreeQuota.Should().BeFalse();
    }

    [Fact]
    public void Estimate_CallsExceedDoubleQuota_WouldExceedFreeQuotaIsTrue()
    {
        // maxRequestsPerWindow=2, need 6 calls → 6 > 2*2=4, so exceeds
        var sut = MakeEstimator(MakeProvider(
            maxRequestsPerWindow: 2,
            supportsIntraday: false));

        var result = sut.Estimate(new BackfillCostRequest(
            Symbols: Enumerable.Range(0, 6).Select(i => $"SYM{i}").ToArray(),
            From: new DateOnly(2025, 1, 1),
            To: new DateOnly(2025, 1, 31)));

        result.ProviderEstimates[0].WouldExceedFreeQuota.Should().BeTrue();
    }

    [Fact]
    public void Estimate_UnlimitedQuota_WouldExceedFreeQuotaAlwaysFalse()
    {
        var sut = MakeEstimator(MakeProvider(maxRequestsPerWindow: int.MaxValue));

        var result = sut.Estimate(new BackfillCostRequest(
            Symbols: Enumerable.Range(0, 200).Select(i => $"SYM{i}").ToArray(),
            From: new DateOnly(2025, 1, 1),
            To: new DateOnly(2025, 12, 31)));

        result.ProviderEstimates[0].WouldExceedFreeQuota.Should().BeFalse();
    }

    // ── Estimate: warnings ───────────────────────────────────────────────────

    [Fact]
    public void Estimate_LargeDateRange_AddsWarning()
    {
        var sut = MakeEstimator(MakeProvider());

        var result = sut.Estimate(new BackfillCostRequest(
            Symbols: new[] { "AAPL" },
            From: new DateOnly(2020, 1, 1),
            To: new DateOnly(2025, 1, 1))); // ~5 years

        result.Warnings.Should().Contain(w => w.Contains("trading days"));
    }

    [Fact]
    public void Estimate_LargeSymbolList_AddsWarning()
    {
        var sut = MakeEstimator(MakeProvider());
        var symbols = Enumerable.Range(0, 51).Select(i => $"SYM{i:D3}").ToArray();

        var result = sut.Estimate(new BackfillCostRequest(
            Symbols: symbols,
            From: new DateOnly(2025, 1, 1),
            To: new DateOnly(2025, 1, 31)));

        result.Warnings.Should().Contain(w => w.Contains("symbol"));
    }

    [Fact]
    public void Estimate_ExceedsQuota_AddsQuotaWarning()
    {
        var sut = MakeEstimator(MakeProvider(maxRequestsPerWindow: 1));
        var symbols = Enumerable.Range(0, 5).Select(i => $"SYM{i}").ToArray();

        var result = sut.Estimate(new BackfillCostRequest(
            Symbols: symbols,
            From: new DateOnly(2025, 1, 1),
            To: new DateOnly(2025, 1, 31)));

        result.Warnings.Should().Contain(w => w.Contains("quota"));
    }

    // ── Estimate: date defaults ──────────────────────────────────────────────

    [Fact]
    public void Estimate_NullFromDate_DefaultsToOneYearAgo()
    {
        var sut = MakeEstimator(MakeProvider());
        var before = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1).AddDays(-1));

        var result = sut.Estimate(new BackfillCostRequest(
            Symbols: new[] { "AAPL" },
            From: null, To: null));

        result.From.Should().BeAfter(before);
    }

    [Fact]
    public void Estimate_NullToDate_DefaultsToToday()
    {
        var sut = MakeEstimator(MakeProvider());
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var result = sut.Estimate(new BackfillCostRequest(
            Symbols: new[] { "AAPL" },
            From: null, To: null));

        result.To.Should().Be(today);
    }

    // ── Estimate: trading days calculation ───────────────────────────────────

    [Fact]
    public void Estimate_ZeroTradingDays_WhenFromEqualsTo()
    {
        var sut = MakeEstimator(MakeProvider());
        var date = new DateOnly(2025, 1, 15);

        var result = sut.Estimate(new BackfillCostRequest(
            Symbols: new[] { "AAPL" },
            From: date, To: date));

        result.TradingDays.Should().Be(0);
    }

    [Fact]
    public void Estimate_TradingDays_ApproximatesWeekdaysOnly()
    {
        var sut = MakeEstimator(MakeProvider());
        // 7 calendar days ≈ 5 trading days
        var result = sut.Estimate(new BackfillCostRequest(
            Symbols: new[] { "AAPL" },
            From: new DateOnly(2025, 1, 6),
            To: new DateOnly(2025, 1, 13)));

        result.TradingDays.Should().BeInRange(4, 6);
    }

    // ── EstimatedAt field ────────────────────────────────────────────────────

    [Fact]
    public void Estimate_EstimatedAt_IsSetToApproximatelyNow()
    {
        var before = DateTimeOffset.UtcNow;
        var sut = MakeEstimator(MakeProvider());

        var result = sut.Estimate(new BackfillCostRequest(
            Symbols: new[] { "AAPL" },
            From: new DateOnly(2025, 1, 1),
            To: new DateOnly(2025, 1, 31)));

        result.EstimatedAt.Should().BeOnOrAfter(before);
        result.EstimatedAt.Should().BeOnOrBefore(DateTimeOffset.UtcNow.AddSeconds(1));
    }

    // ── BackfillCostEstimate.Empty ───────────────────────────────────────────

    [Fact]
    public void Empty_ContainsReasonInWarnings()
    {
        var empty = BackfillCostEstimate.Empty("test reason");

        empty.Symbols.Should().BeEmpty();
        empty.ProviderEstimates.Should().BeEmpty();
        empty.RecommendedProvider.Should().BeNull();
        empty.Warnings.Should().ContainSingle("test reason");
    }

    // ── ProviderCostEstimate fields ──────────────────────────────────────────

    [Fact]
    public void Estimate_ProviderEstimate_ExposesRateLimitMetadata()
    {
        var window = TimeSpan.FromMinutes(30);
        var delay = TimeSpan.FromMilliseconds(50);
        var sut = MakeEstimator(MakeProvider(
            name: "testprovider",
            displayName: "Test Provider",
            priority: 42,
            rateLimitDelay: delay,
            maxRequestsPerWindow: 100,
            rateLimitWindow: window));

        var result = sut.Estimate(new BackfillCostRequest(
            Symbols: new[] { "AAPL" },
            From: new DateOnly(2025, 1, 1),
            To: new DateOnly(2025, 1, 31)));

        var est = result.ProviderEstimates[0];
        est.ProviderName.Should().Be("testprovider");
        est.DisplayName.Should().Be("Test Provider");
        est.Priority.Should().Be(42);
        est.MaxRequestsPerWindow.Should().Be(100);
        est.RateLimitWindow.Should().Be(window);
        est.RateLimitDelay.Should().Be(delay);
    }

    [Fact]
    public void Estimate_UnlimitedProvider_MaxRequestsPerWindowIsNull()
    {
        var sut = MakeEstimator(MakeProvider(maxRequestsPerWindow: int.MaxValue));

        var result = sut.Estimate(new BackfillCostRequest(
            Symbols: new[] { "AAPL" },
            From: new DateOnly(2025, 1, 1),
            To: new DateOnly(2025, 1, 31)));

        result.ProviderEstimates[0].MaxRequestsPerWindow.Should().BeNull();
    }
}

// ── Test fake ────────────────────────────────────────────────────────────────

/// <summary>
/// Minimal <see cref="IHistoricalDataProvider"/> stub for cost-estimator unit tests.
/// </summary>
internal sealed class FakeHistoricalDataProvider : IHistoricalDataProvider
{
    private readonly TimeSpan _rateLimitDelay;
    private readonly int _maxRequestsPerWindow;
    private readonly TimeSpan _rateLimitWindow;
    private readonly bool _supportsIntraday;

    public FakeHistoricalDataProvider(
        string name,
        string displayName,
        int priority,
        TimeSpan rateLimitDelay,
        int maxRequestsPerWindow,
        TimeSpan rateLimitWindow,
        bool supportsIntraday)
    {
        Name = name;
        DisplayName = displayName;
        Priority = priority;
        _rateLimitDelay = rateLimitDelay;
        _maxRequestsPerWindow = maxRequestsPerWindow;
        _rateLimitWindow = rateLimitWindow;
        _supportsIntraday = supportsIntraday;
    }

    public string Name { get; }
    public string DisplayName { get; }
    public string Description => "Fake provider for tests";

    public int Priority { get; }
    public TimeSpan RateLimitDelay => _rateLimitDelay;
    public int MaxRequestsPerWindow => _maxRequestsPerWindow;
    public TimeSpan RateLimitWindow => _rateLimitWindow;

    public HistoricalDataCapabilities Capabilities =>
        _supportsIntraday
            ? HistoricalDataCapabilities.BarsOnly with { Intraday = true }
            : HistoricalDataCapabilities.BarsOnly;

    public Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
        string symbol, DateOnly? from, DateOnly? to, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<HistoricalBar>>(Array.Empty<HistoricalBar>());

    public void Dispose() { }
}
