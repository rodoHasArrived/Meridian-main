using System.Globalization;
using FluentAssertions;
using Meridian.Contracts.Ledger;
using Npgsql;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;

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
    public async Task AuditStore_AppendIsIdempotentOnTheEventId()
    {
        // audit_event_id is the primary key, so a repeat raised a unique violation and the append
        // threw. That matters because the repeat is what RecoverPendingAuditAsync does after a
        // crash between a mutation and its audit -- so the one path written to complete an
        // interrupted audit was the path that could not run twice.
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var auditEvent = AuditEvent("configuration.activate");

        await database.AccountingConfigurationStore.AppendAsync(auditEvent);
        await database.AccountingConfigurationStore.AppendAsync(auditEvent);

        (await database.AccountingConfigurationStore.ListAsync("fund-alpha")).Should().ContainSingle();

        // The chain still advances afterwards: the repeat consumed no sequence.
        await database.AccountingConfigurationStore.AppendAsync(AuditEvent("chart.upsert"));
        (await database.AccountingConfigurationStore.ListAsync("fund-alpha")).Should().HaveCount(2);
    }

    [LedgerDatabaseFact]
    public async Task AuditStore_RefusesTwoDifferentEventsSharingOneId()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var first = AuditEvent("configuration.activate");
        await database.AccountingConfigurationStore.AppendAsync(first);

        var collision = async () => await database.AccountingConfigurationStore
            .AppendAsync(first with { Action = "chart.upsert" });

        (await collision.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("different content");
    }

    [LedgerDatabaseFact]
    public async Task AuditStore_RefusesToAppendOntoAChainWrittenByANewerSchema()
    {
        // The head records schema_version so a build that cannot implement a chain's hashing rules
        // refuses rather than guesses. The file posture already did; here the column was selected by
        // nobody, so a v2 chain would have been checked with v1 rules -- reporting EventMutated over
        // events nobody touched, or, worse, accepting and laying a v1 link on a v2 history that no
        // build could ever verify again.
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        await database.AccountingConfigurationStore.AppendAsync(AuditEvent("configuration.activate"));

        await database.SetAuditChainSchemaVersionAsync(AccountingAuditChainState.CurrentSchemaVersion + 1);

        var append = async () => await database.AccountingConfigurationStore
            .AppendAsync(AuditEvent("chart.upsert"));

        var refusal = await append.Should().ThrowAsync<AccountingAuditChainIntegrityException>();
        refusal.Which.Verification.Status.Should()
            .Be(AccountingAuditChainStatus.UnsupportedSchemaVersion);

        // Nothing was written, and the events that were already there are untouched.
        (await database.AccountingConfigurationStore.ListAsync("fund-alpha")).Should().ContainSingle();

        // Restoring the supported version lets the chain continue, so the refusal is a gate rather
        // than a permanent stop.
        await database.SetAuditChainSchemaVersionAsync(AccountingAuditChainState.CurrentSchemaVersion);
        await database.AccountingConfigurationStore.AppendAsync(AuditEvent("chart.upsert"));
        (await database.AccountingConfigurationStore.ListAsync("fund-alpha")).Should().HaveCount(2);
    }

    private static AccountingActionAuditEventDto AuditEvent(string action)
        => new(
            AuditEventId: Guid.NewGuid(),
            RecordedAtUtc: DateTimeOffset.Parse("2026-06-01T12:00:00Z"),
            Actor: "ops-user",
            Action: action,
            FundProfileId: "fund-alpha",
            LedgerBookId: null,
            CorrelationId: "postgres-audit-test",
            BeforeHash: "before",
            AfterHash: "after",
            ValidationIssues: [],
            EvidenceLinks: ["wpf://accounting/configure"],
            CompanyId: "company-alpha",
            ReportGroupPrincipalIds: ["Accounting"],
            TenantId: "tenant-alpha");

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

    [LedgerDatabaseFact]
    public async Task AnEventRecordedFinerThanAMicrosecond_DoesNotMakeTheNextAppendReportTampering()
    {
        // Fifteenth Codex review round asked whether the audit chain survives a sub-microsecond
        // RecordedAtUtc: AccountingAuditChain truncates to microseconds when it digests, while
        // timestamptz holds microseconds and PostgreSQL rounds a finer TEXT literal rather than
        // truncating it. VerifyChainHeadAsync recomputes the payload digest from the retained row
        // before every append, so a row holding a different instant than the one that was hashed
        // would report its predecessor as mutated and stop the chain permanently.
        //
        // It holds, because Npgsql truncates the same way when it encodes the parameter, so
        // PostgreSQL never sees a finer value. That is a property of the driver rather than of any
        // code here, which is exactly why it is worth pinning: a protocol or driver change that
        // started sending text literals would reintroduce it silently.
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var store = database.AccountingConfigurationStore;

        // Ticks ending in 7: finer than a microsecond, and rounds up rather than down.
        var roundsUp = new DateTimeOffset(
            DateTime.SpecifyKind(DateTime.Parse("2026-06-01T12:00:00.0000000Z").AddTicks(1234567), DateTimeKind.Utc));
        var first = AuditEvent(recordedAtUtc: roundsUp);
        await store.AppendAsync(first);

        var appendNext = async () => await store.AppendAsync(AuditEvent());

        await appendNext.Should().NotThrowAsync(
            "the digest recorded for an event must name the instant the row actually holds");
        var retained = (await store.ListAsync("fund-alpha"))
            .Should().ContainSingle(item => item.AuditEventId == first.AuditEventId).Subject;
        retained.RecordedAtUtc.Should().Be(
            AccountingAuditChain.ToRetainedPrecision(roundsUp),
            "the row must hold the instant that was hashed, not one PostgreSQL rounded up to");
    }

    [LedgerDatabaseFact]
    public async Task ARetriedAppendOfAnEventRecordedFinerThanAMicrosecond_StaysIdempotent()
    {
        // The same invariant on the path recovery actually takes: RecoverPendingAuditAsync replays
        // the append from the marker's copy of the event, which still carries the full tick, and
        // AppendAsync decides "already retained" by recomputing the digest from the row. A digest
        // that disagreed with the row would read as two events claiming one identity, and recovery
        // would fail on the one path written to complete it.
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var store = database.AccountingConfigurationStore;
        var declared = AuditEvent(recordedAtUtc: new DateTimeOffset(
            DateTime.SpecifyKind(DateTime.Parse("2026-06-01T12:00:00.0000000Z").AddTicks(9999999), DateTimeKind.Utc)));

        await store.AppendAsync(declared);
        var replay = async () => await store.AppendAsync(declared);

        await replay.Should().NotThrowAsync(
            "replaying the declared event is what recovery does, and it is the same event");
        (await store.ListAsync("fund-alpha")).Should().ContainSingle();
    }

    [LedgerDatabaseFact]
    public async Task AMarkerLeftByAMutationThisStoreRetained_IsClearedRatherThanRaised()
    {
        // Fifteenth Codex review round. AfterHash was taken over the workspace as it stood in
        // memory, carrying a derived RulesStudio this store never persists and GetAsync rebuilds as
        // null, so it could never match a reload. Recovery's already-audited check then raised on
        // every interrupted mutation -- and since every mutation runs recovery first, one crash
        // blocked the scope permanently. The file posture round-trips the whole DTO as JSON, which
        // is why the same test on it passes either way; only a store that drops a derived field can
        // see this.
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var store = database.AccountingConfigurationStore;
        using var markerRoot = new TempDirectory();
        var markers = new FileAccountingAuditPendingMarkerStore(
            Path.Combine(markerRoot.Path, "pending-audit.json"));

        // Both sides landed and only the clear was lost.
        await CreateService(store, markers).UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(
            "fund-alpha",
            new ChartOfAccountsNodeDto("node-one", "assets.node-one", "Cash", "Asset"),
            Actor: "operator@example.test"));
        var declared = (await store.ListAsync("fund-alpha")).Should().ContainSingle().Subject;
        await markers.WriteAsync(new AccountingAuditPendingMarker(declared, DateTimeOffset.UtcNow));

        var recovery = await CreateService(store, markers).RecoverPendingAuditAsync();

        recovery.Outcome.Should().Be(AccountingAuditRecoveryOutcome.AlreadyAudited);
        (await markers.ReadAsync()).Should().BeNull("a resolved marker must not re-fire");
    }

    [LedgerDatabaseFact]
    public async Task AMutationFollowingAnInterruptedOne_IsNotBlockedByTheRecoveryItRunsFirst()
    {
        // The consequence that makes the digest mismatch severe rather than cosmetic: every
        // mutation resolves any outstanding marker before starting, so a recovery that can never
        // succeed is a permanent block on the whole scope, not a one-off failed recovery.
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var store = database.AccountingConfigurationStore;
        using var markerRoot = new TempDirectory();
        var markers = new FileAccountingAuditPendingMarkerStore(
            Path.Combine(markerRoot.Path, "pending-audit.json"));

        await CreateService(store, markers).UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(
            "fund-alpha",
            new ChartOfAccountsNodeDto("node-one", "assets.node-one", "Cash", "Asset"),
            Actor: "operator@example.test"));
        var declared = (await store.ListAsync("fund-alpha")).Should().ContainSingle().Subject;
        await markers.WriteAsync(new AccountingAuditPendingMarker(declared, DateTimeOffset.UtcNow));

        var nextMutation = async () => await CreateService(store, markers).UpsertChartNodeAsync(
            new UpsertChartOfAccountsNodeRequest(
                "fund-alpha",
                new ChartOfAccountsNodeDto("node-two", "assets.node-two", "Bank", "Asset"),
                Actor: "operator@example.test"));

        await nextMutation.Should().NotThrowAsync();
        var workspace = await store.GetAsync("fund-alpha");
        workspace!.ChartOfAccounts.Should().HaveCount(2);
    }

    [LedgerDatabaseFact]
    public async Task AnEventEditedInTheMiddleOfTheChain_StopsTheNextAppend()
    {
        // Seventeenth Codex review round. VerifyChainHeadAsync read only the newest chained row, so
        // an edit anywhere earlier passed and the append added another valid-looking successor: each
        // entry hash binds to its predecessor's ENTRY hash, never to that predecessor's current
        // payload, so checking the tail cannot see historical mutation. The file posture already
        // verified every link on every append, which made AppendAsync's claim that the two postures
        // carry identical tamper-evidence false.
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var store = database.AccountingConfigurationStore;

        var first = AuditEvent();
        await store.AppendAsync(first);
        await store.AppendAsync(AuditEvent());
        await store.AppendAsync(AuditEvent());

        // Edit the FIRST event, leaving its payload_hash and the whole tail untouched.
        await ExecuteAsync(
            database,
            "update {0}.accounting_action_audit_events set actor = 'intruder@example.test' where audit_event_id = @id;",
            ("id", first.AuditEventId));

        var appendNext = async () => await store.AppendAsync(AuditEvent());

        var refusal = await appendNext.Should().ThrowAsync<AccountingAuditChainIntegrityException>();
        refusal.Which.Verification.Status.Should().Be(AccountingAuditChainStatus.EventMutated);
        refusal.Which.Verification.FailedSequence.Should().Be(
            AccountingAuditChainState.FirstSequence,
            "an operator needs the row that broke, not the end of the chain");
    }

    [LedgerDatabaseFact]
    public async Task AnEventDeletedFromTheMiddleOfTheChain_StopsTheNextAppend()
    {
        // The other shape of the same gap. Every surviving link still digests and binds correctly,
        // so nothing but a scan of the sequence itself can see that one is missing.
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var store = database.AccountingConfigurationStore;

        await store.AppendAsync(AuditEvent());
        var second = AuditEvent();
        await store.AppendAsync(second);
        await store.AppendAsync(AuditEvent());

        await ExecuteAsync(
            database,
            "delete from {0}.accounting_action_audit_events where audit_event_id = @id;",
            ("id", second.AuditEventId));

        var appendNext = async () => await store.AppendAsync(AuditEvent());

        var refusal = await appendNext.Should().ThrowAsync<AccountingAuditChainIntegrityException>();
        refusal.Which.Verification.Status.Should().Be(AccountingAuditChainStatus.BrokenSequence);
    }

    [LedgerDatabaseFact]
    public async Task AnUntouchedChain_StillAppends()
    {
        // The control the two tests above need: verifying every link must not make an intact chain
        // refuse. Four appends, each verifying everything before it.
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var store = database.AccountingConfigurationStore;

        var append = async () =>
        {
            for (var i = 0; i < 4; i++)
            {
                await store.AppendAsync(AuditEvent());
            }
        };

        await append.Should().NotThrowAsync();
        (await store.ListAsync("fund-alpha")).Should().HaveCount(4);
    }

    [LedgerDatabaseFact]
    public async Task AMarkerFromAMutationCarryingPaddedOptionalText_IsClearedRatherThanRaised()
    {
        // Seventeenth Codex review round. ReplaceChartAsync writes ParentPath, Symbol and
        // FinancialAccountId through AddTextOrNull, which trims and nulls blank text, while the
        // digest hashed the original strings -- so a padded value reloaded as something AfterHash
        // never covered, and recovery raised on it forever. Same permanent-block shape as the
        // RulesStudio divergence, reached through a different field.
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var store = database.AccountingConfigurationStore;
        using var markerRoot = new TempDirectory();
        var markers = new FileAccountingAuditPendingMarkerStore(
            Path.Combine(markerRoot.Path, "pending-audit.json"));

        await CreateService(store, markers).UpsertChartNodeAsync(new UpsertChartOfAccountsNodeRequest(
            "fund-alpha",
            new ChartOfAccountsNodeDto(
                "node-padded",
                "assets.node-padded",
                "Cash",
                "Asset",
                ParentPath: "  assets  ",
                Symbol: "   ",
                FinancialAccountId: "  FA-1  "),
            Actor: "operator@example.test"));
        var declared = (await store.ListAsync("fund-alpha")).Should().ContainSingle().Subject;
        await markers.WriteAsync(new AccountingAuditPendingMarker(declared, DateTimeOffset.UtcNow));

        var recovery = await CreateService(store, markers).RecoverPendingAuditAsync();

        recovery.Outcome.Should().Be(AccountingAuditRecoveryOutcome.AlreadyAudited);
        (await markers.ReadAsync()).Should().BeNull("a resolved marker must not re-fire");
    }

    private static async Task ExecuteAsync(
        LedgerPostgresTestDatabase database,
        string commandTemplate,
        params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = string.Format(
            CultureInfo.InvariantCulture,
            commandTemplate,
            "\"" + database.Options.SchemaName + "\"");
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private static AccountingConfigurationService CreateService(
        PostgresAccountingConfigurationStore store,
        IAccountingAuditPendingMarkerStore markers)
        => new(store, store, ledgerBookService: null, pendingAuditMarkers: markers);

    private static AccountingActionAuditEventDto AuditEvent(DateTimeOffset? recordedAtUtc = null)
        => new(
            Guid.NewGuid(),
            recordedAtUtc ?? DateTimeOffset.UtcNow,
            Actor: "operator@example.test",
            Action: "chart.upsert",
            FundProfileId: "fund-alpha",
            LedgerBookId: null,
            CorrelationId: null,
            BeforeHash: new string('0', 64),
            AfterHash: new string('1', 64),
            ValidationIssues: [],
            EvidenceLinks: []);

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "meridian-accounting-pg-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // A leaked temp directory must never fail a test run.
            }
        }
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
