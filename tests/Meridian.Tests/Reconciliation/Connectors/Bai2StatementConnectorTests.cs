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
    public void CanHandle_CsvText_ReturnsFalse()
    {
        var document = new StatementSourceDocument(
            "notes.txt",
            Encoding.UTF8.GetBytes("account,symbol,quantity\nFUND,SPY,10\n"));

        _connector.CanHandle(document).Should().BeFalse("only files that open with a 01, header are BAI2");
    }
}
