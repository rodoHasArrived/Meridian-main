using FluentAssertions;
using Meridian.Contracts.FixedIncome;

namespace Meridian.Tests.FixedIncome;

public sealed class BondWorkflowPricingTests
{
    [Fact]
    public void SignedCouponsAndNegativeRates_UsePositiveFactorsAboveOne()
    {
        BondWorkflowDiscountedFlow[] flows = [new(-2m, 0.5m, 1.01m), new(100m, 1m, 1.02m)];
        BondWorkflowPricing.PresentValue(flows).Should().Be(99.98m);
    }

    [Fact]
    public void NegativeCouponThenPrincipal_HasCertifiedUniqueYield()
    {
        // x = exp(-z) = 0.9 solves 100*x^2 - 2*x = 79.2.
        BondWorkflowDiscountedFlow[] flows = [new(-2m, 1m, 1m), new(100m, 2m, 1m)];
        BondWorkflowPricing.TrySolveSpread(flows, 79.2m, out var spread, out var issue).Should().BeTrue(issue);
        spread.Should().BeApproximately((decimal)-Math.Log(0.9d), 0.000000000001m);
        BondWorkflowPricing.PresentValue(flows, spread).Should().BeApproximately(79.2m, 0.00000001m);
    }

    [Fact]
    public void TwoAdmissibleRoots_AreNotAssignedAnArbitraryYield()
    {
        // -100 + 230*x - 132*x^2 = 0 has positive roots 10/11 and 5/6.
        BondWorkflowDiscountedFlow[] flows = [new(230m, 1m, 1m), new(-132m, 2m, 1m)];
        BondWorkflowPricing.TrySolveSpread(flows, 100m, out var spread, out var issue).Should().BeFalse();
        spread.Should().Be(0m);
        issue.Should().Be("YieldUniquenessNotEstablished");
        BondWorkflowPricing.PresentValue(flows).Should().Be(98m);
    }

    [Fact]
    public void SameDateNettingPrecedesUniquenessCertification()
    {
        BondWorkflowDiscountedFlow[] flows = [new(1m, 1m, 1m), new(-2m, 1m, 1m), new(101m, 2m, 1m)];
        BondWorkflowPricing.TrySolveSpread(flows, 100m, out var spread, out var issue).Should().BeTrue(issue);
        spread.Should().BeApproximately(0m, 0.000000000001m);
    }

    [Fact]
    public void CurveSpreadCalibration_RetainsItsDiscountFactors()
    {
        BondWorkflowDiscountedFlow[] flows = [new(-1m, 0.5m, 1.005m), new(100m, 1m, 0.98m)];
        var cash = -1.005m * (decimal)Math.Exp(-0.02d * 0.5d) + 98m * (decimal)Math.Exp(-0.02d);
        BondWorkflowPricing.TrySolveSpread(flows, cash, out var spread, out var issue).Should().BeTrue(issue);
        spread.Should().BeApproximately(0.02m, 0.000000000001m);
    }

    [Fact]
    public void RiskPreservesNegativeSensitivity()
    {
        var risk = BondWorkflowPricing.Risk([new(-100m, 2m, 1m)]);
        risk.PresentValue.Should().Be(-100m);
        risk.Dv01.Should().BeApproximately(-0.020000000133333m, 0.000000000001m);
        risk.Duration.Should().BeApproximately(2m, 0.0000001m);
    }

    [Fact]
    public void NearZeroNetPv_HasAbsoluteRiskWithoutNormalizedDuration()
    {
        var risk = BondWorkflowPricing.Risk([new(-100m, 1m, 1m), new(100m, 2m, 1m)]);
        risk.PresentValue.Should().Be(0m);
        risk.Dv01.Should().BeApproximately(0.010000000116666m, 0.000000000001m);
        risk.Duration.Should().BeNull();
        risk.Convexity.Should().BeNull();
    }

    [Fact]
    public void AllPaymentsOpposeInitialCash_HasNoYieldRoot()
    {
        BondWorkflowPricing.TrySolveSpread([new(-2m, 1m, 1m)], 100m, out _, out var issue).Should().BeFalse();
        issue.Should().Be("YieldNoRoot");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonpositiveDiscountFactors_AreRefused(decimal factor)
    {
        var action = () => BondWorkflowPricing.PresentValue([new(100m, 1m, factor)]);
        action.Should().Throw<ArgumentException>();
        BondWorkflowPricing.TrySolveSpread([new(100m, 1m, factor)], 100m, out _, out var issue).Should().BeFalse();
        issue.Should().Be("InvalidCashFlows");
    }
}
