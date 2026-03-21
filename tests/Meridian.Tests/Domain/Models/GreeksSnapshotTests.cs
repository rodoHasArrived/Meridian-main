using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Xunit;

namespace Meridian.Tests;

/// <summary>
/// Unit tests for the GreeksSnapshot domain model.
/// Tests constructor validation, IV formatting, and intrinsic value calculations.
/// </summary>
public sealed class GreeksSnapshotTests
{
    private static readonly OptionContractSpec CallContract = new("AAPL", 150m, new DateOnly(2026, 3, 21), OptionRight.Call);
    private static readonly OptionContractSpec PutContract = new("AAPL", 150m, new DateOnly(2026, 3, 21), OptionRight.Put);

    [Fact]
    public void Constructor_WithValidData_CreatesSuccessfully()
    {
        var greeks = CreateGreeks();

        greeks.Symbol.Should().Be("AAPL260321C00150000");
        greeks.Delta.Should().Be(0.65m);
        greeks.Gamma.Should().Be(0.03m);
        greeks.Theta.Should().Be(-0.05m);
        greeks.Vega.Should().Be(0.20m);
        greeks.Rho.Should().Be(0.08m);
        greeks.ImpliedVolatility.Should().Be(0.25m);
        greeks.UnderlyingPrice.Should().Be(155m);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithInvalidSymbol_ThrowsArgumentException(string? symbol)
    {
        var act = () => new GreeksSnapshot(DateTimeOffset.UtcNow, symbol!, CallContract,
            0.65m, 0.03m, -0.05m, 0.20m, 0.08m, 0.25m, 155m);

        act.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("Symbol");
    }

    [Fact]
    public void Constructor_WithNullContract_ThrowsArgumentNullException()
    {
        var act = () => new GreeksSnapshot(DateTimeOffset.UtcNow, "SYM", null!,
            0.65m, 0.03m, -0.05m, 0.20m, 0.08m, 0.25m, 155m);

        act.Should().Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("Contract");
    }

    [Fact]
    public void Constructor_WithNegativeIV_ThrowsArgumentOutOfRangeException()
    {
        var act = () => CreateGreeks(iv: -0.01m);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("ImpliedVolatility");
    }

    [Fact]
    public void Constructor_WithZeroIV_CreatesSuccessfully()
    {
        var greeks = CreateGreeks(iv: 0m);

        greeks.ImpliedVolatility.Should().Be(0m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithInvalidUnderlyingPrice_ThrowsArgumentOutOfRangeException(decimal price)
    {
        var act = () => CreateGreeks(underlyingPrice: price);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("UnderlyingPrice");
    }

    [Fact]
    public void Constructor_WithNegativeGamma_ThrowsArgumentOutOfRangeException()
    {
        var act = () => CreateGreeks(gamma: -0.01m);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("Gamma");
    }

    [Fact]
    public void Constructor_WithNegativeVega_ThrowsArgumentOutOfRangeException()
    {
        var act = () => CreateGreeks(vega: -0.01m);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("Vega");
    }

    [Fact]
    public void Constructor_WithNegativeTheta_CreatesSuccessfully()
    {
        // Theta is typically negative (time decay)
        var greeks = CreateGreeks(theta: -0.10m);

        greeks.Theta.Should().Be(-0.10m);
    }

    [Fact]
    public void Constructor_WithNegativeDelta_CreatesSuccessfully()
    {
        // Puts have negative delta
        var greeks = CreateGreeks(delta: -0.35m);

        greeks.Delta.Should().Be(-0.35m);
    }

    [Fact]
    public void ImpliedVolatilityPercent_FormatsCorrectly()
    {
        var greeks = CreateGreeks(iv: 0.25m);

        greeks.ImpliedVolatilityPercent.Should().Be("25.00%");
    }

    [Fact]
    public void ImpliedVolatilityPercent_WithHighIV_FormatsCorrectly()
    {
        var greeks = CreateGreeks(iv: 1.50m);

        greeks.ImpliedVolatilityPercent.Should().Be("150.00%");
    }

    [Fact]
    public void IsInTheMoney_ItmCall_ReturnsTrue()
    {
        var greeks = CreateGreeks(underlyingPrice: 160m);

        greeks.IsInTheMoney.Should().BeTrue();
    }

    [Fact]
    public void IsInTheMoney_OtmCall_ReturnsFalse()
    {
        var greeks = CreateGreeks(underlyingPrice: 140m);

        greeks.IsInTheMoney.Should().BeFalse();
    }

    [Fact]
    public void IsInTheMoney_ItmPut_ReturnsTrue()
    {
        var greeks = CreateGreeks(contract: PutContract, underlyingPrice: 140m);

        greeks.IsInTheMoney.Should().BeTrue();
    }

    [Fact]
    public void IsInTheMoney_OtmPut_ReturnsFalse()
    {
        var greeks = CreateGreeks(contract: PutContract, underlyingPrice: 160m);

        greeks.IsInTheMoney.Should().BeFalse();
    }

    [Fact]
    public void IntrinsicValue_ItmCall_ReturnsPositiveValue()
    {
        // Call strike 150, underlying 160 => intrinsic = 10
        var greeks = CreateGreeks(underlyingPrice: 160m);

        greeks.IntrinsicValue.Should().Be(10m);
    }

    [Fact]
    public void IntrinsicValue_OtmCall_ReturnsZero()
    {
        var greeks = CreateGreeks(underlyingPrice: 140m);

        greeks.IntrinsicValue.Should().Be(0m);
    }

    [Fact]
    public void IntrinsicValue_ItmPut_ReturnsPositiveValue()
    {
        // Put strike 150, underlying 140 => intrinsic = 10
        var greeks = CreateGreeks(contract: PutContract, underlyingPrice: 140m);

        greeks.IntrinsicValue.Should().Be(10m);
    }

    [Fact]
    public void IntrinsicValue_OtmPut_ReturnsZero()
    {
        var greeks = CreateGreeks(contract: PutContract, underlyingPrice: 160m);

        greeks.IntrinsicValue.Should().Be(0m);
    }

    [Fact]
    public void IntrinsicValue_AtmOption_ReturnsZero()
    {
        var greeks = CreateGreeks(underlyingPrice: 150m);

        greeks.IntrinsicValue.Should().Be(0m);
    }

    [Fact]
    public void Constructor_WithOptionalFields_PreservesThem()
    {
        var greeks = new GreeksSnapshot(
            DateTimeOffset.UtcNow, "SYM", CallContract,
            0.65m, 0.03m, -0.05m, 0.20m, 0.08m, 0.25m, 155m,
            TheoreticalPrice: 5.25m, TimeToExpiry: 0.0685m,
            SequenceNumber: 42, Source: "POLYGON");

        greeks.TheoreticalPrice.Should().Be(5.25m);
        greeks.TimeToExpiry.Should().Be(0.0685m);
        greeks.SequenceNumber.Should().Be(42);
        greeks.Source.Should().Be("POLYGON");
    }

    [Fact]
    public void Equality_WithSameValues_AreEqual()
    {
        var ts = DateTimeOffset.UtcNow;
        var g1 = new GreeksSnapshot(ts, "SYM", CallContract, 0.65m, 0.03m, -0.05m, 0.20m, 0.08m, 0.25m, 155m);
        var g2 = new GreeksSnapshot(ts, "SYM", CallContract, 0.65m, 0.03m, -0.05m, 0.20m, 0.08m, 0.25m, 155m);

        g1.Should().Be(g2);
    }

    private static GreeksSnapshot CreateGreeks(
        OptionContractSpec? contract = null,
        decimal delta = 0.65m,
        decimal gamma = 0.03m,
        decimal theta = -0.05m,
        decimal vega = 0.20m,
        decimal iv = 0.25m,
        decimal underlyingPrice = 155m)
        => new(DateTimeOffset.UtcNow, "AAPL260321C00150000", contract ?? CallContract,
            delta, gamma, theta, vega, 0.08m, iv, underlyingPrice);
}
