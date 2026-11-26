using System.Globalization;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Ledger;
using Meridian.Storage.Export;
using Meridian.Storage.Ledger;
using Parquet;
using Xunit;

namespace Meridian.Tests.Storage;

public sealed class LedgerJournalExporterTests
{
    [Fact]
    public void Csv_PreservesAllLinesMetadataAndMapping_IndependentlyOfLocale()
    {
        var ledger = BuildLedger();
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var csv = Encoding.UTF8.GetString(LedgerJournalExporter.ExportCsv(ledger,
                new Dictionary<string, string> { ["Securities"] = "1200", ["Cash"] = "1000" }));
            using var parser = new Microsoft.VisualBasic.FileIO.TextFieldParser(new StringReader(csv))
            { TextFieldType = Microsoft.VisualBasic.FileIO.FieldType.Delimited, HasFieldsEnclosedInQuotes = true };
            parser.SetDelimiters(",");
            var headers = parser.ReadFields()!;
            var rows = new List<Dictionary<string, string>>();
            while (!parser.EndOfData)
            {
                var values = parser.ReadFields()!;
                rows.Add(headers.Zip(values).ToDictionary(pair => pair.First, pair => pair.Second));
            }
            rows.Should().HaveCount(2);
            rows[0]["debit"].Should().Be("123.1234567890123456789");
            rows[0]["external_account"].Should().Be("1200");
            rows[0]["account_name"].Should().Be("Securities");
            rows[0]["description"].Should().Be("'=dangerous, \"quoted\"\nline");
            using var metadata = JsonDocument.Parse(rows[0]["metadata_json"]);
            metadata.RootElement.GetProperty("tags").GetProperty("source").GetString().Should().Be("feed,version=2");
            metadata.RootElement.GetProperty("fillId").GetGuid().Should().Be(ledger.Journal[0].Metadata.FillId!.Value);
            using var currency = JsonDocument.Parse(rows[0]["currency_json"]);
            currency.RootElement.GetProperty("transactionCurrency").GetString().Should().Be("USD");
            rows[0]["dimensions_json"].Should().Contain("portfolio-alpha");
            ledger.Journal[0].Lines[0].Account.Name.Should().Be("Securities");
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public async Task Parquet_IsReadableAndRetainsExactDecimalAndAuditMetadata()
    {
        var ledger = BuildLedger();
        var bytes = await LedgerJournalExporter.ExportParquetAsync(ledger);
        using var stream = new MemoryStream(bytes);
        using var reader = await ParquetReader.CreateAsync(stream);
        reader.RowGroupCount.Should().Be(1);
        using var group = reader.OpenRowGroupReader(0);
        group.RowCount.Should().Be(2);
        var fields = reader.Schema.GetDataFields();
        var debit = await group.ReadColumnAsync(fields.Single(field => field.Name == "debit"));
        ((string[])debit.Data)[0].Should().Be("123.1234567890123456789");
        var metadata = await group.ReadColumnAsync(fields.Single(field => field.Name == "metadata_json"));
        ((string[])metadata.Data).Should().OnlyContain(value => value.Contains("feed,version=2", StringComparison.Ordinal));
    }

    [Fact]
    public void PostedExport_RetainsPostingSourceAndApprovalEnvelope()
    {
        var ledger = BuildLedger();
        var eventId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var record = new LedgerJournalEntryRecord(ledger.Journal[0], Guid.NewGuid(), periodId,
            Guid.NewGuid(), Guid.NewGuid(), 42, ledger.Journal[0].Timestamp,
            AccountingPolicyId: "primary-policy", AccountingPolicyVersion: "v2",
            RuleId: "trade-fill", RuleVersion: "v3", SourceEventId: eventId);
        var csv = Encoding.UTF8.GetString(LedgerJournalExporter.ExportCsv(new[] { record }));
        csv.Should().Contain("posting_metadata_json").And.Contain("primary-policy")
            .And.Contain("trade-fill").And.Contain(eventId.ToString("D")).And.Contain(periodId.ToString("D"));
    }

    [Fact]
    public async Task Export_RejectsPartialMappingsAndSupportsEmptyParquet()
    {
        var mapping = new Dictionary<string, string> { ["Cash"] = "1000" };
        var csv = () => LedgerJournalExporter.ExportCsv(BuildLedger(), mapping);
        csv.Should().Throw<ArgumentException>().WithMessage("*Securities*");
        await Assert.ThrowsAsync<ArgumentException>(() => LedgerJournalExporter.ExportParquetAsync(BuildLedger(), mapping));
        var empty = await LedgerJournalExporter.ExportParquetAsync(new Meridian.Ledger.Ledger());
        using var stream = new MemoryStream(empty);
        using var reader = await ParquetReader.CreateAsync(stream);
        reader.RowGroupCount.Should().Be(0);
        reader.Schema.GetDataFields().Should().Contain(field => field.Name == "metadata_json");
    }

    private static Meridian.Ledger.Ledger BuildLedger()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var time = DateTimeOffset.Parse("2026-11-12T12:00:00Z", CultureInfo.InvariantCulture);
        const decimal amount = 123.1234567890123456789m;
        ledger.PostLines(time, "=dangerous, \"quoted\"\nline",
        [
            (new LedgerAccount("Securities", LedgerAccountType.Asset, "AAPL", "broker"), amount, 0m,
                new LedgerLineDimensionSet(PortfolioId: "portfolio-alpha"), new LedgerEntryCurrency("USD", "USD", amount, 0m, 1m)),
            (new LedgerAccount("Cash", LedgerAccountType.Asset, FinancialAccountId: "broker"), 0m, amount,
                (LedgerLineDimensionSet?)null, new LedgerEntryCurrency("USD", "USD", 0m, amount, 1m))
        ], new JournalEntryMetadata(StrategyId: "alpha", OrderId: Guid.NewGuid(), FillId: Guid.NewGuid(),
            Tags: new Dictionary<string, string> { ["source"] = "feed,version=2" }));
        return ledger;
    }
}
