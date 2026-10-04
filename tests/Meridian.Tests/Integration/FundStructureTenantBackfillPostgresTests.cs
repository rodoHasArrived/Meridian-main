using System.Text.Json;
using FluentAssertions;
using Meridian.Application.FundStructure;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.FundStructure;
using Meridian.Entities.FundStructure;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
using Meridian.Storage.FundAccounts;
using Meridian.Storage.Tenancy;
using Meridian.TestSupport;
using Meridian.Tests.Storage.FundAccounts;
using Npgsql;

namespace Meridian.Tests.Integration;

/// <summary>Legacy tenant attribution through reviewed evidence, PostgreSQL locks, and retained receipts.</summary>
[Trait("Category", "Integration")]
public sealed class FundStructureTenantBackfillPostgresTests
{
    [FundAccountDatabaseFact]
    public async Task PreviewApply_LegacyFund_RetainsReceiptAndProvesStrictReadCounts()
    {
        await using var data = await TestData.CreateAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var runner = data.Runner();

        var preview = await runner.PreviewAsync(timeout.Token);
        (await data.Store.GetNodeTenantsAsync(timeout.Token)).NodeTenants.Should().BeEmpty();
        (await data.CountAsync("fund_structure_tenant_backfill_receipt")).Should().Be(0);
        preview.Stamps.Should().HaveCount(3);
        preview.AttributionComplete.Should().BeTrue();
        preview.Evidence.Evidence.Single().CompanyId.Should().Be("company-b");

        var receipt = await runner.ApplyAsync(Guid.NewGuid(), preview.PlanHash, "migration-operator", "review/tenant-cutover", timeout.Token);
        var tenants = await data.Store.GetNodeTenantsAsync(timeout.Token);
        new[] { data.FundId, data.SleeveId }.Count(id => FundStructureTenantScope.IsVisible(
            tenants, "tenant-a", id, TenantScopeEnforcementMode.FailClosed)).Should().Be(2);
        new[] { data.FundId, data.SleeveId }.Count(id => FundStructureTenantScope.IsVisible(
            tenants, "tenant-b", id, TenantScopeEnforcementMode.FailClosed)).Should().Be(0);
        FundStructureTenantScope.IsCallerAdmissible(tenants, null, TenantScopeEnforcementMode.FailClosed).Should().BeFalse();
        receipt.StampedRows.Should().Be(3);
        receipt.Plan.GetProperty("Evidence").GetProperty("Evidence")[0].GetProperty("CompanyId").GetString().Should().Be("company-b");
        (await data.CountAsync("fund_structure_tenant_backfill_receipt")).Should().Be(1);

        var rewrite = () => data.ExecuteAsync($"UPDATE {data.FundSchema}.fund_structure_tenant_backfill_receipt SET operator_id = 'other'");
        await rewrite.Should().ThrowAsync<PostgresException>().WithMessage("*immutable*");
        var truncate = () => data.ExecuteAsync($"TRUNCATE {data.FundSchema}.fund_structure_tenant_backfill_receipt");
        await truncate.Should().ThrowAsync<PostgresException>().WithMessage("*immutable*");
        (await data.CountAsync("fund_structure_tenant_backfill_receipt")).Should().Be(1);
    }

    [FundAccountDatabaseFact]
    public async Task Preview_VehicleLegalEntityReference_IsAParentAndDoesNotCreateCycle()
    {
        await using var data = await TestData.CreateAsync();
        var entityId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        await data.ExecuteAsync($"""
            INSERT INTO {data.FundSchema}.legal_entity
                (entity_id, entity_type, code, name, jurisdiction, base_currency, effective_from)
            VALUES ('{entityId}', 'LimitedPartnership', 'ENTITY', 'Retained entity', 'US-DE', 'USD', now());
            INSERT INTO {data.FundSchema}.vehicle
                (vehicle_id, fund_id, legal_entity_id, code, name, base_currency, effective_from)
            VALUES ('{vehicleId}', '{data.FundId}', '{entityId}', 'VEHICLE', 'Retained vehicle', 'USD', now());
            INSERT INTO {data.FundSchema}.ownership_link
                (ownership_link_id, parent_node_id, child_node_id, relationship_type, effective_from)
            VALUES ('{Guid.NewGuid()}', '{entityId}', '{vehicleId}', 'Owns', now());
            """);

        var preview = await data.Runner().PreviewAsync();

        preview.AttributionComplete.Should().BeTrue();
        preview.Exceptions.Should().BeEmpty();
        preview.Stamps.Should().Contain(stamp => stamp.Id == entityId && stamp.TenantId == "tenant-a");
        preview.Stamps.Should().Contain(stamp => stamp.Id == vehicleId && stamp.TenantId == "tenant-a");
    }

    [FundAccountDatabaseFact]
    public async Task Apply_StaleGraphAndLedgerEvidence_RefusesWithoutPartialStamp()
    {
        await using var data = await TestData.CreateAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var runner = data.Runner();
        var graphPreview = await runner.PreviewAsync(timeout.Token);
        await data.ExecuteAsync($"UPDATE {data.FundSchema}.fund SET name = 'Changed after review'");

        var staleGraph = () => runner.ApplyAsync(Guid.NewGuid(), graphPreview.PlanHash, "operator", "review/graph", timeout.Token);
        await staleGraph.Should().ThrowAsync<InvalidOperationException>().WithMessage("*stale*");
        var ledgerPreview = await runner.PreviewAsync(timeout.Token);
        await data.ExecuteAsync($"UPDATE {data.LedgerSchema}.fund_profile_tenancy SET company_id = 'different-company'");
        var staleLedger = () => runner.ApplyAsync(Guid.NewGuid(), ledgerPreview.PlanHash, "operator", "review/ledger", timeout.Token);
        await staleLedger.Should().ThrowAsync<InvalidOperationException>().WithMessage("*stale*");
        (await data.Store.GetNodeTenantsAsync(timeout.Token)).NodeTenants.Should().BeEmpty();
        (await data.CountAsync("fund_structure_tenant_backfill_receipt")).Should().Be(0);
    }

    [FundAccountDatabaseFact]
    public async Task Apply_OwnerConflict_QuarantinesDependentRowsAndRetainsExistingOwner()
    {
        await using var data = await TestData.CreateAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await data.Store.StampNodeTenantAsync(data.SleeveId, "tenant-b", timeout.Token);
        var runner = data.Runner();
        var preview = await runner.PreviewAsync(timeout.Token);

        preview.Stamps.Should().BeEmpty();
        preview.Exceptions.Should().HaveCount(4, "the retained ledger book shares the conflicting component");
        var receipt = await runner.ApplyAsync(Guid.NewGuid(), preview.PlanHash, "operator", "review/conflict", timeout.Token);

        receipt.QuarantinedRows.Should().Be(4);
        (await data.CountAsync("fund_structure_tenant_quarantine")).Should().Be(4);
        var tenants = await data.Store.GetNodeTenantsAsync(timeout.Token);
        tenants.NodeTenants.Should().ContainSingle().Which.Should().Be(new KeyValuePair<Guid, string>(data.SleeveId, "tenant-b"));
        preview.AttributionComplete.Should().BeFalse();
    }

    [FundAccountDatabaseFact]
    public async Task Apply_PriorQuarantineNowDerivable_RequiresExplicitResolution()
    {
        await using var data = await TestData.CreateAsync();
        await data.ExecuteAsync($"""
            INSERT INTO {data.FundSchema}.fund_structure_tenant_quarantine
                (node_id, node_kind, reason, candidate_tenant_ids)
            VALUES ('{data.FundId}', 'Fund', 'PriorMissingEvidence', '[]');
            """);
        var runner = data.Runner();
        var preview = await runner.PreviewAsync();

        preview.AttributionComplete.Should().BeFalse();
        preview.BlockingReasons.Should().ContainMatch("*unresolved quarantine*");
        var apply = () => runner.ApplyAsync(Guid.NewGuid(), preview.PlanHash, "operator", "review/prior-quarantine");
        await apply.Should().ThrowAsync<InvalidOperationException>().WithMessage("*blockers*");
        (await data.Store.GetNodeTenantsAsync()).NodeTenants.Should().BeEmpty();
        (await data.CountAsync("fund_structure_tenant_quarantine")).Should().Be(1);
        (await data.CountAsync("fund_structure_tenant_backfill_receipt")).Should().Be(0);

        var reviewedRelease = await runner.PreviewResolutionAsync();
        reviewedRelease.Resolutions.Should().ContainSingle().Which.NodeId.Should().Be(data.FundId);
        var receipt = await runner.ResolveAsync(Guid.NewGuid(), reviewedRelease.PlanHash, "operator", "review/release");
        receipt.Plan.GetProperty("Resolutions").GetArrayLength().Should().Be(1);
        (await data.InspectAsync()).IsReady.Should().BeTrue();
        (await runner.PreviewAsync()).AttributionComplete.Should().BeTrue();
        (await data.CountAsync("fund_structure_tenant_quarantine")).Should().Be(1, "release preserves the retained review record");
    }

    [FundAccountDatabaseFact]
    public async Task Apply_PriorResolutionDisagreesWithRegistry_PreservesResolutionAndRefusesStamp()
    {
        await using var data = await TestData.CreateAsync();
        await data.ExecuteAsync($"""
            INSERT INTO {data.FundSchema}.fund_structure_tenant_quarantine
                (node_id, node_kind, reason, candidate_tenant_ids, resolved_at_utc, resolved_tenant_id, resolution_note)
            VALUES ('{data.FundId}', 'Fund', 'PriorMissingEvidence', '[]', now(), 'tenant-b', 'Retained prior review');
            """);
        var runner = data.Runner();
        var preview = await runner.PreviewAsync();

        preview.BlockingReasons.Should().ContainMatch("*resolution conflicts*");
        preview.Evidence.RetainedQuarantine.Single().GetProperty("resolved_tenant_id").GetString().Should().Be("tenant-b");
        var apply = () => runner.ApplyAsync(Guid.NewGuid(), preview.PlanHash, "operator", "review/prior-resolution");
        await apply.Should().ThrowAsync<InvalidOperationException>().WithMessage("*blockers*");
        (await data.Store.GetNodeTenantsAsync()).NodeTenants.Should().BeEmpty();
        (await data.CountAsync("fund_structure_tenant_backfill_receipt")).Should().Be(0);
    }

    [FundAccountDatabaseFact]
    public async Task Apply_ConcurrentRetries_RetainOneReceipt()
    {
        await using var data = await TestData.CreateAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var preview = await data.Runner().PreviewAsync(timeout.Token);
        var runId = Guid.NewGuid();

        var receipts = await Task.WhenAll(
            data.Runner().ApplyAsync(runId, preview.PlanHash, "operator", "review/retry", timeout.Token),
            data.Runner().ApplyAsync(runId, preview.PlanHash, "operator", "review/retry", timeout.Token));

        receipts.Select(receipt => receipt.AppliedAt).Distinct().Should().ContainSingle();
        (await data.CountAsync("fund_structure_tenant_backfill_receipt")).Should().Be(1);
        (await data.Store.GetNodeTenantsAsync(timeout.Token)).NodeTenants.Should().HaveCount(2);
    }

    [FundAccountDatabaseFact]
    public async Task Apply_LateReceiptFailure_RollsBackEveryStamp()
    {
        await using var data = await TestData.CreateAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await data.ExecuteAsync($"""
            CREATE FUNCTION {data.FundSchema}.reject_test_receipt() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'forced receipt failure'; END; $$;
            CREATE TRIGGER reject_test_receipt BEFORE INSERT ON {data.FundSchema}.fund_structure_tenant_backfill_receipt
            FOR EACH ROW EXECUTE FUNCTION {data.FundSchema}.reject_test_receipt();
            """);
        var runner = data.Runner();
        var preview = await runner.PreviewAsync(timeout.Token);

        var apply = () => runner.ApplyAsync(Guid.NewGuid(), preview.PlanHash, "operator", "review/rollback", timeout.Token);
        await apply.Should().ThrowAsync<PostgresException>().WithMessage("*forced receipt failure*");

        (await data.Store.GetNodeTenantsAsync(timeout.Token)).NodeTenants.Should().BeEmpty();
        (await data.CountAsync("fund_structure_tenant_backfill_receipt")).Should().Be(0);
        (await data.CountAsync("fund_structure_tenant_quarantine")).Should().Be(0);
    }

    [FundAccountDatabaseFact]
    public async Task Preview_CompetingWriterCancellation_ReleasesLocksWithoutMutation()
    {
        await using var data = await TestData.CreateAsync();
        await using var writer = new NpgsqlConnection(data.ConnectionString);
        await writer.OpenAsync();
        await using var transaction = await writer.BeginTransactionAsync();
        await using (var hold = new NpgsqlCommand($"LOCK TABLE {data.FundSchema}.fund IN ACCESS EXCLUSIVE MODE", writer, transaction))
            await hold.ExecuteNonQueryAsync();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        var preview = () => data.Runner().PreviewAsync(cancellation.Token);
        await preview.Should().ThrowAsync<OperationCanceledException>();

        await transaction.RollbackAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        (await data.Runner().PreviewAsync(timeout.Token)).AttributionComplete.Should().BeTrue();
        (await data.Store.GetNodeTenantsAsync(timeout.Token)).NodeTenants.Should().BeEmpty();
        (await data.CountAsync("fund_structure_tenant_backfill_receipt")).Should().Be(0);
    }

    [FundAccountDatabaseFact]
    public async Task Apply_SourceConnectionLost_CannotCommitAttribution()
    {
        await using var data = await TestData.CreateAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var session = await data.BackfillStore().OpenSessionAsync(timeout.Token);
        var preview = FundStructureTenantBackfillPlanner.Create(session.Snapshot);
        await data.ExecuteAsync($"""
            SELECT pg_terminate_backend(a.pid) FROM pg_stat_activity a
            JOIN pg_locks l ON l.pid = a.pid
            WHERE l.relation = '{data.FundSchema}.fund'::regclass
              AND l.mode = 'ShareRowExclusiveLock' AND l.granted AND a.pid <> pg_backend_pid()
            """);

        var commit = () => session.CommitAsync(Guid.NewGuid(), preview.PlanHash, "operator", "review/disconnect",
            JsonSerializer.SerializeToElement(preview), preview.Stamps, preview.Exceptions, timeout.Token);
        await commit.Should().ThrowAsync<NpgsqlException>();
        (await data.Store.GetNodeTenantsAsync(timeout.Token)).NodeTenants.Should().BeEmpty();
        (await data.CountAsync("fund_structure_tenant_backfill_receipt")).Should().Be(0);
    }

    [FundAccountDatabaseFact]
    public async Task Apply_LegacyLinkAndAssignmentAccounts_MaterializesOnlyReviewedNodes()
    {
        await using var data = await TestData.CreateAsync();
        var linkedAccount = Guid.NewGuid();
        var assignedAccount = Guid.NewGuid();
        await data.ExecuteAsync($"""
            INSERT INTO {data.FundSchema}.ownership_link
                (ownership_link_id, parent_node_id, child_node_id, relationship_type, effective_from)
            VALUES ('{Guid.NewGuid()}', '{data.FundId}', '{linkedAccount}', 'Owns', now());
            INSERT INTO {data.FundSchema}.fund_structure_assignment
                (assignment_id, node_id, assignment_type, assignment_reference, effective_from)
            VALUES ('{Guid.NewGuid()}', '{assignedAccount}', 'LedgerBook', 'retained-account', now());
            INSERT INTO {data.LedgerSchema}.ledger_books
                (ledger_book_id, fund_profile_id, fund_structure_node_id, fund_structure_node_kind,
                 display_name, base_currency, created_at, updated_at, tenant_id)
            VALUES ('{Guid.NewGuid()}', 'retained-fund', '{assignedAccount}', 'Account', 'Account book', 'USD', now(), now(), 'tenant-a');
            """);
        var runner = data.Runner();
        var plan = await runner.PreviewAsync();
        plan.AttributionComplete.Should().BeTrue();
        plan.Evidence.Rows.Where(row => row.IsInferred).Select(row => row.Id)
            .Should().BeEquivalentTo([linkedAccount, assignedAccount]);
        (await data.CountAsync("fund_structure_linked_account")).Should().Be(0, "preview does not materialize nodes");

        await runner.ApplyAsync(Guid.NewGuid(), plan.PlanHash, "operator", "review/legacy-accounts");

        (await data.CountAsync("fund_structure_linked_account")).Should().Be(2);
        var owners = await data.Store.GetNodeTenantsAsync();
        owners.NodeTenants[linkedAccount].Should().Be("tenant-a");
        owners.NodeTenants[assignedAccount].Should().Be("tenant-a");
        (await new FundStructureTenantBackfillRunner(data.BackfillStore()).PreviewAsync()).Stamps.Should().BeEmpty();
    }

    [FundAccountDatabaseFact]
    public async Task Apply_LateFailure_RollsBackInferredAccountMaterialization()
    {
        await using var data = await TestData.CreateAsync();
        var account = Guid.NewGuid();
        await data.ExecuteAsync($"""
            INSERT INTO {data.FundSchema}.ownership_link
                (ownership_link_id, parent_node_id, child_node_id, relationship_type, effective_from)
            VALUES ('{Guid.NewGuid()}', '{data.FundId}', '{account}', 'Owns', now());
            CREATE FUNCTION {data.FundSchema}.reject_account_receipt() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'forced account receipt failure'; END; $$;
            CREATE TRIGGER reject_account_receipt BEFORE INSERT ON {data.FundSchema}.fund_structure_tenant_backfill_receipt
            FOR EACH ROW EXECUTE FUNCTION {data.FundSchema}.reject_account_receipt();
            """);
        var runner = data.Runner();
        var plan = await runner.PreviewAsync();
        plan.AttributionComplete.Should().BeTrue();
        var apply = () => runner.ApplyAsync(Guid.NewGuid(), plan.PlanHash, "operator", "review/account-rollback");
        await apply.Should().ThrowAsync<PostgresException>().WithMessage("*forced account receipt failure*");
        (await data.CountAsync("fund_structure_linked_account")).Should().Be(0);
        (await data.CountAsync("fund_structure_tenant_backfill_receipt")).Should().Be(0);
        (await data.Store.GetNodeTenantsAsync()).NodeTenants.Should().BeEmpty();
    }

    [FundAccountDatabaseFact]
    public async Task Apply_CommittedRetry_IgnoresGraphLocksAndUnavailableLedgerSource()
    {
        await using var data = await TestData.CreateAsync();
        var runner = data.Runner();
        var plan = await runner.PreviewAsync();
        var runId = Guid.NewGuid();
        var receipt = await runner.ApplyAsync(runId, plan.PlanHash, "operator", "review/recovery");
        await using var writer = new NpgsqlConnection(data.ConnectionString);
        await writer.OpenAsync();
        await using var transaction = await writer.BeginTransactionAsync();
        await using (var hold = new NpgsqlCommand($"LOCK TABLE {data.FundSchema}.fund IN ACCESS EXCLUSIVE MODE", writer, transaction))
            await hold.ExecuteNonQueryAsync();
        var brokenLedger = new NpgsqlConnectionStringBuilder(data.ConnectionString) { Database = "missing_ledger_database" };
        var recovery = new FundStructureTenantBackfillRunner(new PostgresFundStructureTenantBackfillStore(
            new() { ConnectionString = data.ConnectionString, Schema = data.FundSchema }, brokenLedger.ConnectionString, data.LedgerSchema));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var recovered = await recovery.ApplyAsync(runId, plan.PlanHash, "operator", "review/recovery", deadline.Token);
        recovered.RunId.Should().Be(receipt.RunId);
        recovered.AppliedAt.Should().Be(receipt.AppliedAt);
        var wrongReview = () => recovery.ApplyAsync(runId, plan.PlanHash, "operator", "review/different", deadline.Token);
        await wrongReview.Should().ThrowAsync<InvalidOperationException>().WithMessage("*different review evidence*");
        await transaction.RollbackAsync();
    }

    [FundAccountDatabaseFact]
    public async Task Preview_EntityBook_UsesPersistedContractKind()
    {
        await using var data = await TestData.CreateAsync();
        var entityId = Guid.NewGuid();
        await data.ExecuteAsync($"""
            INSERT INTO {data.FundSchema}.legal_entity
                (entity_id, entity_type, code, name, jurisdiction, base_currency, effective_from)
            VALUES ('{entityId}', 'LimitedPartnership', 'ENTITY', 'Entity book owner', 'US-DE', 'USD', now());
            INSERT INTO {data.LedgerSchema}.ledger_books
                (ledger_book_id, fund_profile_id, fund_structure_node_id, fund_structure_node_kind,
                 display_name, base_currency, created_at, updated_at, tenant_id)
            VALUES ('{Guid.NewGuid()}', 'retained-fund', '{entityId}', 'Entity', 'Entity book', 'USD', now(), now(), 'tenant-a');
            """);
        var plan = await data.Runner().PreviewAsync();
        plan.AttributionComplete.Should().BeTrue();
        plan.Stamps.Should().Contain(stamp => stamp.Id == entityId && stamp.TenantId == "tenant-a");
    }

    [FundAccountDatabaseFact]
    public async Task Cutover_AllNullableTenantStores_BlockBeforeBackfillAndRetainVisibilityAfterwards()
    {
        await using var data = await TestData.CreateAsync();
        var period = Guid.NewGuid();
        var account = Guid.NewGuid();
        var workflow = Guid.NewGuid();
        var workflowJson = JsonSerializer.Serialize(new { ledgerBookId = data.BookId });
        await new FundAccountMigrationRunner(data.AccountOptions).EnsureMigratedAsync();
        await data.ExecuteAsync($"""
            UPDATE {data.LedgerSchema}.ledger_books SET tenant_id = NULL;
            INSERT INTO {data.LedgerSchema}.accounting_periods
                (period_id, ledger_book_id, fiscal_year, period_no, label, start_date, end_date, status, opened_at)
            VALUES ('{period}', '{data.BookId}', 2026, 9, 'Legacy September', '2026-09-01', '2026-09-30', 'Open', now());
            INSERT INTO {data.AccountsSchema}.account_definition
                (account_id, account_type, fund_id, account_code, display_name, base_currency, effective_from)
            VALUES ('{account}', 'Custody', '{data.FundId}', 'LEGACY', 'Retained custody account', 'USD', now());
            INSERT INTO {data.LedgerSchema}.operations_continuity_workflows
                (workflow_id, fund_account_id, period_id, broker_source, derived_status, version, created_at_utc, updated_at_utc, workflow_json)
            VALUES ('{workflow}', '{account}', '2026-09', 'Legacy', 'Open', 1, now(), now(), '{workflowJson}');
            """);
        var before = await data.InspectAsync(includeAccounts: true);
        before.IsReady.Should().BeFalse();
        before.Findings.Where(finding => finding.Reason == "MissingTenantAttribution").Select(finding => finding.Table)
            .Should().Contain(["ledger_books", "accounting_periods", "operations_continuity_workflows", "account_definition"]);
        var runner = new FundStructureTenantBackfillRunner(data.BackfillStore(includeAccounts: true));
        var plan = await runner.PreviewAsync();
        plan.AttributionComplete.Should().BeTrue();
        plan.Stamps.Should().HaveCount(7);
        await runner.ApplyAsync(Guid.NewGuid(), plan.PlanHash, "operator", "review/all-stores");

        (await data.InspectAsync(includeAccounts: true)).IsReady.Should().BeTrue();
        (await runner.PreviewAsync()).Stamps.Should().BeEmpty();
        await data.ExecuteAsync($"UPDATE {data.AccountsSchema}.account_definition SET tenant_id = 'tenant-b'");
        (await data.InspectAsync(includeAccounts: true)).Findings.Should()
            .Contain(new TenantCutoverFinding("fund-accounts", "account_definition", "TenantAuthorityMismatch", 1));
    }

    [FundAccountDatabaseFact]
    public async Task Cutover_AuditedUnattributedPeriod_RemainsQuarantinedAndAuditVerifiable()
    {
        await using var data = await TestData.CreateAsync();
        await data.ExecuteAsync($"DELETE FROM {data.LedgerSchema}.fund_profile_tenancy");
        var journal = new PostgresLedgerJournalStore(new() { ConnectionString = data.ConnectionString, SchemaName = data.LedgerSchema });
        var period = await journal.SavePeriodAsync(new(Guid.NewGuid(), data.BookId, 2026, 9, "Audited legacy",
            new(2026, 9, 1), new(2026, 9, 30), "Open", DateTimeOffset.UtcNow, null, 0), 0);
        (await journal.VerifyLedgerEventAuditAsync()).ChainedEvents.Should().Be(1);
        await data.ExecuteAsync($"INSERT INTO {data.LedgerSchema}.fund_profile_tenancy (fund_profile_id, tenant_id, company_id) VALUES ('retained-fund', 'tenant-a', 'company-b')");
        await new LedgerMigrationRunner(new() { ConnectionString = data.ConnectionString, SchemaName = data.LedgerSchema }).EnsureMigratedAsync();
        (await journal.VerifyLedgerEventAuditAsync()).ChainedEvents.Should().Be(1, "repeatable startup backfill must also preserve audited legacy periods");
        var runner = data.Runner();
        var plan = await runner.PreviewAsync();
        plan.Exceptions.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new FundStructureTenantBackfillException(period.PeriodId, "AccountingPeriod", "AuditedPeriodRequiresGovernedTenantRepair", []));
        await runner.ApplyAsync(Guid.NewGuid(), plan.PlanHash, "operator", "review/audited-legacy");

        (await journal.VerifyLedgerEventAuditAsync()).ChainedEvents.Should().Be(1);
        var readiness = await data.InspectAsync();
        readiness.IsReady.Should().BeFalse();
        readiness.Findings.Should().Contain(new TenantCutoverFinding("ledger", "accounting_periods", "MissingTenantAttribution", 1));
        readiness.Findings.Should().Contain(new TenantCutoverFinding("fund-structure", "fund_structure_tenant_quarantine", "UnresolvedQuarantine", 1));
        (await runner.PreviewResolutionAsync()).Resolutions.Should().BeEmpty();
    }

    [FundAccountDatabaseFact]
    public async Task Cutover_AttributedBooklessWorkflow_RestartsWithoutInventingQuarantine()
    {
        await using var data = await TestData.CreateAsync();
        var runner = data.Runner();
        var initial = await runner.PreviewAsync();
        await runner.ApplyAsync(Guid.NewGuid(), initial.PlanHash, "operator", "review/graph");
        await data.ExecuteAsync($"""
            INSERT INTO {data.LedgerSchema}.operations_continuity_workflows
                (workflow_id, fund_account_id, period_id, broker_source, derived_status, version, created_at_utc, updated_at_utc, workflow_json, tenant_id)
            VALUES ('{Guid.NewGuid()}', '{Guid.NewGuid()}', '2026-09', 'Retained', 'Open', 1, now(), now(), jsonb_build_object(), 'tenant-a');
            """);
        (await data.InspectAsync()).IsReady.Should().BeTrue();
        (await runner.PreviewAsync()).AttributionComplete.Should().BeTrue();
        await new LedgerMigrationRunner(new() { ConnectionString = data.ConnectionString, SchemaName = data.LedgerSchema }).EnsureMigratedAsync();
        (await data.InspectAsync()).IsReady.Should().BeTrue();

        await data.ExecuteAsync($"UPDATE {data.LedgerSchema}.operations_continuity_workflows SET tenant_id = NULL");
        (await data.InspectAsync()).Findings.Should().Contain(new TenantCutoverFinding("ledger", "operations_continuity_workflows", "MissingTenantAttribution", 1));
        var brokenJson = JsonSerializer.Serialize(new { ledgerBookId = "invalid-book" });
        await data.ExecuteAsync($"UPDATE {data.LedgerSchema}.operations_continuity_workflows SET tenant_id = 'tenant-a', workflow_json = '{brokenJson}'");
        (await data.InspectAsync()).Findings.Should().Contain(new TenantCutoverFinding("ledger", "operations_continuity_workflows", "MissingOwnerAuthority", 1));
        (await runner.PreviewAsync()).Exceptions.Should().Contain(item => item.NodeKind == "OperationsWorkflow");
    }

    [FundAccountDatabaseFact]
    public async Task Cutover_RepeatableMigration_AttributesBooksAndDependentWorkflowsInOneStartup()
    {
        await using var data = await TestData.CreateAsync();
        var workflowId = Guid.NewGuid();
        var malformedId = Guid.NewGuid();
        await data.ExecuteAsync($"""
            UPDATE {data.LedgerSchema}.ledger_books SET tenant_id = NULL WHERE ledger_book_id = '{data.BookId}';
            INSERT INTO {data.LedgerSchema}.operations_continuity_workflows
                (workflow_id, fund_account_id, period_id, broker_source, derived_status, version, created_at_utc, updated_at_utc, workflow_json)
            VALUES ('{workflowId}', '{Guid.NewGuid()}', '2026-09', 'Retained', 'Open', 1, now(), now(),
                jsonb_build_object('ledgerBookId', '{data.BookId}')),
                ('{malformedId}', '{Guid.NewGuid()}', '2026-09', 'Retained', 'Open', 1, now(), now(),
                jsonb_build_object('ledgerBookId', 'invalid-book'));
            """);

        await new LedgerMigrationRunner(new() { ConnectionString = data.ConnectionString, SchemaName = data.LedgerSchema }).EnsureMigratedAsync();

        await using var connection = new NpgsqlConnection(data.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT (SELECT tenant_id FROM {data.LedgerSchema}.ledger_books WHERE ledger_book_id = @book) = 'tenant-a'
               AND (SELECT tenant_id FROM {data.LedgerSchema}.operations_continuity_workflows WHERE workflow_id = @workflow) = 'tenant-a'
               AND (SELECT tenant_id IS NULL FROM {data.LedgerSchema}.operations_continuity_workflows WHERE workflow_id = @malformed)
            """;
        command.Parameters.AddWithValue("book", data.BookId);
        command.Parameters.AddWithValue("workflow", workflowId);
        command.Parameters.AddWithValue("malformed", malformedId);
        (await command.ExecuteScalarAsync()).Should().Be(true);
        (await data.InspectAsync()).Findings.Should().Contain(
            new TenantCutoverFinding("ledger", "operations_continuity_workflows", "MissingOwnerAuthority", 1));
    }

    [FundAccountDatabaseFact]
    public async Task StrictStructureWrites_StampEdgesAndAssignments_AndRemainReadyOnRestart()
    {
        await using var data = await TestData.CreateAsync();
        var runner = data.Runner();
        var initial = await runner.PreviewAsync();
        await runner.ApplyAsync(Guid.NewGuid(), initial.PlanHash, "operator", "review/graph");
        PostgresFundStructureService Service(string? tenant) => new(data.Store, new InMemoryFundAccountService(),
            new FundStructurePolicyService(), tenantAccessor: new TestTenantAccessor(tenant), tenantScope: TenantScopeEnforcementOptions.FailClosed);
        var service = Service("tenant-a");
        var sleeve = await service.CreateSleeveAsync(new(Guid.NewGuid(), data.FundId, "NEW", "Strict new sleeve", DateTimeOffset.UtcNow, "operator"));
        var assignment = await service.AssignNodeAsync(new(Guid.NewGuid(), sleeve.SleeveId, "LedgerBook", data.BookId.ToString("D"), DateTimeOffset.UtcNow, "operator"));
        var link = (await data.Store.GetAllOwnershipLinksAsync()).Single(item => item.ChildNodeId == sleeve.SleeveId);
        (await data.InspectAsync()).IsReady.Should().BeTrue();
        (await runner.PreviewAsync()).Stamps.Should().BeEmpty();

        var foreignLinkWrite = () => data.Store.UpsertOwnershipLinkAsync(link with { Notes = "foreign mutation" }, "tenant-b", CancellationToken.None);
        await foreignLinkWrite.Should().ThrowAsync<TenantScopeRejectedException>();
        var foreignAssignmentWrite = () => data.Store.UpsertAssignmentAsync(assignment with { AssignmentReference = "foreign mutation" }, "tenant-b", CancellationToken.None);
        await foreignAssignmentWrite.Should().ThrowAsync<TenantScopeRejectedException>();
        (await data.Store.GetAllAssignmentsAsync()).Single(item => item.AssignmentId == assignment.AssignmentId).AssignmentReference.Should().Be(data.BookId.ToString("D"));
        (await data.Store.GetAllOwnershipLinksAsync()).Single(item => item.OwnershipLinkId == link.OwnershipLinkId).Notes.Should().Be(link.Notes);

        foreach (var tenant in new string?[] { null, "all" })
        {
            var id = Guid.NewGuid();
            var denied = () => Service(tenant).CreateOrganizationAsync(new(id, "DENIED", "No authority", "USD", DateTimeOffset.UtcNow, "operator"));
            await denied.Should().ThrowAsync<FundStructureTenantScopeException>();
            (await data.Store.GetOrganizationAsync(id)).Should().BeNull();
        }
        var foreignChild = () => Service("tenant-b").CreateSleeveAsync(new(Guid.NewGuid(), data.FundId, "DENIED", "Foreign fund", DateTimeOffset.UtcNow, "operator"));
        await foreignChild.Should().ThrowAsync<InvalidOperationException>();
        (await data.InspectAsync()).IsReady.Should().BeTrue();
    }

    private sealed class TestTenantAccessor(string? tenant) : IFundScopeTenantAccessor
    {
        public string? ResolveCallerTenant() => tenant;
    }

    private sealed class TestData(PostgresTestServer server, string fundSchema, string ledgerSchema) : IAsyncDisposable
    {
        public string ConnectionString => server.ConnectionString;
        public string FundSchema { get; } = fundSchema;
        public string LedgerSchema { get; } = ledgerSchema;
        public string AccountsSchema { get; } = server.CreateSchemaName("accounts_attribution");
        public Guid BookId { get; } = Guid.NewGuid();
        public Guid FundId { get; } = Guid.NewGuid();
        public Guid SleeveId { get; } = Guid.NewGuid();
        public PostgresFundStructureStore Store => new(new() { ConnectionString = ConnectionString, Schema = FundSchema });
        public FundAccountStoreOptions AccountOptions => new() { ConnectionString = ConnectionString, Schema = AccountsSchema };
        public PostgresFundStructureTenantBackfillStore BackfillStore(bool includeAccounts = false) => new(
            new() { ConnectionString = ConnectionString, Schema = FundSchema }, ConnectionString, LedgerSchema,
            fundAccounts: includeAccounts ? AccountOptions : null);
        public FundStructureTenantBackfillRunner Runner() => new(BackfillStore());
        public Task<TenantCutoverReadiness> InspectAsync(bool includeAccounts = false) => PostgresTenantCutoverInspector.InspectAsync(
            new() { ConnectionString = ConnectionString, Schema = FundSchema },
            new() { ConnectionString = ConnectionString, SchemaName = LedgerSchema }, includeAccounts ? AccountOptions : null);

        public static async Task<TestData> CreateAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var server = await PostgresTestServer.CreateAsync("MERIDIAN_FUND_ACCOUNTS_CONNECTION_STRING", ct: timeout.Token);
            var data = new TestData(server, server.CreateSchemaName("fs_attribution"), server.CreateSchemaName("ledger_attribution"));
            try
            {
                await new FundStructureMigrationRunner(new() { ConnectionString = data.ConnectionString, Schema = data.FundSchema }).EnsureMigratedAsync(timeout.Token);
                await new LedgerMigrationRunner(new() { ConnectionString = data.ConnectionString, SchemaName = data.LedgerSchema }).EnsureMigratedAsync(timeout.Token);
                await data.ExecuteAsync($"""
                    INSERT INTO {data.FundSchema}.fund (fund_id, code, name, base_currency, effective_from, sleeve_ids)
                    VALUES ('{data.FundId}', 'RETAINED', 'Retained private fund', 'USD', now(), '["{data.SleeveId}"]');
                    INSERT INTO {data.FundSchema}.sleeve (sleeve_id, fund_id, code, name, effective_from)
                    VALUES ('{data.SleeveId}', '{data.FundId}', 'SLEEVE', 'Retained sleeve', now());
                    INSERT INTO {data.FundSchema}.ownership_link (ownership_link_id, parent_node_id, child_node_id, relationship_type, effective_from)
                    VALUES ('{Guid.NewGuid()}', '{data.FundId}', '{data.SleeveId}', 'AllocatesTo', now());
                    INSERT INTO {data.LedgerSchema}.fund_profile_tenancy (fund_profile_id, tenant_id, company_id)
                    VALUES ('retained-fund', 'tenant-a', 'company-b');
                    INSERT INTO {data.LedgerSchema}.ledger_books
                        (ledger_book_id, fund_profile_id, fund_structure_node_id, fund_structure_node_kind,
                         display_name, base_currency, created_at, updated_at, tenant_id)
                    VALUES ('{data.BookId}', 'retained-fund', '{data.FundId}', 'Fund', 'Retained book', 'USD', now(), now(), 'tenant-a');
                    """);
                return data;
            }
            catch { await data.DisposeAsync(); throw; }
        }

        public async Task ExecuteAsync(string sql)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync(timeout.Token);
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(timeout.Token);
        }

        public async Task<long> CountAsync(string table)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync(timeout.Token);
            await using var command = new NpgsqlCommand($"SELECT count(*) FROM {FundSchema}.{table}", connection);
            return (long)(await command.ExecuteScalarAsync(timeout.Token))!;
        }

        public ValueTask DisposeAsync() => server.DisposeAsync();
    }
}
