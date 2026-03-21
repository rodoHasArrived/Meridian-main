using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Xunit;

namespace Meridian.Tests;

/// <summary>
/// Unit tests for the OptionQuote domain model.
/// Tests constructor validation, spread/mid calculations, and moneyness.
/// </summary>
public sealed class OptionQuoteTests
{
    private static readonly OptionContractSpec CallContract = new("AAPL", 150m, new DateOnly(2026, 3, 21), OptionRight.Call);
    private static readonly OptionContractSpec PutContract = new("AAPL", 150m, new DateOnly(2026, 3, 21), OptionRight.Put);

    [Fact]
    public void Constructor_WithValidData_CreatesSuccessfully()
    {
        var quote = CreateQuote();

        quote.Symbol.Should().Be("AAPL260321C00150000");
        quote.BidPrice.Should().Be(5.00m);
        quote.AskPrice.Should().Be(5.20m);
        quote.BidSize.Should().Be(10);
        quote.AskSize.Should().Be(15);
        quote.UnderlyingPrice.Should().Be(155m);
        quote.Contract.Should().Be(CallContract);
    }

    [Fact]
    public void Constructor_WithAllOptionalFields_CreatesSuccessfully()
    {
        var quote = CreateQuote(
            lastPrice: 5.10m,
            iv: 0.30m,
            delta: 0.65m,
            gamma: 0.03m,
            theta: -0.05m,
            vega: 0.20m,
            oi: 5000,
            volume: 1200);

        quote.LastPrice.Should().Be(5.10m);
        quote.ImpliedVolatility.Should().Be(0.30m);
        quote.Delta.Should().Be(0.65m);
        quote.Gamma.Should().Be(0.03m);
        quote.Theta.Should().Be(-0.05m);
        quote.Vega.Should().Be(0.20m);
        quote.OpenInterest.Should().Be(5000);
        quote.Volume.Should().Be(1200);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithInvalidSymbol_ThrowsArgumentException(string? symbol)
    {
        var act = () => new OptionQuote(DateTimeOffset.UtcNow, symbol!, CallContract, 5m, 10, 5.20m, 15, 155m);

        act.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("Symbol");
    }

    [Fact]
    public void Constructor_WithNegativeBidPrice_ThrowsArgumentOutOfRangeException()
    {
        var act = () => CreateQuote(bidPrice: -1m);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("BidPrice");
    }

    [Fact]
    public void Constructor_WithNegativeAskPrice_ThrowsArgumentOutOfRangeException()
    {
        var act = () => CreateQuote(askPrice: -1m);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("AskPrice");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithInvalidUnderlyingPrice_ThrowsArgumentOutOfRangeException(decimal price)
    {
        var act = () => CreateQuote(underlyingPrice: price);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("UnderlyingPrice");
    }

    [Fact]
    public void Constructor_WithNullContract_ThrowsArgumentNullException()
    {
        var act = () => new OptionQuote(DateTimeOffset.UtcNow, "SYM", null!, 5m, 10, 5.20m, 15, 155m);

        act.Should().Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("Contract");
    }

    [Fact]
    public void Constructor_WithZeroBidPrice_CreatesSuccessfully()
    {
        // Zero bid is valid (deep OTM options may have no bid)
        var quote = CreateQuote(bidPrice: 0m);

        quote.BidPrice.Should().Be(0m);
    }

    [Fact]
    public void Spread_CalculatesCorrectly()
    {
        var quote = CreateQuote(bidPrice: 5.00m, askPrice: 5.20m);

        quote.Spread.Should().Be(0.20m);
    }

    [Fact]
    public void MidPrice_WithValidQuote_CalculatesCorrectly()
    {
        var quote = CreateQuote(bidPrice: 5.00m, askPrice: 5.20m);

        quote.MidPrice.Should().Be(5.10m);
    }

    [Fact]
    public void MidPrice_WithZeroBid_ReturnsNull()
    {
        var quote = CreateQuote(bidPrice: 0m, askPrice: 5.20m);

        quote.MidPrice.Should().BeNull();
    }

    [Fact]
    public void MidPrice_WithCrossedQuote_ReturnsNull()
    {
        var quote = CreateQuote(bidPrice: 5.30m, askPrice: 5.00m);

        quote.MidPrice.Should().BeNull();
    }

    [Fact]
    public void NotionalValue_WithValidMidPrice_CalculatesCorrectly()
    {
        // MidPrice = 5.10, Multiplier = 100 => 510
        var quote = CreateQuote(bidPrice: 5.00m, askPrice: 5.20m);

        quote.NotionalValue.Should().Be(510m);
    }

    [Fact]
    public void NotionalValue_WithNoMidPrice_ReturnsNull()
    {
        var quote = CreateQuote(bidPrice: 0m, askPrice: 5.20m);

        quote.NotionalValue.Should().BeNull();
    }

    [Fact]
    public void IsInTheMoney_CallAboveStrike_ReturnsTrue()
    {
        // Call with strike 150, underlying at 155
        var quote = CreateQuote(underlyingPrice: 155m);

        quote.IsInTheMoney.Should().BeTrue();
    }

    [Fact]
    public void IsInTheMoney_CallBelowStrike_ReturnsFalse()
    {
        var quote = CreateQuote(underlyingPrice: 145m);

        quote.IsInTheMoney.Should().BeFalse();
    }

    [Fact]
    public void IsInTheMoney_PutBelowStrike_ReturnsTrue()
    {
        var quote = CreateQuote(contract: PutContract, underlyingPrice: 145m);

        quote.IsInTheMoney.Should().BeTrue();
    }

    [Fact]
    public void IsInTheMoney_PutAboveStrike_ReturnsFalse()
    {
        var quote = CreateQuote(contract: PutContract, underlyingPrice: 155m);

        quote.IsInTheMoney.Should().BeFalse();
    }

    [Fact]
    public void Moneyness_ForItmCall_IsGreaterThanOne()
    {
        var quote = CreateQuote(underlyingPrice: 160m);

        // 160 / 150 = 1.0667
        quote.Moneyness.Should().BeGreaterThan(1m);
    }

    [Fact]
    public void Moneyness_ForOtmCall_IsLessThanOne()
    {
        var quote = CreateQuote(underlyingPrice: 140m);

        // 140 / 150 = 0.9333
        quote.Moneyness.Should().BeLessThan(1m);
    }

    [Fact]
    public void Moneyness_ForAtmOption_IsApproximatelyOne()
    {
        var quote = CreateQuote(underlyingPrice: 150m);

        quote.Moneyness.Should().Be(1m);
    }

    [Fact]
    public void Equality_WithSameValues_AreEqual()
    {
        var ts = DateTimeOffset.UtcNow;
        var q1 = new OptionQuote(ts, "SYM", CallContract, 5m, 10, 5.20m, 15, 155m, SequenceNumber: 1);
        var q2 = new OptionQuote(ts, "SYM", CallContract, 5m, 10, 5.20m, 15, 155m, SequenceNumber: 1);

        q1.Should().Be(q2);
    }

    private static OptionQuote CreateQuote(
        OptionContractSpec? contract = null,
        decimal bidPrice = 5.00m,
        decimal askPrice = 5.20m,
        decimal underlyingPrice = 155m,
        decimal? lastPrice = null,
        decimal? iv = null,
        decimal? delta = null,
        decimal? gamma = null,
        decimal? theta = null,
        decimal? vega = null,
        long? oi = null,
        long? volume = null)
        => new(
            DateTimeOffset.UtcNow,
            "AAPL260321C00150000",
            contract ?? CallContract,
            bidPrice, 10, askPrice, 15,
            underlyingPrice,
            lastPrice, iv, delta, gamma, theta, vega, oi, volume);
}
