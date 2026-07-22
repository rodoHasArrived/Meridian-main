using FluentAssertions;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.FinancialOperations.Reconciliation.Connectors.IbFlex;
using Xunit;

namespace Meridian.Tests.Reconciliation.Connectors;

public sealed class IbFlexStatementConnectorTests : IDisposable
{
    private readonly string _root = StatementConnectorTestData.CreateTempRoot("mdc_ibflex_connector");
    private readonly IbFlexStatementConnector _connector;

    public IbFlexStatementConnectorTests()
    {
        _connector = new IbFlexStatementConnector(
            new StatementMappingProfileCatalog(new FileStatementMappingProfileStore(_root)));
    }

    [Fact]
    public async Task Parse_FlexReport_YieldsTradesCashActivityAndPositions()
    {
        var document = new StatementSourceDocument(
            "ib-flex-sample.xml",
            StatementConnectorTestData.ReadFixture("ib-flex-sample.xml"));

        _connector.CanHandle(document).Should().BeTrue();
        var result = await _connector.ParseAsync(document);

        result.HasErrors.Should().BeFalse();
        result.ProfileId.Should().Be(StatementBuiltInProfiles.IbFlexV1ProfileId);
        result.Records.Should().HaveCount(7);
        result.Records.Should().OnlyContain(record => record.Account == "U1234567");

        var byKind = result.Records.GroupBy(record => record.Kind).ToDictionary(group => group.Key, group => group.Count());
        byKind.Should().BeEquivalentTo(new Dictionary<StatementRecordKind, int>
        {
            [StatementRecordKind.Transaction] = 2,
            [StatementRecordKind.Dividend] = 1,
            [StatementRecordKind.Fee] = 1,
            [StatementRecordKind.CashBalance] = 1,
            [StatementRecordKind.Position] = 2
        });

        var buy = result.Records.Single(record => record.ExternalTransactionId == "7001001");
        buy.Symbol.Should().Be("AAPL");
        buy.Quantity.Should().Be(100m);
        buy.Price.Should().Be(187.25m);
        buy.CashAmount.Should().Be(-18725m);
        buy.TradeDate.Should().Be(new DateOnly(2026, 6, 2), "yyyyMMdd Flex dates parse");
        buy.SettlementDate.Should().Be(new DateOnly(2026, 6, 4));
        buy.FeesCommission.Should().Be(-1.05m);

        var sell = result.Records.Single(record => record.ExternalTransactionId == "7001002");
        sell.TradeDate.Should().Be(new DateOnly(2026, 6, 15), "yyyy-MM-dd Flex dates parse too");

        var dividend = result.Records.Single(record => record.Kind == StatementRecordKind.Dividend);
        dividend.Symbol.Should().Be("AAPL");
        dividend.CashAmount.Should().Be(132.50m);
        dividend.TradeDate.Should().Be(new DateOnly(2026, 6, 10), "timestamped Flex dateTime reduces to its date");

        var deposit = result.Records.Single(record => record.Kind == StatementRecordKind.CashBalance);
        deposit.CashAmount.Should().Be(50000.00m);

        var positions = result.Records.Where(record => record.Kind == StatementRecordKind.Position).ToArray();
        positions.Select(position => position.Symbol).Should().BeEquivalentTo("AAPL", "TLT");
        positions.Single(position => position.Symbol == "TLT").CashAmount.Should().Be(19080m);
        positions.Should().OnlyContain(position => position.TradeDate == new DateOnly(2026, 6, 30));

        result.Issues.Should().Contain(issue => issue.Code == "FLEX_SECTIONS" && issue.Severity == StatementParseIssue.InfoSeverity);
    }

    [Fact]
    public async Task Parse_NonFlexXml_ReportsBlockingError()
    {
        var document = new StatementSourceDocument("other.xml", "<SomethingElse />"u8.ToArray());

        var result = await _connector.ParseAsync(document);

        result.HasErrors.Should().BeTrue();
        result.Issues.Should().Contain(issue => issue.Code == "NOT_FLEX_REPORT");
    }

    [Fact]
    public async Task Parse_MalformedXml_ReportsInvalidXml()
    {
        var document = new StatementSourceDocument("broken.xml", "<FlexQueryResponse><Unclosed"u8.ToArray());

        var result = await _connector.ParseAsync(document);

        result.HasErrors.Should().BeTrue();
        result.Issues.Should().Contain(issue => issue.Code == "INVALID_XML");
    }

    [Fact]
    public async Task Parse_UnknownCashTransactionType_WarnsAndDefaultsToTransaction()
    {
        const string content = """
            <FlexQueryResponse>
              <FlexStatements count="1">
                <FlexStatement accountId="U1">
                  <CashTransactions>
                    <CashTransaction type="Mystery Charge" amount="-1.00" dateTime="20260601" currency="USD" transactionID="X-1" />
                  </CashTransactions>
                </FlexStatement>
              </FlexStatements>
            </FlexQueryResponse>
            """;
        var document = new StatementSourceDocument("flex.xml", System.Text.Encoding.UTF8.GetBytes(content));

        var result = await _connector.ParseAsync(document);

        result.HasErrors.Should().BeFalse();
        result.Records.Should().ContainSingle().Which.Kind.Should().Be(StatementRecordKind.Transaction);
        result.Issues.Should().Contain(issue => issue.Code == "UNKNOWN_ACTIVITY_CODE" && issue.Message.Contains("Mystery Charge"));
    }

    [Fact]
    public async Task Parse_MultiAccountFlexReport_IsRejected()
    {
        // Two FlexStatement sections for two different IB accounts. The matcher normalizes every row to
        // the run's single external account, so a multi-account report must be rejected rather than
        // reconciling one account's rows against another account's Meridian records.
        const string xml = """
            <FlexQueryResponse queryName="MeridianDaily" type="AF">
              <FlexStatements count="2">
                <FlexStatement accountId="U1234567" fromDate="20260601" toDate="20260630" period="Month">
                  <Trades>
                    <Trade accountId="U1234567" symbol="AAPL" tradeDate="20260615" quantity="100" tradePrice="201.35" netCash="-20136.00" currency="USD" buySell="BUY" tradeID="1000001" assetCategory="STK" />
                  </Trades>
                </FlexStatement>
                <FlexStatement accountId="U7654321" fromDate="20260601" toDate="20260630" period="Month">
                  <Trades>
                    <Trade accountId="U7654321" symbol="MSFT" tradeDate="20260616" quantity="10" tradePrice="500.10" netCash="-5001.00" currency="USD" buySell="BUY" tradeID="1000002" assetCategory="STK" />
                  </Trades>
                </FlexStatement>
              </FlexStatements>
            </FlexQueryResponse>
            """;
        var document = new StatementSourceDocument("ib-flex-multi-account.xml", System.Text.Encoding.UTF8.GetBytes(xml));

        _connector.CanHandle(document).Should().BeTrue();
        var result = await _connector.ParseAsync(document);

        result.HasErrors.Should().BeTrue();
        result.Records.Should().BeEmpty("a multi-account Flex report must not commit into a single-account run");
        result.Issues.Should().Contain(issue => issue.Code == "IBFLEX_MULTIPLE_ACCOUNTS");
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
