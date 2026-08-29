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

    [Fact]
    public async Task Parse_Camt053_WithMultipleStatementsForOneAccount_IsRejected()
    {
        // Two Stmt elements for the SAME IBAN across different periods. Combining them would give the
        // matcher two closing balances for one internal cash record, letting it match one and open a
        // false break for the other under the single operator-supplied period, so it must be rejected.
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.053.001.02">
              <BkToCstmrStmt>
                <Stmt>
                  <Acct><Id><IBAN>DE89370400440532013000</IBAN></Id><Ccy>EUR</Ccy></Acct>
                  <Bal><Tp><CdOrPrtry><Cd>CLBD</Cd></CdOrPrtry></Tp><Amt Ccy="EUR">100.00</Amt><CdtDbtInd>CRDT</CdtDbtInd><Dt><Dt>2026-04-30</Dt></Dt></Bal>
                </Stmt>
                <Stmt>
                  <Acct><Id><IBAN>DE89370400440532013000</IBAN></Id><Ccy>EUR</Ccy></Acct>
                  <Bal><Tp><CdOrPrtry><Cd>CLBD</Cd></CdOrPrtry></Tp><Amt Ccy="EUR">200.00</Amt><CdtDbtInd>CRDT</CdtDbtInd><Dt><Dt>2026-05-31</Dt></Dt></Bal>
                </Stmt>
              </BkToCstmrStmt>
            </Document>
            """;
        var document = new StatementSourceDocument("multi-statement.xml", Encoding.UTF8.GetBytes(xml));

        _connector.CanHandle(document).Should().BeTrue();
        var result = await _connector.ParseAsync(document);

        result.HasErrors.Should().BeTrue();
        result.Records.Should().BeEmpty("multiple statements for one account must not combine into a single run");
        result.Issues.Should().Contain(issue => issue.Code == "CAMT_MULTIPLE_STATEMENTS");
    }

    [Fact]
    public async Task Parse_Camt053_WithNonNumericClosingBalanceAmount_IsRejected()
    {
        // The closing-balance Amt is non-numeric. Emitting it as 0 could exact-match an internal zero
        // cash balance and leave the malformed statement apparently reconciled, so the connector must
        // reject it with a parse error and not manufacture a zero-valued canonical row.
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.053.001.02">
              <BkToCstmrStmt>
                <Stmt>
                  <Acct><Id><IBAN>DE89370400440532013000</IBAN></Id><Ccy>EUR</Ccy></Acct>
                  <Bal><Tp><CdOrPrtry><Cd>CLBD</Cd></CdOrPrtry></Tp><Amt Ccy="EUR">not-a-number</Amt><CdtDbtInd>CRDT</CdtDbtInd><Dt><Dt>2026-05-31</Dt></Dt></Bal>
                </Stmt>
              </BkToCstmrStmt>
            </Document>
            """;
        var document = new StatementSourceDocument("malformed-amount.xml", Encoding.UTF8.GetBytes(xml));

        _connector.CanHandle(document).Should().BeTrue();
        var result = await _connector.ParseAsync(document);

        result.HasErrors.Should().BeTrue();
        result.Records.Should().BeEmpty("a malformed monetary amount must not become a zero-valued canonical row");
        result.Issues.Should().Contain(issue => issue.Code == "CAMT_BALANCE_BAD_AMOUNT");
    }

    [Fact]
    public async Task Parse_Camt053_ExcludesPendingEntriesFromReconciliation()
    {
        // One booked (BOOK) entry and one pending (PDNG) entry. Only booked movements contribute to the
        // closing booked balance, so the pending entry must be skipped rather than opening a false
        // transaction case that would double-count the movement once it books.
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.053.001.02">
              <BkToCstmrStmt>
                <Stmt>
                  <Acct><Id><IBAN>DE89370400440532013000</IBAN></Id><Ccy>EUR</Ccy></Acct>
                  <Bal><Tp><CdOrPrtry><Cd>CLBD</Cd></CdOrPrtry></Tp><Amt Ccy="EUR">100.00</Amt><CdtDbtInd>CRDT</CdtDbtInd><Dt><Dt>2026-05-31</Dt></Dt></Bal>
                  <Ntry><Amt Ccy="EUR">50.00</Amt><CdtDbtInd>CRDT</CdtDbtInd><Sts>BOOK</Sts><BookgDt><Dt>2026-05-30</Dt></BookgDt></Ntry>
                  <Ntry><Amt Ccy="EUR">75.00</Amt><CdtDbtInd>CRDT</CdtDbtInd><Sts>PDNG</Sts><BookgDt><Dt>2026-05-31</Dt></BookgDt></Ntry>
                </Stmt>
              </BkToCstmrStmt>
            </Document>
            """;
        var document = new StatementSourceDocument("pending-entry.xml", Encoding.UTF8.GetBytes(xml));

        var result = await _connector.ParseAsync(document);

        result.HasErrors.Should().BeFalse();
        var transactions = result.Records.Where(record => record.Kind == StatementRecordKind.Transaction).ToArray();
        transactions.Should().ContainSingle("only the booked entry reconciles");
        transactions[0].CashAmount.Should().Be(50.00m);
        result.Issues.Should().Contain(issue => issue.Code == "CAMT_ENTRY_NOT_BOOKED");
    }

    [Fact]
    public async Task Parse_Camt053_SignsReversedCreditEntriesAsDebits()
    {
        // An entry whose CdtDbtInd is RCRD (reversal of a credit) is a debit: the amount must be
        // negative. Treating every non-DBIT indicator as a positive credit would flip the sign and
        // manufacture false matches or breaks for statements containing reversals.
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.053.001.02">
              <BkToCstmrStmt>
                <Stmt>
                  <Acct><Id><IBAN>DE89370400440532013000</IBAN></Id><Ccy>EUR</Ccy></Acct>
                  <Bal><Tp><CdOrPrtry><Cd>CLBD</Cd></CdOrPrtry></Tp><Amt Ccy="EUR">100.00</Amt><CdtDbtInd>CRDT</CdtDbtInd><Dt><Dt>2026-05-31</Dt></Dt></Bal>
                  <Ntry><Amt Ccy="EUR">40.00</Amt><CdtDbtInd>RCRD</CdtDbtInd><Sts>BOOK</Sts><BookgDt><Dt>2026-05-30</Dt></BookgDt></Ntry>
                </Stmt>
              </BkToCstmrStmt>
            </Document>
            """;
        var document = new StatementSourceDocument("reversed-credit.xml", Encoding.UTF8.GetBytes(xml));

        var result = await _connector.ParseAsync(document);

        result.HasErrors.Should().BeFalse();
        var transaction = result.Records.Single(record => record.Kind == StatementRecordKind.Transaction);
        transaction.CashAmount.Should().Be(-40.00m, "a reversal of a credit is a debit and must be negative");
    }

    [Fact]
    public async Task Parse_Camt053_WithMissingCreditDebitIndicator_IsRejected()
    {
        // The closing balance omits CdtDbtInd. Assuming a positive credit could give a malformed debit
        // balance the wrong sign and exact-match an internal positive, so a row with no recognized
        // credit/debit direction must be rejected rather than defaulted to a credit.
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.053.001.02">
              <BkToCstmrStmt>
                <Stmt>
                  <Acct><Id><IBAN>DE89370400440532013000</IBAN></Id><Ccy>EUR</Ccy></Acct>
                  <Bal><Tp><CdOrPrtry><Cd>CLBD</Cd></CdOrPrtry></Tp><Amt Ccy="EUR">100.00</Amt><Dt><Dt>2026-05-31</Dt></Dt></Bal>
                </Stmt>
              </BkToCstmrStmt>
            </Document>
            """;
        var document = new StatementSourceDocument("missing-direction.xml", Encoding.UTF8.GetBytes(xml));

        var result = await _connector.ParseAsync(document);

        result.HasErrors.Should().BeTrue();
        result.Records.Should().BeEmpty("a balance with no credit/debit direction must not become a signed canonical row");
        result.Issues.Should().Contain(issue => issue.Code == "CAMT_BALANCE_BAD_DIRECTION");
    }

    [Fact]
    public async Task Parse_Camt053_WithUnrecognizedEntryDirection_IsRejected()
    {
        // A booked entry carries an unrecognized CdtDbtInd. It must be rejected, not imported as a
        // positive credit, so a malformed debit cannot exact-match an internal positive transaction.
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.053.001.02">
              <BkToCstmrStmt>
                <Stmt>
                  <Acct><Id><IBAN>DE89370400440532013000</IBAN></Id><Ccy>EUR</Ccy></Acct>
                  <Bal><Tp><CdOrPrtry><Cd>CLBD</Cd></CdOrPrtry></Tp><Amt Ccy="EUR">100.00</Amt><CdtDbtInd>CRDT</CdtDbtInd><Dt><Dt>2026-05-31</Dt></Dt></Bal>
                  <Ntry><Amt Ccy="EUR">50.00</Amt><CdtDbtInd>XXXX</CdtDbtInd><Sts>BOOK</Sts><BookgDt><Dt>2026-05-30</Dt></BookgDt></Ntry>
                </Stmt>
              </BkToCstmrStmt>
            </Document>
            """;
        var document = new StatementSourceDocument("bad-entry-direction.xml", Encoding.UTF8.GetBytes(xml));

        var result = await _connector.ParseAsync(document);

        result.HasErrors.Should().BeTrue();
        result.Records.Should().NotContain(record => record.Kind == StatementRecordKind.Transaction,
            "the entry with no recognized direction must not become a canonical transaction");
        result.Issues.Should().Contain(issue => issue.Code == "CAMT_ENTRY_BAD_DIRECTION");
    }

    [Fact]
    public async Task Parse_Camt053_SignsReversedDebitEntriesAsCredits()
    {
        // An entry whose CdtDbtInd is RDBT (reversal of a debit) is a credit: the amount must be
        // positive. This also proves RDBT is a recognized direction and is not rejected.
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.053.001.02">
              <BkToCstmrStmt>
                <Stmt>
                  <Acct><Id><IBAN>DE89370400440532013000</IBAN></Id><Ccy>EUR</Ccy></Acct>
                  <Bal><Tp><CdOrPrtry><Cd>CLBD</Cd></CdOrPrtry></Tp><Amt Ccy="EUR">100.00</Amt><CdtDbtInd>CRDT</CdtDbtInd><Dt><Dt>2026-05-31</Dt></Dt></Bal>
                  <Ntry><Amt Ccy="EUR">40.00</Amt><CdtDbtInd>RDBT</CdtDbtInd><Sts>BOOK</Sts><BookgDt><Dt>2026-05-30</Dt></BookgDt></Ntry>
                </Stmt>
              </BkToCstmrStmt>
            </Document>
            """;
        var document = new StatementSourceDocument("reversed-debit.xml", Encoding.UTF8.GetBytes(xml));

        var result = await _connector.ParseAsync(document);

        result.HasErrors.Should().BeFalse();
        var transaction = result.Records.Single(record => record.Kind == StatementRecordKind.Transaction);
        transaction.CashAmount.Should().Be(40.00m, "a reversal of a debit is a credit and must be positive");
    }

    [Fact]
    public async Task Parse_Camt053_WithoutAccountIdentifier_IsRejected()
    {
        // The single statement's Acct carries no IBAN or other account id. Falling back to an
        // "unknown-account" placeholder would let the matcher normalize the rows to the run's account and
        // reconcile an unidentifiable statement against the selected Meridian account, so it must be
        // rejected — the same treatment the BAI2 connector gives a blank 03 account number.
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.053.001.02">
              <BkToCstmrStmt>
                <Stmt>
                  <Acct><Ccy>EUR</Ccy></Acct>
                  <Bal><Tp><CdOrPrtry><Cd>CLBD</Cd></CdOrPrtry></Tp><Amt Ccy="EUR">100.00</Amt><CdtDbtInd>CRDT</CdtDbtInd><Dt><Dt>2026-05-31</Dt></Dt></Bal>
                </Stmt>
              </BkToCstmrStmt>
            </Document>
            """;
        var document = new StatementSourceDocument("no-account.xml", Encoding.UTF8.GetBytes(xml));

        var result = await _connector.ParseAsync(document);

        result.HasErrors.Should().BeTrue();
        result.Records.Should().BeEmpty("a camt.053 statement with no account identifier must not commit");
        result.Issues.Should().Contain(issue => issue.Code == "CAMT_MISSING_ACCOUNT_ID");
    }

    [Fact]
    public async Task Parse_Camt053_OverRecordLimit_FailsClosedWithoutCanonicalRows()
    {
        var xml = new StringBuilder()
            .Append("<Document><BkToCstmrStmt><Stmt><Acct><Id><IBAN>DE89370400440532013000</IBAN></Id><Ccy>EUR</Ccy></Acct>");
        for (var index = 0; index <= StatementConnectorLimits.MaxRecords; index++)
        {
            xml.Append("<Bal/>");
        }

        xml.Append("</Stmt></BkToCstmrStmt></Document>");
        var document = new StatementSourceDocument("oversized-record-population.xml", Encoding.UTF8.GetBytes(xml.ToString()));

        var result = await _connector.ParseAsync(document);

        result.HasErrors.Should().BeTrue();
        result.Records.Should().BeEmpty("a record population beyond the deterministic ingress bound must fail closed");
        result.Issues.Should().Contain(issue => issue.Code == "STATEMENT_RECORD_LIMIT_EXCEEDED");
    }
}
