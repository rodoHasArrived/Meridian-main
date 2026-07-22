using System.Text;
using FluentAssertions;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.FinancialOperations.Reconciliation.Connectors.Bai2;
using Xunit;

namespace Meridian.Tests.Reconciliation.Connectors;

public sealed class Bai2StatementConnectorTests
{
    private readonly Bai2StatementConnector _connector = new();

    [Fact]
    public async Task Parse_Bai2_YieldsClosingLedgerBalanceAndSignedTransactions()
    {
        var document = new StatementSourceDocument(
            "bai2-sample.bai",
            StatementConnectorTestData.ReadFixture("bai2-sample.bai"));

        _connector.CanHandle(document).Should().BeTrue();
        var result = await _connector.ParseAsync(document);

        result.HasErrors.Should().BeFalse();
        result.ConnectorId.Should().Be(Bai2StatementConnector.ConnectorId);
        result.Records.Should().HaveCount(3);
        result.Records.Should().OnlyContain(record => record.Account == "0975312468");
        result.Records.Should().OnlyContain(record => record.Currency == "USD");

        var balance = result.Records.Single(record => record.Kind == StatementRecordKind.CashBalance);
        balance.CashAmount.Should().Be(12345.67m, "BAI2 minor units (cents) scale to major units");
        balance.TradeDate.Should().Be(new DateOnly(2026, 5, 31));

        var credit = result.Records.Single(record => record.ExternalTransactionId == "CUSTREF01");
        credit.Kind.Should().Be(StatementRecordKind.Transaction);
        credit.CashAmount.Should().Be(2500.00m, "type code 115 is a credit");

        var debit = result.Records.Single(record => record.ExternalTransactionId == "CUSTREF02");
        debit.CashAmount.Should().Be(-154.33m, "type code 475 is a debit");
    }

    [Fact]
    public async Task Parse_Bai2_WithMultipleAccounts_IsRejected()
    {
        // Two 03 account-identifier records for two different accounts. The statement-run matcher would
        // normalize every row to the run's single external account, so committing this file would compare
        // one account's balances against another account's Meridian records. The connector must reject it.
        const string bai2 = """
            01,CITIBANK,MERIDIAN,260531,0800,1,,,2/
            02,MERIDIAN,CITIBANK,1,260531,,USD,2/
            03,0975312468,USD,015,1234567,,/
            16,115,250000,,BANKREF01,CUSTREF01,Incoming wire/
            03,1122334455,USD,015,7654321,,/
            16,475,15433,,BANKREF02,CUSTREF02,Service fee/
            49,1234567,3/
            98,1234567,1,3/
            99,1234567,1,5/
            """;
        var document = new StatementSourceDocument("multi-account.bai", Encoding.UTF8.GetBytes(bai2));

        _connector.CanHandle(document).Should().BeTrue();
        var result = await _connector.ParseAsync(document);

        result.HasErrors.Should().BeTrue();
        result.Records.Should().BeEmpty("a multi-account BAI2 file must not commit into a single-account run");
        result.Issues.Should().Contain(issue => issue.Code == "BAI2_MULTIPLE_ACCOUNTS");
    }

    [Fact]
    public async Task Parse_Bai2_WithMultipleGroupsForOneAccount_IsRejected()
    {
        // Two 02 group headers for the SAME account across different statement dates (30 Apr and 31 May),
        // each a well-formed group with its own 49/98 trailers. Combining them would give the matcher two
        // closing balances for one internal cash record under the single operator-supplied period, letting
        // it match one and open a false break for the other, so the connector must reject the file.
        const string bai2 = """
            01,CITIBANK,MERIDIAN,260531,0800,1,,,2/
            02,MERIDIAN,CITIBANK,1,260430,,USD,2/
            03,0975312468,USD,015,1000000,,/
            16,115,250000,,BANKREF01,CUSTREF01,Incoming wire/
            49,1250000,3/
            98,1250000,1,3/
            02,MERIDIAN,CITIBANK,1,260531,,USD,2/
            03,0975312468,USD,015,2000000,,/
            16,115,500000,,BANKREF02,CUSTREF02,Incoming wire/
            49,2500000,3/
            98,2500000,1,3/
            99,3750000,2,10/
            """;
        var document = new StatementSourceDocument("multi-group.bai", Encoding.UTF8.GetBytes(bai2));

        _connector.CanHandle(document).Should().BeTrue();
        var result = await _connector.ParseAsync(document);

        result.HasErrors.Should().BeTrue();
        result.Records.Should().BeEmpty("multiple statement groups for one account must not combine into a single run");
        result.Issues.Should().Contain(issue => issue.Code == "BAI2_MULTIPLE_GROUPS");
    }

    [Fact]
    public async Task Parse_Bai2_WithBlankAccountIdentifier_IsRejected()
    {
        // Two 03 account sections whose required account-number field is blank. Both would otherwise share
        // the "unknown-account" placeholder, collapsing to one distinct account and slipping past the
        // multi-account guard, so a section could reconcile against the selected Meridian account. A
        // missing 03 account identifier must be a parse error instead.
        const string bai2 = """
            01,CITIBANK,MERIDIAN,260531,0800,1,,,2/
            02,MERIDIAN,CITIBANK,1,260531,,USD,2/
            03,,USD,015,1000000,,/
            16,115,250000,,BANKREF01,CUSTREF01,Incoming wire/
            03,,USD,015,2000000,,/
            16,115,500000,,BANKREF02,CUSTREF02,Incoming wire/
            49,3750000,5/
            98,3750000,1,5/
            99,3750000,1,9/
            """;
        var document = new StatementSourceDocument("blank-account.bai", Encoding.UTF8.GetBytes(bai2));

        _connector.CanHandle(document).Should().BeTrue();
        var result = await _connector.ParseAsync(document);

        result.HasErrors.Should().BeTrue();
        result.Records.Should().BeEmpty("a BAI2 file with an unidentifiable account must not commit");
        result.Issues.Should().Contain(issue => issue.Code == "BAI2_MISSING_ACCOUNT_ID");
    }

    [Fact]
    public async Task Parse_Bai2_WithTransactionOutsideAccountSection_IsRejected()
    {
        // A 02 group carrying a 16 transaction but no 03 account-identifier record. The row would be
        // emitted under the initial "unknown-account" and normalized to the run's account, so an
        // unidentifiable statement could reconcile against the selected Meridian account. Reject it.
        const string bai2 = """
            01,CITIBANK,MERIDIAN,260531,0800,1,,,2/
            02,MERIDIAN,CITIBANK,1,260531,,USD,2/
            16,115,250000,,BANKREF01,CUSTREF01,Incoming wire/
            98,250000,1,3/
            99,250000,1,5/
            """;
        var document = new StatementSourceDocument("no-account-section.bai", Encoding.UTF8.GetBytes(bai2));

        _connector.CanHandle(document).Should().BeTrue();
        var result = await _connector.ParseAsync(document);

        result.HasErrors.Should().BeTrue();
        result.Records.Should().BeEmpty("a BAI2 transaction outside an account section must not commit");
        result.Issues.Should().Contain(issue => issue.Code == "BAI2_TRANSACTION_WITHOUT_ACCOUNT");
    }

    [Fact]
    public async Task Parse_Bai2_WithoutAccountSection_IsRejected()
    {
        // An empty custody statement with a valid group and trailers still must identify the account it
        // covers. Otherwise import would succeed without account evidence and operators could mistake it
        // for an empty statement for the selected Meridian account.
        const string bai2 = """
            01,CITIBANK,MERIDIAN,260531,0800,1,,,2/
            02,MERIDIAN,CITIBANK,1,260531,,USD,2/
            98,0,1,2/
            99,0,1,4/
            """;
        var document = new StatementSourceDocument("missing-account-section.bai", Encoding.UTF8.GetBytes(bai2));

        _connector.CanHandle(document).Should().BeTrue();
        var result = await _connector.ParseAsync(document);

        result.HasErrors.Should().BeTrue();
        result.Records.Should().BeEmpty("a BAI2 statement must identify an account even when it has no activity");
        result.Issues.Should().Contain(issue => issue.Code == "BAI2_MISSING_ACCOUNT_SECTION");
    }

    [Fact]
    public async Task Parse_Bai2_ScalesAmountsByDeclaredCurrencyExponent()
    {
        // JPY has no minor unit, so a BAI2 amount of 10000 is 10000 yen, not 100. Assuming cents would
        // understate every balance and transaction by two orders of magnitude for zero-decimal currencies.
        const string bai2 = """
            01,CITIBANK,MERIDIAN,260531,0800,1,,,2/
            02,MERIDIAN,CITIBANK,1,260531,,JPY,2/
            03,0975312468,JPY,015,10000,,/
            16,115,25000,,BANKREF01,CUSTREF01,Incoming wire/
            49,10000,3/
            98,10000,1,3/
            99,10000,1,5/
            """;
        var document = new StatementSourceDocument("jpy.bai", Encoding.UTF8.GetBytes(bai2));

        _connector.CanHandle(document).Should().BeTrue();
        var result = await _connector.ParseAsync(document);

        result.HasErrors.Should().BeFalse();
        result.Records.Should().OnlyContain(record => record.Currency == "JPY");

        var balance = result.Records.Single(record => record.Kind == StatementRecordKind.CashBalance);
        balance.CashAmount.Should().Be(10000m, "JPY amounts carry no minor unit and must not be divided by 100");

        var transaction = result.Records.Single(record => record.Kind == StatementRecordKind.Transaction);
        transaction.CashAmount.Should().Be(25000m, "type code 115 is a JPY credit expressed in whole yen");
    }

    [Fact]
    public async Task Parse_Bai2_TruncatedFileMissingTrailers_IsRejected()
    {
        // A file with valid 03/16 records but no 49/98/99 trailers is truncated. Accepting it would
        // reconcile a partial bank statement as if it were complete, so it must be rejected.
        const string bai2 = """
            01,CITIBANK,MERIDIAN,260531,0800,1,,,2/
            02,MERIDIAN,CITIBANK,1,260531,,USD,2/
            03,0975312468,USD,015,1234567,,/
            16,115,250000,,BANKREF01,CUSTREF01,Incoming wire/
            """;
        var document = new StatementSourceDocument("truncated.bai", Encoding.UTF8.GetBytes(bai2));

        _connector.CanHandle(document).Should().BeTrue();
        var result = await _connector.ParseAsync(document);

        result.HasErrors.Should().BeTrue();
        result.Records.Should().BeEmpty("a truncated BAI2 file must not commit as a reconciled statement");
        result.Issues.Should().Contain(issue => issue.Code == "BAI2_MISSING_FILE_TRAILER");
    }

    [Fact]
    public void CanHandle_CsvText_ReturnsFalse()
    {
        var document = new StatementSourceDocument(
            "notes.txt",
            Encoding.UTF8.GetBytes("account,symbol,quantity\nFUND,SPY,10\n"));

        _connector.CanHandle(document).Should().BeFalse("only files that open with a 01, header are BAI2");
    }
}
