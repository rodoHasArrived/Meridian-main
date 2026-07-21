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
    public void CanHandle_CsvText_ReturnsFalse()
    {
        var document = new StatementSourceDocument(
            "notes.txt",
            Encoding.UTF8.GetBytes("account,symbol,quantity\nFUND,SPY,10\n"));

        _connector.CanHandle(document).Should().BeFalse("only files that open with a 01, header are BAI2");
    }
}
