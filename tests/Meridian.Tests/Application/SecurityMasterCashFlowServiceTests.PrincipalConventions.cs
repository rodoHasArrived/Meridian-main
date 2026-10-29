using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.SecurityMaster;
using NSubstitute;
using SecurityMasterQueryContract = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Tests.Application;

public sealed partial class SecurityMasterCashFlowServiceTests
{
    [Theory]
    [InlineData("2026-01-01", 100)]
    [InlineData("2026-04-01", 60)]
    public async Task PrincipalConventions_DatedCashAndMidPeriodAsOfKeepTheFullCoupon(string asOf, decimal totalPrincipal)
    {
        var projection = await ProjectPrincipalConventionsAsync(new
        {
            assetClassTermsVersion = 2,
            issueDate = "2026-01-01",
            maturity = "2026-07-01",
            par = 100m,
            couponRate = 12m,
            paymentFrequency = "SemiAnnual",
            dayCount = "ACT/365",
            principalSchedule = new[]
            {
                new { paymentDate = "2026-03-01", amount = 40m },
                new { paymentDate = "2026-05-01", amount = 20m }
            },
            scheduleConventions = new
            {
                couponRateUnit = "Percent",
                couponCashBasis = "DayCount",
                accrualAdjusted = false,
                paymentCalendars = new[] { "WEEKENDS" },
                paymentLagBusinessDays = 2
            }
        }, DateOnly.Parse(asOf));
        projection.BlockedReason.Should().BeNull();
        projection.ScheduleEvidence.Should().NotBeNull();
        projection.Schedule.Last().PeriodDate.Should().Be(Utc(2026, 7, 3));
        projection.Schedule.Last().InterestAmount.Should().Be(3.9452m);
        projection.Schedule.Last().PrincipalAmount.Should().Be(40m);
        projection.Schedule.First(row => row.PrincipalAmount == 20m).PeriodDate.Should().Be(Utc(2026, 5, 1));
        if (asOf == "2026-01-01")
        {
            projection.Schedule[0].PeriodDate.Should().Be(Utc(2026, 3, 1), "a dated principal payment is not rolled off its contractual Sunday");
        }
        projection.Schedule.Sum(row => row.PrincipalAmount).Should().Be(totalPrincipal);
        projection.Schedule.Last().Factor.Should().Be(0m);
    }

    [Fact]
    public async Task PrincipalConventions_PrincipalDuringPaymentLagDoesNotExtendThePriorCoupon()
    {
        var projection = await ProjectPrincipalConventionsAsync(new
        {
            assetClassTermsVersion = 2,
            issueDate = "2026-01-01",
            maturity = "2026-07-01",
            par = 100m,
            couponRate = 12m,
            paymentFrequency = "Quarterly",
            dayCount = "ACT/365",
            principalSchedule = new[] { new { paymentDate = "2026-04-03", amount = 40m } },
            scheduleConventions = new
            {
                couponRateUnit = "Percent",
                couponCashBasis = "DayCount",
                accrualAdjusted = false,
                paymentCalendars = new[] { "WEEKENDS" },
                paymentLagBusinessDays = 3
            }
        }, new DateOnly(2026, 1, 1));
        projection.Schedule.Should().HaveCount(3);
        projection.Schedule[0].PeriodDate.Should().Be(Utc(2026, 4, 3));
        projection.Schedule[0].PrincipalAmount.Should().Be(40m);
        projection.Schedule[1].PeriodDate.Should().Be(Utc(2026, 4, 6));
        projection.Schedule[1].InterestAmount.Should().Be(2.9589m);
        projection.Schedule[2].InterestAmount.Should().Be(1.8214m);
        projection.Schedule[2].PrincipalAmount.Should().Be(60m);
    }

    [Fact]
    public async Task PrincipalConventions_PaymentBeforeAdjustedEffectiveDateIsStillFutureCash()
    {
        var projection = await ProjectPrincipalConventionsAsync(new
        {
            assetClassTermsVersion = 2,
            issueDate = "2026-01-31",
            maturity = "2026-07-31",
            par = 100m,
            couponRate = 12m,
            paymentFrequency = "Quarterly",
            dayCount = "ACT/365",
            principalSchedule = new[] { new { paymentDate = "2026-02-01", amount = 40m } },
            scheduleConventions = new
            {
                couponRateUnit = "Percent",
                couponCashBasis = "DayCount",
                accrualAdjusted = true,
                businessDayConvention = "Following",
                paymentCalendars = new[] { "WEEKENDS" }
            }
        }, new DateOnly(2026, 1, 31));
        projection.BlockedReason.Should().BeNull();
        projection.Schedule[0].PeriodDate.Should().Be(Utc(2026, 2, 1));
        projection.Schedule[0].PrincipalAmount.Should().Be(40m);
        projection.Schedule.Where(row => row.InterestAmount > 0m).Select(row => row.InterestAmount)
            .Should().Equal(1.7162m, 1.8148m);
        projection.Schedule.Sum(row => row.PrincipalAmount).Should().Be(100m);
    }

    [Theory]
    [InlineData("Short", 4.2134)]
    [InlineData("Long", 4.2093)]
    public async Task PrincipalConventions_LevelRegularWeightsAndStubDayCountKeepTheirDistinctRules(string stub, decimal totalInterest)
    {
        var projection = await ProjectPrincipalConventionsAsync(new
        {
            assetClassTermsVersion = 2,
            issueDate = "2026-01-01",
            maturity = "2026-08-15",
            par = 100m,
            couponRate = 12m,
            paymentFrequency = "Quarterly",
            dayCount = "ACT/365",
            principalSchedule = new[]
            {
                new { paymentDate = "2026-03-01", amount = 50m },
                new { paymentDate = "2026-07-15", amount = 50m }
            },
            scheduleConventions = new
            {
                couponRateUnit = "Percent",
                couponCashBasis = "Level",
                accrualAdjusted = false,
                stubPolicy = stub
            }
        }, new DateOnly(2026, 1, 1));
        projection.BlockedReason.Should().BeNull();
        projection.Schedule.First(row => row.InterestAmount > 0m).InterestAmount.Should().Be(2.4833m);
        projection.Schedule.Sum(row => row.InterestAmount).Should().Be(totalInterest);
        projection.Schedule.Sum(row => row.PrincipalAmount).Should().Be(100m);
        projection.Schedule.Last().PrincipalAmount.Should().Be(0m);
    }

    [Fact]
    public async Task PrincipalConventions_DuplicatePrincipalRowsAndCouponCollisionsAggregate()
    {
        var projection = await ProjectPrincipalConventionsAsync(new
        {
            assetClassTermsVersion = 2,
            issueDate = "2026-01-01",
            maturity = "2026-07-01",
            par = 100m,
            couponRate = 12m,
            paymentFrequency = "Quarterly",
            dayCount = "ACT/365",
            principalSchedule = new[]
            {
                new { paymentDate = "2026-04-01", amount = 20m },
                new { paymentDate = "2026-04-01", amount = 30m }
            },
            scheduleConventions = new { couponRateUnit = "Percent", couponCashBasis = "DayCount", accrualAdjusted = false }
        }, new DateOnly(2026, 1, 1));
        projection.Schedule.Should().HaveCount(2);
        projection.Schedule.Select(row => row.PrincipalAmount).Should().Equal(50m, 50m);
        projection.Schedule.Select(row => row.InterestAmount).Should().Equal(2.9589m, 1.4959m);
        projection.Schedule.Select(row => row.Factor).Should().Equal(0.5m, 0m);
    }

    [Theory]
    [InlineData(0.5, false)]
    [InlineData(0.4, true)]
    public async Task PrincipalConventions_DatedFactorEvidenceMustAgreeWithTheCashContract(decimal factor, bool blocked)
    {
        var projection = await ProjectPrincipalConventionsAsync(new
        {
            assetClassTermsVersion = 2,
            issueDate = "2026-01-01",
            maturity = "2026-07-01",
            par = 100m,
            couponRate = 12m,
            paymentFrequency = "SemiAnnual",
            dayCount = "ACT/365",
            factorScheduleEntries = new[]
            {
                new { asOfDate = "2026-01-01", factor = 1m },
                new { asOfDate = "2026-03-01", factor }
            },
            principalSchedule = new[] { new { paymentDate = "2026-03-01", amount = 50m } },
            scheduleConventions = new { couponRateUnit = "Percent", couponCashBasis = "DayCount", accrualAdjusted = false }
        }, new DateOnly(2026, 4, 1));
        if (blocked)
        {
            projection.BlockedReason.Should().Contain("factor");
            projection.Schedule.Should().BeEmpty();
            projection.ScheduleEvidence.Should().BeNull();
        }
        else
        {
            projection.BlockedReason.Should().BeNull();
            projection.Schedule.Last().PrincipalAmount.Should().Be(50m);
            projection.Schedule.Last().InterestAmount.Should().Be(3.9452m);
        }
    }

    [Fact]
    public async Task PrincipalConventions_ZeroCouponDoesNotRequireDayCount()
    {
        var projection = await ProjectPrincipalConventionsAsync(new
        {
            assetClassTermsVersion = 2,
            issueDate = "2026-01-01",
            maturity = "2026-07-01",
            par = 100m,
            couponRate = 0m,
            paymentFrequency = "SemiAnnual",
            principalSchedule = new[] { new { paymentDate = "2026-03-01", amount = 50m } },
            scheduleConventions = new { accrualAdjusted = false }
        }, new DateOnly(2026, 1, 1));
        projection.BlockedReason.Should().BeNull();
        projection.ScheduleEvidence.Should().NotBeNull();
        projection.Schedule.Sum(row => row.PrincipalAmount).Should().Be(100m);
        projection.Schedule.Should().OnlyContain(row => row.InterestAmount == 0m);
    }

    [Fact]
    public async Task PrincipalConventions_Business252RetainsCalendarAndSegmentCoverageEvidence()
    {
        var partial = new HolidayBusinessDayCalendar("PARTIAL", 1,
            coverage: new CalendarCoverage(new DateOnly(2026, 4, 1), new DateOnly(2026, 12, 31)));
        var projection = await ProjectPrincipalConventionsAsync(new
        {
            assetClassTermsVersion = 2,
            issueDate = "2026-01-01",
            maturity = "2026-07-01",
            par = 100m,
            couponRate = 12m,
            paymentFrequency = "SemiAnnual",
            dayCount = "BUS/252",
            principalSchedule = new[] { new { paymentDate = "2026-03-01", amount = 50m } },
            scheduleConventions = new { couponRateUnit = "Percent", accrualAdjusted = false, paymentCalendars = new[] { "PARTIAL" } }
        }, new DateOnly(2026, 1, 1), new StaticBusinessDayCalendarSource([partial]));
        projection.BlockedReason.Should().BeNull();
        projection.Schedule.Last().InterestAmount.Should().Be(4.0714m);
        projection.ScheduleEvidence!.Calendars.Should().ContainSingle()
            .Which.Purpose.Should().Be("DayCount");
        projection.ScheduleEvidence.CoverageGaps.Should().ContainSingle()
            .Which.Should().Be(new StructuredCashFlowCoverageGap("PARTIAL", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31)));
    }

    [Fact]
    public async Task PrincipalConventions_RealPrincipalCannotBeCappedToNormalized100()
    {
        var projection = await ProjectPrincipalConventionsAsync(new
        {
            assetClassTermsVersion = 2,
            issueDate = "2026-01-01",
            maturity = "2026-07-01",
            couponRate = 5m,
            paymentFrequency = "SemiAnnual",
            dayCount = "ACT/365",
            principalSchedule = new[] { new { paymentDate = "2026-03-01", amount = 1000000m } },
            scheduleConventions = new { couponRateUnit = "Percent", accrualAdjusted = false }
        }, new DateOnly(2026, 1, 1));
        projection.BlockedReason.Should().Contain("retained principal basis");
        projection.Schedule.Should().BeEmpty();
        projection.ScheduleEvidence.Should().BeNull();
    }

    private static async Task<StructuredCashFlowProjectionDto> ProjectPrincipalConventionsAsync(
        object payload, DateOnly asOf, IBusinessDayCalendarSource? calendars = null)
    {
        var id = Guid.NewGuid();
        var query = Substitute.For<SecurityMasterQueryContract>();
        query.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(BuildSecurity(id, JsonSerializer.SerializeToElement(payload)));
        var service = BuildService(StoreWith(id, StructuredCashFlowSourceKind.CalculatedBullet), query, calendars ?? WeekendsSource);
        return (await service.GetProjectionAsOfAsync(id, StructuredCashFlowScenario.Base,
            new DateTimeOffset(asOf.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc))))!;
    }

    private static DateTimeOffset Utc(int year, int month, int day) => new(year, month, day, 0, 0, 0, TimeSpan.Zero);
}
