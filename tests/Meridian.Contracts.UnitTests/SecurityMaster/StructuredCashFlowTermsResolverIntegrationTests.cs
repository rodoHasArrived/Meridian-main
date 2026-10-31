using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>Validating shared schedules must preserve the declared bond coupon contract.</summary>
[Trait("Category", "Unit")]
public sealed class StructuredCashFlowTermsResolverIntegrationTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Percent", SecurityCouponRateUnit.Percent)]
    [InlineData("Fraction", SecurityCouponRateUnit.Fraction)]
    public void FloatingBond_ValidatedSchedulesRetainDeclaredUnitAndLegEconomics(
        string rateUnit, SecurityCouponRateUnit expectedUnit)
    {
        var security = Bond(new
        {
            assetClassTermsVersion = 2,
            couponType = "Floating",
            floatingIndex = "SOFR",
            spreadBps = 25m,
            currentIndexRate = 0.5m,
            capRate = 1m,
            floorRate = 0.25m,
            par = 100m,
            scheduleConventions = new { couponRateUnit = rateUnit },
            factorScheduleEntries = new[]
            {
                new { asOfDate = "2027-01-01", factor = 0.75m },
                new { asOfDate = "2026-01-01", factor = 1m }
            },
            principalSchedule = new[]
            {
                new { paymentDate = "2027-01-01", amount = 10m },
                new { paymentDate = "2027-01-01", amount = 15m }
            },
            resetSchedule = new[] { new { resetDate = "2026-01-01", fixingRate = 0.5m } }
        });

        var terms = StructuredCashFlowTermsResolver.Resolve(security);

        terms.CouponKind.Should().Be(SecurityCouponKind.Floating);
        terms.CouponRateUnit.Should().Be(expectedUnit);
        terms.FactorAsOf(new DateOnly(2027, 1, 1)).Should().Be(0.75m);
        terms.PrincipalSchedule.Should().ContainSingle().Which.Should().Be(
            new StructuredPrincipalScheduleEntry(new DateOnly(2027, 1, 1), 25m));
        terms.StepCouponSchedule.Should().BeNull("no source declared a step coupon schedule");
        terms.CapRate.Should().Be(1m);
        terms.FloorRate.Should().Be(0.25m);
        var leg = terms.Legs.Should().ContainSingle().Which;
        leg.LegId.Should().Be("bond-coupon");
        leg.RateKind.Should().Be(CashFlowLegRateKind.Floating);
        leg.Direction.Should().Be(CashFlowLegDirection.Receive);
        leg.RateUnit.Should().Be(expectedUnit);
        leg.IndexName.Should().Be("SOFR");
        leg.SpreadBps.Should().Be(25m);
        leg.CurrentIndexRate.Should().Be(0.5m);
        leg.CapRate.Should().Be(1m);
        leg.FloorRate.Should().Be(0.25m);
        leg.ExchangesPrincipal.Should().BeTrue();
        leg.HasDeclaredResetSchedule.Should().BeTrue();
        leg.ResetSchedule.Should().ContainSingle().Which.Should().Be(
            new StructuredCashFlowResetFixing(new DateOnly(2026, 1, 1), 0.5m));
    }

    [Fact]
    public void ZeroCouponBond_ValidatedPrincipalDoesNotActivateStaleScalarCoupon()
    {
        var terms = StructuredCashFlowTermsResolver.Resolve(Bond(new
        {
            assetClassTermsVersion = 2,
            couponType = "ZeroCoupon",
            couponRate = 9m,
            scheduleConventions = new { couponRateUnit = "Percent" },
            principalSchedule = new[] { new { paymentDate = "2027-01-01", amount = 100m } }
        }));

        terms.CouponKind.Should().Be(SecurityCouponKind.ZeroCoupon);
        terms.CouponRate.Should().Be(0m);
        terms.CouponRateUnit.Should().Be(SecurityCouponRateUnit.Percent);
        terms.PrincipalSchedule.Should().ContainSingle().Which.Amount.Should().Be(100m);
        terms.Legs.Should().BeNull();
    }

    [Fact]
    public void StepBond_ValidatedScheduleRetainsDeclaredUnitAndHistoricalRates()
    {
        var terms = StructuredCashFlowTermsResolver.Resolve(Bond(new
        {
            assetClassTermsVersion = 2,
            couponType = "Step",
            couponRate = 0.5m,
            scheduleConventions = new { couponRateUnit = "Percent" },
            stepSchedule = new[]
            {
                new { effectiveDate = "2028-01-01", rate = 0.75m },
                new { effectiveDate = "2027-01-01", rate = 0.6m }
            }
        }));

        terms.CouponKind.Should().Be(SecurityCouponKind.Step);
        terms.CouponRateUnit.Should().Be(SecurityCouponRateUnit.Percent);
        terms.CouponRateAsOf(new DateOnly(2026, 1, 1)).Should().Be(0.5m);
        terms.CouponRateAsOf(new DateOnly(2027, 1, 1)).Should().Be(0.6m);
        terms.CouponRateAsOf(new DateOnly(2028, 1, 1)).Should().Be(0.75m);
        terms.PrincipalSchedule.Should().BeNull("no source declared contractual principal instalments");
        terms.Legs.Should().BeNull();
    }

    [Theory]
    [InlineData("Floating", "factorScheduleEntries",
        "[{\"asOfDate\":\"2027-01-01\",\"factor\":0.8},{\"asOfDate\":\"2027-01-01\",\"factor\":0.7}]",
        "[{\"asOfDate\":\"2027-01-01\",\"factor\":0.8}]",
        "SM_SCHEDULE_DUPLICATE_DATE", "[1].asOfDate")]
    [InlineData("ZeroCoupon", "principalSchedule",
        "[{\"paymentDate\":\"2027-01-01\",\"amount\":0}]",
        "[{\"paymentDate\":\"2027-01-01\",\"amount\":100}]",
        "SM_SCHEDULE_RANGE_INVALID", "[0].amount")]
    [InlineData("Step", "stepSchedule",
        "[{\"effectiveDate\":\"2027-01-01\",\"rate\":0.5},{\"effectiveDate\":\"2027-01-01\",\"rate\":0.6}]",
        "[{\"effectiveDate\":\"2027-01-01\",\"rate\":0.5}]",
        "SM_SCHEDULE_DUPLICATE_DATE", "[1].effectiveDate")]
    public void DeclaredBond_MalformedGovernedScheduleRefusesValidPassThroughFallback(
        string couponType, string key, string invalidSchedule, string validSchedule, string code, string suffix)
    {
        var validRows = JsonSerializer.Deserialize<JsonElement>(validSchedule);
        var security = Bond(new Dictionary<string, object>
        {
            ["assetClassTermsVersion"] = 2,
            ["scheduleConventions"] = new { couponRateUnit = "Percent" },
            ["profileFields"] = new Dictionary<string, object>
            {
                ["couponType"] = couponType,
                ["floatingIndex"] = "SOFR",
                [key] = JsonSerializer.Deserialize<JsonElement>(invalidSchedule)
            },
            [key] = validRows
        }) with
        {
            CommonTerms = JsonSerializer.SerializeToElement(new Dictionary<string, object> { [key] = validRows })
        };

        Action resolve = () => StructuredCashFlowTermsResolver.Resolve(security);

        var diagnostic = resolve.Should().Throw<SecurityScheduleValidationException>()
            .Which.Diagnostics.Should().ContainSingle().Which;
        diagnostic.Code.Should().Be(code);
        diagnostic.Path.Should().Be($"assetSpecificTerms.profileFields.{key}{suffix}");
    }

    [Fact]
    public void FloatingBond_GovernedEmptySchedulesAndFixingsRemainAuthoritative()
    {
        var terms = StructuredCashFlowTermsResolver.Resolve(Bond(new
        {
            assetClassTermsVersion = 2,
            couponType = "Fixed",
            scheduleConventions = new { couponRateUnit = "Percent" },
            profileFields = new
            {
                couponType = "Floating",
                floatingIndex = "SOFR",
                factorScheduleEntries = Array.Empty<object>(),
                principalSchedule = Array.Empty<object>(),
                stepSchedule = Array.Empty<object>(),
                resetSchedule = Array.Empty<object>()
            },
            factorScheduleEntries = new[] { new { asOfDate = "2027-01-01", factor = 0.8m } },
            principalSchedule = new[] { new { paymentDate = "2027-01-01", amount = 20m } },
            stepSchedule = new[] { new { effectiveDate = "2027-01-01", rate = 9m } },
            resetSchedule = new[] { new { resetDate = "2027-01-01", fixingRate = 9m } }
        }));

        terms.CouponKind.Should().Be(SecurityCouponKind.Floating);
        terms.CouponRateUnit.Should().Be(SecurityCouponRateUnit.Percent);
        terms.FactorSchedule.Should().BeEmpty();
        terms.PrincipalSchedule.Should().NotBeNull().And.BeEmpty();
        terms.StepCouponSchedule.Should().NotBeNull().And.BeEmpty();
        var leg = terms.Legs.Should().ContainSingle().Which;
        leg.HasDeclaredResetSchedule.Should().BeTrue();
        leg.ResetSchedule.Should().BeEmpty();
    }

    [Theory]
    [InlineData(1, SecurityCouponRateUnit.Unspecified)]
    [InlineData(2, SecurityCouponRateUnit.Fraction)]
    public void FloatingBond_ParsedSchedulesRespectTheStoredRateUnitVersionGate(
        int version, SecurityCouponRateUnit expectedUnit)
    {
        var terms = StructuredCashFlowTermsResolver.Resolve(Bond(new
        {
            assetClassTermsVersion = version,
            couponType = "Floating",
            floatingIndex = "SOFR",
            scheduleConventions = new { couponRateUnit = "Fraction" },
            principalSchedule = new[] { new { paymentDate = "2027-01-01", amount = 100m } }
        }));

        terms.PrincipalSchedule.Should().ContainSingle().Which.Amount.Should().Be(100m);
        terms.CouponRateUnit.Should().Be(expectedUnit);
        terms.Legs.Should().ContainSingle().Which.RateUnit.Should().Be(expectedUnit);
    }

    private static SecurityDetailDto Bond(object terms)
        => new(Guid.NewGuid(), "Bond", SecurityStatusDto.Active, "Combined schedule and coupon contract", "USD",
            JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(terms), [], [], 1, AsOf, null);
}
