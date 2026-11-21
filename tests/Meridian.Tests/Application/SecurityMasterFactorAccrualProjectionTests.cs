using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Application;

public sealed class SecurityMasterFactorAccrualProjectionTests
{
    [Fact]
    public async Task ZeroCouponBondCanCarryFactorEvidenceWithoutInventingARateUnit()
    {
        var terms = Terms();
        terms["assetClassTermsVersion"] = 1;
        terms["couponType"] = "ZeroCoupon";
        terms["fixedCouponRate"] = 0m;
        terms.Remove("rateUnit");

        var projection = await ProjectAsync(terms, assetClass: "Bond");

        projection.BlockedReason.Should().BeNull();
        projection.Schedule.Sum(row => row.InterestAmount).Should().Be(0m);
        projection.Schedule.Last().InterestSegments.Should().HaveCount(2);
        projection.Schedule.Sum(row => row.PrincipalAmount).Should().Be(800m);
    }

    [Fact]
    public async Task NonzeroBondWithoutAnAdmissibleRateUnitRemainsBlocked()
    {
        var terms = Terms();
        terms["assetClassTermsVersion"] = 1;
        terms.Remove("rateUnit");

        var projection = await ProjectAsync(terms, assetClass: "Bond");

        projection.BlockedReason.Should().NotBeNull();
        projection.Schedule.Should().BeEmpty();
    }

    [Theory]
    [InlineData("2026-01-01", 800)]
    [InlineData("2026-08-01", 600)]
    public async Task DatedFactorsRetainThePrePaymentInterestBasisAcrossTheWholeCoupon(string asOf, decimal futurePrincipal)
    {
        var projection = await ProjectAsync(Terms(), asOf: DateOnly.Parse(asOf));

        projection.BlockedReason.Should().BeNull();
        projection.Schedule.Sum(row => row.PrincipalAmount).Should().Be(futurePrincipal);
        var coupon = projection.Schedule.Single(row => row.InterestAmount > 0m);
        coupon.InterestAmount.Should().Be(35m);
        coupon.Factor.Should().Be(0m, "maturity principal is paid after the final coupon accrues");
        coupon.InterestSegments.Should().HaveCount(2);
        coupon.InterestSegments!.Select(segment => segment.ReferencePrincipal).Should().Equal(800m, 600m);
        coupon.InterestSegments.Select(segment => segment.Factor).Should().Equal(0.8m, 0.6m);
        coupon.InterestSegments.Select(segment => segment.FactorAfter).Should().Equal(0.6m, 0m);
        coupon.InterestSegments.Select(segment => segment.UnroundedInterestAmount).Should().Equal(20m, 15m);
        coupon.InterestSegments.Should().OnlyContain(segment => segment.PeriodStart == new DateOnly(2026, 1, 1)
            && segment.PeriodEnd == new DateOnly(2027, 1, 1));
    }

    [Fact]
    public async Task MatchingPrincipalAndFactorEventsReduceTheBalanceOnlyOnce()
    {
        var terms = Terms();
        terms["principalSchedule"] = new[] { new { paymentDate = "2026-07-01", amount = 200m } };

        var projection = await ProjectAsync(terms);

        projection.BlockedReason.Should().BeNull();
        projection.Schedule.Select(row => row.PrincipalAmount).Should().Equal(200m, 600m);
        projection.Schedule.Sum(row => row.InterestAmount).Should().Be(35m);
    }

    [Fact]
    public async Task ContradictoryPrincipalAndFactorEventsReturnNoPartialSchedule()
    {
        var terms = Terms();
        terms["principalSchedule"] = new[] { new { paymentDate = "2026-07-01", amount = 100m } };

        var projection = await ProjectAsync(terms);

        projection.BlockedReason.Should().Contain("do not reconcile");
        projection.Schedule.Should().BeEmpty();
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(200, false)]
    public async Task ARecentFactorCannotHideAnEarlierContradictoryPrincipalPayment(decimal payment, bool blocked)
    {
        var terms = Terms();
        terms["maturity"] = "2026-12-31";
        terms["paymentFrequency"] = "Monthly";
        terms.Remove("currentFactor");
        terms.Remove("factorDate");
        terms["factorScheduleEntries"] = new[]
        {
            new { asOfDate = "2026-01-01", factor = 1m },
            new { asOfDate = "2026-02-01", factor = .8m }
        };
        terms["principalSchedule"] = new[] { new { paymentDate = "2026-02-01", amount = payment } };

        var projection = await ProjectAsync(terms, asOf: new DateOnly(2026, 6, 20));

        if (blocked)
        {
            projection.BlockedReason.Should().Contain("do not reconcile");
            projection.Schedule.Should().BeEmpty();
        }
        else
        {
            projection.BlockedReason.Should().BeNull();
            projection.Schedule.Sum(row => row.PrincipalAmount).Should().Be(800m);
            projection.Schedule.Where(row => row.InterestSegments is { Count: > 0 })
                .SelectMany(row => row.InterestSegments!)
                .Should().OnlyContain(segment => segment.ReferencePrincipal == 800m);
        }
    }

    [Fact]
    public async Task AFactorHistoryWithoutAnOpeningCouponBaselineIsRefused()
    {
        var terms = Terms();
        terms.Remove("currentFactor");
        terms.Remove("factorDate");
        terms["factorScheduleEntries"] = new[] { new { asOfDate = "2026-07-01", factor = 0.6m } };

        var projection = await ProjectAsync(terms);

        projection.BlockedReason.Should().Contain("dated factor at or before");
        projection.Schedule.Should().BeEmpty();
    }

    [Fact]
    public async Task AnUndatedScalarCannotBeUsedAsHistoricalPoolEvidence()
    {
        var terms = Terms();
        terms.Remove("factorDate");
        terms.Remove("factorScheduleEntries");

        var projection = await ProjectAsync(terms);

        projection.BlockedReason.Should().Contain("undated current factor");
        projection.Schedule.Should().BeEmpty();
    }

    [Fact]
    public async Task FactoredSyntheticSinkerNeedsExplicitPrincipalReconciliation()
    {
        var projection = await ProjectAsync(Terms(), StructuredCashFlowSourceKind.CalculatedSinker);

        projection.BlockedReason.Should().Contain("synthetic sinker");
        projection.Schedule.Should().BeEmpty();
    }

    [Fact]
    public async Task FullMidCouponPaydownEarnsInterestThroughItsDateAndNoneAfter()
    {
        var terms = Terms();
        terms["factorScheduleEntries"] = new[]
        {
            new { asOfDate = "2026-01-01", factor = 0.8m },
            new { asOfDate = "2026-07-01", factor = 0m }
        };

        var projection = await ProjectAsync(terms);

        projection.BlockedReason.Should().BeNull();
        projection.Schedule.Select(row => row.PrincipalAmount).Should().Equal(800m, 0m);
        var coupon = projection.Schedule.Last();
        coupon.InterestAmount.Should().Be(20m);
        coupon.InterestSegments!.Last().ReferencePrincipal.Should().Be(0m);
        coupon.InterestSegments.Last().UnroundedInterestAmount.Should().Be(0m);
    }

    [Fact]
    public async Task SegmentedThirty360MeasuresStayAnchoredToTheOriginalPeriod()
    {
        var terms = Terms();
        terms["issueDate"] = "2026-01-31";
        terms["maturity"] = "2026-07-31";
        terms["fixedCouponRate"] = 12m;
        terms["paymentFrequency"] = "SemiAnnual";
        terms["currentFactor"] = 1m;
        terms["factorDate"] = "2026-01-31";
        terms["factorScheduleEntries"] = new[]
        {
            new { asOfDate = "2026-01-31", factor = 1m },
            new { asOfDate = "2026-02-28", factor = 0.8m }
        };

        var projection = await ProjectAsync(terms, asOf: new DateOnly(2026, 1, 31));

        projection.BlockedReason.Should().BeNull();
        var coupon = projection.Schedule.Last();
        var start = new DateOnly(2026, 1, 31);
        var change = new DateOnly(2026, 2, 28);
        var end = new DateOnly(2026, 7, 31);
        var first = DayCountConventions.Fraction("30/360", start, change);
        var whole = DayCountConventions.Fraction("30/360", start, end);
        var expected = decimal.Round(1000m * .12m * first + 800m * .12m * (whole - first),
            4, MidpointRounding.AwayFromZero);
        coupon.InterestAmount.Should().Be(expected);
        coupon.InterestSegments![1].UnroundedInterestAmount.Should().Be(800m * .12m * (whole - first));
    }

    [Fact]
    public async Task UnfactoredFixedScheduleRetainsItsExistingAmountsAndAnExplicitBasis()
    {
        var terms = Terms();
        terms.Remove("factorScheduleEntries");
        terms.Remove("factorDate");
        terms.Remove("currentFactor");

        var projection = await ProjectAsync(terms);

        projection.BlockedReason.Should().BeNull();
        projection.Schedule.Should().ContainSingle();
        projection.Schedule[0].InterestAmount.Should().Be(50m);
        projection.Schedule[0].PrincipalAmount.Should().Be(1000m);
        projection.Schedule[0].InterestSegments.Should().ContainSingle()
            .Which.ReferencePrincipal.Should().Be(1000m);
    }

    private static Dictionary<string, object?> Terms() => new()
    {
        ["assetClassTermsVersion"] = 6,
        ["issueDate"] = "2026-01-01",
        ["maturity"] = "2027-01-01",
        ["originalFace"] = 1000m,
        ["fixedCouponRate"] = 5m,
        ["rateUnit"] = "Percent",
        ["paymentFrequency"] = "Annual",
        ["dayCountConvention"] = "30/360",
        ["currentFactor"] = .8m,
        ["factorDate"] = "2026-01-01",
        ["factorScheduleEntries"] = new[]
        {
            new { asOfDate = "2026-01-01", factor = .8m },
            new { asOfDate = "2026-07-01", factor = .6m }
        }
    };

    private static async Task<StructuredCashFlowProjectionDto> ProjectAsync(
        Dictionary<string, object?> terms,
        StructuredCashFlowSourceKind sourceKind = StructuredCashFlowSourceKind.CalculatedBullet,
        DateOnly? asOf = null,
        string assetClass = "StructuredCredit")
    {
        var securityId = Guid.Parse("53d59514-b9c8-42e4-8f69-39821733ba80");
        var security = new SecurityDetailDto(securityId, assetClass, SecurityStatusDto.Active,
            "Synthetic pool", "USD", JsonSerializer.SerializeToElement(new { }),
            JsonSerializer.SerializeToElement(terms), [], [], 1,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), null);
        var store = Substitute.For<ISecurityMasterCashFlowStore>();
        store.GetSourceAsync(securityId, Arg.Any<CancellationToken>()).Returns(
            new SecurityCashFlowSourceDto(securityId, sourceKind, DateTimeOffset.UtcNow, false, null, null));
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetByIdAsync(securityId, Arg.Any<CancellationToken>()).Returns(security);
        var service = new SecurityMasterCashFlowService(store, [], query,
            NullLogger<SecurityMasterCashFlowService>.Instance);
        return (await service.GetProjectionAsOfAsync(securityId, StructuredCashFlowScenario.Base,
            new DateTimeOffset((asOf ?? new DateOnly(2026, 1, 1)).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)))!;
    }
}
