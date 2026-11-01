using FluentAssertions;
using Meridian.Contracts.FixedIncome;
using Xunit;

namespace Meridian.Contracts.UnitTests;

public sealed class DiscountMathTests
{
    [Theory]
    [InlineData(0.05, 2.0, 2.0)]
    [InlineData(0.0, 1.5, 4.0)]
    [InlineData(-0.01, 3.0, 12.0)]
    public void TryYieldFactor_NominalPeriodic_MatchesClosedFormAndSensitivities(double yield, double time, double frequency)
    {
        DiscountMath.TryYieldFactor(yield, time, BondYieldConvention.NominalPeriodic, frequency,
            out var discount, out var modified, out var convexity).Should().BeTrue();

        var basis = 1d + yield / frequency;
        discount.Should().Be(Math.Pow(basis, -frequency * time));
        modified.Should().Be(time / basis);
        convexity.Should().Be(time * (time + 1d / frequency) / (basis * basis));
    }

    [Fact]
    public void TryYieldFactor_AnnualEffectiveAndSimple_UseTheirOwnBases()
    {
        DiscountMath.TryYieldFactor(0.04, 2.5, BondYieldConvention.AnnualEffective, 12d,
            out var effective, out _, out _).Should().BeTrue();
        effective.Should().Be(Math.Pow(1.04, -2.5));

        DiscountMath.TryYieldFactor(0.04, 2.5, BondYieldConvention.Simple, 12d,
            out var simple, out var modified, out var convexity).Should().BeTrue();
        simple.Should().Be(1d / 1.1);
        modified.Should().Be(2.5 / 1.1);
        convexity.Should().Be(2d * modified * modified);
    }

    [Fact]
    public void TryYieldFactor_NonpositiveBase_Refuses()
    {
        DiscountMath.TryYieldFactor(-2.5, 1d, BondYieldConvention.NominalPeriodic, 1d, out var discount, out _, out _)
            .Should().BeFalse();
        discount.Should().Be(0d);
    }

    [Theory]
    [InlineData(0.05, 2.0, 2)]
    [InlineData(0.03, 0.75, 1)]
    [InlineData(0.0, 4.0, 12)]
    public void TryContractualFactor_Periodic_FormsDecimalBaseBeforeThePower(decimal rate, decimal years, int compounds)
    {
        DiscountMath.TryContractualFactor(rate, years, compounds, out var factor, out var code, out _).Should().BeTrue();

        code.Should().BeNull();
        factor.Should().Be((decimal)Math.Pow((double)(1m + rate / compounds), -(double)(compounds * years)));
    }

    [Fact]
    public void TryContractualFactor_ContinuousCompounding_UsesExp()
    {
        DiscountMath.TryContractualFactor(0.05m, 2m, 0, out var factor, out _, out _).Should().BeTrue();
        factor.Should().Be((decimal)Math.Exp(-(double)(0.05m * 2m)));
    }

    [Fact]
    public void TryContractualFactor_NonpositivePeriodicBase_RefusesWithCode()
    {
        DiscountMath.TryContractualFactor(-3m, 1m, 2, out var factor, out var code, out var message).Should().BeFalse();
        factor.Should().Be(0m);
        code.Should().Be("DiscountRateInvalid");
        message.Should().Contain("positive");
    }
}
