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

        // STMTTRN movements are transactions with their FITID identity intact — not cash balances.
        // The prior "cash" mapping routed every bank movement into the cash-balance lane, where a
        // mid-period movement can structurally never reconcile against a period-end balance.
        var credit = result.Records.Single(record => record.ExternalTransactionId == "B1-20260603-1");
        credit.Kind.Should().Be(StatementRecordKind.Transaction);
        credit.CashAmount.Should().Be(50000.00m);
        credit.TradeDate.Should().Be(new DateOnly(2026, 6, 3), "OFX timestamps reduce to their date component");

        var debit = result.Records.Single(record => record.ExternalTransactionId == "B1-20260610-1");
        debit.Kind.Should().Be(StatementRecordKind.Transaction);
        debit.CashAmount.Should().Be(-18726.05m);

        var serviceCharge = result.Records.Single(record => record.ExternalTransactionId == "B1-20260628-1");
        serviceCharge.Kind.Should().Be(StatementRecordKind.Fee);
        serviceCharge.CashAmount.Should().Be(-25.00m);

        // LEDGERBAL remains the genuine period-end cash balance.
        var ledgerBalance = result.Records.Single(record => record.ExternalTransactionId is null);
        ledgerBalance.Kind.Should().Be(StatementRecordKind.CashBalance);
        ledgerBalance.CashAmount.Should().Be(31248.95m);
        ledgerBalance.TradeDate.Should().Be(new DateOnly(2026, 6, 30));
        result.Records.Where(record => record.Kind == StatementRecordKind.CashBalance)
            .Should().ContainSingle("only the LEDGERBAL aggregate is a cash balance");
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

    [Theory]
    [InlineData("STMTRS", "BANKACCTFROM", false)]
    [InlineData("STMTRS", "BANKACCTFROM", true)]
    [InlineData("CCSTMTRS", "CCACCTFROM", false)]
    [InlineData("CCSTMTRS", "CCACCTFROM", true)]
    [InlineData("INVSTMTRS", "INVACCTFROM", false)]
    [InlineData("INVSTMTRS", "INVACCTFROM", true)]
    public void AccountIdentity_IsScopedToContainingStatement(string statement, string header, bool sgml)
    {
        string Leaf(string tag, string value) => $"<{tag}>{value}" + (sgml ? "\n" : $"</{tag}>");
        string Entry(string id) => "<STMTTRN>" + Leaf("FITID", id) + "</STMTTRN>";
        string Scope(string account, string id) => $"<{statement}><{header}>" + Leaf("ACCTID", account)
            + $"</{header}>" + Entry(id) + $"</{statement}>";
        var content = "<OFX>" + Scope("ACCOUNT-A", "1") + Scope("ACCOUNT-B", "2")
            + $"<{statement}>" + Entry("3") + $"</{statement}>" + Entry("4") + "</OFX>";
        var parsed = OfxDocumentParser.Parse(content);
        parsed.Entries.Should().HaveCount(4);
        parsed.Entries[0]["ACCTID"].Should().Be("ACCOUNT-A");
        parsed.Entries[1]["ACCTID"].Should().Be("ACCOUNT-B");
        parsed.Entries[2].Should().NotContainKey("ACCTID");
        parsed.Entries[3].Should().NotContainKey("ACCTID");
        parsed.AccountId.Should().BeNull("a multi-account file has no single authoritative account");
    }

    [Theory]
    [InlineData("ACCOUNT-B")]
    [InlineData(" ")]
    public void ConflictingOrBlankStatementHeaders_DoNotSupplyAccountIdentity(string other)
    {
        var content = "<OFX><STMTRS><BANKACCTFROM><ACCTID>ACCOUNT-A</ACCTID></BANKACCTFROM>"
            + "<BANKACCTFROM><ACCTID>" + other + "</ACCTID></BANKACCTFROM>"
            + "<STMTTRN><FITID>1</FITID></STMTTRN></STMTRS></OFX>";
        var parsed = OfxDocumentParser.Parse(content);
        parsed.Entries.Should().ContainSingle().Which.Should().NotContainKey("ACCTID");
        parsed.AccountId.Should().BeNull();
    }

    [Fact]
    public async Task MissingAccountInLaterStatement_IsReportedInsteadOfBorrowed()
    {
        const string row = "<STMTTRN><TRNTYPE>CREDIT</TRNTYPE><DTPOSTED>20260601</DTPOSTED><TRNAMT>10</TRNAMT></STMTTRN>";
        var content = "<OFX><STMTRS><BANKACCTFROM><ACCTID>A</ACCTID></BANKACCTFROM>" + row
            + "</STMTRS><STMTRS>" + row + "</STMTRS></OFX>";
        var result = await _connector.ParseAsync(new StatementSourceDocument("accounts.ofx", System.Text.Encoding.UTF8.GetBytes(content)));
        result.HasErrors.Should().BeTrue();
        result.Issues.Should().Contain(issue => issue.Code == "ROW_MISSING_ACCOUNT");
        result.Records.Should().ContainSingle().Which.Account.Should().Be("A");
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("EUR")]
    public async Task StatementCurrency_IsPreservedOnEveryCanonicalRow(string currency)
    {
        var content = System.Text.Encoding.UTF8.GetString(StatementConnectorTestData.ReadFixture("ofx-102-bank.ofx"))
            .Replace("<CURDEF>USD", "<CURDEF>" + currency, StringComparison.Ordinal);
        var result = await _connector.ParseAsync(new StatementSourceDocument("currency.ofx", System.Text.Encoding.UTF8.GetBytes(content)));
        result.HasErrors.Should().BeFalse();
        result.Records.Should().HaveCount(4);
        result.Records.Should().OnlyContain(record => record.Currency == currency);
    }

    [Fact]
    public void StatementCurrency_DoesNotCrossSiblingsOrOverrideRowEvidence()
    {
        const string content = "<OFX><STMTRS><CURDEF>USD</CURDEF><STMTTRN><FITID>1</FITID><CURSYM>GBP</CURSYM></STMTTRN></STMTRS>"
            + "<STMTRS><CURDEF>CAD</CURDEF><STMTTRN><FITID>2</FITID></STMTTRN></STMTRS>"
            + "<STMTRS><STMTTRN><FITID>3</FITID></STMTTRN></STMTRS></OFX>";
        var entries = OfxDocumentParser.Parse(content).Entries;
        entries.Should().HaveCount(3);
        entries[0]["CURSYM"].Should().Be("GBP");
        entries[0].Should().NotContainKey("CURDEF");
        entries[1]["CURDEF"].Should().Be("CAD");
        entries[2].Should().NotContainKey("CURDEF");
    }

    [Theory]
    [InlineData("STMTRS", "BANKACCTFROM", "ACCOUNT-B")]
    [InlineData("STMTRS", "BANKACCTFROM", " ")]
    [InlineData("CCSTMTRS", "CCACCTFROM", "ACCOUNT-B")]
    [InlineData("CCSTMTRS", "CCACCTFROM", " ")]
    [InlineData("INVSTMTRS", "INVACCTFROM", "ACCOUNT-B")]
    [InlineData("INVSTMTRS", "INVACCTFROM", " ")]
    public async Task MonthEndOfx_DuplicateConflictingAccountTagsCannotOverwriteAuthority(string statement, string header, string duplicate)
    {
        var content = $"<OFX><{statement}><CURDEF>USD</CURDEF><{header}><ACCTID>ACCOUNT-A</ACCTID>"
            + $"<ACCTID>{duplicate}</ACCTID><ACCTID>ACCOUNT-A</ACCTID></{header}>"
            + "<STMTTRN><TRNTYPE>CREDIT</TRNTYPE><DTPOSTED>20260601</DTPOSTED><TRNAMT>10</TRNAMT></STMTTRN>"
            + $"</{statement}></OFX>";

        var result = await _connector.ParseAsync(new StatementSourceDocument("ambiguous-account.ofx", System.Text.Encoding.UTF8.GetBytes(content)));

        result.HasErrors.Should().BeTrue();
        result.Records.Should().BeEmpty();
        result.Issues.Should().Contain(issue => issue.Code == "ROW_MISSING_ACCOUNT");
    }

    [Theory]
    [InlineData("\t")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task MonthEndOfx_XmlWhitespaceBeforeAttributesPreservesAccountIdentity(string whitespace)
    {
        var content = $"<OFX><STMTRS{whitespace}xmlns=\"urn:ofx\"><CURDEF>USD</CURDEF>"
            + "<BANKACCTFROM><ACCTID>ACCOUNT-A</ACCTID><ACCTID>ACCOUNT-A</ACCTID></BANKACCTFROM>"
            + "<STMTTRN><TRNTYPE>CREDIT</TRNTYPE><DTPOSTED>20260601</DTPOSTED><TRNAMT>10</TRNAMT></STMTTRN>"
            + "</STMTRS></OFX>";

        var result = await _connector.ParseAsync(new StatementSourceDocument("formatted-account.ofx", System.Text.Encoding.UTF8.GetBytes(content)));

        result.HasErrors.Should().BeFalse();
        result.Records.Should().ContainSingle().Which.Account.Should().Be("ACCOUNT-A");
        result.Records[0].Currency.Should().Be("USD");
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
