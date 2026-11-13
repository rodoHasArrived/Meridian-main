using Meridian.Contracts.SecurityMaster;
using FluentAssertions;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

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
