using FluentAssertions;
using Meridian.Backtesting.Metrics;

namespace Meridian.Backtesting.Tests;

public sealed class XirrCalculatorTests
{
    [Fact]
    public void Calculate_ReturnsExpectedRate_ForSimpleAnnualReturn()
    {
        // Invest 100 at t=0, receive 110 one year later → 10% XIRR
        var flows = new List<(DateTimeOffset, decimal)>
        {
            (new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), -100m),
            (new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), 110m)
        };

        var result = XirrCalculator.Calculate(flows);

        result.Should().BeApproximately(0.10, 0.001);
    }

    [Fact]
    public void Calculate_ReturnsNaN_ForInsufficientFlows()
    {
        var flows = new List<(DateTimeOffset, decimal)>
        {
            (DateTimeOffset.UtcNow, 100m)
        };

        var result = XirrCalculator.Calculate(flows);

        double.IsNaN(result).Should().BeTrue();
    }

    [Fact]
    public void Calculate_HandlesZeroReturn()
    {
        var start = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var end = start.AddYears(1);

        var flows = new List<(DateTimeOffset, decimal)>
        {
            (start, -100m),
            (end, 100m)
        };

        var result = XirrCalculator.Calculate(flows);

        result.Should().BeApproximately(0.0, 0.001);
    }
}
