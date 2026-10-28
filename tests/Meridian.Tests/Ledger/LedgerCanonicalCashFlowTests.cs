using Meridian.Contracts.SecurityMaster;
using FluentAssertions;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

public sealed class LedgerCanonicalCashFlowTests
{
    [Fact]
    public void TaxLotMaterialization_RetainsFinancialAccountAndExplicitDimensions()
    {
        var account = LedgerAccounts.Securities("AAPL", "broker-a");
        var projection = LedgerTaxLotReliefProjector.Project(new LedgerTaxLotReliefInput(account,
            new(2026, 1, 2), 1m, 100m, LedgerTaxLotReliefMethod.Fifo,
            [new LedgerTaxLot("lot", new(2025, 1, 1), 1m, 110m) { UnitKind = SecurityLotUnitKind.Shares, Multiplier = 1m }]));
        var dimensions = new LedgerLineDimensionSet(FundId: "fund-a");
        var entry = projection.ToJournalEntry(Guid.NewGuid(), CanonicalCashFlowTestData.Start.AddDays(1),
            "Scoped disposal", lineDimensions: dimensions);
        var ledger = new Meridian.Ledger.Ledger();
        ledger.Post(entry);

        entry.Lines.Should().OnlyContain(line => line.Account.FinancialAccountId == "broker-a" && line.Dimensions == dimensions);
        var cashFlow = LedgerFinancialStatementBuilder.BuildForPeriod(ledger, CanonicalCashFlowTestData.Start,
            entry.Timestamp, financialAccountId: "broker-a", lineDimensions: dimensions).CashFlow!;
        cashFlow.InvestingCashFlow.Should().Be(100m);
        cashFlow.OperatingCashFlow.Should().Be(0m);
        cashFlow.ReconciliationVariance.Should().Be(0m);
    }

    [Theory]
    [InlineData(110)]
    [InlineData(90)]
    public void TaxLotDisposal_RetainsActualInvestingProceedsAndReversal(int basis)
    {
        var account = LedgerAccounts.Securities("AAPL");
        var projection = LedgerTaxLotReliefProjector.Project(new LedgerTaxLotReliefInput(account,
            new(2026, 1, 2), 1m, 100m, LedgerTaxLotReliefMethod.Fifo,
            [new LedgerTaxLot("lot", new(2025, 1, 1), 1m, basis) { UnitKind = SecurityLotUnitKind.Shares, Multiplier = 1m }]));
        var ledger = new Meridian.Ledger.Ledger();
        ledger.PostLines(CanonicalCashFlowTestData.Start, "opening holding",
            [(account, basis, 0m), (LedgerAccounts.RetainedEarnings, 0m, basis)]);
        var entry = projection.ToJournalEntry(Guid.NewGuid(), CanonicalCashFlowTestData.Start.AddDays(1), "Disposal");

        ledger.Post(entry);

        entry.Lines.Select(line => (line.Account, line.Debit, line.Credit)).Should().Equal(projection.Lines);
        CanonicalCashFlowTestData.AssertReportAndReversal(ledger, entry, 100m);
    }
}

internal static class CanonicalCashFlowTestData
{
    internal static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    internal static void AssertReportAndReversal(Meridian.Ledger.Ledger ledger, JournalEntry entry, decimal proceeds)
    {
        var report = Build(ledger);
        var cashFlow = report.Statements.CashFlow!;
        cashFlow.InvestingCashFlow.Should().Be(proceeds);
        cashFlow.OperatingCashFlow.Should().Be(0m, "realized gain/loss is a noncash basis adjustment");
        cashFlow.FinancingCashFlow.Should().Be(0m);
        cashFlow.NetCashFlow.Should().Be(proceeds);
        cashFlow.ReconciliationVariance.Should().Be(0m);
        report.Artifacts.Should().Contain(artifact => artifact.Name == "cash-flow-statement.csv");

        ledger.Post(LedgerJournalReversal.Reverse(entry, Guid.NewGuid(), entry.Timestamp.AddDays(1)));

        var reversed = Build(ledger).Statements.CashFlow!;
        reversed.NetCashFlow.Should().Be(0m);
        reversed.InvestingCashFlow.Should().Be(0m);
        reversed.OperatingCashFlow.Should().Be(0m);
        reversed.ReconciliationVariance.Should().Be(0m);
        reversed.Lines.Should().BeEmpty();
    }

    private static LedgerFinancialReportPack Build(Meridian.Ledger.Ledger ledger)
        => LedgerReportPackBuilder.Build(ledger, new LedgerReportPackRequest("cash-proof", "fund", "period",
            Start, Start.AddYears(1), Start.AddYears(1), "USD", "reviewer", Start.AddYears(1)));
}
