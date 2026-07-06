using FluentAssertions;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

/// <summary>
/// Tests period-close closing entries: temporary accounts zeroed, net income rolled
/// into retained earnings, and the governed draft path for posting the close.
/// </summary>
public sealed class PeriodCloseProjectorTests
{
    private static readonly DateTimeOffset CloseAt = new(2026, 06, 30, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Project_ClosesTemporaryAccountsAndRollsNetIncomeToRetainedEarnings()
    {
        var ledger = new Meridian.Ledger.Ledger();
        // Period activity: 1,000 realized gain, 400 dividend income, 300 commissions.
        ledger.PostLines(CloseAt.AddDays(-20), "Sell gain",
        [
            (LedgerAccounts.Cash, 1_000m, 0m),
            (LedgerAccounts.RealizedGain, 0m, 1_000m)
        ]);
        ledger.PostLines(CloseAt.AddDays(-10), "Dividend received",
        [
            (LedgerAccounts.Cash, 400m, 0m),
            (LedgerAccounts.DividendIncome, 0m, 400m)
        ]);
        ledger.PostLines(CloseAt.AddDays(-5), "Commissions",
        [
            (LedgerAccounts.CommissionExpense, 300m, 0m),
            (LedgerAccounts.Cash, 0m, 300m)
        ]);

        var projection = PeriodCloseProjector.ProjectFrom(ledger, "2026-Q2", CloseAt, "controller");

        projection.HasClosingEntries.Should().BeTrue();
        projection.IsBalanced.Should().BeTrue();
        projection.NetIncome.Should().Be(1_000m + 400m - 300m);
        projection.Lines.Should().HaveCount(3, "each non-zero temporary account closes once");

        // Post the close and verify the roll: temporary accounts land at zero,
        // retained earnings carries the period's net income.
        ledger.PostLines(CloseAt, "Period close 2026-Q2", projection.JournalLines);

        ledger.GetBalance(LedgerAccounts.RealizedGain).Should().Be(0m);
        ledger.GetBalance(LedgerAccounts.DividendIncome).Should().Be(0m);
        ledger.GetBalance(LedgerAccounts.CommissionExpense).Should().Be(0m);
        ledger.GetBalance(LedgerAccounts.RetainedEarnings).Should().Be(1_100m);
    }

    [Fact]
    public void Project_NetLossPeriod_DebitsRetainedEarnings()
    {
        var ledger = new Meridian.Ledger.Ledger();
        ledger.PostLines(CloseAt.AddDays(-10), "Loss",
        [
            (LedgerAccounts.RealizedLoss, 800m, 0m),
            (LedgerAccounts.Cash, 0m, 800m)
        ]);
        ledger.PostLines(CloseAt.AddDays(-5), "Interest income",
        [
            (LedgerAccounts.Cash, 200m, 0m),
            (LedgerAccounts.CashInterestIncome, 0m, 200m)
        ]);

        var projection = PeriodCloseProjector.ProjectFrom(ledger, "2026-Q2", CloseAt, "controller");

        projection.NetIncome.Should().Be(-600m);

        ledger.PostLines(CloseAt, "Period close 2026-Q2", projection.JournalLines);
        ledger.GetBalance(LedgerAccounts.RetainedEarnings).Should().Be(-600m,
            "a net loss reduces retained earnings");
        ledger.GetBalance(LedgerAccounts.RealizedLoss).Should().Be(0m);
        ledger.GetBalance(LedgerAccounts.CashInterestIncome).Should().Be(0m);
    }

    [Fact]
    public void Project_ScopedAccounts_RollIntoScopedRetainedEarnings()
    {
        var ledger = new Meridian.Ledger.Ledger();
        ledger.PostLines(CloseAt.AddDays(-10), "Broker A gain",
        [
            (LedgerAccounts.CashAccount("broker-a"), 500m, 0m),
            (LedgerAccounts.RealizedGainFor("broker-a"), 0m, 500m)
        ]);
        ledger.PostLines(CloseAt.AddDays(-9), "Broker B gain",
        [
            (LedgerAccounts.CashAccount("broker-b"), 250m, 0m),
            (LedgerAccounts.RealizedGainFor("broker-b"), 0m, 250m)
        ]);

        var projection = PeriodCloseProjector.ProjectFrom(ledger, "2026-Q2", CloseAt, "controller");

        projection.NetIncomeByScope.Should().HaveCount(2);
        projection.NetIncomeByScope["broker-a"].Should().Be(500m);
        projection.NetIncomeByScope["broker-b"].Should().Be(250m);

        ledger.PostLines(CloseAt, "Period close 2026-Q2", projection.JournalLines);
        ledger.GetBalance(LedgerAccounts.RetainedEarningsFor("broker-a")).Should().Be(500m);
        ledger.GetBalance(LedgerAccounts.RetainedEarningsFor("broker-b")).Should().Be(250m);
    }

    [Fact]
    public void Project_NoTemporaryBalances_ProducesNoClosingEntries()
    {
        var ledger = new Meridian.Ledger.Ledger();
        ledger.PostLines(CloseAt.AddDays(-10), "Capital deposit",
        [
            (LedgerAccounts.Cash, 10_000m, 0m),
            (LedgerAccounts.CapitalAccount, 0m, 10_000m)
        ]);

        var projection = PeriodCloseProjector.ProjectFrom(ledger, "2026-Q2", CloseAt, "controller");

        projection.HasClosingEntries.Should().BeFalse();
        projection.NetIncome.Should().Be(0m);
        PeriodCloseDraftBuilder.BuildDraft(projection).Should().BeNull();
    }

    [Fact]
    public void BuildDraft_ProducesGovernedBalancedDraft_ThatPostsThroughApproval()
    {
        var ledger = new Meridian.Ledger.Ledger();
        ledger.PostLines(CloseAt.AddDays(-10), "Gain",
        [
            (LedgerAccounts.Cash, 1_000m, 0m),
            (LedgerAccounts.RealizedGain, 0m, 1_000m)
        ]);

        var projection = PeriodCloseProjector.ProjectFrom(ledger, "2026-Q2", CloseAt, "controller");
        var draft = PeriodCloseDraftBuilder.BuildDraft(projection);

        draft.Should().NotBeNull();
        draft!.IsBalanced.Should().BeTrue();
        draft.Event.Kind.Should().Be(AutomatedJournalEventKind.PeriodCloseClosingEntries);
        draft.Event.IdempotencyKey.Should().Be("period-close|2026-Q2");

        var posted = AutomatedJournalApproval
            .Submit(draft, "controller", CloseAt, "quarter close")
            .Approve("cfo", CloseAt, "close reviewed", ["/evidence/close/2026-Q2"])
            .PostTo(ledger, "cfo", CloseAt, "close posted", ["/evidence/close/2026-Q2"]);

        posted.Status.Should().Be(AutomatedJournalApprovalStatus.Posted);
        ledger.GetBalance(LedgerAccounts.RealizedGain).Should().Be(0m);
        ledger.GetBalance(LedgerAccounts.RetainedEarnings).Should().Be(1_000m);
    }
}
