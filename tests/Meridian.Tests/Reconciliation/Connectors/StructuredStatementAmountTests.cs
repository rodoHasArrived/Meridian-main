using System.Text;
using FluentAssertions;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.FinancialOperations.Reconciliation.Connectors.IbFlex;
using Meridian.FinancialOperations.Reconciliation.Connectors.Ofx;
using Meridian.Infrastructure.Reconciliation;
using Meridian.Storage.Archival;
using Xunit;

namespace Meridian.Tests.Reconciliation.Connectors;

public sealed class StructuredStatementAmountTests : IDisposable
{
    private readonly string _root = StatementConnectorTestData.CreateTempRoot("mdc_structured_amounts");
    private readonly StatementMappingProfileCatalog _catalog;
    private readonly StatementImportService _service;
    private readonly IStatementRunWorkflowService _workflow;
    private readonly OfxStatementConnector _ofx;
    private readonly IbFlexStatementConnector _flex;

    public StructuredStatementAmountTests()
    {
        _catalog = new StatementMappingProfileCatalog(new FileStatementMappingProfileStore(_root));
        _ofx = new OfxStatementConnector(_catalog);
        _flex = new IbFlexStatementConnector(_catalog);
        var statementStore = new JsonCanonicalStatementStore(_root, new AtomicFileWriterAdapter());
        _workflow = StatementRunWorkflowService.CreateEphemeralForTesting(
            statementStore,
            new JsonReconciliationCaseStore(_root, new AtomicFileWriterAdapter()),
            new JsonReconciliationBreakStore(_root, new AtomicFileWriterAdapter()),
            new CsvBrokerStatementService(statementStore),
            new StatementReconciliationContextAdapter(new StatementReconciliationService()));
        _service = new StatementImportService(new StatementConnectorRegistry([_ofx, _flex]), _catalog, _workflow, _root);
    }

    [Theory]
    [InlineData("TRNAMT", "1,25")]
    [InlineData("TRNAMT", "not-an-amount")]
    [InlineData("TRNAMT", " ")]
    [InlineData("TRNAMT", null)]
    [InlineData("COMMISSION", "1,25")]
    [InlineData("COMMISSION", "not-an-amount")]
    public async Task OfxBank_MalformedAmountsFailBeforeRetention(string field, string? value)
    {
        var amount = field == "TRNAMT" ? Tag(field, value) : "<TRNAMT>-25.50</TRNAMT>";
        var fees = field == "COMMISSION" ? Tag(field, value) : string.Empty;
        var document = OfxBankDocument(amount + fees);

        await AssertRejectedBeforeRetention(document, _ofx);
    }

    [Theory]
    [InlineData("UNITS", "1,25")]
    [InlineData("UNITPRICE", "1,25")]
    [InlineData("TOTAL", "1,25")]
    [InlineData("UNITS", " ")]
    [InlineData("UNITPRICE", null)]
    [InlineData("TOTAL", null)]
    public async Task OfxInvestment_MalformedAmountsFailBeforeRetention(string field, string? value)
    {
        var fields = new Dictionary<string, string?> { ["UNITS"] = "10", ["UNITPRICE"] = "25.50", ["TOTAL"] = "-255" };
        fields[field] = value;
        var content = "<OFX><INVSTMTRS><CURDEF>USD</CURDEF><INVACCTFROM><ACCTID>FUND-A</ACCTID></INVACCTFROM>"
            + "<BUYSTOCK><INVBUY><INVTRAN><FITID>BUY-1</FITID><DTTRADE>20260602</DTTRADE></INVTRAN>"
            + "<SECID><UNIQUEID>SPY</UNIQUEID></SECID>"
            + string.Concat(fields.Select(pair => Tag(pair.Key, pair.Value)))
            + "</INVBUY></BUYSTOCK></INVSTMTRS></OFX>";

        await AssertRejectedBeforeRetention(new StatementSourceDocument("amounts.ofx", Encoding.UTF8.GetBytes(content)), _ofx);
    }

    [Theory]
    [InlineData("quantity", "1,25")]
    [InlineData("tradePrice", "1,25")]
    [InlineData("netCash", "1,25")]
    [InlineData("ibCommission", "1,25")]
    [InlineData("ibCommission", "not-an-amount")]
    [InlineData("quantity", " ")]
    [InlineData("tradePrice", null)]
    [InlineData("netCash", null)]
    public async Task FlexTrade_MalformedAmountsFailBeforeRetention(string field, string? value)
    {
        var attributes = new Dictionary<string, string?>
        {
            ["quantity"] = "10",
            ["tradePrice"] = "25.50",
            ["netCash"] = "-255",
            ["ibCommission"] = "-1.25"
        };
        attributes[field] = value;
        var trade = "<Trades><Trade symbol=\"SPY\" currency=\"USD\" tradeDate=\"20260602\" tradeID=\"TRADE-1\" "
            + string.Join(' ', attributes.Where(pair => pair.Value is not null).Select(pair => $"{pair.Key}=\"{pair.Value}\""))
            + " /></Trades>";

        await AssertRejectedBeforeRetention(FlexDocument(trade), _flex);
    }

    [Theory]
    [InlineData("<CashTransactions><CashTransaction type=\"Deposits/Withdrawals\" amount=\"1,25\" currency=\"USD\" dateTime=\"20260602\" /></CashTransactions>")]
    [InlineData("<CashTransactions><CashTransaction type=\"Deposits/Withdrawals\" currency=\"USD\" dateTime=\"20260602\" /></CashTransactions>")]
    [InlineData("<OpenPositions><OpenPosition symbol=\"SPY\" position=\"10\" markPrice=\"1,25\" positionValue=\"255\" currency=\"USD\" reportDate=\"20260602\" /></OpenPositions>")]
    [InlineData("<CashReport><CashReportCurrency currency=\"USD\" endingCash=\"not-an-amount\" reportDate=\"20260602\" /></CashReport>")]
    [InlineData("<MarginReport><MarginReportCurrency currency=\"USD\" netLiquidationValue=\"1,25\" /></MarginReport>")]
    [InlineData("<InterestDetails><InterestDetail amount=\"not-an-amount\" currency=\"USD\" dateTime=\"20260602\" /></InterestDetails>")]
    [InlineData("<BorrowFeeDetails><BorrowFeeDetail feeAmount=\"1,25\" currency=\"USD\" dateTime=\"20260602\" /></BorrowFeeDetails>")]
    [InlineData("<CommissionDetails><CommissionDetail totalCommission=\"not-an-amount\" currency=\"USD\" dateTime=\"20260602\" /></CommissionDetails>")]
    [InlineData("<CorporateActions><CorporateAction symbol=\"SPY\" type=\"Split\" quantity=\"1,25\" currency=\"USD\" dateTime=\"20260602\" /></CorporateActions>")]
    [InlineData("<Transfers><Transfer amount=\"not-an-amount\" currency=\"USD\" dateTime=\"20260602\" /></Transfers>")]
    [InlineData("<OptionEAE><OptionEAE symbol=\"SPY\" quantity=\"1\" strike=\"not-an-amount\" currency=\"USD\" dateTime=\"20260602\" /></OptionEAE>")]
    [InlineData("<OpenLots><OpenLot symbol=\"SPY\" quantity=\"10\" costBasisMoney=\"not-an-amount\" currency=\"USD\" openDateTime=\"20260602\" /></OpenLots>")]
    [InlineData("<SecuritiesBorrowed><SecurityBorrowed symbol=\"SPY\" quantity=\"10\" feeRate=\"1,25\" currency=\"USD\" /></SecuritiesBorrowed>")]
    public async Task FlexSupplementalEvidence_MalformedAmountsFailBeforeRetention(string section)
    {
        await AssertRejectedBeforeRetention(FlexDocument(ValidFlexCash + section), _flex);
    }

    [Theory]
    [InlineData("ofx")]
    [InlineData("ib-flex")]
    public async Task CustomProfile_CannotApplyLocalizedNumbersToStructuredEvidence(string connectorId)
    {
        var builtInId = connectorId == "ofx" ? StatementBuiltInProfiles.OfxBankV1ProfileId : StatementBuiltInProfiles.IbFlexV1ProfileId;
        var builtIn = (await _catalog.FindAsync(builtInId))!;
        var profile = builtIn with { ProfileId = $"custom-{connectorId}", IsBuiltIn = false, Culture = "de-DE" };
        await _catalog.UpsertAsync(profile);
        var document = connectorId == "ofx"
            ? OfxBankDocument("<TRNAMT>1,25</TRNAMT>")
            : FlexDocument(ValidFlexCash.Replace("25.50", "1,25", StringComparison.Ordinal));

        await AssertRejectedBeforeRetention(document with { MappingProfileId = profile.ProfileId }, connectorId == "ofx" ? _ofx : _flex);
    }

    [Theory]
    [InlineData("ofx")]
    [InlineData("ib-flex")]
    public async Task CashMovement_AbsentQuantityAndPricePreserveExactSignedAmount(string connectorId)
    {
        var document = connectorId == "ofx" ? OfxBankDocument("<TRNAMT>-25.50</TRNAMT>") : FlexDocument(ValidFlexCash);
        IStatementConnector connector = connectorId == "ofx" ? _ofx : _flex;
        var parsed = await connector.ParseAsync(document);

        parsed.HasErrors.Should().BeFalse();
        parsed.Records.Should().ContainSingle().Which.CashAmount.Should().Be(-25.50m);
        parsed.Records[0].Quantity.Should().Be(0m);
        parsed.Records[0].Price.Should().Be(0m);
        (await _service.ValidateAsync(document, connectorId)).IsValid.Should().BeTrue();
        await _service.CommitAsync(Request(document, connectorId));
        (await _workflow.ListImportsAsync()).Should().ContainSingle();
    }

    private async Task AssertRejectedBeforeRetention(StatementSourceDocument document, IStatementConnector connector)
    {
        var parsed = await connector.ParseAsync(document);
        parsed.Issues.Should().Contain(issue => issue.Code == "ROW_INVALID_NUMBER" || issue.Code == "ROW_INVALID_FEES" || issue.Code == "IBFLEX_INVALID_NUMBER");
        (await _service.ValidateAsync(document, connector.Descriptor.ConnectorId)).IsValid.Should().BeFalse();
        (await _service.PreviewAsync(document, connector.Descriptor.ConnectorId)).Status.Should().Be("NeedsAttention");
        await Assert.ThrowsAsync<InvalidDataException>(() => _service.CommitAsync(Request(document, connector.Descriptor.ConnectorId)));
        (await _workflow.ListImportsAsync()).Should().BeEmpty();
        Directory.Exists(Path.Combine(_root, "reconciliation", "statement-connector-imports")).Should().BeFalse();
    }

    private static StatementImportCommitRequest Request(StatementSourceDocument document, string connectorId)
        => new(document, connectorId, "broker", "Sample Broker", "FUND-A", "FUND-A",
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), null, "test-operator");

    private static string Tag(string name, string? value) => value is null ? string.Empty : $"<{name}>{value}</{name}>";

    private static StatementSourceDocument OfxBankDocument(string amountTags)
        => new("amounts.ofx", Encoding.UTF8.GetBytes("<OFX><STMTRS><CURDEF>USD</CURDEF><BANKACCTFROM><ACCTID>FUND-A</ACCTID></BANKACCTFROM>"
            + "<STMTTRN><TRNTYPE>DEBIT</TRNTYPE><DTPOSTED>20260602</DTPOSTED><FITID>BANK-1</FITID>"
            + amountTags + "</STMTTRN></STMTRS></OFX>"));

    private const string ValidFlexCash = "<CashTransactions><CashTransaction type=\"Deposits/Withdrawals\" amount=\"-25.50\" currency=\"USD\" dateTime=\"20260602\" transactionID=\"CASH-1\" /></CashTransactions>";

    private static StatementSourceDocument FlexDocument(string sections)
        => new("amounts.xml", Encoding.UTF8.GetBytes("<FlexQueryResponse><FlexStatements><FlexStatement accountId=\"FUND-A\" toDate=\"20260630\">"
            + sections + "</FlexStatement></FlexStatements></FlexQueryResponse>"));

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
