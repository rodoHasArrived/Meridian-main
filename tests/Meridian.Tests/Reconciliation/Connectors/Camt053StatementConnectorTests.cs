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
}
