using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Execution.Adapters;
using Meridian.Execution.PaperMatching;
using Meridian.Risk;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;
using Moq;
using Xunit;
using OrderSide = Meridian.Execution.Sdk.OrderSide;

namespace Meridian.Tests.Ui;

/// <summary>
/// The pre-trade guard predicts the paper matcher, so under an observation freshness window it
/// must drop exactly the observations the matcher drops.
/// </summary>
public sealed class AggregatePortfolioExposureProviderFreshnessTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 2, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TriggerReference_SharesTheWindow_SoItDropsThePrintTheMatcherDrops()
    {
        var aggregate = new Mock<IAggregatePortfolioService>();
        aggregate.Setup(a => a.GetAggregatedPositions(null)).Returns([]);

        var cache = new LiveMarketDataCache();
        cache.RecordTrade("AAPL", new Trade(
            Now.AddMinutes(-6), "AAPL", 130m, Size: 100, Aggressor: AggressorSide.Buy, SequenceNumber: 1));
        cache.RecordQuote("AAPL", new BboQuotePayload(
            Now.AddSeconds(-1), "AAPL", 90m, 100, 100m, 100, 95m, 10m, 1));

        IPortfolioExposureProvider Guard(PaperObservationFreshnessPolicy? freshness) =>
            new AggregatePortfolioExposureProvider(
                aggregate.Object,
                clock: () => Now,
                liveFeedAccessor: () => cache,
                paperMatchingIsAuthoritative: () => true,
                paperObservationFreshness: freshness);

        Guard(null).TryGetTriggerReferencePrice("AAPL", OrderSide.Buy)
            .Should().Be(130m, "with no window the matcher still fires from the old print");
        Guard(new PaperObservationFreshnessPolicy(TimeSpan.FromSeconds(5), () => Now))
            .TryGetTriggerReferencePrice("AAPL", OrderSide.Buy)
            .Should().Be(100m, "under the window the matcher resolves the trigger from the fresh ask");
    }
}
