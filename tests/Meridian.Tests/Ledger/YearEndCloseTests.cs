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
        // Opening retained earnings for next year = prior 5000 + net income 700 (a single scope here).
        var roll = projection.OpeningRetainedEarnings.Should().ContainSingle().Which;
        roll.FinancialScope.Should().Be(PeriodCloseProjection.DefaultScope);
        roll.OpeningBalance.Should().Be(5_700m);
        projection.TotalOpeningRetainedEarnings.Should().Be(5_700m);
    }

    [Fact]
    public void Project_DimensionSplitRetainedEarnings_RollsForwardPerEntity()
    {
        var entityA = new LedgerLineDimensionSet(EntityId: "ENT-A");
        var entityB = new LedgerLineDimensionSet(EntityId: "ENT-B");
        var trialBalance = new List<PeriodCloseAccountBalance>
        {
            new(LedgerAccounts.RealizedGain, 1_000m, entityA),     // entity A: +1000 net income
            new(LedgerAccounts.RetainedEarnings, 200m, entityA),   // entity A: 200 prior retained earnings
            new(LedgerAccounts.CommissionExpense, 400m, entityB),  // entity B: -400 net loss
            new(LedgerAccounts.RetainedEarnings, 500m, entityB),   // entity B: 500 prior retained earnings
        };

        var projection = YearEndCloseProjector.Project(new YearEndCloseInput(
            "FY2026", YearEnd, trialBalance, "controller"));

        projection.OpeningRetainedEarnings.Should().HaveCount(2, "each entity rolls forward independently");
        projection.OpeningRetainedEarnings.Single(roll => roll.Dimensions?.EntityId == "ENT-A")
            .OpeningBalance.Should().Be(1_200m, "200 prior + 1000 net income");
        projection.OpeningRetainedEarnings.Single(roll => roll.Dimensions?.EntityId == "ENT-B")
            .OpeningBalance.Should().Be(100m, "500 prior - 400 net loss");
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
