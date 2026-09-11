using System.Text;
using FluentAssertions;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.FinancialOperations.Reconciliation.Connectors.Bai2;
using Meridian.FinancialOperations.Reconciliation.Connectors.Camt;
using Meridian.Infrastructure.Reconciliation;
using Xunit;

namespace Meridian.Tests.Reconciliation.Connectors;

public sealed class BankStatementCurrencyEvidenceTests : IDisposable
{
    private readonly string _root = StatementConnectorTestData.CreateTempRoot("bank-currency-evidence");

    [Theory]
    [InlineData("camt", "missing")]
    [InlineData("camt", "blank-amount")]
    [InlineData("camt", "invalid-amount")]
    [InlineData("bai2", "missing")]
    [InlineData("bai2", "invalid")]
    [InlineData("bai2", "previous-group")]
    public async Task MonthEndBankStatement_UnknownCurrencyCannotCreateAmountsOrRetainedEvidence(string format, string defect)
    {
        var (source, connector, account) = Fixture(format);
        if (format == "camt")
        {
            source = defect switch
            {
                "missing" => source.Replace("<Ccy>EUR</Ccy>", "").Replace(" Ccy=\"EUR\"", ""),
                "blank-amount" => source.Replace("Ccy=\"EUR\"", "Ccy=\" \""),
                _ => source.Replace("Ccy=\"EUR\"", "Ccy=\"???\"")
            };
        }
        else
        {
            source = source.Replace(",USD,", defect == "invalid" ? ",???," : ",,");
            if (defect == "previous-group")
            {
                source = source.Replace("02,MERIDIAN", "02,MERIDIAN,CITIBANK,1,260531,,JPY,2/\n98,0,0,0/\n02,MERIDIAN");
            }
        }

        var document = new StatementSourceDocument($"month-end-{format}", Encoding.UTF8.GetBytes(source));
        var parsed = await connector.ParseAsync(document);
        parsed.HasErrors.Should().BeTrue();
        parsed.Records.Should().BeEmpty("currency evidence must be checked before monetary values are admitted");
        parsed.Issues.Should().Contain(issue => issue.Code.Contains("CURRENCY", StringComparison.Ordinal));
        var (service, workflow) = CreateService(connector);
        (await service.ValidateAsync(document, connector.Descriptor.ConnectorId)).IsValid.Should().BeFalse();
        (await service.PreviewAsync(document, connector.Descriptor.ConnectorId)).Status.Should().Be("NeedsAttention");
        var refusal = await Assert.ThrowsAsync<InvalidDataException>(() => service.CommitAsync(Request(document, connector.Descriptor.ConnectorId, account)));
        refusal.Message.Should().Contain("CURRENCY");
        Directory.Exists(Path.Combine(_root, "reconciliation", "statement-connector-imports")).Should().BeFalse();
        (await workflow.ListImportsAsync()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("camt", "account")]
    [InlineData("camt", "amount")]
    [InlineData("bai2", "group")]
    [InlineData("bai2", "account")]
    public async Task MonthEndBankStatement_ExplicitContainingCurrencyPreservesCommittedAmounts(string format, string evidence)
    {
        var (source, connector, account) = Fixture(format);
        source = evidence switch
        {
            "account" when format == "camt" => source.Replace(" Ccy=\"EUR\"", ""),
            "amount" => source.Replace("<Ccy>EUR</Ccy>", ""),
            "group" => source.Replace("03,0975312468,USD,", "03,0975312468,,"),
            _ => source.Replace("260531,,USD,2/", "260531,,,2/")
        };
        var document = new StatementSourceDocument($"month-end-{format}", Encoding.UTF8.GetBytes(source));
        var parsed = await connector.ParseAsync(document);
        parsed.HasErrors.Should().BeFalse();
        parsed.Records.Should().HaveCount(3).And.OnlyContain(row => row.Currency == (format == "camt" ? "EUR" : "USD"));
        parsed.Records.Select(row => row.CashAmount).Should().BeEquivalentTo(new[] { 12345.67m, 2500m, -154.33m });
        var (service, _) = CreateService(connector);
        (await service.ValidateAsync(document, connector.Descriptor.ConnectorId)).IsValid.Should().BeTrue();
        var committed = await service.CommitAsync(Request(document, connector.Descriptor.ConnectorId, account));
        committed.RecordCount.Should().Be(3);
        File.Exists(Path.Combine(_root, committed.RetainedCanonicalPath)).Should().BeTrue();
    }

    private static (string Source, IStatementConnector Connector, string Account) Fixture(string format)
        => format == "camt"
            ? (Encoding.UTF8.GetString(StatementConnectorTestData.ReadFixture("camt053-sample.xml")), new Camt053StatementConnector(), "DE89370400440532013000")
            : (Encoding.UTF8.GetString(StatementConnectorTestData.ReadFixture("bai2-sample.bai")), new Bai2StatementConnector(), "0975312468");

    private (StatementImportService Service, IStatementRunWorkflowService Workflow) CreateService(IStatementConnector connector)
    {
        var store = new JsonCanonicalStatementStore(_root);
        var workflow = StatementRunWorkflowService.CreateEphemeralForTesting(store,
            new JsonReconciliationCaseStore(_root), new JsonReconciliationBreakStore(_root),
            new CsvBrokerStatementService(store), new StatementReconciliationContextAdapter(new StatementReconciliationService()));
        var catalog = new StatementMappingProfileCatalog(new FileStatementMappingProfileStore(_root));
        return (new StatementImportService(new StatementConnectorRegistry([connector]), catalog, workflow, _root), workflow);
    }

    private static StatementImportCommitRequest Request(StatementSourceDocument document, string connectorId, string account)
        => new(document, connectorId, "custodian", "May bank", "FUND-A", account,
            new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), null, "bank-operator");

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
