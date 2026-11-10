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
    public void Calculate_ReturnsExpectedRate_ForIrregularDatedContributions()
    {
        var start = new DateTimeOffset(2023, 1, 3, 0, 0, 0, TimeSpan.Zero);
        // Each contribution earns 8% annually until day 538:
        // 100,000 * 1.08^(538/365) + 25,000 * 1.08^(401/365) = 139,218.0242521799.
        var flows = new List<(DateTimeOffset, decimal)>
        {
            (start, -100_000m),
            (start.AddDays(137), -25_000m),
            (start.AddDays(538), 139_218.0242521799m)
        };

        var result = XirrCalculator.Calculate(flows);

        result.Should().BeApproximately(0.08, 1e-9);
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
