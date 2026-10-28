using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.Application;

/// <summary>
/// Characterization pins for the Security Master calculated coupon walk, recorded against base
/// 7d072638 before the schedule kernel (ADR-025) takes over its period roll. The pins go through the
/// public <see cref="Meridian.Application.SecurityMaster.SecurityMasterCashFlowService.GetProjectionAsync"/>
/// seam only, so they hold whichever generator sits behind it.
/// </summary>
/// <remarks>
/// <para>Tests tagged <c>Category=KnownDefect</c> assert TODAY'S defective behavior from the registry
/// in <c>tests/fixtures/schedules/KNOWN-DEFECTS.md</c> and name the TARGET behavior in a comment.
/// When a fix lands the pin fails; the fixing PR flips the assertion to the target and updates the
/// registry in the same change. The trait sits on each method, not on this partial class, so the
/// shared class's other tests stay untagged.</para>
/// <para>The service reads the wall clock for its as-of date and drops periods paid before it, so
/// every pin anchors on <c>T0</c>, the first day of next month (UTC): all periods lie ahead of the
/// as-of, and a first-of-month start keeps 30/360 periods whole.</para>
/// </remarks>
public sealed partial class SecurityMasterCashFlowServiceTests
{
    [Fact]
    [Trait("Category", "KnownDefect")]
    public async Task ScheduleCharacterization_AtMaturityCadence_CurrentBehavior_ProjectsAnnualCoupons_SCH_DEF_001()
    {
        // SCH-DEF-001. TARGET: AtMaturity is one Term period, so the bond projects a single row at
        // T0+36M carrying interest 150.0000 and principal 1000.
        var t0 = ScheduleCharacterizationT0();
        var projection = await ProjectCalculatedBulletAsync(
            Guid.Parse("5c4ed001-0000-4000-8000-000000000001"),
            new
            {
                issueDate = t0,
                maturityDate = t0.AddMonths(36),
                par = 1000m,
                couponRate = 5m,
                paymentFrequency = "AtMaturity",
                dayCountConvention = "30/360"
            });

        projection.Schedule.Select(static row => ScheduleRowDate(row)).Should().Equal(
            new[] { t0.AddMonths(12), t0.AddMonths(24), t0.AddMonths(36) },
            "CURRENT defective behavior: AtMaturity has no months-per-period and falls through to one payment a year");
        projection.Schedule.Select(static row => row.InterestAmount).Should().Equal(50.0000m, 50.0000m, 50.0000m);
        projection.Schedule.Select(static row => row.PrincipalAmount).Should().Equal(0m, 0m, 1000m);
    }

    [Fact]
    [Trait("Category", "KnownDefect")]
    public async Task ScheduleCharacterization_SubOneCoupon_CurrentBehavior_ReadAsFraction_SCH_DEF_003()
    {
        // SCH-DEF-003. TARGET: a declared coupon unit reads 0.5 as half a percent, so each 30/360
        // quarter on par 100 pays 0.1250 (100 x 0.005 x 0.25).
        var t0 = ScheduleCharacterizationT0();
        var projection = await ProjectCalculatedBulletAsync(
            Guid.Parse("5c4ed003-0000-4000-8000-000000000003"),
            new
            {
                issueDate = t0,
                maturityDate = t0.AddMonths(12),
                par = 100m,
                couponRate = 0.5m,
                paymentFrequency = "Quarterly",
                dayCountConvention = "30/360"
            });

        projection.Schedule.Should().HaveCount(4);
        projection.Schedule.Should().OnlyContain(static row => row.InterestAmount == 12.5000m,
            "CURRENT defective behavior: NormalizeAnnualRate reads a coupon at or below 1 as a fraction (50%)");
    }

    [Fact]
    [Trait("Category", "KnownDefect")]
    public async Task ScheduleCharacterization_ActActIcma_CurrentBehavior_DegradesToIsda_SCH_DEF_004()
    {
        // SCH-DEF-004. TARGET: Act/Act ICMA divides by the coupon period, so a regular semiannual
        // period on par 100 at 5% pays exactly 2.5000.
        var issue = IcmaCharacterizationAnchor();
        var projection = await ProjectCalculatedBulletAsync(
            Guid.Parse("5c4ed004-0000-4000-8000-000000000004"),
            new
            {
                issueDate = issue,
                maturityDate = issue.AddMonths(12),
                par = 100m,
                couponRate = 5m,
                paymentFrequency = "SemiAnnual",
                dayCountConvention = "ACT/ACT ICMA"
            });

        var isdaRowOne = RoundCashLikeService(
            100m * 0.05m * DayCountConventions.Fraction(DayCountConvention.ActualActualIsda, issue, issue.AddMonths(6)));
        projection.Schedule.Should().HaveCount(2);
        projection.Schedule[0].InterestAmount.Should().Be(isdaRowOne,
            "CURRENT defective behavior: the period-less Fraction call degrades Act/Act ICMA to Act/Act ISDA");
        projection.Schedule[0].InterestAmount.Should().NotBe(2.5000m);
    }

    [Fact]
    [Trait("Category", "KnownDefect")]
    public async Task ScheduleCharacterization_Business252_CurrentBehavior_DegradesToActual365_SCH_DEF_004()
    {
        // SCH-DEF-004. TARGET: Business/252 counts business days on a declared calendar over 252.
        var t0 = ScheduleCharacterizationT0();
        var projection = await ProjectCalculatedBulletAsync(
            Guid.Parse("5c4ed004-0000-4000-8000-000000000252"),
            new
            {
                issueDate = t0,
                maturityDate = t0.AddMonths(12),
                par = 100m,
                couponRate = 5m,
                paymentFrequency = "SemiAnnual",
                dayCountConvention = "BUS/252"
            });

        var actualDays = t0.AddMonths(6).DayNumber - t0.DayNumber;
        projection.Schedule.Should().HaveCount(2);
        projection.Schedule[0].InterestAmount.Should().Be(RoundCashLikeService(5m * actualDays / 365m),
            "CURRENT defective behavior: without a calendar Business/252 degrades to actual days over 365");
    }

    [Fact]
    public async Task ScheduleCharacterization_OffGridIssueAndMaturity_AnchorsOnIssueDayAndClosesAtMaturity()
    {
        // Not a defect: the issue-anchored roll keeps every earlier row on the issue day of month,
        // and the final row is dated at maturity because the next grid date would overshoot it.
        var t0 = ScheduleCharacterizationT0();
        var issue = t0.AddDays(14);
        var maturity = t0.AddMonths(12).AddDays(-10);
        var projection = await ProjectCalculatedBulletAsync(
            Guid.Parse("5c4ed0ff-0000-4000-8000-0000000000ff"),
            new
            {
                issueDate = issue,
                maturityDate = maturity,
                par = 100m,
                couponRate = 4m,
                paymentFrequency = "Quarterly",
                dayCountConvention = "30/360"
            });

        projection.Schedule.Select(static row => ScheduleRowDate(row)).Should().Equal(
            issue.AddMonths(3), issue.AddMonths(6), issue.AddMonths(9), maturity);
        projection.Schedule.Select(static row => row.PrincipalAmount).Should().Equal(0m, 0m, 0m, 100m);
    }

    [Fact]
    [Trait("Category", "KnownDefect")]
    public async Task ScheduleCharacterization_MaturityPastTheLastRepresentableGridDate_CurrentBehavior_Throws_SCH_DEF_010()
    {
        // SCH-DEF-010. TARGET: a far-dated or perpetual-sentinel maturity (9999-12-31) projects a
        // bounded schedule or is blocked with a reason; the projection never throws.
        var t0 = ScheduleCharacterizationT0();
        var securityId = Guid.Parse("5c4ed010-0000-4000-8000-000000000010");
        var service = BuildService(
            StoreWith(securityId, StructuredCashFlowSourceKind.CalculatedBullet),
            QueryWith(securityId, JsonSerializer.SerializeToElement(new
            {
                issueDate = t0,
                maturityDate = new DateOnly(9999, 12, 31),
                par = 100m,
                couponRate = 4m,
                paymentFrequency = "Quarterly",
                dayCountConvention = "30/360"
            })));

        // T0 is the first of a month, so the quarterly grid never lands on 9999-12-31 and the grid
        // date that would close the schedule is 10000-01-01, beyond DateOnly's range.
        await service.Invoking(s => s.GetProjectionAsync(securityId, StructuredCashFlowScenario.Base))
            .Should().ThrowAsync<ArgumentOutOfRangeException>(
                "CURRENT defective behavior: the issue-anchored roll evaluates the closing grid date, which is out of range");
    }

    private static async Task<StructuredCashFlowProjectionDto> ProjectCalculatedBulletAsync(Guid securityId, object terms)
    {
        var service = BuildService(
            StoreWith(securityId, StructuredCashFlowSourceKind.CalculatedBullet),
            QueryWith(securityId, JsonSerializer.SerializeToElement(terms)));

        var projection = await service.GetProjectionAsync(securityId, StructuredCashFlowScenario.Base);

        projection.Should().NotBeNull();
        projection!.BlockedReason.Should().BeNull();
        return projection;
    }

    /// <summary>T0: the first day of next month (UTC), so every period lies after the service's as-of.</summary>
    private static DateOnly ScheduleCharacterizationT0()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        return new DateOnly(today.Year, today.Month, 1).AddMonths(1);
    }

    /// <summary>
    /// T0, moved on a month at a time while Act/Act ISDA over [T, T+6M] is exactly one half. That
    /// happens only for a 183-day window wholly inside a leap year (for example 2028-04-01 and
    /// 2028-06-01), where the degraded ISDA row would coincide with the ICMA target.
    /// </summary>
    private static DateOnly IcmaCharacterizationAnchor()
    {
        var anchor = ScheduleCharacterizationT0();
        while (DayCountConventions.Fraction(DayCountConvention.ActualActualIsda, anchor, anchor.AddMonths(6)) == 0.5m)
        {
            anchor = anchor.AddMonths(1);
        }

        return anchor;
    }

    private static DateOnly ScheduleRowDate(StructuredCashFlowScheduleEntry row)
        => DateOnly.FromDateTime(row.PeriodDate.UtcDateTime.Date);

    private static decimal RoundCashLikeService(decimal amount)
        => decimal.Round(amount, 4, MidpointRounding.AwayFromZero);
}
