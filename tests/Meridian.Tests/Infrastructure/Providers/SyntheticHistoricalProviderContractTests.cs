using FluentAssertions;
using Meridian.Contracts.Domain.Models;
using Meridian.Infrastructure.Adapters.Synthetic;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class SyntheticHistoricalProviderContractTests : HistoricalDataProviderContractTests<SyntheticHistoricalDataProvider>
{
    protected override SyntheticHistoricalDataProvider CreateProvider()
        => SyntheticProviderTestHarness.CreateHistorical();

    [Fact]
    public async Task GeneratedHistory_DoesNotEstablishRawExchangePriceEvidence()
    {
        using var provider = CreateProvider();
        var session = new DateOnly(2024, 1, 2);

        var bar = (await provider.GetAdjustedDailyBarsAsync("AAPL", session, session)).Should().ContainSingle().Subject;

        provider.IsSimulated.Should().BeTrue();
        bar.PriceBasis.Should().Be(HistoricalPriceBasis.Unknown);
        bar.ToHistoricalBar().PriceBasis.Should().Be(HistoricalPriceBasis.Unknown);
        var raw = () => bar.ToHistoricalBar(preferAdjusted: false);
        raw.Should().Throw<InvalidOperationException>();
    }
}
