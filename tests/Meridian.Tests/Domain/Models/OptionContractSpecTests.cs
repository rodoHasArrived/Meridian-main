using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Xunit;

namespace Meridian.Tests;

/// <summary>
/// Unit tests for the OptionContractSpec domain model.
/// Tests constructor validation, OCC symbol generation, and calculated properties.
/// </summary>
public sealed class OptionContractSpecTests
{
    [Fact]
    public void Constructor_WithValidData_CreatesSuccessfully()
    {
        var spec = CreateSpec();

        spec.UnderlyingSymbol.Should().Be("AAPL");
        spec.Strike.Should().Be(150m);
        spec.Expiration.Should().Be(new DateOnly(2026, 3, 21));
        spec.Right.Should().Be(OptionRight.Call);
        spec.Style.Should().Be(OptionStyle.American);
        spec.Multiplier.Should().Be(100);
        spec.Exchange.Should().Be("SMART");
        spec.Currency.Should().Be("USD");
        spec.InstrumentType.Should().Be(InstrumentType.EquityOption);
    }

    [Fact]
    public void Constructor_WithIndexOption_CreatesSuccessfully()
    {
        var spec = CreateSpec(underlying: "SPX", style: OptionStyle.European, instrumentType: InstrumentType.IndexOption);

        spec.UnderlyingSymbol.Should().Be("SPX");
        spec.Style.Should().Be(OptionStyle.European);
        spec.InstrumentType.Should().Be(InstrumentType.IndexOption);
    }

    [Fact]
    public void Constructor_WithPutOption_CreatesSuccessfully()
    {
        var spec = CreateSpec(right: OptionRight.Put);

        spec.Right.Should().Be(OptionRight.Put);
    }

    [Fact]
    public void Constructor_WithCustomMultiplier_CreatesSuccessfully()
    {
        // Mini options have multiplier of 10
        var spec = CreateSpec(multiplier: 10);

        spec.Multiplier.Should().Be(10);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithInvalidSymbol_ThrowsArgumentException(string? symbol)
    {
        var act = () => CreateSpec(underlying: symbol!);

        act.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("UnderlyingSymbol");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-0.01)]
    public void Constructor_WithInvalidStrike_ThrowsArgumentOutOfRangeException(decimal strike)
    {
        var act = () => CreateSpec(strike: strike);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("Strike");
    }

    [Fact]
    public void Constructor_WithZeroMultiplier_ThrowsArgumentOutOfRangeException()
    {
        var act = () => CreateSpec(multiplier: 0);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("Multiplier");
    }

    [Theory]
    [InlineData(InstrumentType.Equity)]
    [InlineData(InstrumentType.Future)]
    [InlineData(InstrumentType.SingleStockFuture)]
    public void Constructor_WithInvalidInstrumentType_ThrowsArgumentException(InstrumentType type)
    {
        var act = () => CreateSpec(instrumentType: type);

        act.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("InstrumentType");
    }

    [Fact]
    public void ToOccSymbol_ForCall_GeneratesCorrectFormat()
    {
        var spec = CreateSpec(underlying: "AAPL", strike: 150m, right: OptionRight.Call);

        var occ = spec.ToOccSymbol();

        // AAPL  260321C00150000
        occ.Should().Be("AAPL  260321C00150000");
    }

    [Fact]
    public void ToOccSymbol_ForPut_GeneratesCorrectFormat()
    {
        var spec = CreateSpec(underlying: "AAPL", strike: 150m, right: OptionRight.Put);

        var occ = spec.ToOccSymbol();

        occ.Should().Be("AAPL  260321P00150000");
    }

    [Fact]
    public void ToOccSymbol_WithFractionalStrike_IncludesDecimals()
    {
        var spec = CreateSpec(strike: 152.50m);

        var occ = spec.ToOccSymbol();

        // 152.50 * 1000 = 152500 -> "00152500"
        occ.Should().Contain("00152500");
    }

    [Fact]
    public void ToOccSymbol_WithLongUnderlying_PadsCorrectly()
    {
        // 6 chars exactly, no padding needed
        var spec = CreateSpec(underlying: "GOOGL");

        var occ = spec.ToOccSymbol();

        occ.Should().StartWith("GOOGL ");
    }

    [Fact]
    public void DaysToExpiration_ReturnsCorrectDays()
    {
        var spec = CreateSpec();
        var asOf = new DateOnly(2026, 3, 1);

        spec.DaysToExpiration(asOf).Should().Be(20);
    }

    [Fact]
    public void DaysToExpiration_OnExpirationDay_ReturnsZero()
    {
        var spec = CreateSpec();

        spec.DaysToExpiration(new DateOnly(2026, 3, 21)).Should().Be(0);
    }

    [Fact]
    public void DaysToExpiration_AfterExpiration_ReturnsNegative()
    {
        var spec = CreateSpec();

        spec.DaysToExpiration(new DateOnly(2026, 3, 22)).Should().BeNegative();
    }

    [Fact]
    public void IsExpired_BeforeExpiration_ReturnsFalse()
    {
        var spec = CreateSpec();

        spec.IsExpired(new DateOnly(2026, 3, 20)).Should().BeFalse();
    }

    [Fact]
    public void IsExpired_OnExpirationDay_ReturnsFalse()
    {
        var spec = CreateSpec();

        // On expiration day, options are still tradeable
        spec.IsExpired(new DateOnly(2026, 3, 21)).Should().BeFalse();
    }

    [Fact]
    public void IsExpired_AfterExpiration_ReturnsTrue()
    {
        var spec = CreateSpec();

        spec.IsExpired(new DateOnly(2026, 3, 22)).Should().BeTrue();
    }

    [Fact]
    public void ToString_ReturnsHumanReadableDescription()
    {
        var spec = CreateSpec();

        spec.ToString().Should().Be("AAPL 2026-03-21 150.00 Call");
    }

    [Fact]
    public void ToString_ForPut_ShowsPut()
    {
        var spec = CreateSpec(right: OptionRight.Put);

        spec.ToString().Should().Contain("Put");
    }

    [Fact]
    public void Equality_WithSameValues_AreEqual()
    {
        var spec1 = CreateSpec();
        var spec2 = CreateSpec();

        spec1.Should().Be(spec2);
    }

    [Fact]
    public void Equality_WithDifferentStrike_AreNotEqual()
    {
        var spec1 = CreateSpec(strike: 150m);
        var spec2 = CreateSpec(strike: 155m);

        spec1.Should().NotBe(spec2);
    }

    [Fact]
    public void Constructor_WithOccSymbol_PreservesIt()
    {
        var spec = new OptionContractSpec("AAPL", 150m, new DateOnly(2026, 3, 21), OptionRight.Call,
            OccSymbol: "AAPL  260321C00150000");

        spec.OccSymbol.Should().Be("AAPL  260321C00150000");
    }

    private static OptionContractSpec CreateSpec(
        string underlying = "AAPL",
        decimal strike = 150m,
        OptionRight right = OptionRight.Call,
        OptionStyle style = OptionStyle.American,
        ushort multiplier = 100,
        InstrumentType instrumentType = InstrumentType.EquityOption)
        => new(underlying, strike, new DateOnly(2026, 3, 21), right, style, multiplier, InstrumentType: instrumentType);
}
