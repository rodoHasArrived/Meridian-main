using FluentAssertions;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

public sealed class ShareClassUnitRegisterTests
{
    private static ShareClass Class(EqualizationMethod method = EqualizationMethod.None, decimal perfFee = 0.20m)
        => new(
            "class-a",
            "fund-a",
            "Class A",
            "USD",
            managementFeeRateAnnual: 0.02m,
            performanceFeeRate: perfFee,
            inceptionNavPerUnit: 100m,
            inceptionDate: new DateOnly(2026, 1, 1),
            equalizationMethod: method);

    [Fact]
    public void Subscription_IssuesUnitsAtDealingNav()
    {
        var register = ShareClassUnitRegisterProjector.Project(
            Class(),
            [new UnitTransaction("t1", "class-a", "lp-1", new DateOnly(2026, 1, 2), UnitTransactionType.Subscription, navPerUnit: 100m, amount: 1_000_000m)],
            asOf: new DateOnly(2026, 1, 31),
            classNav: 1_000_000m);

        register.UnitsOutstanding.Should().Be(10_000m);
        register.NavPerUnit.Should().Be(100m);
        register.Holdings.Single().Units.Should().Be(10_000m);
    }

    [Fact]
    public void NavPerUnit_RisesWithClassNav()
    {
        var register = ShareClassUnitRegisterProjector.Project(
            Class(),
            [new UnitTransaction("t1", "class-a", "lp-1", new DateOnly(2026, 1, 2), UnitTransactionType.Subscription, navPerUnit: 100m, amount: 1_000_000m)],
            asOf: new DateOnly(2026, 6, 30),
            classNav: 1_200_000m);

        register.NavPerUnit.Should().Be(120m);
        register.Holdings.Single().Value(register.NavPerUnit).Should().Be(1_200_000m);
    }

    [Fact]
    public void Redemption_ReducesUnitsAndFlagsOverRedemption()
    {
        var register = ShareClassUnitRegisterProjector.Project(
            Class(),
            [
                new UnitTransaction("t1", "class-a", "lp-1", new DateOnly(2026, 1, 2), UnitTransactionType.Subscription, navPerUnit: 100m, amount: 1_000_000m),
                new UnitTransaction("t2", "class-a", "lp-1", new DateOnly(2026, 3, 2), UnitTransactionType.Redemption, navPerUnit: 110m, units: 4_000m),
            ],
            asOf: new DateOnly(2026, 3, 31),
            classNav: 660_000m);

        register.UnitsOutstanding.Should().Be(6_000m);
        register.ValidationIssues.Should().BeEmpty();
    }

    [Fact]
    public void OverRedemption_IsFlaggedCritical()
    {
        var register = ShareClassUnitRegisterProjector.Project(
            Class(),
            [
                new UnitTransaction("t1", "class-a", "lp-1", new DateOnly(2026, 1, 2), UnitTransactionType.Subscription, navPerUnit: 100m, amount: 1_000_000m),
                new UnitTransaction("t2", "class-a", "lp-1", new DateOnly(2026, 3, 2), UnitTransactionType.Redemption, navPerUnit: 110m, units: 20_000m),
            ],
            asOf: new DateOnly(2026, 3, 31));

        register.ValidationIssues.Should().Contain(issue =>
            issue.Code == ShareClassUnitRegisterProjector.RedemptionExceedsHoldingIssueCode);
    }

    [Fact]
    public void Equalisation_CreditsSubscriberAboveHighWater()
    {
        // First subscription sets high-water at 100; second subscribes at 120 (above HW) => credit.
        var register = ShareClassUnitRegisterProjector.Project(
            Class(EqualizationMethod.Equalisation),
            [
                new UnitTransaction("t1", "class-a", "lp-1", new DateOnly(2026, 1, 2), UnitTransactionType.Subscription, navPerUnit: 100m, amount: 1_000_000m),
                new UnitTransaction("t2", "class-a", "lp-2", new DateOnly(2026, 6, 2), UnitTransactionType.Subscription, navPerUnit: 120m, amount: 1_200_000m),
            ],
            asOf: new DateOnly(2026, 6, 30),
            classNav: 2_400_000m);

        var lp2 = register.Holdings.Single(holding => holding.InvestorId == "lp-2");
        lp2.EqualisationCredit.Should().BeGreaterThan(0m);
        lp2.ContingentRedemption.Should().Be(0m);
    }

    [Fact]
    public void Equalisation_ContingentRedemptionBelowHighWater()
    {
        // High-water 120 set by first sub; second subscribes at 110 (below HW) => contingent redemption.
        var register = ShareClassUnitRegisterProjector.Project(
            Class(EqualizationMethod.Equalisation),
            [
                new UnitTransaction("t1", "class-a", "lp-1", new DateOnly(2026, 1, 2), UnitTransactionType.Subscription, navPerUnit: 120m, amount: 1_200_000m),
                new UnitTransaction("t2", "class-a", "lp-2", new DateOnly(2026, 6, 2), UnitTransactionType.Subscription, navPerUnit: 110m, amount: 1_100_000m),
            ],
            asOf: new DateOnly(2026, 6, 30),
            classNav: 2_300_000m);

        var lp2 = register.Holdings.Single(holding => holding.InvestorId == "lp-2");
        lp2.ContingentRedemption.Should().BeGreaterThan(0m);
        lp2.EqualisationCredit.Should().Be(0m);
    }
}
