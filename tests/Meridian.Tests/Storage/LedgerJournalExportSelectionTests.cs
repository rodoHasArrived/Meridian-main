using System.Text;
using FluentAssertions;
using Meridian.Ledger;
using Meridian.Storage.Export;
using Meridian.Storage.Ledger;
using Xunit;

namespace Meridian.Tests.Storage;

public sealed class LedgerJournalExportSelectionTests
{
    [Fact]
    public void SelectionRetainsWholeBalancedJournalsAndIndependentSourceDigest()
    {
        var (book, snapshot) = GeneralLedgerExportTestData.Create();
        var period = snapshot.Period!;
        var selected = LedgerJournalExportSelection.Select(snapshot, book.LedgerBookId, period.PeriodId,
            period.StartDate, period.EndDate, [snapshot.Journals[0].Entry.JournalEntryId]);
        selected.Journals.Should().ContainSingle();
        selected.Journals[0].Entry.Lines.Should().HaveCount(2);
        selected.SnapshotSha256.Should().HaveLength(64);
        var mapped = LedgerJournalExporter.ExportCsv(selected.Journals,
            new Dictionary<string, string> { ["Assets:Cash"] = "1000", ["Equity:Capital"] = "3000" }, book.LedgerBookId);
        Encoding.UTF8.GetString(mapped).Should().Contain("1000").And.Contain("3000");
        LedgerJournalExportSelection.Select(snapshot, book.LedgerBookId, period.PeriodId,
            period.StartDate, period.EndDate).SnapshotSha256.Should().Be(selected.SnapshotSha256);
    }

    [Fact]
    public void MissingOrOutOfDateIdsRejectEntireSelection()
    {
        var (book, snapshot) = GeneralLedgerExportTestData.Create();
        var period = snapshot.Period!;
        Action missing = () => LedgerJournalExportSelection.Select(snapshot, book.LedgerBookId, period.PeriodId,
            period.StartDate, period.EndDate, [snapshot.Journals[0].Entry.JournalEntryId, Guid.NewGuid()]);
        missing.Should().Throw<ArgumentException>();
        Action outside = () => LedgerJournalExportSelection.Select(snapshot, book.LedgerBookId, period.PeriodId,
            period.StartDate, period.StartDate, [snapshot.Journals[0].Entry.JournalEntryId]);
        outside.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ForeignPeriodPopulationCannotLeakThroughSelection()
    {
        var (book, snapshot) = GeneralLedgerExportTestData.Create();
        var period = snapshot.Period!;
        var bad = snapshot with { Journals = [snapshot.Journals[0] with { PeriodId = Guid.NewGuid() }] };
        Action export = () => LedgerJournalExportSelection.Select(bad, book.LedgerBookId, period.PeriodId,
            period.StartDate, period.EndDate);
        export.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AccountingDateOverridesLatePostingTimestampAndMissingDateUsesUtc()
    {
        var (book, snapshot) = GeneralLedgerExportTestData.Create();
        var record = snapshot.Journals[0];
        var date = new DateOnly(2026, 5, 20);
        var late = Rebuild(record, record.Entry.Description, record.Entry.Metadata with { EffectiveDate = date },
            DateTimeOffset.Parse("2026-06-02T01:02:03Z"));
        var selected = LedgerJournalExportSelection.Select(snapshot with { Journals = [late] }, book.LedgerBookId,
            snapshot.Period!.PeriodId, date, date);
        selected.Journals.Should().ContainSingle();
        LedgerJournalExportSelection.Select(snapshot, book.LedgerBookId, snapshot.Period.PeriodId, date, date)
            .Journals.Should().ContainSingle();
    }

    [Fact]
    public void DefaultSelectionRetainsPeriodMembershipForEarlierEffectiveDates()
    {
        var (book, snapshot) = GeneralLedgerExportTestData.Create();
        var period = snapshot.Period!;
        var record = snapshot.Journals[0];
        var late = Rebuild(record, record.Entry.Description,
            record.Entry.Metadata with { EffectiveDate = period.StartDate.AddDays(-1) }, record.Entry.Timestamp);
        snapshot = snapshot with { Journals = [late] };

        var complete = LedgerJournalExportSelection.Select(snapshot, book.LedgerBookId, period.PeriodId);
        complete.Journals.Select(item => item.Entry.JournalEntryId).Should().Equal(late.Entry.JournalEntryId);
        complete.Journals.SelectMany(item => item.Entry.Lines).Sum(line => line.Debit).Should().Be(GeneralLedgerExportTestData.Amount);
        complete.Journals.SelectMany(item => item.Entry.Lines).Sum(line => line.Credit).Should().Be(GeneralLedgerExportTestData.Amount);
        LedgerJournalExportSelection.Select(snapshot, book.LedgerBookId, period.PeriodId,
            journalEntryIds: [late.Entry.JournalEntryId]).SnapshotSha256.Should().Be(complete.SnapshotSha256);

        LedgerJournalExportSelection.Select(snapshot, book.LedgerBookId, period.PeriodId,
            period.StartDate, period.EndDate).Journals.Should().BeEmpty();
        Action selectedOutsideWindow = () => LedgerJournalExportSelection.Select(snapshot, book.LedgerBookId,
            period.PeriodId, period.StartDate, period.EndDate, [late.Entry.JournalEntryId]);
        selectedOutsideWindow.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OneExplicitDateBoundStillUsesThePeriodBoundaryForTheOther(bool supplyFrom)
    {
        var (book, snapshot) = GeneralLedgerExportTestData.Create();
        var period = snapshot.Period!;
        var record = snapshot.Journals[0];
        var effectiveDate = supplyFrom ? period.EndDate.AddDays(1) : period.StartDate.AddDays(-1);
        snapshot = snapshot with
        {
            Journals = [Rebuild(record, record.Entry.Description,
            record.Entry.Metadata with { EffectiveDate = effectiveDate }, record.Entry.Timestamp)]
        };

        LedgerJournalExportSelection.Select(snapshot, book.LedgerBookId, period.PeriodId,
            supplyFrom ? period.StartDate : null, supplyFrom ? null : period.EndDate).Journals.Should().BeEmpty();
    }

    [Fact]
    public void DigestDistinguishesPeriodMembershipFromExplicitDateWindow()
    {
        var (book, snapshot) = GeneralLedgerExportTestData.Create();
        var period = snapshot.Period!;
        var complete = LedgerJournalExportSelection.Select(snapshot, book.LedgerBookId, period.PeriodId);
        var window = LedgerJournalExportSelection.Select(snapshot, book.LedgerBookId, period.PeriodId,
            period.StartDate, period.EndDate);

        complete.Journals.Should().Equal(window.Journals);
        complete.SnapshotSha256.Should().NotBe(window.SnapshotSha256);
    }

    [Fact]
    public void DigestBindsOriginalTextEvenWhenFormulaEscapingProducesIdenticalCells()
    {
        var (book, snapshot) = GeneralLedgerExportTestData.Create();
        var record = snapshot.Journals[0];
        var changed = Rebuild(record, "'" + record.Entry.Description, record.Entry.Metadata, record.Entry.Timestamp);
        LedgerJournalExporter.ExportCsv([record]).Should().Equal(LedgerJournalExporter.ExportCsv([changed]));
        var period = snapshot.Period!;
        var original = LedgerJournalExportSelection.Select(snapshot, book.LedgerBookId, period.PeriodId, period.StartDate, period.EndDate);
        var modified = LedgerJournalExportSelection.Select(snapshot with { Journals = [changed] }, book.LedgerBookId,
            period.PeriodId, period.StartDate, period.EndDate);
        original.SnapshotSha256.Should().NotBe(modified.SnapshotSha256);
    }

    [Fact]
    public void DigestCanonicalizesTagOrderAndCsvUsesPortableLineEndings()
    {
        var (book, snapshot) = GeneralLedgerExportTestData.Create();
        var record = snapshot.Journals[0];
        var first = Rebuild(record, record.Entry.Description, record.Entry.Metadata with
        { Tags = new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" } }, record.Entry.Timestamp);
        var second = Rebuild(record, record.Entry.Description, record.Entry.Metadata with
        { Tags = new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" } }, record.Entry.Timestamp);
        var period = snapshot.Period!;
        LedgerJournalExportSelection.Select(snapshot with { Journals = [first] }, book.LedgerBookId,
            period.PeriodId, period.StartDate, period.EndDate).SnapshotSha256.Should().Be(
                LedgerJournalExportSelection.Select(snapshot with { Journals = [second] }, book.LedgerBookId,
                    period.PeriodId, period.StartDate, period.EndDate).SnapshotSha256);
        var csv = Encoding.UTF8.GetString(LedgerJournalExporter.ExportCsv([record]));
        csv.Should().EndWith("\r\n");
        csv.Replace("\r\n", "").Should().NotContain("\n");
    }

    private static LedgerJournalEntryRecord Rebuild(LedgerJournalEntryRecord record, string description,
        JournalEntryMetadata metadata, DateTimeOffset timestamp)
        => record with
        {
            Entry = new JournalEntry(record.Entry.JournalEntryId, timestamp, description,
            record.Entry.Lines.Select(line => new LedgerEntry(line.EntryId, line.JournalEntryId, timestamp,
                line.Account, line.Debit, line.Credit, description, line.Dimensions, line.Currency)).ToArray(), metadata)
        };
}
