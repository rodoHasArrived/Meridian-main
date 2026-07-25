using FluentAssertions;
using Meridian.Contracts.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// The composite failover chain must not let a frozen dataset (e.g. Nasdaq WIKI, ended March
/// 2018) win a request for recent data just because it is first in priority order and non-empty.
/// </summary>
public sealed class CompositeProviderStaleDataTests
{
    private static HistoricalBar Bar(DateOnly sessionDate, string source)
        => new("AAPL", sessionDate, 100m, 101m, 99m, 100.5m, 1_000, Source: source);

    private static IReadOnlyList<HistoricalBar> BarsEndingAt(DateOnly latest, string source)
        => [Bar(latest.AddDays(-1), source), Bar(latest, source)];

    [Fact]
    public async Task GetDailyBarsAsync_SkipsStaleProviderWhenFresherProviderExists()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var frozen = new StubProvider("frozen-wiki", priority: 1, BarsEndingAt(new DateOnly(2018, 3, 27), "frozen-wiki"));
        var fresh = new StubProvider("fresh", priority: 2, BarsEndingAt(today, "fresh"));

        using var composite = new CompositeHistoricalDataProvider([frozen, fresh]);

        var bars = await composite.GetDailyBarsAsync("AAPL", from: null, to: null);

        bars.Should().NotBeEmpty();
        bars.Should().OnlyContain(b => b.Source == "fresh",
            "a stale first-priority result must yield to a fresher provider further down the chain");
        frozen.CallCount.Should().Be(1);
        fresh.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetDailyBarsAsync_ReturnsFreshestStaleResultWhenAllProvidersAreStale()
    {
        var older = new StubProvider("older", priority: 1, BarsEndingAt(new DateOnly(2018, 3, 27), "older"));
        var newer = new StubProvider("newer", priority: 2, BarsEndingAt(new DateOnly(2020, 6, 15), "newer"));

        using var composite = new CompositeHistoricalDataProvider([older, newer]);

        var bars = await composite.GetDailyBarsAsync("AAPL", from: null, to: null);

        bars.Should().NotBeEmpty("stale data with a loud signal beats no data");
        bars.Should().OnlyContain(b => b.Source == "newer",
            "when every provider is stale the freshest stale result wins");
    }

    [Fact]
    public async Task GetDailyBarsAsync_HistoricalEraRequest_AcceptsFirstProviderNormally()
    {
        var frozen = new StubProvider("frozen-wiki", priority: 1, BarsEndingAt(new DateOnly(2016, 12, 30), "frozen-wiki"));
        var fresh = new StubProvider("fresh", priority: 2, BarsEndingAt(DateOnly.FromDateTime(DateTime.UtcNow), "fresh"));

        using var composite = new CompositeHistoricalDataProvider([frozen, fresh]);

        var bars = await composite.GetDailyBarsAsync(
            "AAPL",
            from: new DateOnly(2016, 1, 1),
            to: new DateOnly(2016, 12, 31));

        bars.Should().OnlyContain(b => b.Source == "frozen-wiki",
            "a request for an old range is satisfied by an old dataset; recency only applies to range ends near today");
        fresh.CallCount.Should().Be(0);
    }

    private sealed class StubProvider : IHistoricalDataProvider
    {
        private readonly IReadOnlyList<HistoricalBar> _bars;

        public StubProvider(string name, int priority, IReadOnlyList<HistoricalBar> bars)
        {
            Name = DisplayName = name;
            Priority = priority;
            _bars = bars;
        }

        public string Name { get; }
        public string DisplayName { get; }
        public string Description => string.Empty;
        public int Priority { get; }
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
            string symbol, DateOnly? from, DateOnly? to, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(_bars);
        }
    }
}
