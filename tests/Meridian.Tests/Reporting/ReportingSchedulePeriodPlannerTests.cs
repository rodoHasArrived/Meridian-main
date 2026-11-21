using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Reporting;

namespace Meridian.Tests.Reporting;

public sealed class ReportingSchedulePeriodPlannerTests
{
    [Fact]
    public void Next_MonthlyPeriodEndingOnLeapDay_UsesTheLedgerPeriodBoundaries()
    {
        var next = ReportingSchedulePeriodPlanner.Next(
            new ReportingSchedulePeriodCadenceDto(ReportingSchedulePeriodFrequencyDto.Monthly, 5),
            currentPeriodEnd: new DateOnly(2024, 1, 31),
            currentDueAtUtc: new DateTimeOffset(2024, 2, 5, 8, 0, 0, TimeSpan.Zero),
            TimeZoneInfo.Utc);

        var ledgerPeriod = LedgerReportSchedulePlanner.PeriodStartingOn(
            LedgerReportScheduleFrequency.Monthly,
            new DateOnly(2024, 2, 1));
        next.PeriodId.Should().Be(ledgerPeriod.PeriodId).And.Be("2024-02");
        next.PeriodEnd.Should().Be(ledgerPeriod.PeriodEnd).And.Be(new DateOnly(2024, 2, 29));
        next.DueAtUtc.Should().Be(new DateTimeOffset(2024, 3, 5, 8, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Next_KeepsTheLocalDueTimeAcrossADaylightSavingChange()
    {
        var newYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

        // Due 08:00 New York on 5 March (EST, 13:00 UTC); the next due date, 5 April, is in
        // daylight time, so 08:00 local is 12:00 UTC.
        var next = ReportingSchedulePeriodPlanner.Next(
            new ReportingSchedulePeriodCadenceDto(ReportingSchedulePeriodFrequencyDto.Monthly, 5),
            currentPeriodEnd: new DateOnly(2026, 2, 28),
            currentDueAtUtc: new DateTimeOffset(2026, 3, 5, 13, 0, 0, TimeSpan.Zero),
            newYork);

        next.PeriodEnd.Should().Be(new DateOnly(2026, 3, 31));
        next.DueAtUtc.Should().Be(new DateTimeOffset(2026, 4, 5, 12, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData(ReportingSchedulePeriodFrequencyDto.Quarterly, "2026-Q2", 2026, 6, 30)]
    [InlineData(ReportingSchedulePeriodFrequencyDto.Annually, "2026", 2027, 3, 31)]
    public void Next_QuarterlyAndAnnualPeriods_FollowTheCurrentPeriodEnd(
        ReportingSchedulePeriodFrequencyDto frequency, string periodId, int year, int month, int day)
    {
        var next = ReportingSchedulePeriodPlanner.Next(
            new ReportingSchedulePeriodCadenceDto(frequency),
            currentPeriodEnd: new DateOnly(2026, 3, 31),
            currentDueAtUtc: new DateTimeOffset(2026, 3, 31, 20, 0, 0, TimeSpan.Zero),
            TimeZoneInfo.Utc);

        next.PeriodId.Should().Be(periodId);
        next.PeriodEnd.Should().Be(new DateOnly(year, month, day));
        next.DueAtUtc.Should().Be(new DateTimeOffset(year, month, day, 20, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Next_DueTimeInASpringForwardGap_MovesOnlyThatRunWhenTheIntendedTimeIsKnown()
    {
        var newYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var cadence = new ReportingSchedulePeriodCadenceDto(ReportingSchedulePeriodFrequencyDto.Monthly, 8);
        var intended = new TimeOnly(2, 30);

        // Due 02:30 on 8 March 2026 does not exist in New York; that run moves to 03:00 EDT.
        var march = ReportingSchedulePeriodPlanner.Next(
            cadence,
            currentPeriodEnd: new DateOnly(2026, 1, 31),
            currentDueAtUtc: new DateTimeOffset(2026, 2, 8, 7, 30, 0, TimeSpan.Zero),
            newYork,
            intended);
        march.DueAtUtc.Should().Be(new DateTimeOffset(2026, 3, 8, 7, 0, 0, TimeSpan.Zero));

        // The following run returns to 02:30 local (06:30Z in daylight time), not 03:00.
        var april = ReportingSchedulePeriodPlanner.Next(cadence, march.PeriodEnd, march.DueAtUtc, newYork, intended);
        april.DueAtUtc.Should().Be(new DateTimeOffset(2026, 4, 8, 6, 30, 0, TimeSpan.Zero));

        // Without the intended time the shifted 03:00 would stick.
        ReportingSchedulePeriodPlanner.Next(cadence, march.PeriodEnd, march.DueAtUtc, newYork)
            .DueAtUtc.Should().Be(new DateTimeOffset(2026, 4, 8, 7, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    // The browser's zonedWallTimeToUtc (lib/time-zone.ts) is tested against these same instants,
    // so a due time saved from the form and one advanced by the worker agree.
    // Skipped hour, 8 March 2026: 02:30 does not exist and moves to 03:00 EDT.
    [InlineData(2026, 3, 8, 2, 30, "2026-03-08T07:00:00Z")]
    // Repeated hour, 1 November 2026: 01:30 resolves to its second, standard-time occurrence.
    [InlineData(2026, 11, 1, 1, 30, "2026-11-01T06:30:00Z")]
    [InlineData(2026, 7, 1, 8, 0, "2026-07-01T12:00:00Z")]
    public void DueAtUtc_ResolvesSkippedAndRepeatedWallTimesTheSameWayAsTheBrowser(
        int year, int month, int day, int hour, int minute, string expectedUtc)
    {
        var newYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

        var due = ReportingSchedulePeriodPlanner.DueAtUtc(
            new DateOnly(year, month, day), 0, newYork, new TimeOnly(hour, minute));

        due.Should().Be(DateTimeOffset.Parse(expectedUtc, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(367)]
    public void IsValid_RejectsDueOffsetsOutsideTheSupportedRange(int dueDays)
    {
        ReportingSchedulePeriodPlanner.IsValid(
                new ReportingSchedulePeriodCadenceDto(ReportingSchedulePeriodFrequencyDto.Monthly, dueDays))
            .Should().BeFalse();
    }
}
