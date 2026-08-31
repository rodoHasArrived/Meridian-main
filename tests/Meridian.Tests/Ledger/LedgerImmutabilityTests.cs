using FluentAssertions;
using Meridian.FSharp.Ledger;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

public sealed class LedgerImmutabilityTests
{
    [Fact]
    public void JournalEntry_CopiesCallerOwnedLinesAndMetadataCollections()
    {
        var journalId = Guid.NewGuid();
        var timestamp = DateTimeOffset.UtcNow;
        var description = "immutable posting";
        var lines = new List<LedgerEntry>
        {
            new(Guid.NewGuid(), journalId, timestamp, new LedgerAccount("Cash", LedgerAccountType.Asset), 100m, 0m, description),
            new(Guid.NewGuid(), journalId, timestamp, new LedgerAccount("Revenue", LedgerAccountType.Revenue), 0m, 100m, description),
        };
        var tags = new Dictionary<string, string> { ["source"] = "statement" };
        var evidence = new List<JournalEvidenceReference>
        {
            new(
                "evidence-1",
                "vault://evidence-1",
                "statement",
                "unit-test",
                timestamp,
                "unit-test"),
        };

        var entry = new JournalEntry(
            journalId,
            timestamp,
            description,
            lines,
            new JournalEntryMetadata(Tags: tags, EvidenceReferences: evidence));

        lines.Clear();
        tags["source"] = "mutated";
        evidence.Clear();

        entry.Lines.Should().HaveCount(2);
        entry.Metadata.Tags.Should().Contain("source", "statement");
        entry.Metadata.EvidenceReferences.Should().ContainSingle();
        ((IList<LedgerEntry>)entry.Lines).Invoking(collection => collection.Clear())
            .Should().Throw<NotSupportedException>();
        ((IDictionary<string, string>)entry.Metadata.Tags!).Invoking(collection => collection.Clear())
            .Should().Throw<NotSupportedException>();
        ((IList<JournalEvidenceReference>)entry.Metadata.EvidenceReferences).Invoking(collection => collection.Clear())
            .Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void JournalEntry_ValidatesAndStoresTheSameFrozenLineSnapshot()
    {
        var journalId = Guid.NewGuid();
        var timestamp = DateTimeOffset.UtcNow;
        var description = "single snapshot posting";
        LedgerEntry[] validatedLines =
        [
            new(Guid.NewGuid(), journalId, timestamp, new LedgerAccount("Cash", LedgerAccountType.Asset), 100m, 0m, description),
            new(Guid.NewGuid(), journalId, timestamp, new LedgerAccount("Revenue", LedgerAccountType.Revenue), 0m, 100m, description),
        ];
        LedgerEntry[] unvalidatedLines =
        [
            new(Guid.NewGuid(), Guid.NewGuid(), timestamp, new LedgerAccount("Other", LedgerAccountType.Asset), 1m, 0m, "different posting"),
        ];
        var callerOwnedLines = new SuccessiveEnumerationReadOnlyList<LedgerEntry>(validatedLines, unvalidatedLines);

        var entry = new JournalEntry(journalId, timestamp, description, callerOwnedLines);

        callerOwnedLines.EnumerationCount.Should().Be(1);
        entry.Lines.Should().Equal(validatedLines);
    }

    [Fact]
    public void Ledger_JournalExposesAReadOnlyLiveViewInsteadOfItsBackingList()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var journalView = ledger.Journal;
        var timestamp = DateTimeOffset.UtcNow;

        ledger.PostLines(
            timestamp,
            "sale",
            new[]
            {
                (new LedgerAccount("Cash", LedgerAccountType.Asset), 100m, 0m),
                (new LedgerAccount("Revenue", LedgerAccountType.Revenue), 0m, 100m),
            });

        journalView.Should().ContainSingle();
        journalView.Should().NotBeAssignableTo<List<JournalEntry>>();
        ((IList<JournalEntry>)journalView).Invoking(collection => collection.Clear())
            .Should().Throw<NotSupportedException>();
        ledger.JournalEntryCount.Should().Be(1);
    }

    private sealed class SuccessiveEnumerationReadOnlyList<T>(
        IReadOnlyList<T> firstEnumeration,
        IReadOnlyList<T> subsequentEnumerations) : IReadOnlyList<T>
    {
        private int _enumerationCount;

        public int EnumerationCount => _enumerationCount;

        public int Count => firstEnumeration.Count;

        public T this[int index] => firstEnumeration[index];

        public IEnumerator<T> GetEnumerator()
        {
            _enumerationCount++;
            return (_enumerationCount == 1 ? firstEnumeration : subsequentEnumerations).GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
