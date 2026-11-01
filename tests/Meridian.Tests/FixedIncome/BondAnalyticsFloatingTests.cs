using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Instruments.FixedIncome;
using Meridian.Tests.SecurityMaster;
using Moq;

namespace Meridian.Tests.FixedIncome;

public sealed class BondAnalyticsFloatingTests
{
    private static readonly Guid SecurityId = Guid.Parse("5d1e7c3a-9b2f-4e6a-8c1d-3f4a5b6c7d8e");
    private static readonly DateTimeOffset AsOf = new(2026, 2, 15, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task V3_ProjectsObservedAndForwardCoupons_AndReportsRateAndSpreadSensitivities()
    {
        var curves = new InMemorySecurityForwardCurveProvider();
        curves.Publish(new SecurityForwardCurve("SOFR", new DateOnly(2026, 1, 1), [new(new DateOnly(2026, 1, 1), 0.02m), new(new DateOnly(2029, 1, 1), 0.02m)], Source: "test-curve"));
        var service = Service(Floating(), curves);

        var result = (await service.CalculateAsync(Request(BondAnalyticsService.FloatingModelVersion, yield: 0.03m)))!;

        result.IsBlocked.Should().BeFalse(string.Join(" ", result.Blockers.Select(b => b.Reason)));
        result.ModelVersion.Should().Be("bond-analytics-v3");
        result.CashFlows.Should().HaveCount(4, "four semi-annual periods remain after a 2026-02-15 settlement on a 2028-01-15 maturity");
        result.CashFlows[0].Interest.Should().Be(2m, "the observed 3% fixing plus 100 bp on a level semi-annual coupon");
        result.CashFlows.Skip(1).Should().OnlyContain(flow => flow.Interest == 1.5m, "forward 2% plus 100 bp");
        result.CashFlows[^1].Principal.Should().Be(100m);
        result.AccruedInterest.Should().BeApproximately(2m * 30m / 180m, 0.000000000001m, "one 30/360 month of the current 4% coupon at twelve-decimal precision");
        result.YieldToMaturity.Should().Be(0.03m);
        result.YieldToWorst.Should().Be(0.03m, "no exercise scenarios are calculated for floaters");
        result.ExerciseYields.Should().BeEmpty();
        result.Dv01.Should().BeGreaterThan(0m);
        // A one-basis-point spread bump adds 0.0001/2 × 100 to every remaining semi-annual coupon; at the unchanged
        // 3% nominal semi-annual yield its value is that annuity discounted on the same flow times.
        var expectedSpreadDv01 = result.CashFlows.Sum(flow => 0.005m / (decimal)Math.Pow(1.015, (double)(flow.TimeInYears * 2m)));
        result.SpreadDv01.Should().BeApproximately(expectedSpreadDv01, 0.00000001m);
        result.Inputs.Should().Contain(input => input.Kind == "ForwardCurve" && input.Key == "SOFR" && input.ContentSha256 != null);
        result.Inputs.Should().Contain(input => input.Kind == "FloatingCoupon");
        result.Diagnostics.Should().Contain(d => d.Contains("not a discount margin"));
        result.CurrentYield.Should().Be(0.04m * 100m / result.CleanPrice!.Value);

        var priced = (await service.CalculateAsync(Request(BondAnalyticsService.FloatingModelVersion, cleanPrice: result.CleanPrice)))!;
        priced.YieldToMaturity.Should().BeApproximately(0.03m, 0.000000001m, "solving the yield from the derived clean price round-trips");
    }

    [Fact]
    public async Task V3_RefusesAMissingHistoricalFixing_AndAMissingForwardCurve()
    {
        var curves = new InMemorySecurityForwardCurveProvider();
        curves.Publish(new SecurityForwardCurve("SOFR", new DateOnly(2026, 1, 1), [new(new DateOnly(2026, 1, 1), 0.02m)]));
        var missingFixing = (await Service(Floating(resetSchedule: false), curves).CalculateAsync(Request(BondAnalyticsService.FloatingModelVersion, yield: 0.03m)))!;
        missingFixing.Blockers.Should().ContainSingle().Which.Code.Should().Be("MissingFixing");
        missingFixing.CleanPrice.Should().BeNull();

        var noCurve = (await Service(Floating(), new InMemorySecurityForwardCurveProvider()).CalculateAsync(Request(BondAnalyticsService.FloatingModelVersion, yield: 0.03m)))!;
        noCurve.Blockers.Should().ContainSingle().Which.Code.Should().Be("ForwardCurveRequired");

        var noProvider = (await Service(Floating(), null).CalculateAsync(Request(BondAnalyticsService.FloatingModelVersion, yield: 0.03m)))!;
        noProvider.Blockers.Should().ContainSingle().Which.Code.Should().Be("ForwardCurveRequired");
    }

    [Fact]
    public async Task ModelsRefuseTheWrongCouponKind_AndV2OutputIsUnchangedForFixedBonds()
    {
        var curves = new InMemorySecurityForwardCurveProvider();
        var v3OnFixed = (await Service(Fixed(), curves).CalculateAsync(Request(BondAnalyticsService.FloatingModelVersion, yield: 0.03m)))!;
        v3OnFixed.Blockers.Should().ContainSingle().Which.Code.Should().Be("UnsupportedCouponStructure");
        var v2OnFloating = (await Service(Floating(), curves).CalculateAsync(Request(BondAnalyticsService.ModelVersion, yield: 0.03m)))!;
        v2OnFloating.Blockers.Should().ContainSingle().Which.Code.Should().Be("UnsupportedCouponStructure");
        var fixedV2 = (await Service(Fixed(), curves).CalculateAsync(Request(BondAnalyticsService.ModelVersion, yield: 0.03m)))!;
        fixedV2.IsBlocked.Should().BeFalse();
        fixedV2.SpreadDv01.Should().BeNull("fixed-coupon analytics never report a spread sensitivity");
        JsonSerializer.Serialize(fixedV2).Should().NotContain("spreadDv01", "the optional field is omitted so retained v1/v2 fixtures keep their bytes");
    }

    private static BondAnalyticsService Service(SecurityDetailDto security, ISecurityForwardCurveProvider? curves)
    {
        var query = new Mock<ISecurityMasterQueryService>(MockBehavior.Strict);
        query.Setup(value => value.GetByIdAsync(SecurityId, It.IsAny<CancellationToken>())).ReturnsAsync(security);
        return new BondAnalyticsService(query.Object, new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]), null, curves);
    }

    private static BondAnalyticsRequest Request(string version, decimal? yield = null, decimal? cleanPrice = null) =>
        new(SecurityId, new BondCalculationContext(AsOf), CleanPrice: cleanPrice, Yield: yield) { CalculationVersion = version };

    private static SecurityDetailDto Floating(bool resetSchedule = true)
    {
        var terms = new Dictionary<string, object?>
        {
            ["assetClassTermsVersion"] = 2,
            ["issueDate"] = "2026-01-15",
            ["maturity"] = "2028-01-15",
            ["couponType"] = "Floating",
            ["floatingIndex"] = "SOFR",
            ["spreadBps"] = 100,
            ["dayCount"] = "30/360",
            ["par"] = 100m,
            ["paymentFrequency"] = "SemiAnnual",
            ["scheduleConventions"] = new { accrualAdjusted = false, couponRateUnit = "Fraction", couponCashBasis = "Level" }
        };
        if (resetSchedule)
            terms["resetSchedule"] = new[] { new { resetDate = "2026-01-15", fixingRate = 0.03m } };
        return Security(terms);
    }

    private static SecurityDetailDto Fixed() => Security(new Dictionary<string, object?>
    {
        ["assetClassTermsVersion"] = 2,
        ["issueDate"] = "2026-01-15",
        ["maturity"] = "2028-01-15",
        ["couponType"] = "Fixed",
        ["couponRate"] = 5m,
        ["dayCount"] = "30/360",
        ["par"] = 100m,
        ["paymentFrequency"] = "SemiAnnual",
        ["scheduleConventions"] = new { accrualAdjusted = false, couponRateUnit = "Percent", couponCashBasis = "Level" }
    });

    private static SecurityDetailDto Security(Dictionary<string, object?> terms) => new(SecurityId, "Bond", SecurityStatusDto.Active, "Floater", "USD",
        JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(terms), [], [], 7, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), null);
}
