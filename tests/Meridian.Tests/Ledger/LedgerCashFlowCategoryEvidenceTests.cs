using System.Text.Json;
using FluentAssertions;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

public sealed class LedgerCashFlowCategoryEvidenceTests
{
    [Theory]
    [InlineData("partial")]
    [InlineData("missing")]
    [InlineData("unknown")]
    [InlineData("mixed")]
    [InlineData("foreign")]
    [InlineData("version")]
    [InlineData("ungrouped")]
    public void InvalidRetainedActivityCategory_RefusesReporting(string defect)
    {
        var entry = Disposal();
        var tags = entry.Metadata.Tags!.ToDictionary(pair => pair.Key, pair => pair.Value);
        var keys = tags.Keys.Where(key => key.StartsWith("cash-flow.category.")).ToArray();
        switch (defect)
        {
            case "partial":
                tags.Remove(keys[0]);
                break;
            case "missing":
                foreach (var key in keys)
                    tags.Remove(key);
                break;
            case "unknown":
                tags[keys[0]] = "999";
                break;
            case "mixed":
                tags[keys[0]] = "Operating";
                break;
            case "foreign":
                tags["cash-flow.category.v1." + Guid.NewGuid()] = "Investing";
                break;
            case "version":
                tags[keys[0].Replace(".v1.", ".v2.")] = tags[keys[0]];
                tags.Remove(keys[0]);
                break;
            case "ungrouped":
                foreach (var key in tags.Keys.Where(key => key.StartsWith("cash-flow.group.")).ToArray())
                    tags.Remove(key);
                break;
        }
        var ledger = new Meridian.Ledger.Ledger();
        ledger.Post(new JournalEntry(entry.JournalEntryId, entry.Timestamp, entry.Description, entry.Lines,
            entry.Metadata with { Tags = tags }));

        var report = () => LedgerFinancialStatementBuilder.BuildForPeriod(ledger,
            CanonicalCashFlowTestData.Start, CanonicalCashFlowTestData.Start.AddYears(1));

        report.Should().Throw<LedgerValidationException>().WithMessage("*invalid cash-flow attribution*");
    }

    [Fact]
    public void SerializedReorderedActivity_RetainsCategoryAndRemapsItsReversal()
    {
        var source = Disposal();
        var metadata = JsonSerializer.Deserialize<JournalEntryMetadata>(JsonSerializer.Serialize(source.Metadata))!;
        var entry = new JournalEntry(source.JournalEntryId, source.Timestamp, source.Description,
            source.Lines.Reverse().ToArray(), metadata);
        var ledger = new Meridian.Ledger.Ledger();
        ledger.Post(entry);

        CanonicalCashFlowTestData.AssertReportAndReversal(ledger, entry, 100m);
    }

    [Fact]
    public void ClassifiedActivity_CannotHideABalancedPartialAccountScope()
    {
        var timestamp = CanonicalCashFlowTestData.Start.AddDays(1);
        var id = Guid.NewGuid();
        var entry = LedgerCashFlowAttribution.WithClassifiedActivity(new JournalEntry(id, timestamp, "two owners",
            [new(Guid.NewGuid(), id, timestamp, LedgerAccounts.CashAccount("a"), 100m, 0m, "two owners"),
             new(Guid.NewGuid(), id, timestamp, LedgerAccounts.Securities("AAPL", "a"), 0m, 100m, "two owners"),
             new(Guid.NewGuid(), id, timestamp, LedgerAccounts.CashAccount("b"), 50m, 0m, "two owners"),
             new(Guid.NewGuid(), id, timestamp, LedgerAccounts.Securities("AAPL", "b"), 0m, 50m, "two owners")]),
            LedgerCashFlowCategory.Investing, "disposal");
        var ledger = new Meridian.Ledger.Ledger();
        ledger.Post(entry);

        var report = () => LedgerFinancialStatementBuilder.BuildForPeriod(ledger,
            CanonicalCashFlowTestData.Start, timestamp, financialAccountId: "a");

        report.Should().Throw<LedgerValidationException>().WithMessage("*report scope*");
    }

    private static JournalEntry Disposal()
    {
        var projection = LedgerTaxLotReliefProjector.Project(new LedgerTaxLotReliefInput(LedgerAccounts.Securities("AAPL"),
            new(2026, 1, 2), 1m, 100m, LedgerTaxLotReliefMethod.Fifo,
            [new LedgerTaxLot("lot", new(2025, 1, 1), 1m, 110m)]));
        return projection.ToJournalEntry(Guid.NewGuid(), CanonicalCashFlowTestData.Start.AddDays(1), "Disposal");
    }
}
