using FluentAssertions;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Tests.SecurityMaster.Schedules;

/// <summary>
/// Pins schedule period identity: keyed on unadjusted dates within a scope and stream, stable across
/// calendar and convention changes, and shaped as a name-based (version 5 style) GUID.
/// </summary>
public sealed class SchedulePeriodIdTests
{
    private static readonly Guid Scope = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    private static ScheduleSpec Quarterly2026()
        => ScheduleSpec.IssueAnchoredForward(D(2026, 1, 31), D(2027, 1, 31), ScheduleTenor.Months(3));

    [Fact]
    public void AdjustmentChanges_NeverReidentifyAPeriod()
    {
        var unadjusted = ScheduleGenerator.Generate(Quarterly2026());
        var modifiedFollowing = BusinessDayAdjustment.Of(BusinessDayConvention.ModifiedFollowing, BusinessDayCalendars.WeekendsOnly);
        var adjusted = ScheduleGenerator.Generate(Quarterly2026() with
        {
            AccrualAdjustment = modifiedFollowing,
            PaymentAdjustment = modifiedFollowing,
            PaymentLagBusinessDays = 2,
        });

        adjusted.Periods[2].AccrualEnd.Should().NotBe(unadjusted.Periods[2].AccrualEnd, "the adjustment must actually move a date");
        adjusted.Periods.Select(period => SchedulePeriodId.Derive(Scope, "coupon", period))
            .Should().Equal(unadjusted.Periods.Select(period => SchedulePeriodId.Derive(Scope, "coupon", period)));
    }

    [Fact]
    public void Id_DiffersByStreamAndScope()
    {
        var period = ScheduleGenerator.Generate(Quarterly2026()).Periods[0];
        var id = SchedulePeriodId.Derive(Scope, "coupon", period);

        SchedulePeriodId.Derive(Scope, "principal", period).Should().NotBe(id);
        SchedulePeriodId.Derive(Guid.Parse("11111111-2222-3333-4444-555555555556"), "coupon", period).Should().NotBe(id);
        SchedulePeriodId.Derive(Scope, "coupon", period.UnadjustedStart, period.UnadjustedEnd).Should().Be(id);
        SchedulePeriodId.Derive(Scope, "coupon", period.UnadjustedStart, period.UnadjustedEnd.AddDays(1)).Should().NotBe(id);
    }

    [Fact]
    public void Id_IsVersionFiveStyleAndPinned()
    {
        var id = SchedulePeriodId.Derive(Scope, "coupon", D(2026, 1, 31), D(2026, 4, 30));
        var text = id.ToString("D");

        text[14].Should().Be('5');
        "89ab".Should().Contain(text[19].ToString());
        // Independently computed from the documented derivation:
        // SHA-256("780e78eb-4b9c-4b04-8b13-233f6a94a559|meridian/schedule-period/v1|<scope>|coupon|2026-01-31|2026-04-30").
        id.Should().Be(Guid.Parse("56bd45c2-d01b-51de-b674-36c9f37a3f65"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void BlankStreamKey_Throws(string? streamKey)
    {
        var act = () => SchedulePeriodId.Derive(Scope, streamKey!, D(2026, 1, 31), D(2026, 4, 30));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void PeriodKey_IsTheUnadjustedDates()
    {
        var period = ScheduleGenerator.Generate(Quarterly2026()).Periods[0];

        SchedulePeriodId.PeriodKey(period).Should().Be("2026-01-31/2026-04-30");
    }
}
