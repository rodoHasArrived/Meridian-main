using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;

namespace Meridian.Tests.Ledger;

public sealed class LedgerStatementControlPackTests
{
    private static readonly DateTimeOffset Start = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 7, 31, 23, 59, 59, TimeSpan.Zero);

    [Fact]
    public void BuildsTiedPeriodControlsFromAllJournalLinesIncludingClosingAndExchangeEffects()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var foreignCash = LedgerAccounts.CashInCurrency("EUR");
        var instrument = Guid.NewGuid();
        ledger.PostLines(Start.AddDays(-1), "Opening capital", [(foreignCash, 1000m, 0m), (LedgerAccounts.CapitalAccount, 0m, 1000m)]);
        ledger.PostLines(Start.AddDays(1), "Realized profit", [(LedgerAccounts.Cash, 100m, 0m), (LedgerAccounts.RealizedGain, 0m, 100m)],
            new JournalEntryMetadata(Symbol: "ABC", SecurityId: instrument));
        ledger.PostLines(Start.AddDays(2), "Market mark", [(LedgerAccounts.Securities(LedgerSecurityKey.Unresolved("ABC")), 60m, 0m), (LedgerAccounts.UnrealizedGain, 0m, 60m)],
            new JournalEntryMetadata(Symbol: "ABC", SecurityId: instrument));
        ledger.PostLines(Start.AddDays(3), "Cash revaluation", [(foreignCash, 5m, 0m), (LedgerAccounts.UnrealizedFxGain, 0m, 5m)]);
        var fxJournal = ledger.Journal.Last().JournalEntryId;
        ledger.PostLines(End, "Hard close", [(LedgerAccounts.RealizedGain, 100m, 0m), (LedgerAccounts.UnrealizedGain, 60m, 0m),
            (LedgerAccounts.UnrealizedFxGain, 5m, 0m), (LedgerAccounts.RetainedEarnings, 0m, 165m)]);

        var report = LedgerReportPackBuilder.Build(ledger, Request() with
        {
            StatementControl = Definition() with { CashFxRevaluationJournalIds = [fxJournal] }
        });
        var controls = report.StatementControl!;

        controls.Rows.Single(row => row.RowKey == "net-income").Amount.Should().Be(165m);
        controls.Rows.Single(row => row.RowKey == "equity").Amount.Should().Be(1165m);
        controls.Rows.Single(row => row.RowKey == "ending-cash").Amount.Should().Be(1105m);
        controls.Rows.Single(row => row.RowKey == "exchange-rate-effects").Amount.Should().Be(5m);
        report.Statements.CashFlow!.NetCashFlow.Should().Be(100m);
        controls.Rows.Where(row => row.InstrumentId == instrument).Select(row => row.Amount).Should().BeEquivalentTo([100m, 60m]);
        controls.Rows.Where(row => row.Statement == "Agreement Controls").Should().OnlyContain(row => row.Amount == 0m);
        controls.LineMappings.Should().HaveCount(ledger.Journal.Sum(entry => entry.Lines.Count));
        controls.LineMappings.Where(line => line.IsClosingTransfer).Should().HaveCount(4)
            .And.OnlyContain(line => line.ProfitAndLossRowKey == null);
        report.Artifacts.Single(artifact => artifact.Name == LedgerStatementControlPack.ArtifactName).Content.Should().Be(controls.Serialize());
    }

    [Fact]
    public void RejectsAnUnbalancedDimensionalSubsetBeforeGivingItCertifiedControls()
    {
        var ledger = new Meridian.Ledger.Ledger();
        ledger.PostLines(Start.AddDays(1), "Unbalanced perimeter",
        [
            (LedgerAccounts.Securities(LedgerSecurityKey.Unresolved("ABC")), 100m, 0m, new LedgerLineDimensionSet(EntityId: "account-a")),
            (LedgerAccounts.CapitalAccount, 0m, 100m, new LedgerLineDimensionSet(EntityId: "account-b"))
        ]);
        var request = Request(new LedgerLineDimensionSet(EntityId: "account-a")) with { StatementControl = Definition() };

        var build = () => LedgerReportPackBuilder.Build(ledger, request);

        build.Should().Throw<LedgerValidationException>().WithMessage("*does not reconcile*");
    }

    [Fact]
    public void AnnualPackSeparatesClosingTransfersAndKeepsFirstDayIncomeInAllExports()
    {
        var ledger = new Meridian.Ledger.Ledger();
        ledger.PostLines(Start.AddTicks(-1), "Opening capital",
            [(LedgerAccounts.Cash, 1000m, 0m), (LedgerAccounts.CapitalAccount, 0m, 1000m)]);
        ledger.PostLines(Start, "First day profit",
            [(LedgerAccounts.Cash, 100m, 0m), (LedgerAccounts.RealizedGain, 0m, 100m)]);
        var profit = ledger.Journal.Last().JournalEntryId;
        ledger.PostLines(End, "Close retained earnings",
            [(LedgerAccounts.RealizedGain, 100m, 0m), (LedgerAccounts.RetainedEarnings, 0m, 100m)]);
        var request = Request() with
        {
            StatementControl = Definition() with { FiscalYearCloseReceiptId = Guid.NewGuid() }
        };

        var report = LedgerReportPackBuilder.Build(ledger, request);

        report.Statements.NetIncome.Should().Be(0m);
        report.Statements.PeriodIncome!.NetIncome.Should().Be(100m);
        report.Statements.CashFlow!.BeginningCash.Should().Be(1000m);
        report.StatementControl!.Rows.Single(row => row.RowKey == "net-income").Amount.Should().Be(100m);
        report.StatementControl.Rows.Where(row => row.Statement == "Fiscal Close" && row.Label == "Retained Earnings")
            .Select(row => (row.Measure, row.Amount)).Should().Equal(
                ("Before closing transfers", 0m), ("Posted closing transfers", 100m),
                ("After closing transfers", 100m), ("Next-year opening", 100m));
        report.Artifacts.Single(item => item.Name == "income-statement.csv").Content.Should().Contain("Realized Gain");
        report.LineProvenance.Where(item => item.ArtifactName == "income-statement.csv")
            .Should().OnlyContain(item => item.LedgerJournalEntryIds.Count == 1 && item.LedgerJournalEntryIds[0] == profit);
    }

    private static LedgerReportPackRequest Request(LedgerLineDimensionSet? scope = null) =>
        new("report-1", "fund-a", "2026-07", Start, End, End, "USD", "controller", End, lineDimensions: scope);

    private static LedgerStatementControlDefinition Definition() => new(LedgerStatementControlDefinition.CurrentVersion, AccountingBasisKindDto.Gaap);
}
