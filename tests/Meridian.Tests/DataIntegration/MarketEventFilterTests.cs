using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.DataIntegration.Filters;
using Meridian.DataIntegration.Testing;
using Meridian.Domain.Events;

namespace Meridian.Tests.DataIntegration;

public sealed class MarketEventFilterTests
{
    [Fact]
    public void All_MatchesAnyMarketEvent()
    {
        var evt = CreateEvent("SPY", MarketEventType.Trade, MarketEventTier.Raw);

        MarketEventFilter.All.Matches(evt).Should().BeTrue();
    }

    [Fact]
    public void Matches_UsesCaseInsensitiveSymbol()
    {
        var filter = new MarketEventFilter { Symbol = "spy" };
        var evt = CreateEvent("SPY", MarketEventType.Trade, MarketEventTier.Raw);

        filter.Matches(evt).Should().BeTrue();
    }

    [Fact]
    public void Matches_RequiresConfiguredTypeAndTier()
    {
        var filter = new MarketEventFilter
        {
            Type = MarketEventType.Trade,
            Tier = MarketEventTier.Enriched
        };

        filter.Matches(CreateEvent("SPY", MarketEventType.Trade, MarketEventTier.Enriched)).Should().BeTrue();
        filter.Matches(CreateEvent("SPY", MarketEventType.BboQuote, MarketEventTier.Enriched)).Should().BeFalse();
        filter.Matches(CreateEvent("SPY", MarketEventType.Trade, MarketEventTier.Raw)).Should().BeFalse();
    }

    [Fact]
    public void Matches_RejectsDifferentSymbol()
    {
        var filter = new MarketEventFilter { Symbol = "QQQ" };

        filter.Matches(CreateEvent("SPY", MarketEventType.Trade, MarketEventTier.Raw)).Should().BeFalse();
    }

    [Fact]
    public void DepthBufferSelfTests_RunCompletes()
    {
        var act = () => DepthBufferSelfTests.Run();

        act.Should().NotThrow();
    }

    private static MarketEvent CreateEvent(string symbol, MarketEventType type, MarketEventTier tier)
        => new(
            DateTimeOffset.UtcNow,
            symbol,
            type,
            new global::Meridian.Contracts.Domain.Events.MarketEventPayload.HeartbeatPayload(),
            Source: "TEST",
            Tier: tier);
}
