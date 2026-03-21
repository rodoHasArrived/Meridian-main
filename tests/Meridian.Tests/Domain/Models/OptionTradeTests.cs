using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Xunit;

namespace Meridian.Tests;

/// <summary>
/// Unit tests for the OptionTrade domain model.
/// Tests constructor validation, notional value, and ITM classification.
/// </summary>
public sealed class OptionTradeTests
{
    private static readonly OptionContractSpec CallContract = new("AAPL", 150m, new DateOnly(2026, 3, 21), OptionRight.Call);
    private static readonly OptionContractSpec PutContract = new("AAPL", 150m, new DateOnly(2026, 3, 21), OptionRight.Put);

    [Fact]
    public void Constructor_WithValidData_CreatesSuccessfully()
    {
        var trade = CreateTrade();

        trade.Symbol.Should().Be("AAPL260321C00150000");
        trade.Price.Should().Be(5.50m);
        trade.Size.Should().Be(10);
        trade.Aggressor.Should().Be(AggressorSide.Buy);
        trade.UnderlyingPrice.Should().Be(155m);
        trade.Contract.Should().Be(CallContract);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithInvalidSymbol_ThrowsArgumentException(string? symbol)
    {
        var act = () => new OptionTrade(DateTimeOffset.UtcNow, symbol!, CallContract, 5m, 10, AggressorSide.Buy, 155m);

        act.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("Symbol");
    }

    [Fact]
    public void Constructor_WithNegativePrice_ThrowsArgumentOutOfRangeException()
    {
        var act = () => CreateTrade(price: -1m);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("Price");
    }

    [Fact]
    public void Constructor_WithZeroPrice_CreatesSuccessfully()
    {
        // Zero-price trades can occur (cabinet trades, combo legs)
        var trade = CreateTrade(price: 0m);

        trade.Price.Should().Be(0m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithInvalidSize_ThrowsArgumentOutOfRangeException(long size)
    {
        var act = () => CreateTrade(size: size);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("Size");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithInvalidUnderlyingPrice_ThrowsArgumentOutOfRangeException(decimal price)
    {
        var act = () => CreateTrade(underlyingPrice: price);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("UnderlyingPrice");
    }

    [Fact]
    public void Constructor_WithNullContract_ThrowsArgumentNullException()
    {
        var act = () => new OptionTrade(DateTimeOffset.UtcNow, "SYM", null!, 5m, 10, AggressorSide.Buy, 155m);

        act.Should().Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("Contract");
    }

    [Fact]
    public void NotionalValue_CalculatesCorrectly()
    {
        // Price * Size * Multiplier = 5.50 * 10 * 100 = 5500
        var trade = CreateTrade(price: 5.50m, size: 10);

        trade.NotionalValue.Should().Be(5500m);
    }

    [Fact]
    public void NotionalValue_WithLargeVolume_CalculatesCorrectly()
    {
        var trade = CreateTrade(price: 2.00m, size: 1000);

        trade.NotionalValue.Should().Be(200_000m);
    }

    [Fact]
    public void IsInTheMoney_CallAboveStrike_ReturnsTrue()
    {
        var trade = CreateTrade(underlyingPrice: 160m);

        trade.IsInTheMoney.Should().BeTrue();
    }

    [Fact]
    public void IsInTheMoney_CallBelowStrike_ReturnsFalse()
    {
        var trade = CreateTrade(underlyingPrice: 140m);

        trade.IsInTheMoney.Should().BeFalse();
    }

    [Fact]
    public void IsInTheMoney_PutBelowStrike_ReturnsTrue()
    {
        var trade = CreateTrade(contract: PutContract, underlyingPrice: 140m);

        trade.IsInTheMoney.Should().BeTrue();
    }

    [Fact]
    public void IsInTheMoney_PutAboveStrike_ReturnsFalse()
    {
        var trade = CreateTrade(contract: PutContract, underlyingPrice: 160m);

        trade.IsInTheMoney.Should().BeFalse();
    }

    [Fact]
    public void Constructor_WithOptionalFields_PreservesThem()
    {
        var conditions = new[] { "AutoExecution", "IntermarketSweep" };
        var trade = new OptionTrade(
            DateTimeOffset.UtcNow, "SYM", CallContract, 5m, 10, AggressorSide.Sell, 155m,
            ImpliedVolatility: 0.30m, TradeExchange: "CBOE", Conditions: conditions,
            SequenceNumber: 42, Source: "POLYGON");

        trade.ImpliedVolatility.Should().Be(0.30m);
        trade.TradeExchange.Should().Be("CBOE");
        trade.Conditions.Should().BeEquivalentTo(conditions);
        trade.SequenceNumber.Should().Be(42);
        trade.Source.Should().Be("POLYGON");
    }

    [Theory]
    [InlineData(AggressorSide.Buy)]
    [InlineData(AggressorSide.Sell)]
    [InlineData(AggressorSide.Unknown)]
    public void Constructor_WithAllAggressorSides_CreatesSuccessfully(AggressorSide aggressor)
    {
        var trade = CreateTrade(aggressor: aggressor);

        trade.Aggressor.Should().Be(aggressor);
    }

    [Fact]
    public void Equality_WithSameValues_AreEqual()
    {
        var ts = DateTimeOffset.UtcNow;
        var t1 = new OptionTrade(ts, "SYM", CallContract, 5m, 10, AggressorSide.Buy, 155m, SequenceNumber: 1);
        var t2 = new OptionTrade(ts, "SYM", CallContract, 5m, 10, AggressorSide.Buy, 155m, SequenceNumber: 1);

        t1.Should().Be(t2);
    }

    private static OptionTrade CreateTrade(
        OptionContractSpec? contract = null,
        decimal price = 5.50m,
        long size = 10,
        AggressorSide aggressor = AggressorSide.Buy,
        decimal underlyingPrice = 155m)
        => new(DateTimeOffset.UtcNow, "AAPL260321C00150000", contract ?? CallContract,
            price, size, aggressor, underlyingPrice);
}
