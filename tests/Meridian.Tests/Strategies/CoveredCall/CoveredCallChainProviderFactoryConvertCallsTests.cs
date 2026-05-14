using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Ui.Shared.Services.CoveredCall;
using Xunit;

namespace Meridian.Tests.Strategies.CoveredCall;

public sealed class CoveredCallChainProviderFactoryConvertCallsTests
{
    [Fact]
    public void ConvertCalls_MapsAllFieldsFromOptionQuote()
    {
        var expiration = new DateOnly(2024, 3, 15);
        var contract = new OptionContractSpec(
            UnderlyingSymbol: "SPY",
            Strike: 525m,
            Expiration: expiration,
            Right: OptionRight.Call,
            Style: OptionStyle.American);

        var quote = new OptionQuote(
            Timestamp: new DateTimeOffset(2024, 2, 13, 16, 0, 0, TimeSpan.Zero),
            Symbol: "SPY240315C00525000",
            Contract: contract,
            BidPrice: 4.50m,
            BidSize: 10,
            AskPrice: 4.60m,
            AskSize: 12,
            UnderlyingPrice: 520m,
            LastPrice: 4.55m,
            ImpliedVolatility: 0.18m,
            Delta: 0.32m,
            Vega: 0.55m,
            OpenInterest: 7500L,
            Volume: 320L);

        var snapshot = new OptionChainSnapshot(
            Timestamp: new DateTimeOffset(2024, 2, 13, 16, 0, 0, TimeSpan.Zero),
            UnderlyingSymbol: "SPY",
            UnderlyingPrice: 520m,
            Expiration: expiration,
            Strikes: new[] { 525m },
            Calls: new[] { quote },
            Puts: Array.Empty<OptionQuote>());

        var scanDate = new DateOnly(2024, 2, 13);
        var result = CoveredCallChainProviderFactory.ConvertCalls(snapshot, scanDate);

        result.Should().HaveCount(1);
        var c = result[0];
        c.UnderlyingSymbol.Should().Be("SPY");
        c.Strike.Should().Be(525m);
        c.Expiration.Should().Be(expiration);
        c.Style.Should().Be(OptionStyle.American);
        c.Multiplier.Should().Be(100);
        c.Bid.Should().Be(4.50m);
        c.Ask.Should().Be(4.60m);
        c.Delta.Should().Be(0.32);
        c.ImpliedVolatility.Should().Be(0.18);
        c.Vega.Should().Be(0.55);
        c.OpenInterest.Should().Be(7500L);
        c.Volume.Should().Be(320L);
        c.DaysToExpiration.Should().Be(31);
    }

    [Fact]
    public void ConvertCalls_NullDeltaDefaultsToZero()
    {
        var expiration = new DateOnly(2024, 3, 15);
        var contract = new OptionContractSpec(
            UnderlyingSymbol: "SPY",
            Strike: 525m,
            Expiration: expiration,
            Right: OptionRight.Call,
            Style: OptionStyle.American);

        var quote = new OptionQuote(
            Timestamp: new DateTimeOffset(2024, 2, 13, 16, 0, 0, TimeSpan.Zero),
            Symbol: "SPY240315C00525000",
            Contract: contract,
            BidPrice: 4.50m,
            BidSize: 10,
            AskPrice: 4.60m,
            AskSize: 12,
            UnderlyingPrice: 520m);

        var snapshot = new OptionChainSnapshot(
            Timestamp: new DateTimeOffset(2024, 2, 13, 16, 0, 0, TimeSpan.Zero),
            UnderlyingSymbol: "SPY",
            UnderlyingPrice: 520m,
            Expiration: expiration,
            Strikes: new[] { 525m },
            Calls: new[] { quote },
            Puts: Array.Empty<OptionQuote>());

        var result = CoveredCallChainProviderFactory.ConvertCalls(snapshot, new DateOnly(2024, 2, 13));

        result[0].Delta.Should().Be(0.0);
        result[0].ImpliedVolatility.Should().BeNull();
        result[0].OpenInterest.Should().Be(0L);
        result[0].Volume.Should().Be(0L);
    }

    [Fact]
    public void ConvertCalls_EmptySnapshotReturnsEmpty()
    {
        var snapshot = new OptionChainSnapshot(
            Timestamp: DateTimeOffset.UtcNow,
            UnderlyingSymbol: "SPY",
            UnderlyingPrice: 520m,
            Expiration: new DateOnly(2024, 3, 15),
            Strikes: Array.Empty<decimal>(),
            Calls: Array.Empty<OptionQuote>(),
            Puts: Array.Empty<OptionQuote>());

        var result = CoveredCallChainProviderFactory.ConvertCalls(snapshot, new DateOnly(2024, 2, 13));

        result.Should().BeEmpty();
    }
}
