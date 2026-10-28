using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Application;

public sealed partial class SecurityMasterCashFlowServiceTests
{
    [Theory]
    [InlineData("Percent", 0.5, 0.5)]
    [InlineData("Fraction", 0.005, 0.5)]
    public async Task DeclaredCoupon_UsesExplicitRateUnitAndAccrualEnd(string unit, decimal rate, decimal expected)
    {
        var projection = await DeclaredProjection(new
        {
            assetClassTermsVersion = 2, issueDate = "2026-01-31", maturityDate = "2027-01-31",
            par = 100m, couponRate = rate, paymentFrequency = "Annual", dayCountConvention = "30/360",
            scheduleConventions = new
            {
                couponRateUnit = unit, couponCashBasis = "DayCount", businessDayConvention = "Following",
                paymentCalendars = new[] { "WEEKENDS" }, paymentLagBusinessDays = 2
            }
        });
        projection.BlockedReason.Should().Contain("preview-only");
        projection.Schedule.Should().ContainSingle();
        projection.Schedule[0].InterestAmount.Should().Be(expected);
        projection.Schedule[0].PeriodDate.Should().Be(new DateTimeOffset(2027, 2, 2, 0, 0, 0, TimeSpan.Zero));
        projection.Schedule[0].PrincipalAmount.Should().Be(100m);
    }

    [Fact]
    public async Task DeclaredCalendar_UnknownIdReturnsBlockedEmptySchedule()
    {
        var projection = await DeclaredProjection(new
        {
            assetClassTermsVersion = 2, issueDate = "2026-01-31", maturityDate = "2027-01-31",
            par = 100m, couponRate = 6m, paymentFrequency = "Annual",
            scheduleConventions = new
            {
                couponRateUnit = "Percent", couponCashBasis = "Level", paymentCalendars = new[] { "UNKNOWN" }
            }
        });
        projection.BlockedReason.Should().Contain("cannot be resolved");
        projection.Schedule.Should().BeEmpty();
    }

    [Fact]
    public async Task UndeclaredVersion_StrayBlockKeepsLegacyDatesAndRateNormalization()
    {
        var projection = await DeclaredProjection(new
        {
            assetClassTermsVersion = 1, issueDate = "2026-01-31", maturityDate = "2027-01-31",
            par = 100m, couponRate = 0.5m, paymentFrequency = "Annual", dayCountConvention = "30/360",
            scheduleConventions = new { businessDayConvention = "NoSuchConvention" }
        });
        projection.BlockedReason.Should().BeNull();
        projection.Schedule[0].PeriodDate.Should().Be(new DateTimeOffset(2027, 1, 31, 0, 0, 0, TimeSpan.Zero));
        projection.Schedule[0].InterestAmount.Should().Be(50m);
    }

    [Fact]
    public async Task DeclaredSwapLegs_UseIndependentUnitsAndPaymentConventions()
    {
        var projection = await DeclaredProjection(new
        {
            assetClassTermsVersion = 2, issueDate = "2026-01-31", maturityDate = "2027-01-31",
            notional = 100m, paymentFrequency = "Annual",
            legs = new object[]
            {
                new { legId = "receive", legType = "Fixed", direction = "Receive", fixedRate = 0.5m,
                    scheduleConventions = new { couponRateUnit = "Percent", couponCashBasis = "Level" } },
                new { legId = "pay", legType = "Fixed", direction = "Pay", fixedRate = 0.002m,
                    scheduleConventions = new { couponRateUnit = "Fraction", couponCashBasis = "Level" } }
            }
        }, "Swap");
        projection.BlockedReason.Should().Contain("preview-only");
        projection.LegSchedules.Should().HaveCount(2);
        projection.Schedule.Should().ContainSingle();
        projection.Schedule[0].InterestAmount.Should().Be(0.3m);
    }

    [Theory]
    [InlineData("Short", 3, 0.4891)]
    [InlineData("Long", 2, 3.4891)]
    public async Task DeclaredLevelCoupon_StubProratesUnadjustedReferencePeriods(string stub, int count, decimal finalInterest)
    {
        var projection = await DeclaredProjection(new
        {
            assetClassTermsVersion = 2, issueDate = "2026-01-31", maturityDate = "2026-08-15",
            par = 100m, couponRate = 12m, paymentFrequency = "Quarterly",
            scheduleConventions = new
            {
                couponRateUnit = "Percent", couponCashBasis = "Level", stubPolicy = stub,
                businessDayConvention = "Following", paymentCalendars = new[] { "WEEKENDS" }, accrualAdjusted = true
            }
        });
        projection.BlockedReason.Should().Contain("preview-only");
        projection.Schedule.Should().HaveCount(count);
        projection.Schedule[^1].InterestAmount.Should().Be(finalInterest);
        projection.Schedule.Sum(entry => entry.InterestAmount).Should().Be(6.4891m);
        projection.Schedule[^1].PeriodDate.Should().Be(new DateTimeOffset(2026, 8, 17, 0, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData("Short", 2, 2.5109)]
    [InlineData("Long", 1, 5.5109)]
    public async Task DeclaredLevelCoupon_InitialStubKeepsTheBackwardReferenceGrid(string stub, int count, decimal firstInterest)
    {
        var projection = await DeclaredProjection(new
        {
            assetClassTermsVersion = 2, issueDate = "2026-03-15", maturityDate = "2026-08-31",
            par = 100m, couponRate = 12m, paymentFrequency = "Quarterly",
            scheduleConventions = new
            {
                couponRateUnit = "Percent", couponCashBasis = "Level", stubPolicy = stub, direction = "Backward",
                businessDayConvention = "Following", paymentCalendars = new[] { "WEEKENDS" }, accrualAdjusted = true
            }
        }, asOfDate: new DateOnly(2026, 3, 15));
        projection.Schedule.Should().HaveCount(count);
        projection.Schedule[0].InterestAmount.Should().Be(firstInterest);
        projection.Schedule.Sum(entry => entry.InterestAmount).Should().Be(5.5109m);
        projection.Schedule.Last().PrincipalAmount.Should().Be(100m);
    }

    [Theory]
    [InlineData("2026-01-01")]
    [InlineData("2026-04-01")]
    public async Task DeclaredPrincipal_DatedCashStaysOnItsOwnDatesAndCouponStopsAtAccrualEnd(string asOf)
    {
        var projection = await DeclaredProjection(new
        {
            assetClassTermsVersion = 2, issueDate = "2026-01-01", maturityDate = "2026-07-01",
            par = 100m, couponRate = 12m, paymentFrequency = "SemiAnnual", dayCountConvention = "ACT/365",
            principalSchedule = new[]
            {
                new { paymentDate = "2026-03-01", amount = 40m },
                new { paymentDate = "2026-05-01", amount = 20m }
            },
            scheduleConventions = new
            {
                couponRateUnit = "Percent", couponCashBasis = "DayCount", businessDayConvention = "Following",
                paymentCalendars = new[] { "WEEKENDS" }, paymentLagBusinessDays = 2
            }
        }, asOfDate: DateOnly.Parse(asOf));
        projection.BlockedReason.Should().Contain("preview-only");
        projection.Schedule.Last().PeriodDate.Should().Be(new DateTimeOffset(2026, 7, 3, 0, 0, 0, TimeSpan.Zero));
        projection.Schedule.Last().InterestAmount.Should().Be(3.9452m);
        projection.Schedule.Last().PrincipalAmount.Should().Be(40m);
        projection.Schedule.First(entry => entry.PrincipalAmount == 20m).PeriodDate
            .Should().Be(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));
        if (asOf == "2026-01-01")
            projection.Schedule[0].PeriodDate.Should().Be(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));
        projection.Schedule.Sum(entry => entry.PrincipalAmount).Should().Be(asOf == "2026-01-01" ? 100m : 60m);
        projection.Schedule.Last().Factor.Should().Be(0m);
    }

    [Fact]
    public async Task DeclaredPrincipal_PaymentBetweenAccrualEndAndCouponCashDoesNotExtendTheEarlierCoupon()
    {
        var projection = await DeclaredProjection(new
        {
            assetClassTermsVersion = 2, issueDate = "2026-01-01", maturityDate = "2026-07-01",
            par = 100m, couponRate = 12m, paymentFrequency = "Quarterly", dayCountConvention = "ACT/365",
            principalSchedule = new[] { new { paymentDate = "2026-04-03", amount = 40m } },
            scheduleConventions = new
            {
                couponRateUnit = "Percent", couponCashBasis = "DayCount", businessDayConvention = "Following",
                paymentCalendars = new[] { "WEEKENDS" }, paymentLagBusinessDays = 3
            }
        }, asOfDate: new DateOnly(2026, 1, 1));
        projection.Schedule.Should().HaveCount(3);
        projection.Schedule[0].PeriodDate.Should().Be(new DateTimeOffset(2026, 4, 3, 0, 0, 0, TimeSpan.Zero));
        projection.Schedule[0].PrincipalAmount.Should().Be(40m);
        projection.Schedule[1].PeriodDate.Should().Be(new DateTimeOffset(2026, 4, 6, 0, 0, 0, TimeSpan.Zero));
        projection.Schedule[1].InterestAmount.Should().Be(2.9589m);
        projection.Schedule[2].InterestAmount.Should().Be(1.8214m);
        projection.Schedule[2].PrincipalAmount.Should().Be(60m);
    }

    [Fact]
    public async Task DeclaredPrincipal_LevelCouponWeightsBalancesOnTheNominalReferenceGrid()
    {
        var projection = await DeclaredProjection(new
        {
            assetClassTermsVersion = 2, issueDate = "2026-01-31", maturityDate = "2026-07-31",
            par = 100m, couponRate = 12m, paymentFrequency = "Quarterly",
            principalSchedule = new[]
            {
                new { paymentDate = "2026-03-16", amount = 40m },
                new { paymentDate = "2026-05-16", amount = 20m }
            },
            scheduleConventions = new { couponRateUnit = "Percent", couponCashBasis = "Level" }
        });
        projection.Schedule.Where(entry => entry.InterestAmount > 0m).Select(entry => entry.InterestAmount)
            .Should().Equal(2.3933m, 1.3043m);
        projection.Schedule.Sum(entry => entry.PrincipalAmount).Should().Be(100m);
    }

    [Fact]
    public async Task DeclaredPrincipal_WithoutFaceCannotCapRealAmountsToNormalized100()
    {
        var projection = await DeclaredProjection(new
        {
            assetClassTermsVersion = 2, issueDate = "2026-01-31", maturityDate = "2027-01-31",
            couponRate = 6m, paymentFrequency = "Annual",
            principalSchedule = new[] { new { paymentDate = "2026-07-31", amount = 1000000m } },
            scheduleConventions = new { couponRateUnit = "Percent", couponCashBasis = "Level" }
        });
        projection.BlockedReason.Should().Contain("retained principal basis");
        projection.Schedule.Should().BeEmpty();
    }

    [Fact]
    public async Task DeclaredPrincipal_DuplicateRowsAndCouponDateCollisionsAggregateBeforeCashOutput()
    {
        var projection = await DeclaredProjection(new
        {
            assetClassTermsVersion = 2, issueDate = "2026-01-01", maturityDate = "2026-07-01",
            par = 100m, couponRate = 12m, paymentFrequency = "Quarterly", dayCountConvention = "ACT/365",
            principalSchedule = new[]
            {
                new { paymentDate = "2026-04-01", amount = 20m },
                new { paymentDate = "2026-04-01", amount = 30m }
            },
            scheduleConventions = new { couponRateUnit = "Percent", couponCashBasis = "DayCount" }
        }, asOfDate: new DateOnly(2026, 1, 1));
        projection.Schedule.Should().HaveCount(2);
        projection.Schedule[0].PrincipalAmount.Should().Be(50m);
        projection.Schedule[0].InterestAmount.Should().Be(2.9589m);
        projection.Schedule[0].Factor.Should().Be(0.5m);
        projection.Schedule[1].PrincipalAmount.Should().Be(50m);
        projection.Schedule[1].InterestAmount.Should().Be(1.4959m);
    }

    [Theory]
    [InlineData(0.5, false)]
    [InlineData(0.4, true)]
    public async Task DeclaredPrincipal_DatedFactorMustAgreeWithTheCashMovement(decimal factor, bool blocked)
    {
        var projection = await DeclaredProjection(new
        {
            assetClassTermsVersion = 2, issueDate = "2026-01-01", maturityDate = "2026-07-01",
            par = 100m, couponRate = 12m, paymentFrequency = "SemiAnnual", dayCountConvention = "ACT/365",
            factorScheduleEntries = new[]
            {
                new { asOfDate = "2026-01-01", factor = 1m },
                new { asOfDate = "2026-03-01", factor }
            },
            principalSchedule = new[] { new { paymentDate = "2026-03-01", amount = 50m } },
            scheduleConventions = new { couponRateUnit = "Percent", couponCashBasis = "DayCount" }
        }, asOfDate: new DateOnly(2026, 4, 1));
        if (blocked)
        {
            projection.BlockedReason.Should().Contain("factor");
            projection.Schedule.Should().BeEmpty();
        }
        else
        {
            projection.BlockedReason.Should().Contain("preview-only");
            projection.Schedule.Last().PrincipalAmount.Should().Be(50m);
            projection.Schedule.Last().InterestAmount.Should().Be(3.9452m);
        }
    }

    [Fact]
    public async Task DeclaredPrincipal_UnexplainedFutureFactorCannotSilentlyChangeTheBalance()
    {
        var projection = await DeclaredProjection(new
        {
            assetClassTermsVersion = 2, issueDate = "2026-01-01", maturityDate = "2026-07-01",
            par = 100m, couponRate = 12m, paymentFrequency = "SemiAnnual", dayCountConvention = "ACT/365",
            factorScheduleEntries = new[] { new { asOfDate = "2026-05-01", factor = 0.2m } },
            principalSchedule = new[] { new { paymentDate = "2026-03-01", amount = 50m } },
            scheduleConventions = new { couponRateUnit = "Percent", couponCashBasis = "DayCount" }
        }, asOfDate: new DateOnly(2026, 1, 1));
        projection.BlockedReason.Should().Contain("factor changes");
        projection.Schedule.Should().BeEmpty();
    }

    [Fact]
    public async Task DeclaredSwap_DuplicateLegKeysCannotProduceAmbiguousPeriodEvidence()
    {
        var projection = await DeclaredProjection(new
        {
            assetClassTermsVersion = 2, issueDate = "2026-01-31", maturityDate = "2027-01-31",
            notional = 100m, paymentFrequency = "Annual",
            legs = new object[]
            {
                new { legId = "same", legType = "Fixed", direction = "Receive", fixedRate = 0.5m,
                    scheduleConventions = new { couponRateUnit = "Percent", couponCashBasis = "Level" } },
                new { legId = "same", legType = "Fixed", direction = "Pay", fixedRate = 0.002m }
            }
        }, "Swap");
        projection.BlockedReason.Should().Contain("identities must be unique");
        projection.Schedule.Should().BeEmpty();
    }

    private static async Task<StructuredCashFlowProjectionDto> DeclaredProjection(object payload, string assetClass = "Bond", DateOnly? asOfDate = null)
    {
        var securityId = Guid.NewGuid();
        var store = Substitute.For<ISecurityMasterCashFlowStore>();
        store.GetSourceAsync(securityId, Arg.Any<CancellationToken>()).Returns(new SecurityCashFlowSourceDto(
            securityId, StructuredCashFlowSourceKind.CalculatedBullet, DateTimeOffset.UtcNow, false, null, null));
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetByIdAsync(securityId, Arg.Any<CancellationToken>()).Returns(BuildSecurity(securityId,
            JsonSerializer.SerializeToElement(payload)) with { AssetClass = assetClass });
        var service = new SecurityMasterCashFlowService(store, [], query,
            NullLogger<SecurityMasterCashFlowService>.Instance,
            calendars: new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]));
        return (await service.GetProjectionAsOfAsync(securityId, StructuredCashFlowScenario.Base,
            (asOfDate ?? new DateOnly(2026, 1, 31)).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)))!;
    }
}
