using FluentAssertions;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

/// <summary>
/// Tests fiscal-year-end close: annual closing entries, the readiness gate over constituent periods,
/// and the retained-earnings roll-forward into next year's opening balance.
/// </summary>
public sealed class YearEndCloseTests
{
    private static readonly DateTimeOffset YearEnd = new(2026, 12, 31, 23, 59, 59, TimeSpan.Zero);

    private static IReadOnlyDictionary<LedgerAccount, decimal> TrialBalance() => new Dictionary<LedgerAccount, decimal>
    {
        [LedgerAccounts.RealizedGain] = 1_000m,        // revenue
        [LedgerAccounts.DividendIncome] = 200m,        // revenue
        [LedgerAccounts.CommissionExpense] = 500m,     // expense
        [LedgerAccounts.RetainedEarnings] = 5_000m,    // prior-year retained earnings
    };

    [Fact]
    public void Project_ProducesBalancedClosingEntriesAndRollsRetainedEarnings()
    {
        var input = new YearEndCloseInput(
            "FY2026",
            YearEnd,
            TrialBalance(),
            "controller",
            requiredPeriodIds: ["Q1", "Q2", "Q3", "Q4"],
            closedPeriodIds: ["Q1", "Q2", "Q3", "Q4"]);

        var projection = YearEndCloseProjector.Project(input);

        projection.IsReady.Should().BeTrue();
        projection.MissingPeriods.Should().BeEmpty();
        projection.ClosingEntries.IsBalanced.Should().BeTrue();
        projection.NetIncome.Should().Be(700m, "1000 + 200 revenue less 500 expense");
        // Opening retained earnings for next year = prior 5000 + net income 700.
        projection.OpeningRetainedEarningsByScope[PeriodCloseProjection.DefaultScope].Should().Be(5_700m);
        projection.TotalOpeningRetainedEarnings.Should().Be(5_700m);
    }

    [Fact]
    public void Project_MissingConstituentPeriods_IsNotReady()
    {
        var input = new YearEndCloseInput(
            "FY2026",
            YearEnd,
            TrialBalance(),
            "controller",
            requiredPeriodIds: ["Q1", "Q2", "Q3", "Q4"],
            closedPeriodIds: ["Q1", "Q2", "Q3"]);

        var projection = YearEndCloseProjector.Project(input);

        projection.IsReady.Should().BeFalse();
        projection.MissingPeriods.Should().ContainSingle().Which.Should().Be("Q4");
        // The projection is still computed so the controller can preview the pending close.
        projection.NetIncome.Should().Be(700m);
    }
}
