using System.Text.Json;
using FluentAssertions;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

public sealed class LedgerCashFlowAttributionEvidenceTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
    private static readonly LedgerAccount Reserve = new("Cash reserve", LedgerAccountType.Asset);
    private static readonly LedgerAccount Expense = new("Expense", LedgerAccountType.Expense);
    private static readonly LedgerAccount Payable = new("Payable", LedgerAccountType.Liability);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AttributedTransfer_ExcludesIndependentAccrualOrReclassification(bool reclassification)
    {
        var ledger = OpeningLedger();
        LedgerCashFlowTestData.Post(ledger, Start.AddDays(1),
            [(LedgerAccounts.Cash, 100m, 0m, null), (Reserve, 0m, 100m, null),
             (reclassification ? LedgerAccounts.Securities("AAPL") : Expense, 25m, 0m, null),
             (Payable, 0m, 25m, null)], "transfer", "transfer", "noncash", "noncash");

        var cashFlow = Build(ledger);

        AssertCash(cashFlow, 0m, 0m, 0m, 500m);
        cashFlow.Lines.Should().BeEmpty();
    }

    [Fact]
    public void NonzeroCashBatchedWithAccrual_AlsoRequiresAttribution()
    {
        var ledger = OpeningLedger();
        ledger.PostLines(Start.AddDays(1), "deposit and accrual",
            [(LedgerAccounts.Cash, 100m, 0m), (LedgerAccounts.RetainedEarnings, 0m, 100m),
             (Expense, 25m, 0m), (Payable, 0m, 25m)]);

        var report = () => Build(ledger);

        report.Should().Throw<LedgerValidationException>().WithMessage("*cash-flow attribution*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UntaggedPureTransferOrNoncashJournal_DoesNotCreateFlows(bool transfer)
    {
        var ledger = OpeningLedger();
        ledger.PostLines(Start.AddDays(1), "independent activity",
            [(transfer ? LedgerAccounts.Cash : Expense, 25m, 0m),
             (transfer ? Reserve : Payable, 0m, 25m)]);

        var cashFlow = Build(ledger);

        AssertCash(cashFlow, 0m, 0m, 0m, 500m);
        cashFlow.Lines.Should().BeEmpty();
    }

    [Fact]
    public void FinancialAccountAndDimensionScope_ExcludeOtherActivities()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var fundA = new LedgerLineDimensionSet(FundId: "fund-a");
        var fundB = new LedgerLineDimensionSet(FundId: "fund-b");
        LedgerCashFlowTestData.Post(ledger, Start.AddDays(1),
            [(new LedgerAccount("Cash A", LedgerAccountType.Asset, FinancialAccountId: "account-a"), 100m, 0m, fundA),
             (new LedgerAccount("Capital A", LedgerAccountType.Equity, FinancialAccountId: "account-a"), 0m, 100m, fundA),
             (new LedgerAccount("Expense B", LedgerAccountType.Expense, FinancialAccountId: "account-b"), 25m, 0m, fundB),
             (new LedgerAccount("Payable B", LedgerAccountType.Liability, FinancialAccountId: "account-b"), 0m, 25m, fundB)],
            "receipt-a", "receipt-a", "accrual-b", "accrual-b");

        foreach (var account in new string?[] { null, " ACCOUNT-A " })
        {
            var cf = LedgerFinancialStatementBuilder.BuildForPeriod(ledger, Start, Start.AddMonths(1),
                financialAccountId: account, lineDimensions: fundA).CashFlow!;
            cf.FinancingCashFlow.Should().Be(100m);
            cf.OperatingCashFlow.Should().Be(0m);
            cf.BeginningCash.Should().Be(0m);
            cf.EndingCash.Should().Be(100m);
            cf.ReconciliationVariance.Should().Be(0m);
            cf.Lines.Should().ContainSingle().Which.Description.Should().Be("Capital A");
        }

        Build(ledger).FinancingCashFlow.Should().Be(100m);
        var other = LedgerFinancialStatementBuilder.BuildForPeriod(ledger, Start, Start.AddMonths(1),
            financialAccountId: "account-b").CashFlow!;
        other.Lines.Should().BeEmpty();
        other.ReconciliationVariance.Should().Be(0m);
    }

    [Fact]
    public void ScopeThatCutsCashFromItsCounterpart_FailsBeforeReporting()
    {
        var ledger = new Meridian.Ledger.Ledger();
        LedgerCashFlowTestData.Post(ledger, Start.AddDays(1),
            [(new LedgerAccount("Cash", LedgerAccountType.Asset, FinancialAccountId: "account-a"), 100m, 0m, null),
             (new LedgerAccount("Capital", LedgerAccountType.Equity, FinancialAccountId: "account-b"), 0m, 100m, null)],
            "receipt", "receipt");

        var report = () => LedgerFinancialStatementBuilder.BuildForPeriod(ledger, Start, Start.AddMonths(1),
            financialAccountId: "account-a");

        report.Should().Throw<LedgerValidationException>().WithMessage("*report scope*");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("foreign")]
    [InlineData("version")]
    [InlineData("unbalanced")]
    [InlineData("empty")]
    public void MalformedRetainedGroups_AreRejected(string defect)
    {
        var source = new Meridian.Ledger.Ledger();
        var entry = LedgerCashFlowTestData.Post(source, Start.AddDays(1),
            [(LedgerAccounts.Cash, 100m, 0m, null), (LedgerAccounts.RetainedEarnings, 0m, 100m, null)],
            "receipt", "receipt");
        var tags = entry.Metadata.Tags!.ToDictionary(pair => pair.Key, pair => pair.Value);
        var key = tags.Keys.First();
        switch (defect)
        {
            case "missing":
                tags.Remove(key);
                break;
            case "foreign":
                tags.Remove(key);
                tags["cash-flow.group.v1." + Guid.NewGuid()] = "receipt";
                break;
            case "version":
                tags.Remove(key);
                tags[key.Replace(".v1.", ".v2.")] = "receipt";
                break;
            case "unbalanced":
                tags[key] = "separate";
                break;
            case "empty":
                tags[key] = " ";
                break;
        }
        var ledger = new Meridian.Ledger.Ledger();
        ledger.Post(new JournalEntry(entry.JournalEntryId, entry.Timestamp, entry.Description, entry.Lines,
            entry.Metadata with { Tags = tags }));

        var report = () => Build(ledger);

        report.Should().Throw<LedgerValidationException>().WithMessage("*invalid cash-flow attribution*");
    }

    [Fact]
    public void RetainedMetadataAndReorderedLines_PreserveAttributionAndReversal()
    {
        var source = new Meridian.Ledger.Ledger();
        var entry = LedgerCashFlowTestData.Post(source, Start.AddDays(1),
            [(LedgerAccounts.Cash, 100m, 0m, null), (LedgerAccounts.RetainedEarnings, 0m, 100m, null),
             (Expense, 100m, 0m, null), (LedgerAccounts.Cash, 0m, 100m, null),
             (Expense, 25m, 0m, null), (Payable, 0m, 25m, null)],
            "receipt", "receipt", "payment", "payment", "accrual", "accrual");
        var metadata = JsonSerializer.Deserialize<JournalEntryMetadata>(JsonSerializer.Serialize(entry.Metadata))!;
        var reordered = new JournalEntry(entry.JournalEntryId, entry.Timestamp, entry.Description,
            entry.Lines.Reverse().ToArray(), metadata);
        var ledger = OpeningLedger();
        ledger.Post(reordered);
        AssertCash(Build(ledger), -100m, 0m, 100m, 500m);

        var reversal = LedgerJournalReversal.Reverse(reordered, Guid.NewGuid(), Start.AddDays(2));
        ledger.Post(reversal);

        AssertCash(Build(ledger), 0m, 0m, 0m, 500m);
        Build(ledger).Lines.Should().BeEmpty();
    }

    private static LedgerCashFlowStatement Build(Meridian.Ledger.Ledger ledger)
        => LedgerFinancialStatementBuilder.BuildForPeriod(ledger, Start, Start.AddMonths(1)).CashFlow!;

    private static Meridian.Ledger.Ledger OpeningLedger()
    {
        var ledger = new Meridian.Ledger.Ledger();
        ledger.PostLines(Start, "opening cash",
            [(LedgerAccounts.Cash, 500m, 0m), (LedgerAccounts.RetainedEarnings, 0m, 500m)]);
        return ledger;
    }

    private static void AssertCash(LedgerCashFlowStatement cf, decimal operating, decimal investing,
        decimal financing, decimal ending)
    {
        cf.OperatingCashFlow.Should().Be(operating);
        cf.InvestingCashFlow.Should().Be(investing);
        cf.FinancingCashFlow.Should().Be(financing);
        cf.BeginningCash.Should().Be(500m);
        cf.EndingCash.Should().Be(ending);
        cf.NetCashFlow.Should().Be(ending - cf.BeginningCash);
        cf.Lines.Sum(line => line.Amount).Should().Be(cf.NetCashFlow);
        cf.ReconciliationVariance.Should().Be(0m);
    }
}
