using FluentAssertions;
using System.Text;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.FinancialOperations.Reconciliation.Connectors.IbFlex;
using Meridian.Execution.Sdk;
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
    public async Task Parse_RichFlexSections_PreservesMarginOptionsBorrowAndTaxLotEvidence()
    {
        const string content = """
            <FlexQueryResponse>
              <FlexStatements count="1">
                <FlexStatement accountId="U-MARGIN-1" toDate="2026-07-17">
                  <AccountInformation accountId="U-MARGIN-1" accountType="Margin" baseCurrency="USD" />
                  <CashReport><CashReportCurrency accountId="U-MARGIN-1" currency="USD" endingCash="25000" reportDate="2026-07-17" /></CashReport>
                  <MarginReport><MarginReportCurrency accountId="U-MARGIN-1" currency="USD" netLiquidationValue="150000" buyingPower="200000" currentInitialMargin="40000" currentMaintenanceMargin="30000" currentExcessLiquidity="120000" sma="5000" /></MarginReport>
                  <InterestDetails><InterestDetail accountId="U-MARGIN-1" currency="USD" amount="-125.50" dateTime="2026-07-17" description="Margin interest" transactionID="INT-1" /></InterestDetails>
                  <BorrowFeeDetails><BorrowFeeDetail accountId="U-MARGIN-1" symbol="GME" currency="USD" quantity="-100" feeRate="18.5" feeAmount="-21.50" collateralAmount="2500" dateTime="2026-07-17" transactionID="BRW-1" /></BorrowFeeDetails>
                  <CommissionDetails><CommissionDetail accountId="U-MARGIN-1" symbol="AAPL" currency="USD" totalCommission="-2.25" tradeID="TRD-1" dateTime="2026-07-17" /></CommissionDetails>
                  <CorporateActions><CorporateAction accountId="U-MARGIN-1" symbol="ABC" currency="USD" type="Split" description="2 for 1 stock split" quantity="20" dateTime="2026-07-17" transactionID="CA-1" /></CorporateActions>
                  <Transfers><Transfer accountId="U-MARGIN-1" symbol="MSFT" currency="USD" type="ACATS" quantity="10" dateTime="2026-07-17" transactionID="TX-1" /></Transfers>
                  <OptionEAE><OptionEAE accountId="U-MARGIN-1" symbol="AAPL" conid="OPT-1" currency="USD" transactionType="Assignment" quantity="-1" putCall="C" strike="190" expiry="2026-07-17" multiplier="100" dateTime="2026-07-17" transactionID="OPT-EAE-1" /></OptionEAE>
                  <OpenLots><OpenLot accountId="U-MARGIN-1" symbol="AAPL" currency="USD" quantity="50" costBasisMoney="8000" openDateTime="2026-01-10" lotCode="LOT-1" marketValue="9500" fifoPnlUnrealized="1500" /></OpenLots>
                  <SecuritiesBorrowed><SecurityBorrowed accountId="U-MARGIN-1" symbol="GME" currency="USD" quantity="-100" feeRate="18.5" feeAmount="21.50" collateralAmount="2500" /></SecuritiesBorrowed>
                </FlexStatement>
              </FlexStatements>
            </FlexQueryResponse>
            """;

        var result = await _connector.ParseAsync(new StatementSourceDocument("rich-flex.xml", Encoding.UTF8.GetBytes(content)));

        result.HasErrors.Should().BeFalse();
        result.AccountSnapshots.Should().ContainSingle();
        result.AccountSnapshots![0].MarginRegime.Should().Be(BrokerageMarginRegime.RegulationT);
        result.AccountSnapshots[0].Cash.Should().Be(25000m);
        result.AccountSnapshots[0].Equity.Should().Be(150000m);
        result.AccountSnapshots[0].BuyingPower.Should().Be(200000m);
        result.AccountSnapshots[0].InitialMargin.Should().Be(40000m);
        result.AccountSnapshots[0].MaintenanceMargin.Should().Be(30000m);
        result.AccountSnapshots[0].ExcessLiquidity.Should().Be(120000m);
        result.ActivityCursors.Should().ContainSingle().Which.IsComplete.Should().BeTrue();
        result.ActivityCursors[0].SourceRecordCount.Should().Be(result.ActivityEvents.Count);
        result.ActivityEvents.Should().Contain(item => item.Subtype == BrokerageActivitySubtype.MarginInterest);
        result.ActivityEvents.Should().Contain(item => item.Subtype == BrokerageActivitySubtype.BorrowFee);
        var assignment = result.ActivityEvents.Should().ContainSingle(item => item.Subtype == BrokerageActivitySubtype.OptionAssignment).Subject;
        assignment.Option.Should().NotBeNull();
        assignment.Option!.StrikePrice.Should().Be(190m);
        assignment.Option.ContractMultiplier.Should().Be(100m);
        result.TaxLots.Should().ContainSingle(item => item.LotId == "LOT-1" && item.CostBasis == 8000m && item.AccountId == "U-MARGIN-1");
        result.BorrowPositions.Should().ContainSingle(item => item.Symbol == "GME" && item.BorrowRate == 18.5m && item.AccountId == "U-MARGIN-1");
        result.Records.Should().Contain(item => item.ActivitySubtype == BrokerageActivitySubtype.OptionAssignment.ToString());
        result.Issues.Should().Contain(item => item.Code == "FLEX_SECTIONS" && item.Message.Contains("MarginReport=1") && item.Message.Contains("OpenLots=1"));
    }

    [Fact]
    public async Task Parse_MarginReportWithoutAccountInformation_StillBuildsProviderSnapshot()
    {
        const string content = """
            <FlexQueryResponse>
              <FlexStatements count="1">
                <FlexStatement accountId="U-MARGIN-ONLY" toDate="2026-07-17">
                  <CashReport><CashReportCurrency accountId="U-MARGIN-ONLY" currency="USD" endingCash="12000" /></CashReport>
                  <MarginReport><MarginReportCurrency accountId="U-MARGIN-ONLY" accountType="Portfolio Margin" currency="USD" netLiquidationValue="100000" currentInitialMargin="18000" currentMaintenanceMargin="15000" currentExcessLiquidity="85000" /></MarginReport>
                </FlexStatement>
              </FlexStatements>
            </FlexQueryResponse>
            """;

        var result = await _connector.ParseAsync(new StatementSourceDocument("margin-only-flex.xml", Encoding.UTF8.GetBytes(content)));

        result.HasErrors.Should().BeFalse();
        var snapshot = result.AccountSnapshots.Should().ContainSingle().Subject;
        snapshot.ProviderId.Should().Be(IbFlexStatementConnector.ConnectorId);
        snapshot.AccountId.Should().Be("U-MARGIN-ONLY");
        snapshot.MarginRegime.Should().Be(BrokerageMarginRegime.PortfolioMargin);
        snapshot.Cash.Should().Be(12000m);
        snapshot.Equity.Should().Be(100000m);
        snapshot.InitialMargin.Should().Be(18000m);
        snapshot.MaintenanceMargin.Should().Be(15000m);
        snapshot.ExcessLiquidity.Should().Be(85000m);
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
