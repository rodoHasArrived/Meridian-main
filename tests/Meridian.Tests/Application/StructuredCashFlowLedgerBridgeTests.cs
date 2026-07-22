using FluentAssertions;
using Meridian.Application.SecurityMaster.CashFlow;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.Application;

public sealed class StructuredCashFlowLedgerBridgeTests
{
    private static readonly Guid SecurityId = Guid.Parse("77777777-1111-2222-3333-777777777777");

    private static StructuredCashFlowProjectionDto BuildProjection(
        StructuredCashFlowStaleness staleness = StructuredCashFlowStaleness.Fresh,
        StructuredCashFlowScenario scenario = StructuredCashFlowScenario.Base,
        decimal firstInterest = 5m,
        decimal secondInterest = 5m)
        => new(
            SecurityId,
            StructuredCashFlowSourceKind.CalculatedBullet,
            scenario,
            DateTimeOffset.UtcNow,
            new[]
            {
                new StructuredCashFlowScheduleEntry(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero), 0m, firstInterest, 1m),
                new StructuredCashFlowScheduleEntry(new DateTimeOffset(2030, 7, 1, 0, 0, 0, TimeSpan.Zero), 100m, secondInterest, 0m)
            },
            staleness);

    [Fact]
    public void BuildCouponAccrualPostings_FreshBaseProjection_ShouldReturnBalancedPostings()
    {
        var bridge = new StructuredCashFlowLedgerBridge();

        var result = bridge.BuildCouponAccrualPostings(BuildProjection(), "ABC");

        result.IsPostable.Should().BeTrue();
        result.BlockedReason.Should().BeNull();
        result.Postings.Should().HaveCount(2);
        result.Postings.Should().OnlyContain(posting => posting.IsBalanced);
        result.Postings[0].Lines.Should().Contain(line =>
            line.Account == "Accrued Interest Receivable" && line.AccountType == "Asset" && line.Debit == 5m);
        result.Postings[0].Lines.Should().Contain(line =>
            line.Account == "Coupon Income" && line.AccountType == "Revenue" && line.Credit == 5m);
    }

    [Fact]
    public void BuildCouponAccrualPostings_StaleSource_ShouldBlock()
    {
        var bridge = new StructuredCashFlowLedgerBridge();

        var result = bridge.BuildCouponAccrualPostings(
            BuildProjection(staleness: StructuredCashFlowStaleness.Stale), "ABC");

        result.IsPostable.Should().BeFalse();
        result.BlockedReason.Should().Contain("stale");
        result.Postings.Should().BeEmpty();
    }

    [Fact]
    public void BuildCouponAccrualPostings_NonBaseScenario_ShouldBlock()
    {
        var bridge = new StructuredCashFlowLedgerBridge();

        var result = bridge.BuildCouponAccrualPostings(
            BuildProjection(scenario: StructuredCashFlowScenario.Up100), "ABC");

        result.IsPostable.Should().BeFalse();
        result.BlockedReason.Should().Contain("what-if");
        result.Postings.Should().BeEmpty();
    }

    [Fact]
    public void BuildCouponAccrualPostings_NoAccruablePeriods_ShouldBlock()
    {
        var bridge = new StructuredCashFlowLedgerBridge();

        var result = bridge.BuildCouponAccrualPostings(
            BuildProjection(firstInterest: 0m, secondInterest: 0m), "ABC");

        result.IsPostable.Should().BeFalse();
        result.BlockedReason.Should().Contain("no accruable");
        result.Postings.Should().BeEmpty();
    }

    [Fact]
    public void BuildCouponAccrualPostings_ThroughFilter_ShouldPostOnlyInWindow()
    {
        var bridge = new StructuredCashFlowLedgerBridge();

        var result = bridge.BuildCouponAccrualPostings(
            BuildProjection(), "ABC", through: new DateOnly(2030, 1, 1));

        result.IsPostable.Should().BeTrue();
        result.Postings.Should().ContainSingle();
        result.Postings[0].PeriodDate.Should().Be(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void BuildCouponAccrualPostings_ShouldScopeAccountsToFinancialAccount()
    {
        var bridge = new StructuredCashFlowLedgerBridge();

        var result = bridge.BuildCouponAccrualPostings(BuildProjection(), "ABC", financialAccountId: "BROKER-1");

        result.IsPostable.Should().BeTrue();
        result.Postings[0].Lines.Should().OnlyContain(line => line.FinancialAccountId == "BROKER-1");
    }
}
