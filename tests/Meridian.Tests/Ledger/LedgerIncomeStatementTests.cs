using FluentAssertions;
using Meridian.Ledger;

namespace Meridian.Tests.Ledger;

public sealed partial class LedgerIncomeStatementTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset AsOf = Start.AddMonths(1);

    [Fact]
    public void PeriodIncome_ExcludesOpeningAndLaterActivity_WithoutChangingAsOfStatements()
    {
        var ledger = new Meridian.Ledger.Ledger();
        Post(ledger, Start.AddDays(-1), LedgerAccounts.Cash, LedgerAccounts.CashInterestIncome, 300m);
        Post(ledger, Start, LedgerAccounts.Cash, LedgerAccounts.CashInterestIncome, 20m);
        Post(ledger, Start.AddDays(1), LedgerAccounts.Cash, LedgerAccounts.CashInterestIncome, 100m);
        Post(ledger, AsOf, LedgerAccounts.CommissionExpense, LedgerAccounts.Cash, 10m);
        Post(ledger, AsOf.AddTicks(1), LedgerAccounts.Cash, LedgerAccounts.CashInterestIncome, 999m);

        var result = LedgerFinancialStatementBuilder.BuildIncomeStatementForPeriod(ledger, Start, AsOf);

        result.PeriodStart.Should().Be(Start);
        result.AsOf.Should().Be(AsOf);
        result.TotalRevenue.Should().Be(100m);
        result.TotalExpenses.Should().Be(10m);
        result.NetIncome.Should().Be(90m);
        LedgerFinancialStatementBuilder.BuildForPeriod(ledger, Start, AsOf).NetIncome.Should().Be(410m,
            "the existing point-in-time statement contract retains accumulated unclosed P&L");
        ledger.JournalEntryCount.Should().Be(5);
    }

    [Fact]
    public void PeriodIncome_RetainsEarnedResultAfterRevenueAndExpensesAreClosed()
    {
        var ledger = new Meridian.Ledger.Ledger();
        Post(ledger, Start.AddDays(-1), LedgerAccounts.Cash, LedgerAccounts.CashInterestIncome, 400m);
        Post(ledger, Start.AddDays(1), LedgerAccounts.Cash, LedgerAccounts.CashInterestIncome, 100m);
        Post(ledger, Start.AddDays(2), LedgerAccounts.CommissionExpense, LedgerAccounts.Cash, 20m);
        var projection = PeriodCloseProjector.ProjectFrom(ledger, "2026-01", AsOf, "synthetic-controller");
        ledger.PostLines(AsOf, "Close January", projection.JournalLines);

        var result = LedgerFinancialStatementBuilder.BuildIncomeStatementForPeriod(ledger, Start, AsOf);

        result.TotalRevenue.Should().Be(100m);
        result.TotalExpenses.Should().Be(20m);
        result.NetIncome.Should().Be(80m);
        ledger.GetBalance(LedgerAccounts.RetainedEarnings).Should().Be(480m);
        LedgerFinancialStatementBuilder.BuildAsOf(ledger, AsOf).NetIncome.Should().Be(0m);
    }

    [Fact]
    public void PeriodIncome_PreservesSignedReversalsAndAllDecimalDigits()
    {
        const decimal tiny = 0.0000000000000000000000000001m;
        var ledger = new Meridian.Ledger.Ledger();
        Post(ledger, Start.AddDays(-1), LedgerAccounts.Cash, LedgerAccounts.CashInterestIncome, tiny);
        Post(ledger, Start.AddDays(-1), LedgerAccounts.CommissionExpense, LedgerAccounts.Cash, 2m * tiny);
        foreach (var original in ledger.Journal.ToArray())
            ledger.Post(LedgerJournalReversal.Reverse(original, Guid.NewGuid(), Start.AddDays(1)));

        var result = LedgerFinancialStatementBuilder.BuildIncomeStatementForPeriod(ledger, Start, AsOf);

        result.TotalRevenue.Should().Be(-tiny);
        result.TotalExpenses.Should().Be(-2m * tiny);
        result.NetIncome.Should().Be(tiny);
    }

    [Fact]
    public void PeriodIncome_ReopenAndRebookChangesOnlyTheCorrectedEconomicResult()
    {
        var ledger = new Meridian.Ledger.Ledger();
        Post(ledger, Start.AddDays(-1), LedgerAccounts.Cash, LedgerAccounts.CashInterestIncome, 400m);
        Post(ledger, Start.AddDays(1), LedgerAccounts.Cash, LedgerAccounts.CashInterestIncome, 100m);
        var correctedIncome = ledger.Journal.Last();
        Post(ledger, Start.AddDays(2), LedgerAccounts.CommissionExpense, LedgerAccounts.Cash, 20m);
        ledger.PostLines(Start.AddDays(10), "Initial close",
            PeriodCloseProjector.ProjectFrom(ledger, "2026-01", Start.AddDays(10), "synthetic-controller").JournalLines);
        var originalClose = ledger.Journal.Last();
        ledger.Post(LedgerJournalReversal.Reverse(originalClose, Guid.NewGuid(), Start.AddDays(11)));
        ledger.Post(LedgerJournalReversal.Reverse(correctedIncome, Guid.NewGuid(), Start.AddDays(12)));
        Post(ledger, Start.AddDays(13), LedgerAccounts.Cash, LedgerAccounts.CashInterestIncome, 90m);
        ledger.PostLines(AsOf, "Corrected close",
            PeriodCloseProjector.ProjectFrom(ledger, "2026-01", AsOf, "synthetic-controller").JournalLines);

        var result = LedgerFinancialStatementBuilder.BuildIncomeStatementForPeriod(ledger, Start, AsOf);

        result.TotalRevenue.Should().Be(90m);
        result.TotalExpenses.Should().Be(20m);
        result.NetIncome.Should().Be(70m);
        LedgerFinancialStatementBuilder.BuildAsOf(ledger, AsOf).NetIncome.Should().Be(0m);
        ledger.GetBalance(LedgerAccounts.RetainedEarnings).Should().Be(470m);
    }

    [Fact]
    public void PeriodIncome_CombinesAccountAndDimensionScope_AndRetainsChartRollups()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var selected = new LedgerLineDimensionSet(FundId: "fund-a");
        var other = new LedgerLineDimensionSet(FundId: "fund-b");
        var incomeA = new LedgerAccount("Revenue:Fees", LedgerAccountType.Revenue, FinancialAccountId: "account-a");
        var incomeB = incomeA with { FinancialAccountId = "account-b" };
        Post(ledger, Start.AddDays(1), LedgerAccounts.CashAccount("account-a"), incomeA, 125m, selected);
        Post(ledger, Start.AddDays(1), LedgerAccounts.CashAccount("account-a"), incomeA, 400m, other);
        Post(ledger, Start.AddDays(1), LedgerAccounts.CashAccount("account-b"), incomeB, 900m, selected);

        var result = LedgerFinancialStatementBuilder.BuildIncomeStatementForPeriod(ledger, Start, AsOf,
            financialAccountId: " ACCOUNT-A ", lineDimensions: selected);

        result.TotalRevenue.Should().Be(125m);
        result.NetIncome.Should().Be(125m);
        result.Rows.Single(row => row.Path == "Revenue:Fees").DirectBalance.Should().Be(125m);
        var parent = result.Rows.Single(row => row.Path == "Revenue");
        parent.DirectBalance.Should().Be(0m);
        parent.AggregateBalance.Should().Be(125m);
    }

    [Fact]
    public void PeriodIncome_RejectsReversedDateRange()
    {
        var act = () => LedgerFinancialStatementBuilder.BuildIncomeStatementForPeriod(
            new Meridian.Ledger.Ledger(), AsOf, Start);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PeriodIncome_RejectsConflictingTypesAtOneChartPath(bool supplyChart, bool expenseFirst)
    {
        var ledger = new Meridian.Ledger.Ledger();
        var revenue = new LedgerAccount("Fees", LedgerAccountType.Revenue);
        var expense = new LedgerAccount("fees", LedgerAccountType.Expense);
        if (expenseFirst)
            Post(ledger, Start.AddDays(1), expense, LedgerAccounts.Cash, 40m);
        Post(ledger, Start.AddDays(1), LedgerAccounts.Cash, revenue, 100m);
        if (!expenseFirst)
            Post(ledger, Start.AddDays(1), expense, LedgerAccounts.Cash, 40m);
        ChartOfAccounts? chart = null;
        if (supplyChart)
        {
            chart = new ChartOfAccounts();
            chart.Register("Fees", LedgerAccountType.Revenue);
        }

        var act = () => LedgerFinancialStatementBuilder.BuildIncomeStatementForPeriod(ledger, Start, AsOf, chart);

        act.Should().Throw<ArgumentException>().WithMessage("*conflicting account types*");
    }

    private static void Post(Meridian.Ledger.Ledger ledger, DateTimeOffset timestamp,
        LedgerAccount debit, LedgerAccount credit, decimal amount, LedgerLineDimensionSet? dimensions = null)
        => ledger.PostLines(timestamp, "Synthetic income-statement activity",
            [(debit, amount, 0m, dimensions), (credit, 0m, amount, dimensions)]);
}
