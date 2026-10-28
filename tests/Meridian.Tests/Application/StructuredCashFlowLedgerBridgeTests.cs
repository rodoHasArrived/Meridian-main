using FluentAssertions;
using Meridian.Application.SecurityMaster.CashFlow;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;

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
    public void NormalizedSchedule_RemainsBlockedEvenWithoutTextReason()
    {
        var result = new StructuredCashFlowLedgerBridge().BuildCouponAccrualPostings(
            BuildProjection() with { IsNormalizedPer100 = true }, "ABC");
        result.IsPostable.Should().BeFalse();
        result.BlockedReason.Should().Contain("normalized");
        result.Postings.Should().BeEmpty();
    }

    [Fact]
    public void DeclaredScheduleProjection_IsBlocked_AfterAnyBlockedReason()
    {
        var evidence = new StructuredCashFlowScheduleEvidence(
            ScheduleGenerator.KernelVersion,
            ScheduleSpecFactory.DeclaredMappingVersion,
            [],
            []);
        var declared = BuildProjection() with { ScheduleEvidence = evidence };
        var bridge = new StructuredCashFlowLedgerBridge();

        var result = bridge.BuildCouponAccrualPostings(declared, "ABC");

        result.IsPostable.Should().BeFalse("a fresh base projection generated from declared conventions is not postable yet");
        result.BlockedReason.Should().Be(
            "Declared-schedule projections are not postable until journal identity keys on schedule period identity and calendar evidence retention is decided.");
        result.Postings.Should().BeEmpty();

        var blocked = bridge.BuildCouponAccrualPostings(declared with { BlockedReason = "Upstream reason." }, "ABC");
        blocked.BlockedReason.Should().Be("Upstream reason.", "a projection's own blocked reason is reported first");
    }

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
    public void BuildCouponAccrualPostings_UnknownSourceFreshness_ShouldBlock()
    {
        var bridge = new StructuredCashFlowLedgerBridge();

        var result = bridge.BuildCouponAccrualPostings(
            BuildProjection(staleness: StructuredCashFlowStaleness.Unknown), "ABC");

        result.IsPostable.Should().BeFalse();
        result.BlockedReason.Should().Contain("freshness is unknown");
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
