using FluentAssertions;
using Meridian.Contracts.Ledger;

namespace Meridian.Tests.Storage;

public sealed class AccountingConfigurationPostgresStoreTests
{
    [LedgerDatabaseFact]
    public async Task Migration_CreatesAccountingConfigurationTables()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();

        var tables = await database.GetTableNamesAsync();

        tables.Should().Contain([
            "accounting_configuration_workspaces",
            "accounting_configuration_chart_nodes",
            "accounting_configuration_journal_templates",
            "accounting_configuration_posting_rules",
            "accounting_action_audit_events"
        ]);
    }

    [LedgerDatabaseFact]
    public async Task SaveAndGetAsync_RoundTripsConfigurationWorkspace()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var workspace = new AccountingConfigurationWorkspaceDto(
            FundProfileId: "fund-alpha",
            LedgerBookId: null,
            Status: AccountingConfigurationStatusDto.Draft,
            ConfigurationVersion: "v1",
            UpdatedAtUtc: DateTimeOffset.Parse("2026-06-01T12:00:00Z"),
            LedgerBooks: [],
            ChartOfAccounts:
            [
                new ChartOfAccountsNodeDto("cash", "Assets:Cash", "Cash", "Asset")
            ],
            JournalTemplates:
            [
                new JournalEntryTemplateDto(
                    "template-interest",
                    "Interest accrual",
                    "Recognize interest.",
                    [
                        new JournalEntryTemplateLineDto("debit-cash", "Assets:Cash", AccountingTemplateLineSideDto.Debit, 100m),
                        new JournalEntryTemplateLineDto("credit-interest", "Income:Interest", AccountingTemplateLineSideDto.Credit, 100m)
                    ])
            ],
            PostingRules:
            [
                new PostingRuleDto("rule-interest", "Interest accrual", "InterestAccrual", "template-interest")
            ],
            ValidationIssues: [],
            AuditTrail: []);

        await database.AccountingConfigurationStore.SaveAsync(workspace);

        var loaded = await database.AccountingConfigurationStore.GetAsync("fund-alpha");

        loaded.Should().NotBeNull();
        loaded!.ChartOfAccounts.Should().ContainSingle(node => node.Path == "Assets:Cash");
        loaded.JournalTemplates.Should().ContainSingle(template => template.TemplateId == "template-interest");
        loaded.PostingRules.Should().ContainSingle(rule => rule.RuleId == "rule-interest");
        loaded.Status.Should().Be(AccountingConfigurationStatusDto.Draft);
    }

    [LedgerDatabaseFact]
    public async Task AuditStore_AppendsAndFiltersAccountingActionEvents()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var auditEvent = new AccountingActionAuditEventDto(
            AuditEventId: Guid.NewGuid(),
            RecordedAtUtc: DateTimeOffset.Parse("2026-06-01T12:00:00Z"),
            Actor: "ops-user",
            Action: "configuration.activate",
            FundProfileId: "fund-alpha",
            LedgerBookId: null,
            CorrelationId: "postgres-audit-test",
            BeforeHash: "before",
            AfterHash: "after",
            ValidationIssues: [],
            EvidenceLinks: ["wpf://accounting/configure"]);

        await database.AccountingConfigurationStore.AppendAsync(auditEvent);

        var loaded = await database.AccountingConfigurationStore.ListAsync("fund-alpha");

        loaded.Should().ContainSingle();
        loaded[0].Actor.Should().Be("ops-user");
        loaded[0].CorrelationId.Should().Be("postgres-audit-test");
        loaded[0].EvidenceLinks.Should().Contain("wpf://accounting/configure");
    }
}
