using System.Text;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Onboarding;
using Meridian.FinancialOperations.Onboarding.OpeningBalances;
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

    [Fact]
    public async Task Bai2_OpeningRole_DoesNotSubstituteOpeningAvailableForMissingOpeningLedger()
    {
        var connector = new Bai2StatementConnector(StatementIngressLimits.Default, StatementBalanceRole.Opening);

        var result = await connector.ParseAsync(Bai2WithOnlyOpeningAvailable());

        result.HasErrors.Should().BeFalse();
        result.Records.Should().NotContain(record => record.Kind == StatementRecordKind.CashBalance);
        result.Records.Should().ContainSingle(record => record.Kind == StatementRecordKind.Transaction);
    }

    [Fact]
    public async Task Bai2_OpeningRole_UsesLedgerWhenOpeningAvailableDiffers()
    {
        var source = Bai2WithOpening.Replace("010,1000000,,,", "010,1000000,,,040,900000,,,", StringComparison.Ordinal);
        var connector = new Bai2StatementConnector(StatementIngressLimits.Default, StatementBalanceRole.Opening);

        var result = await connector.ParseAsync(new("opening-with-hold.bai", Encoding.UTF8.GetBytes(source)));

        result.HasErrors.Should().BeFalse();
        result.Records.Where(record => record.Kind == StatementRecordKind.CashBalance)
            .Should().ContainSingle().Which.CashAmount.Should().Be(10000m);
    }

    [Fact]
    public async Task OpeningSourceReader_RefusesAvailableOnlyBai2AsOpeningBookedEvidence()
    {
        var asOf = new DateOnly(2026, 5, 31);
        var scope = new OnboardingScopeDto("test-tenant", "test-company",
            "ba7467d1-26e0-4f3b-9baa-528063de6774", "test-fund",
            Guid.Parse("c0749249-2738-44be-b336-fb586f5b200b"), ["104d9cb4-60ca-472a-9564-d2d664098405"], asOf, asOf);
        var criteria = new OnboardingCriteriaDto([asOf], ["Balance"], 0m, 0m, 0m, 100m,
            ["test-reviewer"], 1, false, "Compare opening booked cash.");
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var workspace = new OnboardingWorkspaceDto("opening-test", "Opening cash", "test-owner", scope,
            1, 1, now, now, criteria, [], [], [], [], [], [],
            new("AwaitingSources", false, 0, [], [], [], 0, 1, []), "ReadOnly");
        var document = Bai2WithOnlyOpeningAvailable();
        var request = new ImportOnboardingOpeningBalancesRequestDto(1, OnboardingOpeningSourceKinds.StatementOpening,
            "test-map", "v1", [new("0975312468", scope.AccountIds[0])],
            document.FileName, Convert.ToBase64String(document.Content.Span));
        var reader = new OnboardingOpeningSourceReader();

        var read = () => reader.ReadAsync(workspace, request);

        await read.Should().ThrowAsync<OnboardingValidationException>().WithMessage("*no opening booked balance*");
    }

    private static StatementSourceDocument Bai2WithOnlyOpeningAvailable()
        => new("available-only.bai", Encoding.UTF8.GetBytes(
            Bai2WithOpening.Replace("010,1000000", "040,900000", StringComparison.Ordinal)));

    private static StatementSourceDocument CamtDocument()
        => new("camt053-sample.xml", StatementConnectorTestData.ReadFixture("camt053-sample.xml"));
}
