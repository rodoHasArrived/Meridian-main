using FluentAssertions;
using Meridian.Contracts.DirectLending;

namespace Meridian.Tests.Application.DirectLending;

/// <summary>
/// The direct-lending rate normalizer reads the declared unit only: the same economic rate gives
/// the same fraction whatever unit it is declared in, and no value is reinterpreted by magnitude.
/// </summary>
public sealed class RateUnitNormalizerTests
{
    [Theory]
    [InlineData(0.05, DirectLendingRateUnit.Fraction, 0.05)]
    [InlineData(5, DirectLendingRateUnit.Percent, 0.05)]
    [InlineData(500, DirectLendingRateUnit.BasisPoints, 0.05)]
    [InlineData(0.5, DirectLendingRateUnit.Percent, 0.005)] // sub-one percent stays percent
    [InlineData(0.5, DirectLendingRateUnit.Fraction, 0.5)] // sub-one fraction stays fraction
    [InlineData(0, DirectLendingRateUnit.BasisPoints, 0)]
    public void Normalize_UsesTheDeclaredUnitOnly(double value, DirectLendingRateUnit unit, double expected)
    {
        var result = RateUnitNormalizer.Normalize(new DeclaredRate((decimal)value, unit), DirectLendingRateField.IndexFixing);

        result.IsSuccess.Should().BeTrue();
        result.Fraction.Should().Be((decimal)expected);
    }

    [Theory]
    [InlineData(DirectLendingRateField.IndexFixing)]
    [InlineData(DirectLendingRateField.Spread)]
    [InlineData(DirectLendingRateField.Floor)]
    public void Normalize_AcceptsNegativeRatesWhereTheFieldAllowsThem(DirectLendingRateField field)
    {
        var result = RateUnitNormalizer.Normalize(DeclaredRate.BasisPoints(-40m), field);

        result.IsSuccess.Should().BeTrue();
        result.Fraction.Should().Be(-0.004m);
    }

    [Fact]
    public void Normalize_RefusesAMissingRate()
    {
        var result = RateUnitNormalizer.Normalize(null, DirectLendingRateField.Spread);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("must declare its unit");
    }

    [Fact]
    public void Normalize_RefusesAnUndefinedUnit()
    {
        var result = RateUnitNormalizer.Normalize(new DeclaredRate(5m, (DirectLendingRateUnit)9), DirectLendingRateField.IndexFixing);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Normalize_RefusesANegativeCap()
    {
        RateUnitNormalizer.Normalize(DeclaredRate.Percent(-1m), DirectLendingRateField.Cap).IsSuccess.Should().BeFalse();
    }

    [Theory]
    [InlineData(101, DirectLendingRateUnit.Percent)]
    [InlineData(-0.01, DirectLendingRateUnit.Fraction)]
    public void Normalize_RefusesAPikShareOutsideZeroToOne(double value, DirectLendingRateUnit unit)
    {
        RateUnitNormalizer.Normalize(new DeclaredRate((decimal)value, unit), DirectLendingRateField.PikShare).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Normalize_RefusesAValueThatOnlyMakesSenseInAnotherUnit()
    {
        // 500 meant as basis points but declared as percent would be 500%.
        var result = RateUnitNormalizer.Normalize(DeclaredRate.Percent(5_000m), DirectLendingRateField.Spread);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("outside the supported range");
    }

    [Fact]
    public void ToFraction_ThrowsForAnInvalidValue()
    {
        var act = () => RateUnitNormalizer.ToFraction(DeclaredRate.Fraction(2m), DirectLendingRateField.PikShare);

        act.Should().Throw<ArgumentException>();
    }
}
