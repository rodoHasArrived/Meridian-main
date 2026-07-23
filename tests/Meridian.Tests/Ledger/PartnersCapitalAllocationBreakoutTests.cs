using System;
using System.Linq;

using FluentAssertions;

using Meridian.Ledger;

using Xunit;

namespace Meridian.Tests.Ledger;

/// <summary>
/// Golden coverage for the partners' capital statement allocation breakout: the single lumped
/// allocated result is decomposed into income/gain, (non-fee) expense, and fund-fee drivers so a
/// client-grade statement can report the components a fund accountant delivers (W9-REPORT-005),
/// without disturbing the existing reconciliation invariants.
/// </summary>
public sealed class PartnersCapitalAllocationBreakoutTests
{
    private static readonly DateTimeOffset PeriodStart = new(2025, 12, 31, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset AsOf = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

    private static LedgerFinancialStatements BuildForPeriod(Meridian.Ledger.Ledger ledger)
        => LedgerFinancialStatementBuilder.BuildForPeriod(ledger, PeriodStart, AsOf);

    [Fact]
    public void Undistributed_AllocatedResult_SplitsIntoIncomeExpenseAndFee()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var capital = LedgerAccounts.InvestorCapitalFor("lp-1");

        // Opening capital before the period.
        ledger.PostLines(new DateTimeOffset(2025, 6, 15, 0, 0, 0, TimeSpan.Zero), "opening contribution",
            [(LedgerAccounts.Cash, 5_000_000m, 0m), (capital, 0m, 5_000_000m)]);

        // In-period P&L flowing through revenue/expense and cash (no direct capital allocation), so it
        // rides the aggregate Undistributed Net Income line.
        ledger.PostLines(new DateTimeOffset(2026, 3, 31, 0, 0, 0, TimeSpan.Zero), "interest income",
            [(LedgerAccounts.Cash, 200_000m, 0m), (LedgerAccounts.CashInterestIncome, 0m, 200_000m)]);
        ledger.PostLines(new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero), "management fee",
            [(LedgerAccounts.ManagementFeeExpenseFor("fund-a"), 100_000m, 0m), (LedgerAccounts.Cash, 0m, 100_000m)]);
        ledger.PostLines(new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero), "brokerage commission",
            [(LedgerAccounts.CommissionExpense, 20_000m, 0m), (LedgerAccounts.Cash, 0m, 20_000m)]);

        var partnersCapital = BuildForPeriod(ledger).PartnersCapital!;
        var undistributed = partnersCapital.Accounts.Single(a => a.AccountName == "Undistributed Net Income");

        undistributed.IncomeGainAllocations.Should().Be(200_000m);
        undistributed.FeeAllocations.Should().Be(100_000m);           // management fee is a fund fee
        undistributed.ExpenseAllocations.Should().Be(20_000m);        // commission is an operating expense, not a fee
        undistributed.AllocatedResult.Should().Be(80_000m);           // 200k income - 100k fee - 20k expense
        undistributed.AllocationComponentsVariance.Should().Be(0m);

        partnersCapital.IncomeGainAllocations.Should().Be(200_000m);
        partnersCapital.ExpenseAllocations.Should().Be(20_000m);
        partnersCapital.FeeAllocations.Should().Be(100_000m);
        partnersCapital.AllocatedResult.Should().Be(80_000m);
        partnersCapital.AllocationComponentsVariance.Should().Be(0m);
        partnersCapital.IsReconciled.Should().BeTrue();
    }

    [Fact]
    public void PerformanceFee_LandsInFeeBucketAlongsideManagementFee()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var capital = LedgerAccounts.InvestorCapitalFor("lp-1");

        ledger.PostLines(new DateTimeOffset(2025, 6, 15, 0, 0, 0, TimeSpan.Zero), "opening contribution",
            [(LedgerAccounts.Cash, 5_000_000m, 0m), (capital, 0m, 5_000_000m)]);
        ledger.PostLines(new DateTimeOffset(2026, 3, 31, 0, 0, 0, TimeSpan.Zero), "realized gain",
            [(LedgerAccounts.Cash, 500_000m, 0m), (LedgerAccounts.RealizedGainFor("fund-a"), 0m, 500_000m)]);
        ledger.PostLines(new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero), "management fee",
            [(LedgerAccounts.ManagementFeeExpenseFor("fund-a"), 100_000m, 0m), (LedgerAccounts.Cash, 0m, 100_000m)]);
        ledger.PostLines(new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero), "performance fee",
            [(LedgerAccounts.PerformanceFeeExpenseFor("fund-a"), 60_000m, 0m), (LedgerAccounts.Cash, 0m, 60_000m)]);

        var partnersCapital = BuildForPeriod(ledger).PartnersCapital!;

        partnersCapital.IncomeGainAllocations.Should().Be(500_000m);
        partnersCapital.FeeAllocations.Should().Be(160_000m);   // 100k management + 60k performance
        partnersCapital.ExpenseAllocations.Should().Be(0m);
        partnersCapital.AllocatedResult.Should().Be(340_000m);  // 500k - 160k
        partnersCapital.AllocationComponentsVariance.Should().Be(0m);
        partnersCapital.IsReconciled.Should().BeTrue();
    }

    [Fact]
    public void DirectAllocationToInvestorCapital_DecomposesWithClosingSign()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var capital = LedgerAccounts.InvestorCapitalFor("lp-1");
        var managementFee = LedgerAccounts.ManagementFeeExpenseFor("fund-a");

        // Opening capital before the period.
        ledger.PostLines(new DateTimeOffset(2025, 6, 15, 0, 0, 0, TimeSpan.Zero), "opening contribution",
            [(LedgerAccounts.Cash, 1_000_000m, 0m), (capital, 0m, 1_000_000m)]);

        // In-period income earned and a fee accrued through P&L...
        ledger.PostLines(new DateTimeOffset(2026, 3, 31, 0, 0, 0, TimeSpan.Zero), "interest income",
            [(LedgerAccounts.Cash, 300_000m, 0m), (LedgerAccounts.CashInterestIncome, 0m, 300_000m)]);
        ledger.PostLines(new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero), "management fee",
            [(managementFee, 100_000m, 0m), (LedgerAccounts.Cash, 0m, 100_000m)]);

        // ...then closed directly into the LP's capital account: gross income in, fee out.
        ledger.PostLines(new DateTimeOffset(2026, 12, 30, 0, 0, 0, TimeSpan.Zero), "allocate net to capital",
            [(LedgerAccounts.CashInterestIncome, 300_000m, 0m), (managementFee, 0m, 100_000m), (capital, 0m, 200_000m)]);

        var partnersCapital = BuildForPeriod(ledger).PartnersCapital!;
        var lp = partnersCapital.Accounts.Single(a => a.InvestorId == "lp-1");

        lp.AllocatedResult.Should().Be(200_000m);           // net moved into capital
        lp.IncomeGainAllocations.Should().Be(300_000m);     // gross income allocated
        lp.FeeAllocations.Should().Be(100_000m);            // fee charged against the allocation
        lp.ExpenseAllocations.Should().Be(0m);
        lp.AllocationComponentsVariance.Should().Be(0m);    // 300k - 0 - 100k == 200k
        lp.EndingCapital.Should().Be(1_200_000m);

        // The P&L accounts are zeroed by the close, so no undistributed line survives and the
        // aggregate breakout equals the single LP line.
        partnersCapital.Accounts.Should().NotContain(a => a.AccountName == "Undistributed Net Income");
        partnersCapital.IncomeGainAllocations.Should().Be(300_000m);
        partnersCapital.FeeAllocations.Should().Be(100_000m);
        partnersCapital.AllocatedResult.Should().Be(200_000m);
        partnersCapital.AllocationComponentsVariance.Should().Be(0m);
        partnersCapital.IsReconciled.Should().BeTrue();
    }

    [Fact]
    public void ReportPack_PartnersCapitalCsvAndTable_ExposeTheBreakoutColumns()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var capital = LedgerAccounts.InvestorCapitalFor("lp-1");
        ledger.PostLines(new DateTimeOffset(2025, 6, 15, 0, 0, 0, TimeSpan.Zero), "opening contribution",
            [(LedgerAccounts.Cash, 5_000_000m, 0m), (capital, 0m, 5_000_000m)]);
        ledger.PostLines(new DateTimeOffset(2026, 3, 31, 0, 0, 0, TimeSpan.Zero), "interest income",
            [(LedgerAccounts.Cash, 200_000m, 0m), (LedgerAccounts.CashInterestIncome, 0m, 200_000m)]);
        ledger.PostLines(new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero), "management fee",
            [(LedgerAccounts.ManagementFeeExpenseFor("fund-a"), 100_000m, 0m), (LedgerAccounts.Cash, 0m, 100_000m)]);

        var request = new LedgerReportPackRequest(
            reportId: "rp-1",
            fundId: "fund-a",
            periodId: "2026",
            periodStart: PeriodStart,
            periodEnd: AsOf,
            asOf: AsOf,
            baseCurrency: "USD",
            generatedBy: "tester",
            generatedAtUtc: AsOf);

        var pack = LedgerReportPackBuilder.Build(ledger, request);

        var csv = pack.Artifacts.Single(a => a.Name == "partners-capital-statement.csv").Content;
        csv.Should().Contain("IncomeGainAllocations,ExpenseAllocations,FeeAllocations,AllocatedResult");

        // The shared presentation table drives both the built-in and the client-grade PDF/XLSX
        // renderers, so the fee/income/expense columns reach the Excel deliverable.
        var table = LedgerReportPresentation.BuildTables(pack)
            .Single(t => t.Title == "Statement of Changes in Partners' Capital");
        table.Headers.Should().ContainInOrder("Income & Gains", "Expenses", "Fees");

        // The XLSX bytes render deterministically from that table.
        var workbook = BuiltInLedgerReportBinaryRenderer.Instance.RenderWorkbook(pack);
        workbook.Should().NotBeEmpty();
    }
}
