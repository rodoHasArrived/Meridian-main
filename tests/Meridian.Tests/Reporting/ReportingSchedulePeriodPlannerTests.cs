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
