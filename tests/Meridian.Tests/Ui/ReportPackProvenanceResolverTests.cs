using FluentAssertions;
using Meridian.Ledger;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed class ReportPackProvenanceResolverTests
{
    [Fact]
    public void ResolveDerivedToken_JournalEntries_UsesRetainedPostingProvenance()
    {
        var journalEntryId = Guid.NewGuid();
        var timestamp = DateTimeOffset.UtcNow;
        const string description = "seeded posting";
        var entry = new JournalEntry(
            journalEntryId,
            timestamp,
            description,
            [
                new LedgerEntry(
                    Guid.NewGuid(), journalEntryId, timestamp,
                    new LedgerAccount("Assets:Cash", LedgerAccountType.Asset),
                    100m, 0m, description),
                new LedgerEntry(
                    Guid.NewGuid(), journalEntryId, timestamp,
                    new LedgerAccount("Equity:Capital", LedgerAccountType.Equity),
                    0m, 100m, description)
            ],
            new JournalEntryMetadata(Tags: new Dictionary<string, string>
            {
                ["dataProvenance"] = "SEEDED"
            }));

        var token = ReportPackProvenanceResolver.ResolveDerivedToken([entry]);

        token.Should().Be("seeded");
    }

    [Fact]
    public void ResolveDerivedToken_JournalEntries_IgnoresRealInputs()
    {
        var journalEntryId = Guid.NewGuid();
        var timestamp = DateTimeOffset.UtcNow;
        const string description = "real posting";
        var entry = new JournalEntry(
            journalEntryId,
            timestamp,
            description,
            [
                new LedgerEntry(
                    Guid.NewGuid(), journalEntryId, timestamp,
                    new LedgerAccount("Assets:Cash", LedgerAccountType.Asset),
                    100m, 0m, description),
                new LedgerEntry(
                    Guid.NewGuid(), journalEntryId, timestamp,
                    new LedgerAccount("Equity:Capital", LedgerAccountType.Equity),
                    0m, 100m, description)
            ],
            new JournalEntryMetadata(Tags: new Dictionary<string, string>
            {
                ["dataProvenance"] = "real"
            }));

        ReportPackProvenanceResolver.ResolveDerivedToken([entry]).Should().BeNull();
    }
}
