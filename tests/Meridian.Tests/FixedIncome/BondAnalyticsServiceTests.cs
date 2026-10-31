using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Instruments.FixedIncome;
using NSubstitute;

namespace Meridian.Tests.FixedIncome;

public sealed class BondAnalyticsServiceTests
{
    private static readonly Guid SecurityId = Guid.Parse("41345b58-1f08-4fb4-89b5-8d191343af82");
    private static readonly DateTimeOffset AsOf = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ParBond_ProducesPriceYieldAndAnalyticalRisk()
    {
        var result = (await Service(Bond()).CalculateAsync(Request(yield: 0.05m)))!;

        result.Blockers.Should().BeEmpty();
        result.CleanPrice.Should().BeApproximately(100m, 0.00000001m);
        result.DirtyPrice.Should().BeApproximately(100m, 0.00000001m);
        result.AccruedInterest.Should().Be(0m);
        result.YieldToMaturity.Should().Be(0.05m);
        result.CurrentYield.Should().BeApproximately(0.05m, 0.00000001m);
        result.CashFlows.Should().HaveCount(4);
        result.CashFlows.Sum(static flow => flow.Interest).Should().Be(10m);
        result.CashFlows.Sum(static flow => flow.Principal).Should().Be(100m);
        result.ModifiedDuration.Should().BeGreaterThan(1.8m).And.BeLessThan(2m);
        result.Dv01.Should().BeGreaterThan(0m);
        result.Inputs.Should().Contain(static input => input.Kind == "SecurityTerms" && input.Version == 7);
        result.Context.SecurityVersion.Should().Be(7);
    }

    [Fact]
    public async Task StreetExample_SemiannualSettlement_SeparatesAccruedAndCleanPrice()
    {
        // 30/360: three months of a five-percent coupon on 100 face = 1.25.
        var context = new BondCalculationContext(AsOf, new DateOnly(2026, 4, 15));
        var service = Service(Bond());
        var priced = (await service.CalculateAsync(new(SecurityId, context, Yield: 0.04m)))!;
        priced.AccruedInterest.Should().Be(1.25m);
        priced.DirtyPrice.Should().Be(priced.CleanPrice + 1.25m);

        var solved = (await service.CalculateAsync(new(SecurityId, context, CleanPrice: priced.CleanPrice)))!;
        solved.YieldToMaturity.Should().BeApproximately(0.04m, 0.000000001m);
        solved.DirtyPrice.Should().BeApproximately(priced.DirtyPrice!.Value, 0.00000001m);
    }

    [Fact]
    public async Task MicrosoftExcelYieldPublishedExample_MatchesSixPointFivePercent()
    {
        // Independent published fixture: Microsoft's YIELD function example, US 30/360, settlement
        // 2008-02-15, maturity 2016-11-15, coupon 5.75%, clean 95.04287, redemption 100, semiannual.
        // https://support.microsoft.com/en-us/office/yield-function-f5f5ca43-c4bd-434f-8bd2-ed3c9727a4fe
        var bond = Bond(coupon: 5.75m);
        var payload = JsonSerializer.Deserialize<Dictionary<string, object?>>(bond.AssetSpecificTerms.GetRawText())!;
        payload["issueDate"] = "2007-11-15";
        payload["maturity"] = "2016-11-15";
        bond = bond with
        {
            EffectiveFrom = new DateTimeOffset(2007, 11, 15, 0, 0, 0, TimeSpan.Zero),
            AssetSpecificTerms = JsonSerializer.SerializeToElement(payload)
        };
        var context = new BondCalculationContext(new DateTimeOffset(2008, 2, 15, 0, 0, 0, TimeSpan.Zero));
        var result = (await Service(bond).CalculateAsync(new(SecurityId, context, CleanPrice: 95.04287m)))!;
        result.Blockers.Should().BeEmpty();
        result.YieldToMaturity.Should().BeApproximately(0.065m, 0.0000001m);
        result.AccruedInterest.Should().Be(1.4375m);
    }

    [Fact]
    public async Task CanonicalZeroCouponWithoutRate_RequiresExplicitYieldConvention()
    {
        var security = Bond("ZeroCoupon", coupon: null, frequency: "AtMaturity");
        var service = Service(security);
        var blocked = (await service.CalculateAsync(Request(yield: 0.05m)))!;
        blocked.Blockers.Should().ContainSingle().Which.Code.Should().Be("YieldConventionRequired");

        var priced = (await service.CalculateAsync(Request(yield: 0.05m) with
        { YieldConvention = BondYieldConvention.AnnualEffective }))!;
        // 2026-01-15 -> 2028-01-15 = exactly 730 Actual/365 days.
        priced.Blockers.Should().BeEmpty();
        priced.CleanPrice.Should().BeApproximately(100m / (1.05m * 1.05m), 0.00000001m);
        priced.CashFlows.Should().ContainSingle().Which.Interest.Should().Be(0m);
    }

    [Fact]
    public async Task MissingFixedRate_IsBlockedInsteadOfBeingReadAsZero()
    {
        var result = (await Service(Bond(coupon: null)).CalculateAsync(Request(yield: 0.05m)))!;
        result.Blockers.Should().ContainSingle().Which.Code.Should().Be("CouponUnitRequired");
        result.DirtyPrice.Should().BeNull();
    }

    [Fact]
    public async Task SubOnePercent_UsesExplicitUnitRatherThanMagnitude()
    {
        var result = (await Service(Bond(coupon: 0.5m)).CalculateAsync(Request(yield: 0.005m)))!;
        result.Blockers.Should().BeEmpty();
        result.CashFlows.Should().OnlyContain(static flow => flow.Interest == 0.25m);
        result.CleanPrice.Should().BeApproximately(100m, 0.00000001m);
    }

    [Fact]
    public async Task UndeclaredBond_IsBlockedRatherThanUsingLegacyDefaults()
    {
        var bond = Bond();
        var payload = JsonSerializer.Deserialize<Dictionary<string, object?>>(bond.AssetSpecificTerms.GetRawText())!;
        payload.Remove("scheduleConventions");
        bond = bond with { AssetSpecificTerms = JsonSerializer.SerializeToElement(payload) };
        var result = (await Service(bond).CalculateAsync(Request(yield: 0.05m)))!;
        result.Blockers.Should().ContainSingle().Which.Code.Should().Be("DeclaredScheduleRequired");
    }

    [Fact]
    public async Task RepeatedReads_KeepPeriodIdentitiesAndInputHashes()
    {
        var service = Service(Bond());
        var first = (await service.CalculateAsync(Request(yield: 0.05m)))!;
        var second = (await service.CalculateAsync(Request(yield: 0.05m)))!;
        second.CashFlows.Should().Equal(first.CashFlows);
        second.Inputs.Should().Equal(first.Inputs);
    }

    [Theory]
    [InlineData(3, false)]
    [InlineData(4, true)]
    public async Task ContractCompanion_OnlyDeclaringVersionBlocksVanillaAnalytics(int termsVersion, bool declared)
    {
        var bond = Bond();
        var payload = JsonSerializer.Deserialize<Dictionary<string, object?>>(bond.AssetSpecificTerms.GetRawText())!;
        payload["assetClassTermsVersion"] = termsVersion;
        payload[Meridian.Contracts.CashFlows.SecurityContractualCashFlowTerms.Key] = new { version = 1 };
        bond = bond with { AssetSpecificTerms = JsonSerializer.SerializeToElement(payload) };

        var result = (await Service(bond).CalculateAsync(Request(yield: 0.05m)))!;

        if (declared)
        {
            result.Blockers.Should().ContainSingle().Which.Code.Should().Be("UnsupportedCashFlowContract");
            result.CleanPrice.Should().BeNull();
            result.CashFlows.Should().BeEmpty();
        }
        else
            result.Blockers.Should().BeEmpty("residue before the field's declaring version does not change economics");
    }

    [Fact]
    public async Task HistoricalRequest_UsesRecordedTermsAndBlocksMissingEvidence()
    {
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetByIdAsync(SecurityId, Arg.Any<CancellationToken>()).Returns(Bond());
        var context = new BondCalculationContext(AsOf, KnownAt: AsOf.AddDays(-1));
        var result = (await new BondAnalyticsService(query).CalculateAsync(new(SecurityId, context, Yield: 0.05m)))!;
        result.Blockers.Should().ContainSingle().Which.Code.Should().Be("HistoricalTermsUnavailable");
        await query.Received(1).GetRecordedByIdAsOfAsync(SecurityId, AsOf.AddDays(-1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FutureEffectiveRecordedAmendment_CannotLeakIntoEarlierEconomicDate()
    {
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetRecordedByIdAsOfAsync(SecurityId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Bond(coupon: 9m) with { EffectiveFrom = AsOf.AddMonths(1) });
        var request = new BondAnalyticsRequest(SecurityId, new(AsOf, KnownAt: AsOf.AddMonths(2)), Yield: 0.05m);
        var result = (await new BondAnalyticsService(query).CalculateAsync(request))!;
        result.Blockers.Should().ContainSingle().Which.Code.Should().Be("TermsNotEffective");
        result.CleanPrice.Should().BeNull();
    }

    [Fact]
    public async Task DiscreteCallAndPut_AreSeparateAndWorstUsesIssuerCallsOnly()
    {
        var security = Bond(exercises: true);
        var result = (await Service(security).CalculateAsync(Request(price: 104m)))!;
        result.Blockers.Should().BeEmpty();
        result.ExerciseYields.Should().HaveCount(2);
        result.ExerciseYields.Single(static item => item.Kind == "Call").RedemptionPrice.Should().Be(101m);
        result.ExerciseYields.Single(static item => item.Kind == "Put").RedemptionPrice.Should().Be(105m);
        result.YieldToWorst.Should().Be(Math.Min(result.YieldToMaturity!.Value,
            result.ExerciseYields.Single(static item => item.Kind == "Call").Yield));
        result.ExerciseYields.Should().OnlyContain(static item => item.LatestNoticeDate == new DateOnly(2026, 12, 16));
    }

    [Fact]
    public async Task CutoverPolicyBlock_IsReturnedBeforeNumbers()
    {
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetByIdAsync(SecurityId, Arg.Any<CancellationToken>()).Returns(Bond());
        var policy = Substitute.For<IBondCalculationPolicy>();
        policy.GetBlockerAsync(Arg.Any<SecurityDetailDto>(), Arg.Any<CancellationToken>()).Returns("Reviewed terms changed after cutover.");
        var result = (await new BondAnalyticsService(query, policy: policy).CalculateAsync(Request(yield: 0.05m)))!;
        result.Blockers.Should().ContainSingle().Which.Code.Should().Be("CalculationPolicyBlocked");
        result.CleanPrice.Should().BeNull();
    }

    private static BondAnalyticsRequest Request(decimal? price = null, decimal? yield = null)
        => new(SecurityId, new(AsOf), price, yield);

    private static BondAnalyticsService Service(SecurityDetailDto security)
    {
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetByIdAsync(SecurityId, Arg.Any<CancellationToken>()).Returns(security);
        return new(query, new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]));
    }

    private static SecurityDetailDto Bond(string kind = "Fixed", decimal? coupon = 5m,
        string frequency = "SemiAnnual", bool exercises = false)
    {
        var payload = new Dictionary<string, object?>
        {
            ["assetClassTermsVersion"] = exercises ? 3 : 2,
            ["issueDate"] = "2026-01-15",
            ["maturity"] = "2028-01-15",
            ["couponType"] = kind,
            ["couponRate"] = coupon,
            ["dayCount"] = "30/360",
            ["par"] = 100m,
            ["paymentFrequency"] = frequency,
            ["isCallable"] = exercises,
            ["scheduleConventions"] = new { accrualAdjusted = false, couponRateUnit = "Percent", couponCashBasis = "Level" }
        };
        if (exercises)
        {
            payload["callDate"] = "2027-01-15";
            payload["callSchedule"] = new[] { new { callDate = "2027-01-15", exerciseStyle = "European",
                callPrice = 101m, priceBasis = "PercentOfPar", priceType = "Fixed", minimumNoticeDays = 30, noticeDayType = "Calendar" } };
            payload["putSchedule"] = new[] { new { putDate = "2027-01-15", exerciseStyle = "European",
                putPrice = 105m, priceBasis = "PercentOfPar", minimumNoticeDays = 30, noticeDayType = "Calendar" } };
        }
        return new(SecurityId, "Bond", SecurityStatusDto.Active, "Analytics bond", "USD",
            JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(payload), [], [],
            7, AsOf, null);
    }
}
