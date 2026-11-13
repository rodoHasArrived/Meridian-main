using System.Text;
using FluentAssertions;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.FinancialOperations.Reconciliation.Connectors.Bai2;
using Meridian.FinancialOperations.Reconciliation.Connectors.Camt;
using Xunit;

namespace Meridian.Tests.Reconciliation.Connectors;

/// <summary>Shadow onboarding opts in to opening balances; reconciliation runs keep reading closing balances.</summary>
public sealed class StatementOpeningBalanceRoleTests
{
    private const string Bai2WithOpening = """
        01,CITIBANK,MERIDIAN,260531,0800,1,,,2/
        02,MERIDIAN,CITIBANK,1,260531,,USD,2/
        03,0975312468,USD,010,1000000,,,015,1234567,,/
        16,115,250000,,BANKREF01,CUSTREF01,Incoming wire/
        49,2234567,3/
        98,2234567,1,3/
        99,2234567,1,5/
        """;

    [Fact]
    public async Task Camt053_DefaultRole_StillEmitsOnlyTheClosingBookedBalance()
    {
        var result = await new Camt053StatementConnector().ParseAsync(CamtDocument());

        result.Records.Where(record => record.Kind == StatementRecordKind.CashBalance)
            .Should().ContainSingle().Which.CashAmount.Should().Be(12345.67m);
    }

    [Fact]
    public async Task Camt053_OpeningRole_EmitsOnlyTheOpeningBookedBalance()
    {
        var connector = new Camt053StatementConnector(StatementIngressLimits.Default, StatementBalanceRole.Opening);

        var result = await connector.ParseAsync(CamtDocument());

        result.HasErrors.Should().BeFalse();
        var balance = result.Records.Where(record => record.Kind == StatementRecordKind.CashBalance).Should().ContainSingle().Subject;
        balance.CashAmount.Should().Be(10000.00m);
        balance.TradeDate.Should().Be(new DateOnly(2026, 5, 1));
        balance.Account.Should().Be("DE89370400440532013000");
    }

    [Fact]
    public async Task Bai2_DefaultRole_ReadsClosingLedger_OpeningRoleReadsOpeningLedger()
    {
        var document = new StatementSourceDocument("opening.bai", Encoding.UTF8.GetBytes(Bai2WithOpening));

        var closing = await new Bai2StatementConnector().ParseAsync(document);
        var opening = await new Bai2StatementConnector(StatementIngressLimits.Default, StatementBalanceRole.Opening).ParseAsync(document);

        closing.Records.Single(record => record.Kind == StatementRecordKind.CashBalance).CashAmount.Should().Be(12345.67m);
        opening.HasErrors.Should().BeFalse();
        opening.Records.Single(record => record.Kind == StatementRecordKind.CashBalance).CashAmount.Should().Be(10000.00m);
    }

    private static StatementSourceDocument CamtDocument()
        => new("camt053-sample.xml", StatementConnectorTestData.ReadFixture("camt053-sample.xml"));
}
