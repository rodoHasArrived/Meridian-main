using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;
using Meridian.Tests.SecurityMaster.Schedules;

namespace Meridian.Tests.Application;

public sealed class DirectLendingSchedulePeriodsTests
{
    [Fact]
    public void PostgresCadences_KeepLegacyPeriodsAcrossMonthEndsLeapYearsAndStubs()
    {
        foreach (var year in new[] { 2024, 2025, 2026 })
            foreach (var month in Enumerable.Range(1, 12))
                foreach (var day in new[] { 1, 15, DateTime.DaysInMonth(year, month) })
                    foreach (var frequency in Enum.GetValues<PaymentFrequency>())
                        foreach (var termDays in new[] { 1, 30, 365, 731 })
                        {
                            var start = new DateOnly(year, month, day);
                            var maturity = start.AddDays(termDays);
                            var months = frequency switch
                            {
                                PaymentFrequency.Monthly => 1,
                                PaymentFrequency.Quarterly => 3,
                                PaymentFrequency.SemiAnnual => 6,
                                _ => 12
                            };
                            DirectLendingSchedulePeriods.LegacyCompounded(start, maturity, months)
                                .Should().Equal(LegacyScheduleOracles.LoanPostgresAsOf(start, maturity, frequency));
                        }
    }

    [Fact]
    public void InMemoryMonthlyCutoffs_KeepWholeCrossingPeriodsAndLegacyMonthEndDrift()
    {
        foreach (var year in new[] { 2024, 2025, 2026 })
            foreach (var month in Enumerable.Range(1, 12))
                foreach (var day in new[] { 1, 15, DateTime.DaysInMonth(year, month) })
                    foreach (var offset in new[] { -1, 0, 1, 30, 31, 365, 900 })
                    {
                        var start = new DateOnly(year, month, day);
                        var maturity = start.AddDays(731);
                        var cutoff = start.AddDays(offset);
                        DirectLendingSchedulePeriods.LegacyCompounded(start, maturity, 1, cutoff)
                            .Should().Equal(LegacyScheduleOracles.LoanInMemoryMonthly(start, maturity, cutoff));
                    }
    }

    [Fact]
    public void LegacyClosingStepOutsideDateRange_StillThrowsOnlyWhenReached()
    {
        var start = new DateOnly(9999, 1, 31);
        var end = DateOnly.MaxValue;
        var periods = () => DirectLendingSchedulePeriods.LegacyCompounded(start, end, 1).ToArray();
        periods.Should().Throw<ArgumentOutOfRangeException>();
        DirectLendingSchedulePeriods.LegacyCompounded(start, end, 1, new DateOnly(9999, 2, 1))
            .Should().Equal([(start, new DateOnly(9999, 2, 28))]);
    }
}
