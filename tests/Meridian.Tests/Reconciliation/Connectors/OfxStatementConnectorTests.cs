using FluentAssertions;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.FinancialOperations.Reconciliation.Connectors.Ofx;
using Xunit;

namespace Meridian.Tests.Reconciliation.Connectors;

public sealed class OfxStatementConnectorTests : IDisposable
{
    private readonly string _root = StatementConnectorTestData.CreateTempRoot("mdc_ofx_connector");
    private readonly OfxStatementConnector _connector;

    public OfxStatementConnectorTests()
    {
        _connector = new OfxStatementConnector(
            new StatementMappingProfileCatalog(new FileStatementMappingProfileStore(_root)));
    }

    [Fact]
    public async Task Parse_Ofx1BankStatement_YieldsCashActivityFeesAndLedgerBalance()
    {
        var document = new StatementSourceDocument(
            "ofx-102-bank.ofx",
            StatementConnectorTestData.ReadFixture("ofx-102-bank.ofx"));

        _connector.CanHandle(document).Should().BeTrue();
        var result = await _connector.ParseAsync(document);

        result.HasErrors.Should().BeFalse();
        result.ProfileId.Should().Be(StatementBuiltInProfiles.OfxBankV1ProfileId);
        result.Records.Should().HaveCount(4);
        result.Records.Should().OnlyContain(record => record.Account == "FUND-A-CASH");

        var credit = result.Records.Single(record => record.ExternalTransactionId == "B1-20260603-1");
        credit.Kind.Should().Be(StatementRecordKind.CashBalance);
        credit.CashAmount.Should().Be(50000.00m);
        credit.TradeDate.Should().Be(new DateOnly(2026, 6, 3), "OFX timestamps reduce to their date component");

        var debit = result.Records.Single(record => record.ExternalTransactionId == "B1-20260610-1");
        debit.CashAmount.Should().Be(-18726.05m);

        var serviceCharge = result.Records.Single(record => record.ExternalTransactionId == "B1-20260628-1");
        serviceCharge.Kind.Should().Be(StatementRecordKind.Fee);
        serviceCharge.CashAmount.Should().Be(-25.00m);

        var ledgerBalance = result.Records.Single(record => record.ExternalTransactionId is null);
        ledgerBalance.Kind.Should().Be(StatementRecordKind.CashBalance);
        ledgerBalance.CashAmount.Should().Be(31248.95m);
        ledgerBalance.TradeDate.Should().Be(new DateOnly(2026, 6, 30));
    }

    [Fact]
    public async Task Parse_Ofx2InvestmentStatement_YieldsTradesAndPositions()
    {
        var document = new StatementSourceDocument(
            "ofx-211-investment.ofx",
            StatementConnectorTestData.ReadFixture("ofx-211-investment.ofx"));

        _connector.CanHandle(document).Should().BeTrue();
        var result = await _connector.ParseAsync(document);

        result.HasErrors.Should().BeFalse();
        result.Records.Should().HaveCount(4);
        result.Records.Should().OnlyContain(record => record.Account == "FUND-A-INV");

        var buy = result.Records.Single(record => record.ExternalTransactionId == "I1-20260605-1");
        buy.Kind.Should().Be(StatementRecordKind.Transaction);
        buy.Symbol.Should().Be("037833100");
        buy.Quantity.Should().Be(100m);
        buy.Price.Should().Be(187.25m);
        buy.CashAmount.Should().Be(-18726.05m);
        buy.TradeDate.Should().Be(new DateOnly(2026, 6, 5));
        buy.SettlementDate.Should().Be(new DateOnly(2026, 6, 9));
        buy.FeesCommission.Should().Be(1.05m);

        var sell = result.Records.Single(record => record.ExternalTransactionId == "I1-20260618-1");
        sell.Quantity.Should().Be(-50m);
        sell.CashAmount.Should().Be(20603.98m);

        var positions = result.Records.Where(record => record.Kind == StatementRecordKind.Position).ToArray();
        positions.Should().HaveCount(2);
        positions.Select(position => position.Symbol).Should().BeEquivalentTo("037833100", "922908363");
        positions.Should().OnlyContain(position => position.TradeDate == new DateOnly(2026, 6, 30));
        positions.Single(position => position.Symbol == "922908363").Quantity.Should().Be(250.5m);
    }

    [Fact]
    public async Task Parse_NonOfxContent_ReportsBlockingError()
    {
        var document = new StatementSourceDocument("statement.ofx", "account,symbol\nFUND-A,AAPL"u8.ToArray());

        var result = await _connector.ParseAsync(document);

        result.HasErrors.Should().BeTrue();
        result.Issues.Should().Contain(issue => issue.Code == "NOT_OFX");
    }

    [Fact]
    public void Parser_WrappedInvestmentAggregates_DoNotDoubleCount()
    {
        const string content = """
            <OFX>
            <INVSTMTRS>
            <INVACCTFROM><ACCTID>FUND-X</ACCTID></INVACCTFROM>
            <BUYSTOCK>
            <INVBUY>
            <INVTRAN><FITID>W-1</FITID><DTTRADE>20260605</DTTRADE></INVTRAN>
            <SECID><UNIQUEID>ABC123</UNIQUEID></SECID>
            <UNITS>10</UNITS>
            <UNITPRICE>5</UNITPRICE>
            <TOTAL>-50</TOTAL>
            </INVBUY>
            <BUYTYPE>BUY</BUYTYPE>
            </BUYSTOCK>
            </INVSTMTRS>
            </OFX>
            """;

        var parsed = OfxDocumentParser.Parse(content);

        parsed.Entries.Should().ContainSingle("the wrapper and its INVBUY detail are one economic entry");
        parsed.Entries[0]["AGGREGATE"].Should().Be("BUYSTOCK");
        parsed.Entries[0]["FITID"].Should().Be("W-1");
        parsed.Entries[0]["TRNAMT"].Should().Be("-50");
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
