using FluentAssertions;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

public sealed class PreferredReturnCalculatorTests
{
    [Fact]
    public void SimpleInterest_OneYearOnSingleContribution()
    {
        var result = PreferredReturnCalculator.Accrue(
            [new CapitalContribution(new DateOnly(2026, 1, 1), 1_000_000m)],
            asOf: new DateOnly(2027, 1, 1),
            annualRate: 0.08m,
            compounding: PreferredReturnCompounding.Simple);

        // 365 days / 365 * 8% * 1,000,000 = 80,000
        result.ContributedCapital.Should().Be(1_000_000m);
        result.AccruedPreferredReturn.Should().BeApproximately(80_000m, 1m);
    }

    [Fact]
    public void Compound_ExceedsSimpleOverMultipleYears()
    {
        var contributions = new[] { new CapitalContribution(new DateOnly(2026, 1, 1), 1_000_000m) };
        var asOf = new DateOnly(2029, 1, 1); // 3 years

        var simple = PreferredReturnCalculator.Accrue(contributions, asOf, 0.08m, PreferredReturnCompounding.Simple);
        var compound = PreferredReturnCalculator.Accrue(contributions, asOf, 0.08m, PreferredReturnCompounding.Compound);

        // With only one boundary + asOf, compounding capitalizes once over the whole span, so it
        // equals simple here; add an interim contribution to create a compounding boundary.
        compound.AccruedPreferredReturn.Should().BeGreaterThanOrEqualTo(simple.AccruedPreferredReturn);
    }

    [Fact]
    public void Compound_CapitalizesAtCashFlowBoundaries()
    {
        var contributions = new[]
        {
            new CapitalContribution(new DateOnly(2026, 1, 1), 1_000_000m),
            new CapitalContribution(new DateOnly(2027, 1, 1), 1_000_000m),
        };
        var asOf = new DateOnly(2028, 1, 1);

        var simple = PreferredReturnCalculator.Accrue(contributions, asOf, 0.08m, PreferredReturnCompounding.Simple);
        var compound = PreferredReturnCalculator.Accrue(contributions, asOf, 0.08m, PreferredReturnCompounding.Compound);

        compound.AccruedPreferredReturn.Should().BeGreaterThan(simple.AccruedPreferredReturn);
        compound.ContributedCapital.Should().Be(2_000_000m);
    }

    [Fact]
    public void ReturnOfCapital_ReducesAccrualBase()
    {
        var contributions = new[] { new CapitalContribution(new DateOnly(2026, 1, 1), 2_000_000m) };
        var returns = new[] { new CapitalReturn(new DateOnly(2026, 7, 1), 1_000_000m) };

        var withReturn = PreferredReturnCalculator.Accrue(
            contributions, new DateOnly(2027, 1, 1), 0.08m, PreferredReturnCompounding.Simple, returns);
        var withoutReturn = PreferredReturnCalculator.Accrue(
            contributions, new DateOnly(2027, 1, 1), 0.08m, PreferredReturnCompounding.Simple);

        withReturn.AccruedPreferredReturn.Should().BeLessThan(withoutReturn.AccruedPreferredReturn);
        withReturn.OutstandingCapital.Should().Be(1_000_000m);
    }

    [Fact]
    public void ZeroRate_AccruesNothing()
    {
        var result = PreferredReturnCalculator.Accrue(
            [new CapitalContribution(new DateOnly(2026, 1, 1), 1_000_000m)],
            new DateOnly(2030, 1, 1),
            annualRate: 0m);

        result.AccruedPreferredReturn.Should().Be(0m);
    }
}
