using FluentAssertions;
using Meridian.Contracts.Ledger;

namespace Meridian.Tests.Storage;

public sealed class AccountingConfigurationPostgresStoreTests
{
    [Fact]
    public void AccountingConfigurationLedgerBookScopeMigration_DefinesReRunnableScopedWorkspaceKeys()
    {
        var sql = ReadMigration("V_ledger_015__accounting_configuration_ledger_book_scope.sql");

        sql.Should().Contain("add column if not exists configuration_scope_id text not null default 'fund'");
        sql.Should().Contain("drop constraint if exists accounting_configuration_chart_nodes_workspace_fkey");
        sql.Should().Contain("drop constraint if exists accounting_configuration_journal_templates_workspace_fkey");
        sql.Should().Contain("drop constraint if exists accounting_configuration_posting_rules_workspace_fkey");
        sql.Should().Contain("drop constraint if exists accounting_configuration_rule_test_cases_workspace_fkey");
        sql.Should().Contain("primary key (fund_profile_id, configuration_scope_id)");
        sql.Should().Contain("references __SCHEMA__.accounting_configuration_workspaces(fund_profile_id, configuration_scope_id)");
        sql.Should().Contain("on __SCHEMA__.accounting_configuration_chart_nodes(fund_profile_id, configuration_scope_id, lower(path))");
        sql.Should().Contain("on __SCHEMA__.accounting_configuration_posting_rules(fund_profile_id, configuration_scope_id, template_id)");
    }

    [Fact]
    public void AccountingConfigurationTenantCompanyScopeMigration_DefinesReRunnableScopedWorkspaceKeys()
    {
        var sql = ReadMigration("V_ledger_016__accounting_configuration_tenant_company_scope.sql");

        sql.Should().Contain("add column if not exists tenant_id text not null default 'all'");
        sql.Should().Contain("add column if not exists company_id text not null default 'all'");
        sql.Should().Contain("drop constraint if exists accounting_configuration_chart_nodes_workspace_fkey");
        sql.Should().Contain("primary key (tenant_id, company_id, fund_profile_id, configuration_scope_id)");
        sql.Should().Contain("references __SCHEMA__.accounting_configuration_workspaces(tenant_id, company_id, fund_profile_id, configuration_scope_id)");
        sql.Should().Contain("on __SCHEMA__.accounting_configuration_chart_nodes(tenant_id, company_id, fund_profile_id, configuration_scope_id, lower(path))");
        sql.Should().Contain("on __SCHEMA__.accounting_configuration_posting_rules(tenant_id, company_id, fund_profile_id, configuration_scope_id, template_id)");
    }

    [Fact]
    public void AccountingConfigurationAuditTenantScopeMigration_DefinesReRunnableAuditScope()
    {
        var sql = ReadMigration("V_ledger_017__accounting_configuration_audit_tenant_scope.sql");

        sql.Should().Contain("add column if not exists tenant_id text null");
        sql.Should().Contain("set tenant_id = workspace.tenant_id");
        sql.Should().Contain("ix_accounting_action_audit_events_tenant_company");
        sql.Should().Contain("tenant_id, company_id, recorded_at_utc desc");
    }

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
            "accounting_configuration_rule_test_cases",
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
            AuditTrail: [],
            RuleTestCases:
            [
                new AccountingRuleTestCaseDto(
                    "interest-accrual-happy-path",
                    "Interest accrual happy path",
                    new RuleDryRunRequestDto(
                        "fund-alpha",
                        "InterestAccrual",
                        100m,
                        "USD",
                        new DateOnly(2026, 6, 30),
                        "controller"),
                    ExpectedRuleId: "rule-interest",
                    ExpectedRuleVersion: "v1",
                    ExpectedGeneratedPostingLines:
                    [
                        new GeneratedPostingLineDto(
                            "debit-cash",
                            "Assets:Cash",
                            AccountingTemplateLineSideDto.Debit,
                            "source-amount",
                            Amount: 100m,
                            Dimensions: new LedgerDimensionSetDto(
                                FundId: "fund-alpha",
                                EntityId: "entity-master",
                                InstrumentId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                                ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                                {
                                    ["Department"] = "Fund Ops"
                                })),
                        new GeneratedPostingLineDto(
                            "credit-interest",
                            "Income:Interest",
                            AccountingTemplateLineSideDto.Credit,
                            "source-amount",
                            Amount: 100m,
                            Dimensions: new LedgerDimensionSetDto(
                                FundId: "fund-alpha",
                                EntityId: "entity-master",
                                ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                                {
                                    ["Department"] = "Fund Ops"
                                }))
                    ],
                    EvidenceLinks: ["evidence://accounting/rule-tests/interest-accrual"])
            ]);

        await database.AccountingConfigurationStore.SaveAsync(workspace);

        var loaded = await database.AccountingConfigurationStore.GetAsync("fund-alpha");

        loaded.Should().NotBeNull();
        loaded!.ChartOfAccounts.Should().ContainSingle(node => node.Path == "Assets:Cash");
        loaded.JournalTemplates.Should().ContainSingle(template => template.TemplateId == "template-interest");
        loaded.PostingRules.Should().ContainSingle(rule => rule.RuleId == "rule-interest");
        var loadedTestCase = loaded.RuleTestCases.Should()
            .ContainSingle(testCase => testCase.TestCaseId == "interest-accrual-happy-path")
            .Subject;
        loadedTestCase.ExpectedGeneratedPostingLines.Should().HaveCount(2);
        var loadedDebitLine = loadedTestCase.ExpectedGeneratedPostingLines.Should()
            .ContainSingle(line => line.LineId == "debit-cash")
            .Subject;
        loadedDebitLine.Dimensions.Should().NotBeNull();
        loadedDebitLine.Dimensions!.InstrumentId.Should().Be(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        loadedDebitLine.Dimensions.ExternalGlDimensions["Department"].Should().Be("Fund Ops");
        loadedTestCase.EvidenceLinks.Should().Contain("evidence://accounting/rule-tests/interest-accrual");
        loaded.Status.Should().Be(AccountingConfigurationStatusDto.Draft);
    }

    [LedgerDatabaseFact]
    public async Task SaveAndGetAsync_IsolatesConfigurationByLedgerBook()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var primaryBookId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var gaapBookId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        await database.AccountingConfigurationStore.SaveAsync(BuildScopedWorkspace(
            primaryBookId,
            "cash-primary",
            "Assets:Cash:Primary",
            "template-primary",
            "rule-primary",
            "InterestAccrual"));
        await database.AccountingConfigurationStore.SaveAsync(BuildScopedWorkspace(
            gaapBookId,
            "cash-gaap",
            "Assets:Cash:Gaap",
            "template-gaap",
            "rule-gaap",
            "InterestAccrual"));

        var primary = await database.AccountingConfigurationStore.GetAsync("fund-alpha", primaryBookId);
        var gaap = await database.AccountingConfigurationStore.GetAsync("fund-alpha", gaapBookId);
        var fundLevel = await database.AccountingConfigurationStore.GetAsync("fund-alpha");

        primary.Should().NotBeNull();
        primary!.LedgerBookId.Should().Be(primaryBookId);
        primary.ChartOfAccounts.Should().ContainSingle(node => node.NodeId == "cash-primary");
        primary.PostingRules.Should().ContainSingle(rule => rule.RuleId == "rule-primary");
        primary.PostingRules.Should().NotContain(rule => rule.RuleId == "rule-gaap");

        gaap.Should().NotBeNull();
        gaap!.LedgerBookId.Should().Be(gaapBookId);
        gaap.ChartOfAccounts.Should().ContainSingle(node => node.NodeId == "cash-gaap");
        gaap.PostingRules.Should().ContainSingle(rule => rule.RuleId == "rule-gaap");
        gaap.PostingRules.Should().NotContain(rule => rule.RuleId == "rule-primary");

        fundLevel.Should().BeNull("book-scoped accounting configuration must not leak into the fund-level workspace");
    }

    [LedgerDatabaseFact]
    public async Task SaveAndGetAsync_IsolatesConfigurationByTenantAndCompany()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();

        await database.AccountingConfigurationStore.SaveAsync(BuildScopedWorkspace(
            ledgerBookId: null,
            "cash-alpha",
            "Assets:Cash",
            "template-alpha",
            "rule-alpha",
            "InterestAccrual",
            TenantId: "tenant-alpha",
            CompanyId: "company-alpha"));
        await database.AccountingConfigurationStore.SaveAsync(BuildScopedWorkspace(
            ledgerBookId: null,
            "cash-beta",
            "Assets:Cash",
            "template-beta",
            "rule-beta",
            "InterestAccrual",
            TenantId: "tenant-beta",
            CompanyId: "company-beta"));

        var alpha = await database.AccountingConfigurationStore.GetAsync("fund-alpha", tenantId: "tenant-alpha", companyId: "company-alpha");
        var beta = await database.AccountingConfigurationStore.GetAsync("fund-alpha", tenantId: "tenant-beta", companyId: "company-beta");
        var unscoped = await database.AccountingConfigurationStore.GetAsync("fund-alpha");

        alpha.Should().NotBeNull();
        alpha!.TenantId.Should().Be("tenant-alpha");
        alpha.CompanyId.Should().Be("company-alpha");
        alpha.ChartOfAccounts.Should().ContainSingle(node => node.NodeId == "cash-alpha");
        alpha.PostingRules.Should().ContainSingle(rule => rule.RuleId == "rule-alpha");
        alpha.PostingRules.Should().NotContain(rule => rule.RuleId == "rule-beta");

        beta.Should().NotBeNull();
        beta!.TenantId.Should().Be("tenant-beta");
        beta.CompanyId.Should().Be("company-beta");
        beta.ChartOfAccounts.Should().ContainSingle(node => node.NodeId == "cash-beta");
        beta.PostingRules.Should().ContainSingle(rule => rule.RuleId == "rule-beta");
        beta.PostingRules.Should().NotContain(rule => rule.RuleId == "rule-alpha");

        unscoped.Should().BeNull("tenant-scoped accounting configuration must not leak into the unscoped workspace");
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
            EvidenceLinks: ["wpf://accounting/configure"],
            CompanyId: "company-alpha",
            ReportGroupPrincipalIds: ["Accounting", "reporting-ops"],
            TenantId: "tenant-alpha");

        await database.AccountingConfigurationStore.AppendAsync(auditEvent);

        var loaded = await database.AccountingConfigurationStore.ListAsync("fund-alpha");

        loaded.Should().ContainSingle();
        loaded[0].Actor.Should().Be("ops-user");
        loaded[0].CorrelationId.Should().Be("postgres-audit-test");
        loaded[0].EvidenceLinks.Should().Contain("wpf://accounting/configure");
        loaded[0].CompanyId.Should().Be("company-alpha");
        loaded[0].TenantId.Should().Be("tenant-alpha");
        loaded[0].ReportGroupPrincipalIds.Should().BeEquivalentTo(["Accounting", "reporting-ops"]);
    }

    [LedgerDatabaseFact]
    public async Task AuditStore_FiltersAccountingActionEventsByTenantAndCompany()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();

        await database.AccountingConfigurationStore.AppendAsync(new AccountingActionAuditEventDto(
            AuditEventId: Guid.NewGuid(),
            RecordedAtUtc: DateTimeOffset.Parse("2026-06-01T12:00:00Z"),
            Actor: "tenant-alpha-controller",
            Action: "chart.upsert",
            FundProfileId: "fund-alpha",
            LedgerBookId: null,
            CorrelationId: "tenant-alpha-audit",
            BeforeHash: "before-alpha",
            AfterHash: "after-alpha",
            ValidationIssues: [],
            EvidenceLinks: ["evidence://tenant-alpha/configuration"],
            CompanyId: "company-shared",
            TenantId: "tenant-alpha"));

        await database.AccountingConfigurationStore.AppendAsync(new AccountingActionAuditEventDto(
            AuditEventId: Guid.NewGuid(),
            RecordedAtUtc: DateTimeOffset.Parse("2026-06-01T12:01:00Z"),
            Actor: "tenant-beta-controller",
            Action: "chart.upsert",
            FundProfileId: "fund-alpha",
            LedgerBookId: null,
            CorrelationId: "tenant-beta-audit",
            BeforeHash: "before-beta",
            AfterHash: "after-beta",
            ValidationIssues: [],
            EvidenceLinks: ["evidence://tenant-beta/configuration"],
            CompanyId: "company-shared",
            TenantId: "tenant-beta"));

        var alpha = await database.AccountingConfigurationStore.ListAsync(
            "fund-alpha",
            tenantId: "tenant-alpha",
            companyId: "company-shared");
        var beta = await database.AccountingConfigurationStore.ListAsync(
            "fund-alpha",
            tenantId: "tenant-beta",
            companyId: "company-shared");
        var company = await database.AccountingConfigurationStore.ListAsync(
            "fund-alpha",
            companyId: "company-shared");

        alpha.Should().ContainSingle(item =>
            item.Actor == "tenant-alpha-controller" &&
            item.TenantId == "tenant-alpha" &&
            item.CompanyId == "company-shared");
        alpha.Should().NotContain(item => item.TenantId == "tenant-beta");
        beta.Should().ContainSingle(item =>
            item.Actor == "tenant-beta-controller" &&
            item.TenantId == "tenant-beta" &&
            item.CompanyId == "company-shared");
        beta.Should().NotContain(item => item.TenantId == "tenant-alpha");
        company.Should().HaveCount(2);
    }

    private static AccountingConfigurationWorkspaceDto BuildScopedWorkspace(
        Guid? ledgerBookId,
        string nodeId,
        string accountPath,
        string templateId,
        string ruleId,
        string sourceEventType,
        string? TenantId = null,
        string? CompanyId = null)
        => new(
            FundProfileId: "fund-alpha",
            LedgerBookId: ledgerBookId,
            Status: AccountingConfigurationStatusDto.Draft,
            ConfigurationVersion: "v1",
            UpdatedAtUtc: DateTimeOffset.Parse("2026-06-01T12:00:00Z"),
            LedgerBooks: [],
            ChartOfAccounts:
            [
                new ChartOfAccountsNodeDto(nodeId, accountPath, nodeId, "Asset")
            ],
            JournalTemplates:
            [
                new JournalEntryTemplateDto(
                    templateId,
                    templateId,
                    "Book-scoped template.",
                    [
                        new JournalEntryTemplateLineDto("debit", accountPath, AccountingTemplateLineSideDto.Debit, 100m),
                        new JournalEntryTemplateLineDto("credit", accountPath, AccountingTemplateLineSideDto.Credit, 100m)
                    ])
            ],
            PostingRules:
            [
                new PostingRuleDto(ruleId, ruleId, sourceEventType, templateId)
            ],
            ValidationIssues: [],
            AuditTrail: [],
            RuleTestCases: [],
            TenantId: TenantId,
            CompanyId: CompanyId);

    private static string ReadMigration(string fileName)
    {
        var root = FindRepoRoot();
        var path = Path.Combine(root, "src", "Meridian.Storage", "Ledger", "Migrations", fileName);
        return File.ReadAllText(path);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "Meridian.Storage")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate Meridian repository root.");
    }
}
