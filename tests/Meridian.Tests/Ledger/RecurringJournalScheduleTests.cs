using FluentAssertions;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

/// <summary>
/// Tests recurring journal scheduling: cadence-driven occurrence dates, instantiation of the
/// underlying template per occurrence, horizon/end-date bounds, and locked-period awareness.
/// </summary>
public sealed class RecurringJournalScheduleTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 01, 01, 0, 0, 0, TimeSpan.Zero);

    private static JournalTemplate FeeTemplate() => new(
        "fee",
        "Fee Accrual",
        "Accrue a fixed fee.",
        [
            new JournalTemplateLine(LedgerAccounts.ManagementFeeExpenseFor("FUND-A"), JournalTemplateSide.Debit, "fee"),
            new JournalTemplateLine(LedgerAccounts.ManagementFeePayableFor("FUND-A"), JournalTemplateSide.Credit, "fee"),
        ]);

    private static RecurringJournalSchedule MonthlySchedule() => new(
        "sched-1",
        "fee",
        new LedgerBookKey("FUND-A", "Fund"),
        RecurringJournalCadence.Monthly,
        new DateOnly(2026, 1, 15),
        "controller",
        CreatedAt,
        parameters: new Dictionary<string, decimal> { ["fee"] = 1_000m });

    [Fact]
    public void Plan_Monthly_AdvancesEffectiveDatesAndInstantiatesBalancedJournals()
    {
        var occurrences = RecurringJournalPlanner.Plan(MonthlySchedule(), FeeTemplate(), 3);

        occurrences.Should().HaveCount(3);
        occurrences[0].EffectiveDate.Should().Be(new DateOnly(2026, 1, 15));
        occurrences[1].EffectiveDate.Should().Be(new DateOnly(2026, 2, 15));
        occurrences[2].EffectiveDate.Should().Be(new DateOnly(2026, 3, 15));
        occurrences.Should().OnlyContain(occurrence => occurrence.Journal.IsBalanced);
        occurrences[0].Journal.TotalDebits.Should().Be(1_000m);
    }

    [Fact]
    public void PlanThrough_StopsAtHorizonAndEndDate()
    {
        var bounded = new RecurringJournalSchedule(
            "sched-2",
            "fee",
            new LedgerBookKey("FUND-A", "Fund"),
            RecurringJournalCadence.Monthly,
            new DateOnly(2026, 1, 15),
            "controller",
            CreatedAt,
            parameters: new Dictionary<string, decimal> { ["fee"] = 1_000m },
            endsOn: new DateOnly(2026, 2, 20));

        var throughApril = RecurringJournalPlanner.PlanThrough(bounded, FeeTemplate(), new DateOnly(2026, 4, 30));

        // The end date (Feb 20) caps the schedule before the horizon (Apr 30).
        throughApril.Should().HaveCount(2);
        throughApril.Last().EffectiveDate.Should().Be(new DateOnly(2026, 2, 15));
    }

    [Fact]
    public void ApplyLocks_MarksOccurrencesInsideLockedPeriods()
    {
        var schedule = MonthlySchedule();
        var occurrences = RecurringJournalPlanner.Plan(schedule, FeeTemplate(), 3);

        var lockedBook = new LockedAccountingPeriodBook();
        lockedBook.LockPeriod(
            new LedgerBookKey("FUND-A", "Fund"),
            "2026-02",
            new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 2, 28, 23, 59, 59, TimeSpan.Zero),
            CreatedAt,
            "controller",
            "Month-end lock");

        var annotated = RecurringJournalPlanner.ApplyLocks(occurrences, lockedBook);

        annotated[0].BlockedByLock.Should().BeFalse();
        annotated[1].BlockedByLock.Should().BeTrue("the February occurrence falls inside the locked period");
        annotated[1].BlockingPeriodId.Should().Be("2026-02");
        annotated[2].BlockedByLock.Should().BeFalse();
    }
}
