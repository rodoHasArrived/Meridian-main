using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Parquet;

namespace Meridian.Tests.Storage;

public sealed class GeneralLedgerExportTests
{
    [Fact]
    public async Task Package_AllFormatsKeepExactAmountsAndOneSnapshot_WithSafeSpreadsheetText()
    {
        var (book, snapshot) = GeneralLedgerExportTestData.Create();
        var package = await GeneralLedgerExportBuilder.BuildAsync(book, snapshot);
        var files = ReadZip(package.Bytes);
        files.Keys.Should().BeEquivalentTo("general-ledger.v1.csv", "general-ledger.v1.xlsx", "general-ledger.v1.parquet",
            "symbol-pnl.v1.csv", "symbol-pnl.v1.json", "manifest.json");

        using var manifest = JsonDocument.Parse(files["manifest.json"]);
        var root = manifest.RootElement;
        root.GetProperty("schemaVersion").GetString().Should().Be("general-ledger.v1");
        root.GetProperty("snapshotSha256").GetString().Should().Be(package.SnapshotSha256);
        root.GetProperty("rowCount").GetInt32().Should().Be(2);
        root.GetProperty("debitTotal").GetString().Should().Be(GeneralLedgerExportTestData.Amount.ToString(CultureInfo.InvariantCulture));
        root.GetProperty("creditTotal").GetString().Should().Be(root.GetProperty("debitTotal").GetString());
        root.GetProperty("isExactlyBalanced").GetBoolean().Should().BeTrue();
        root.GetProperty("evidenceReferences").EnumerateArray().Select(value => value.GetString()).Should().Contain("evidence://synthetic-fill");
        foreach (var artifact in root.GetProperty("artifacts").EnumerateArray())
            Sha256Digest.Compute(files[artifact.GetProperty("name").GetString()!])
                .Should().Be(artifact.GetProperty("sha256").GetString());

        var csv = Encoding.UTF8.GetString(files["general-ledger.v1.csv"]);
        csv.Should().Contain(GeneralLedgerExportTestData.Amount.ToString(CultureInfo.InvariantCulture));
        csv.Should().Contain("'=untrusted()");
        csv.Should().Contain("explicit-line-currency");
        using (var workbook = new ZipArchive(new MemoryStream(files["general-ledger.v1.xlsx"])))
        {
            using var sheetStream = workbook.GetEntry("xl/worksheets/sheet1.xml")!.Open();
            var sheet = XDocument.Load(sheetStream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            sheet.Descendants(ns + "f").Should().BeEmpty();
            sheet.Descendants(ns + "t").Select(element => element.Value).Should()
                .Contain(GeneralLedgerExportTestData.Amount.ToString(CultureInfo.InvariantCulture)).And.Contain("=untrusted()");
        }

        using var parquet = await ParquetReader.CreateAsync(new MemoryStream(files["general-ledger.v1.parquet"]));
        parquet.CustomMetadata["snapshotSha256"].Should().Be(package.SnapshotSha256);
        using var group = parquet.OpenRowGroupReader(0);
        var debit = await group.ReadColumnAsync(parquet.Schema.GetDataFields().Single(field => field.Name == "Debit"));
        debit.Data.Cast<decimal?>().Sum().Should().Be(GeneralLedgerExportTestData.Amount);
        var description = await group.ReadColumnAsync(parquet.Schema.GetDataFields().Single(field => field.Name == "Description"));
        description.Data.Cast<string>().Should().AllBe("=untrusted()");
    }

    [Fact]
    public async Task Package_IsDeterministicAndFrozenAfterNewSnapshotArrives()
    {
        var (book, snapshot) = GeneralLedgerExportTestData.Create();
        var first = await GeneralLedgerExportBuilder.BuildAsync(book, snapshot);
        var replay = await GeneralLedgerExportBuilder.BuildAsync(book, snapshot);
        replay.Bytes.Should().Equal(first.Bytes);
        var later = snapshot with { Period = snapshot.Period! with { Version = snapshot.Period.Version + 1 } };
        var changed = await GeneralLedgerExportBuilder.BuildAsync(book, later);
        changed.SnapshotSha256.Should().NotBe(first.SnapshotSha256);
        GeneralLedgerExportBuilder.BuildDataset(book, snapshot).SnapshotSha256.Should().Be(first.SnapshotSha256);
    }

    [Fact]
    public async Task EmptyPeriod_HasZeroAgreementAndReadableParquetSchema()
    {
        var (book, snapshot) = GeneralLedgerExportTestData.Create();
        var package = await GeneralLedgerExportBuilder.BuildAsync(book, snapshot with { Journals = [] });
        package.RowCount.Should().Be(0);
        var files = ReadZip(package.Bytes);
        using var reader = await ParquetReader.CreateAsync(new MemoryStream(files["general-ledger.v1.parquet"]));
        reader.Schema.GetDataFields().Select(field => field.Name).Should().Contain("Debit");
        using var manifest = JsonDocument.Parse(files["manifest.json"]);
        manifest.RootElement.GetProperty("debitTotal").GetString().Should().Be("0");
    }

    [Fact]
    public void Dataset_RejectsWrongPeriodDuplicateJournalsAndForeignFunctionalCurrency()
    {
        var (book, snapshot) = GeneralLedgerExportTestData.Create();
        var wrongPeriod = snapshot with { Journals = [snapshot.Journals[0] with { PeriodId = Guid.NewGuid() }] };
        var duplicate = snapshot with { Journals = [snapshot.Journals[0], snapshot.Journals[0]] };
        var wrongBook = snapshot with { Period = snapshot.Period! with { LedgerBookId = Guid.NewGuid() } };
        foreach (var invalid in new[] { wrongPeriod, duplicate, wrongBook })
            ((Action)(() => GeneralLedgerExportBuilder.BuildDataset(book, invalid))).Should().Throw<InvalidOperationException>();
        ((Action)(() => GeneralLedgerExportBuilder.BuildDataset(book with { BaseCurrency = "EUR" }, snapshot)))
            .Should().Throw<InvalidOperationException>().WithMessage("*functional currency*");
    }

    [Fact]
    public async Task Package_CancellationReturnsNoPartialDownload()
    {
        var (book, snapshot) = GeneralLedgerExportTestData.Create();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await ((Func<Task>)(async () => await GeneralLedgerExportBuilder.BuildAsync(book, snapshot, cancellation.Token)))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    internal static Dictionary<string, byte[]> ReadZip(byte[] bytes)
    {
        using var archive = new ZipArchive(new MemoryStream(bytes));
        return archive.Entries.ToDictionary(entry => entry.FullName, entry =>
        {
            using var stream = entry.Open();
            using var output = new MemoryStream();
            stream.CopyTo(output);
            return output.ToArray();
        });
    }
}

internal static class GeneralLedgerExportTestData
{
    public const decimal Amount = 999999999999999999.123456789m;

    public static (LedgerBookRecord Book, LedgerReportingSnapshot Snapshot) Create()
    {
        var date = new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero);
        var book = new LedgerBookRecord(Guid.NewGuid(), "export-fund", Guid.NewGuid(), FundStructureNodeKindDto.Account,
            "Synthetic export book", "USD", date, date);
        var period = new LedgerAccountingPeriod(Guid.NewGuid(), book.LedgerBookId, 2026, 5, "May",
            new(2026, 5, 1), new(2026, 5, 31), "Open", date, null, 1);
        var journalId = Guid.NewGuid();
        var description = "=untrusted()";
        var journal = new JournalEntry(journalId, date, description,
        [
            new(Guid.NewGuid(), journalId, date, new("Assets:Cash", LedgerAccountType.Asset), Amount, 0m, description,
                currency: new("USD", "USD", Amount, 0m, 1m)),
            new(Guid.NewGuid(), journalId, date, new("Equity:Capital", LedgerAccountType.Equity), 0m, Amount, description,
                currency: new("USD", "USD", 0m, Amount, 1m))
        ], new JournalEntryMetadata(EvidenceReferences: [new("synthetic-fill", "evidence://synthetic-fill", "fill", "synthetic", date, "test")]));
        return (book, new([new(journal, Guid.NewGuid(), period.PeriodId, null, null, 1, date)], [], period));
    }
}
