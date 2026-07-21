using FluentAssertions;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

public sealed class LedgerPeriodStatementsTests
{
    private static Meridian.Ledger.Ledger BuildFundLedger()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var investorCapital = LedgerAccounts.InvestorCapitalFor("lp-1");
        var securities = LedgerAccounts.Securities("AAPL");
        var interestIncome = LedgerAccounts.CashInterestIncome;
        var managementFee = LedgerAccounts.ManagementFeeExpenseFor("fund-a");

        // Before the period: opening contribution seeds beginning cash and capital.
        ledger.PostLines(new DateTimeOffset(2025, 12, 15, 0, 0, 0, TimeSpan.Zero), "opening contribution",
            [(LedgerAccounts.Cash, 5_000_000m, 0m), (investorCapital, 0m, 5_000_000m)]);

        // In-period activity.
        ledger.PostLines(new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero), "capital contribution",
            [(LedgerAccounts.Cash, 3_000_000m, 0m), (investorCapital, 0m, 3_000_000m)]);
        ledger.PostLines(new DateTimeOffset(2026, 2, 15, 0, 0, 0, TimeSpan.Zero), "buy securities",
            [(securities, 4_000_000m, 0m), (LedgerAccounts.Cash, 0m, 4_000_000m)]);
        ledger.PostLines(new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero), "management fee",
            [(managementFee, 100_000m, 0m), (LedgerAccounts.Cash, 0m, 100_000m)]);
        ledger.PostLines(new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero), "interest income",
            [(LedgerAccounts.Cash, 200_000m, 0m), (interestIncome, 0m, 200_000m)]);
        ledger.PostLines(new DateTimeOffset(2026, 12, 15, 0, 0, 0, TimeSpan.Zero), "distribution",
            [(investorCapital, 500_000m, 0m), (LedgerAccounts.Cash, 0m, 500_000m)]);

        return ledger;
    }

    private static LedgerFinancialStatements BuildStatements()
        => LedgerFinancialStatementBuilder.BuildForPeriod(
            BuildFundLedger(),
            new DateTimeOffset(2025, 12, 31, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void CashFlow_ReconcilesBeginningToEndingCash()
    {
        var cashFlow = BuildStatements().CashFlow!;

        cashFlow.BeginningCash.Should().Be(5_000_000m);
        cashFlow.EndingCash.Should().Be(3_600_000m);
        cashFlow.IsReconciled.Should().BeTrue();
        cashFlow.NetCashFlow.Should().Be(-1_400_000m);
    }

    [Fact]
    public void CashFlow_ClassifiesActivitiesCorrectly()
    {
        var cashFlow = BuildStatements().CashFlow!;

        cashFlow.OperatingCashFlow.Should().Be(100_000m);   // +200k income - 100k fee
        cashFlow.InvestingCashFlow.Should().Be(-4_000_000m); // securities purchase
        cashFlow.FinancingCashFlow.Should().Be(2_500_000m);  // +3M contribution - 500k distribution
    }

    [Fact]
    public void PartnersCapital_RollsForwardAndReconciles()
    {
        var partnersCapital = BuildStatements().PartnersCapital!;

        partnersCapital.BeginningCapital.Should().Be(5_000_000m);
        partnersCapital.Contributions.Should().Be(3_000_000m);
        partnersCapital.Distributions.Should().Be(500_000m);
        // Unclosed net income (200k interest income - 100k management fee) rides an undistributed line.
        partnersCapital.AllocatedResult.Should().Be(100_000m);
        partnersCapital.EndingCapital.Should().Be(7_600_000m);
        partnersCapital.IsReconciled.Should().BeTrue();
    }

    [Fact]
    public void PartnersCapital_EndingTiesToBalanceSheetEquity()
    {
        var statements = BuildStatements();

        statements.PartnersCapital!.EndingCapital.Should().Be(statements.EndingEquity);
    }

    [Fact]
    public void PeriodStatements_ScopeBalancesToLineDimensions()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var capitalA = LedgerAccounts.InvestorCapitalFor("lp-a");
        var capitalB = LedgerAccounts.InvestorCapitalFor("lp-b");
        var fundA = new LedgerLineDimensionSet(FundId: "fund-a");
        var fundB = new LedgerLineDimensionSet(FundId: "fund-b");

        // Two funds share the same cash account but are separated by line dimensions.
        ledger.PostLines(new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero), "fund-a contribution",
            [(LedgerAccounts.Cash, 1_000_000m, 0m, (LedgerLineDimensionSet?)fundA), (capitalA, 0m, 1_000_000m, fundA)]);
        ledger.PostLines(new DateTimeOffset(2026, 2, 2, 0, 0, 0, TimeSpan.Zero), "fund-b contribution",
            [(LedgerAccounts.Cash, 2_000_000m, 0m, (LedgerLineDimensionSet?)fundB), (capitalB, 0m, 2_000_000m, fundB)]);

        var scoped = LedgerFinancialStatementBuilder.BuildForPeriod(
            ledger,
            new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero),
            lineDimensions: fundA);

        // Only fund-a's 1,000,000 cash and capital appear — fund-b's 2,000,000 must not leak in.
        scoped.CashFlow!.EndingCash.Should().Be(1_000_000m);
        scoped.CashFlow.FinancingCashFlow.Should().Be(1_000_000m);
        scoped.PartnersCapital!.EndingCapital.Should().Be(1_000_000m);
        scoped.PartnersCapital.Accounts.Should().Contain(account => account.InvestorId == "lp-a");
        scoped.PartnersCapital.Accounts.Should().NotContain(account => account.InvestorId == "lp-b");
    }

    [Fact]
    public void PartnersCapital_PerInvestorAccountPresent()
    {
        var partnersCapital = BuildStatements().PartnersCapital!;

        var lp1 = partnersCapital.Accounts.Should().ContainSingle(account => account.InvestorId == "lp-1").Subject;
        lp1.ReconciliationVariance.Should().BeApproximately(0m, 0.0001m);
        partnersCapital.Accounts.Should().Contain(account => account.AccountName == "Undistributed Net Income");
    }
}
