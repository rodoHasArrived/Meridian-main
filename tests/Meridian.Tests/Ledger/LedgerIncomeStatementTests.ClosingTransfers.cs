using FluentAssertions;
using Meridian.Ledger;

namespace Meridian.Tests.Ledger;

public sealed partial class LedgerIncomeStatementTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void PeriodIncome_RecognizesGovernedZeroResultClose_AndItsReversalAndRebook(int correctionStage)
    {
        var ledger = new Meridian.Ledger.Ledger();
        Post(ledger, Start.AddDays(1), LedgerAccounts.Cash, LedgerAccounts.CashInterestIncome, 100m);
        var originalIncome = ledger.Journal.Single();
        Post(ledger, Start.AddDays(2), LedgerAccounts.CommissionExpense, LedgerAccounts.Cash, 100m);
        var close = PostApprovedClose(ledger, Start.AddDays(10));
        close.Lines.Should().OnlyContain(line => line.Account.AccountType == LedgerAccountType.Revenue || line.Account.AccountType == LedgerAccountType.Expense,
            "a zero-result close has no retained-earnings line");
        if (correctionStage > 0)
        {
            ledger.Post(LedgerJournalReversal.Reverse(close, Guid.NewGuid(), Start.AddDays(11)));
            if (correctionStage == 2)
            {
                ledger.Post(LedgerJournalReversal.Reverse(originalIncome, Guid.NewGuid(), Start.AddDays(12)));
                Post(ledger, Start.AddDays(13), LedgerAccounts.Cash, LedgerAccounts.CashInterestIncome, 90m);
            }
            PostApprovedClose(ledger, AsOf);
        }

        var result = LedgerFinancialStatementBuilder.BuildIncomeStatementForPeriod(ledger, Start, AsOf);

        var expectedRevenue = correctionStage == 2 ? 90m : 100m;
        result.TotalRevenue.Should().Be(expectedRevenue);
        result.TotalExpenses.Should().Be(100m);
        result.NetIncome.Should().Be(expectedRevenue - 100m);
        LedgerFinancialStatementBuilder.BuildAsOf(ledger, AsOf).NetIncome.Should().Be(0m);
        var pack = LedgerReportPackBuilder.Build(ledger, new("synthetic-close-report", "fund-a", "2026-01",
            Start, AsOf, AsOf, "USD", "synthetic-controller", AsOf));
        pack.Artifacts.Single(artifact => artifact.Name == "pnl-by-strategy-symbol.csv").Content
            .Should().Contain($",,{expectedRevenue},100,{expectedRevenue - 100m}");
    }

    [Fact]
    public void PeriodIncome_RecognizesZeroResultScopeWithinANonzeroGovernedClose()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var selected = new LedgerLineDimensionSet(FundId: "fund-a");
        var other = new LedgerLineDimensionSet(FundId: "fund-b");
        var cash = LedgerAccounts.CashAccount("account-a");
        var income = new LedgerAccount("Income", LedgerAccountType.Revenue, FinancialAccountId: "account-a");
        var expense = new LedgerAccount("Expenses", LedgerAccountType.Expense, FinancialAccountId: "account-a");
        Post(ledger, Start.AddDays(1), cash, income, 100m, selected);
        Post(ledger, Start.AddDays(2), expense, cash, 100m, selected);
        Post(ledger, Start.AddDays(1), cash, income, 70m, other);
        Post(ledger, Start.AddDays(2), expense, cash, 20m, other);
        var close = PostApprovedClose(ledger, AsOf,
        [
            new(income, 100m, selected), new(expense, 100m, selected),
            new(income, 70m, other), new(expense, 20m, other)
        ]);
        close.Lines.Where(line => line.Dimensions == selected).Should()
            .OnlyContain(line => line.Account.AccountType == LedgerAccountType.Revenue || line.Account.AccountType == LedgerAccountType.Expense);
        close.Lines.Should().Contain(line => line.Account == LedgerAccounts.RetainedEarningsFor("account-a")
            && line.Dimensions == other && line.Credit == 50m);

        var result = LedgerFinancialStatementBuilder.BuildIncomeStatementForPeriod(ledger, Start, AsOf,
            financialAccountId: "account-a", lineDimensions: selected);

        result.TotalRevenue.Should().Be(100m);
        result.TotalExpenses.Should().Be(100m);
        result.NetIncome.Should().Be(0m);
        var all = LedgerFinancialStatementBuilder.BuildIncomeStatementForPeriod(ledger, Start, AsOf);
        all.TotalRevenue.Should().Be(170m);
        all.TotalExpenses.Should().Be(120m);
        all.NetIncome.Should().Be(50m);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("activity")]
    [InlineData("period")]
    [InlineData("fingerprint")]
    public void PeriodIncome_PreservesOrdinaryPnlReclassificationsWithoutCompleteCloseProvenance(string missing)
    {
        var ledger = new Meridian.Ledger.Ledger();
        var tags = new Dictionary<string, string>();
        if (missing is not ("none" or "period"))
            tags["close.periodId"] = "2026-01";
        if (missing is not ("none" or "fingerprint"))
            tags["close.projectionFingerprint"] = "synthetic-projection";
        ledger.PostLines(Start.AddDays(1), "Ordinary P&L reclassification",
            [(LedgerAccounts.CashInterestIncome, 100m, 0m), (LedgerAccounts.CommissionExpense, 0m, 100m)],
            new JournalEntryMetadata(ActivityType: missing is "none" or "activity" ? null : "period-close", Tags: tags));

        var result = LedgerFinancialStatementBuilder.BuildIncomeStatementForPeriod(ledger, Start, AsOf);

        result.TotalRevenue.Should().Be(-100m);
        result.TotalExpenses.Should().Be(-100m);
        result.NetIncome.Should().Be(0m);
    }

    [Fact]
    public void PeriodIncome_DoesNotExcludeMislabeledMixedCashActivityOutsideTheSelectedScope()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var incomeA = new LedgerAccount("Income", LedgerAccountType.Revenue, FinancialAccountId: "account-a");
        var expenseA = new LedgerAccount("Expenses", LedgerAccountType.Expense, FinancialAccountId: "account-a");
        ledger.PostLines(Start.AddDays(1), "Mixed activity with a close label",
        [
            (incomeA, 100m, 0m), (expenseA, 0m, 100m),
            (LedgerAccounts.CashAccount("account-b"), 25m, 0m),
            (incomeA with { FinancialAccountId = "account-b" }, 0m, 25m)
        ], CompleteCloseMetadata());

        var result = LedgerFinancialStatementBuilder.BuildIncomeStatementForPeriod(ledger, Start, AsOf,
            financialAccountId: "account-a");

        result.TotalRevenue.Should().Be(-100m);
        result.TotalExpenses.Should().Be(-100m);
    }

    [Fact]
    public void PeriodIncome_DoesNotExcludeAnUnbalancedScopedPnlSliceWithACloseLabel()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var income = new LedgerAccount("Income", LedgerAccountType.Revenue, FinancialAccountId: "account-a");
        var expense = new LedgerAccount("Expenses", LedgerAccountType.Expense, FinancialAccountId: "account-a");
        ledger.PostLines(Start.AddDays(1), "Cross-account reclassification with a close label",
        [
            (income, 100m, 0m), (expense, 0m, 40m),
            (expense with { FinancialAccountId = "account-b" }, 0m, 60m)
        ], CompleteCloseMetadata());

        var result = LedgerFinancialStatementBuilder.BuildIncomeStatementForPeriod(ledger, Start, AsOf,
            financialAccountId: "account-a");

        result.TotalRevenue.Should().Be(-100m);
        result.TotalExpenses.Should().Be(-40m);
        result.NetIncome.Should().Be(-60m);
    }

    private static JournalEntry PostApprovedClose(Meridian.Ledger.Ledger ledger, DateTimeOffset timestamp,
        IReadOnlyList<PeriodCloseAccountBalance>? balances = null)
    {
        var projection = balances is null
            ? PeriodCloseProjector.ProjectFrom(ledger, "2026-01", timestamp, "synthetic-controller")
            : PeriodCloseProjector.Project(new PeriodCloseInput("2026-01", timestamp, balances, "synthetic-controller"));
        var draft = PeriodCloseDraftBuilder.BuildDraft(projection)
            ?? throw new InvalidOperationException("Synthetic close must have temporary balances to close.");
        var journal = AutomatedJournalApproval.Submit(draft, "synthetic-controller", timestamp, "Prepare close")
            .Approve("synthetic-reviewer", timestamp, "Review close", ["/synthetic/close-evidence"])
            .ToJournalEntry();
        ledger.Post(journal);
        return journal;
    }

    private static JournalEntryMetadata CompleteCloseMetadata() => new(ActivityType: "period-close",
        Tags: new Dictionary<string, string>
        {
            ["close.periodId"] = "2026-01",
            ["close.projectionFingerprint"] = "synthetic-projection"
        });
}
