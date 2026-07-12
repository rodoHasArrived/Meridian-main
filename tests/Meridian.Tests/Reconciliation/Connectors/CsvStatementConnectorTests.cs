using System.Text;
using FluentAssertions;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Xunit;

namespace Meridian.Tests.Reconciliation.Connectors;

public sealed class CsvStatementConnectorTests : IDisposable
{
    private readonly string _root = StatementConnectorTestData.CreateTempRoot("mdc_csv_connector");
    private readonly StatementMappingProfileCatalog _catalog;
    private readonly CsvStatementConnector _connector;

    public CsvStatementConnectorTests()
    {
        _catalog = new StatementMappingProfileCatalog(new FileStatementMappingProfileStore(_root));
        _connector = new CsvStatementConnector(_catalog);
    }

    [Fact]
    public async Task Parse_MixedKindStatement_ClassifiesEveryKind()
    {
        var document = new StatementSourceDocument(
            "csv-mixed-kinds.csv",
            StatementConnectorTestData.ReadFixture("csv-mixed-kinds.csv"));

        var result = await _connector.ParseAsync(document);

        result.HasErrors.Should().BeFalse();
        result.ProfileId.Should().Be(StatementMappingProfileRegistry.CanonicalCsvV1ProfileId);
        result.Records.Should().HaveCount(6);

        var byKind = result.Records.GroupBy(record => record.Kind).ToDictionary(group => group.Key, group => group.Count());
        byKind.Should().BeEquivalentTo(new Dictionary<StatementRecordKind, int>
        {
            [StatementRecordKind.Transaction] = 2,
            [StatementRecordKind.Position] = 1,
            [StatementRecordKind.CashBalance] = 1,
            [StatementRecordKind.Fee] = 1,
            [StatementRecordKind.Dividend] = 1
        });

        var buy = result.Records[0];
        buy.Account.Should().Be("FUND-A");
        buy.Symbol.Should().Be("AAPL");
        buy.Quantity.Should().Be(100m);
        buy.Price.Should().Be(187.25m);
        buy.CashAmount.Should().Be(-18726.05m);
        buy.ActivityType.Should().Be("trade");
        buy.TradeDate.Should().Be(new DateOnly(2026, 6, 2));
        buy.SettlementDate.Should().Be(new DateOnly(2026, 6, 4));
        buy.Currency.Should().Be("USD");
        buy.FeesCommission.Should().Be(1.05m);
        buy.ExternalTransactionId.Should().Be("T-1001");

        result.ColumnMappings.Should().OnlyContain(mapping => mapping.Confidence == StatementMappingConfidence.Exact);
    }

    [Fact]
    public async Task Parse_QuotedFieldsAndBom_ParseCleanly()
    {
        var document = new StatementSourceDocument(
            "csv-quoted-bom.csv",
            StatementConnectorTestData.ReadFixture("csv-quoted-bom.csv"));

        var result = await _connector.ParseAsync(document);

        result.HasErrors.Should().BeFalse();
        result.Records.Should().HaveCount(2);
        result.Records[0].Account.Should().Be("ACME, LLC Fund");
        result.Records[0].Quantity.Should().Be(1000m);
        result.Records[1].Kind.Should().Be(StatementRecordKind.Position);
        result.DetectedColumns[0].Should().Be("account", "the UTF-8 BOM must not corrupt the first header");
    }

    [Fact]
    public async Task Parse_SemicolonDelimitedAliasHeaders_SniffsDelimiterAndMapsWithAliasConfidence()
    {
        var document = new StatementSourceDocument(
            "csv-semicolon.csv",
            StatementConnectorTestData.ReadFixture("csv-semicolon.csv"));

        var result = await _connector.ParseAsync(document);

        result.HasErrors.Should().BeFalse();
        result.Fingerprint.Delimiter.Should().Be(";");
        result.Records.Should().HaveCount(2);
        result.Records[0].Kind.Should().Be(StatementRecordKind.Transaction);
        result.Records[0].TradeDate.Should().Be(new DateOnly(2026, 6, 3));
        result.Records[1].Kind.Should().Be(StatementRecordKind.Position);
        result.ColumnMappings.Should().OnlyContain(mapping =>
            mapping.Confidence == StatementMappingConfidence.Alias
            || mapping.Confidence == StatementMappingConfidence.Fuzzy);
    }

    [Fact]
    public async Task Parse_DriftedHeaders_StillMapThroughAliasesAndFuzzyMatching()
    {
        var document = new StatementSourceDocument(
            "csv-drifted-headers.csv",
            StatementConnectorTestData.ReadFixture("csv-drifted-headers.csv"));

        var result = await _connector.ParseAsync(document);

        result.HasErrors.Should().BeFalse();
        result.Records.Should().ContainSingle();
        result.Records[0].Symbol.Should().Be("SPY");
        result.ColumnMappings.Single(mapping => mapping.SourceColumn == "trade dt")
            .Confidence.Should().Be(StatementMappingConfidence.Fuzzy);
    }

    [Fact]
    public async Task Parse_UnknownActivityCode_WarnsOncePerCodeAndTreatsRowsAsTransactions()
    {
        var content = "account,symbol,quantity,price,cashAmount,activityType,tradeDate\n" +
                      "FUND-A,AAPL,1,10,-10,XYZ,2026-06-01\n" +
                      "FUND-A,MSFT,1,10,-10,XYZ,2026-06-02\n";
        var document = new StatementSourceDocument("unknown-codes.csv", Encoding.UTF8.GetBytes(content));

        var result = await _connector.ParseAsync(document);

        result.HasErrors.Should().BeFalse();
        result.Records.Should().HaveCount(2);
        result.Records.Should().OnlyContain(record => record.Kind == StatementRecordKind.Transaction);
        result.Issues.Where(issue => issue.Code == "UNKNOWN_ACTIVITY_CODE").Should().ContainSingle()
            .Which.Message.Should().Contain("XYZ");
    }

    [Fact]
    public async Task Parse_MissingRequiredColumn_ReportsBlockingError()
    {
        var content = "symbol,quantity,price,cashAmount,activityType,tradeDate\nAAPL,1,10,-10,trade,2026-06-01\n";
        var document = new StatementSourceDocument("missing-account.csv", Encoding.UTF8.GetBytes(content));

        var result = await _connector.ParseAsync(document);

        result.HasErrors.Should().BeTrue();
        result.Issues.Should().Contain(issue =>
            issue.Code == "MISSING_REQUIRED_COLUMN" && issue.Message.Contains("Account"));
        result.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task Parse_PositionRowWithoutSymbol_IsRejectedWithRowError()
    {
        var content = "account,symbol,quantity,price,cashAmount,activityType,tradeDate\n" +
                      "FUND-A,,100,10,1000,position,2026-06-01\n";
        var document = new StatementSourceDocument("position-no-symbol.csv", Encoding.UTF8.GetBytes(content));

        var result = await _connector.ParseAsync(document);

        result.HasErrors.Should().BeTrue();
        result.Issues.Should().Contain(issue => issue.Code == "POSITION_SYMBOL_MISSING" && issue.RowNumber == 2);
    }

    [Fact]
    public async Task Parse_UnknownProfile_ReportsProfileNotFound()
    {
        var document = new StatementSourceDocument(
            "csv-mixed-kinds.csv",
            StatementConnectorTestData.ReadFixture("csv-mixed-kinds.csv"),
            MappingProfileId: "does-not-exist");

        var result = await _connector.ParseAsync(document);

        result.HasErrors.Should().BeTrue();
        result.Issues.Should().Contain(issue => issue.Code == "PROFILE_NOT_FOUND");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
