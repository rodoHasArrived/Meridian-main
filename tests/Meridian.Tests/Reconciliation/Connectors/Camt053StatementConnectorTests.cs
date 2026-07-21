using System.Text;
using FluentAssertions;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.FinancialOperations.Reconciliation.Connectors.Camt;
using Xunit;

namespace Meridian.Tests.Reconciliation.Connectors;

public sealed class Camt053StatementConnectorTests
{
    private readonly Camt053StatementConnector _connector = new();

    [Fact]
    public async Task Parse_Camt053_YieldsClosingBalanceAndSignedEntries()
    {
        var document = new StatementSourceDocument(
            "camt053-sample.xml",
            StatementConnectorTestData.ReadFixture("camt053-sample.xml"));

        _connector.CanHandle(document).Should().BeTrue();
        var result = await _connector.ParseAsync(document);

        result.HasErrors.Should().BeFalse();
        result.ConnectorId.Should().Be(Camt053StatementConnector.ConnectorId);
        // Only the closing booked (CLBD) balance is reconciled; the opening balance is skipped.
        result.Records.Should().HaveCount(3);
        result.Records.Should().OnlyContain(record => record.Account == "DE89370400440532013000");
        result.Records.Should().OnlyContain(record => record.Currency == "EUR");

        var balance = result.Records.Single(record => record.Kind == StatementRecordKind.CashBalance);
        balance.CashAmount.Should().Be(12345.67m);
        balance.ActivityType.Should().Be("cashbalance");
        balance.TradeDate.Should().Be(new DateOnly(2026, 5, 31));

        var credit = result.Records.Single(record => record.ExternalTransactionId == "ACCTSVCR-001");
        credit.Kind.Should().Be(StatementRecordKind.Transaction);
        credit.CashAmount.Should().Be(2500.00m);
        credit.SettlementDate.Should().Be(new DateOnly(2026, 5, 11));

        var debit = result.Records.Single(record => record.ExternalTransactionId == "ACCTSVCR-002");
        debit.CashAmount.Should().Be(-154.33m, "a DBIT entry is negative");
    }

    [Fact]
    public void CanHandle_FlexXml_ReturnsFalse()
    {
        var document = new StatementSourceDocument(
            "flex.xml",
            Encoding.UTF8.GetBytes("<FlexQueryResponse></FlexQueryResponse>"));

        _connector.CanHandle(document).Should().BeFalse("camt.053 must not claim IB Flex XML");
    }

    [Fact]
    public async Task Parse_Camt053_WithMultipleAccounts_IsRejected()
    {
        // Two Stmt elements for two different IBANs. The statement-run matcher would normalize every
        // row to the run's single external account, so committing this file would compare one account's
        // balances against another account's Meridian records. The connector must reject it instead.
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.053.001.02">
              <BkToCstmrStmt>
                <Stmt>
                  <Acct><Id><IBAN>DE89370400440532013000</IBAN></Id><Ccy>EUR</Ccy></Acct>
                  <Bal><Tp><CdOrPrtry><Cd>CLBD</Cd></CdOrPrtry></Tp><Amt Ccy="EUR">100.00</Amt><CdtDbtInd>CRDT</CdtDbtInd><Dt><Dt>2026-05-31</Dt></Dt></Bal>
                </Stmt>
                <Stmt>
                  <Acct><Id><IBAN>FR7630006000011234567890189</IBAN></Id><Ccy>EUR</Ccy></Acct>
                  <Bal><Tp><CdOrPrtry><Cd>CLBD</Cd></CdOrPrtry></Tp><Amt Ccy="EUR">200.00</Amt><CdtDbtInd>CRDT</CdtDbtInd><Dt><Dt>2026-05-31</Dt></Dt></Bal>
                </Stmt>
              </BkToCstmrStmt>
            </Document>
            """;
        var document = new StatementSourceDocument("multi-account.xml", Encoding.UTF8.GetBytes(xml));

        _connector.CanHandle(document).Should().BeTrue();
        var result = await _connector.ParseAsync(document);

        result.HasErrors.Should().BeTrue();
        result.Records.Should().BeEmpty("a multi-account camt.053 file must not commit into a single-account run");
        result.Issues.Should().Contain(issue => issue.Code == "CAMT_MULTIPLE_ACCOUNTS");
    }
}
