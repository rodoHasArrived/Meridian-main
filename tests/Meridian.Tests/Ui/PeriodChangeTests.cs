using FluentAssertions;
using Meridian.Contracts.Workstation;
using Xunit;

namespace Meridian.Tests.Ui;

/// <summary>The one prior-period rule every comparison uses (UX Phase E).</summary>
public sealed class PeriodChangeTests
{
    [Theory]
    [InlineData(120, 100, 20, 20)]
    [InlineData(80, 100, -20, -20)]
    // The base is |prior|, so a shrinking loss is a positive change and a positive percent.
    [InlineData(-50, -200, 150, 75)]
    [InlineData(-100, 100, -200, -200)]
    public void ChangeIsCurrentMinusPriorAndPercentDividesByTheAbsolutePrior(double current, double prior, double change, double percent)
    {
        PeriodChange.Change((decimal)current, (decimal)prior).Should().Be((decimal)change);
        PeriodChange.Percent((decimal)current, (decimal)prior).Should().Be((decimal)percent);
    }

    [Fact]
    public void AZeroPriorHasNoPercent()
    {
        PeriodChange.Percent(25m, 0m).Should().BeNull();
        PeriodComparisonLineDto.Create("x", "X", 25m, 0m).Should().Be(new PeriodComparisonLineDto("x", "X", 25m, 0m, 25m, null));
    }
}
