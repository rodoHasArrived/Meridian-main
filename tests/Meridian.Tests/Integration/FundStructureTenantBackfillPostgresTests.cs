using System.Text.Json;
using FluentAssertions;
using Meridian.Application.FundStructure;
using Meridian.Contracts.Tenancy;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
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
        preview.Exceptions.Should().HaveCount(3);
        var receipt = await runner.ApplyAsync(Guid.NewGuid(), preview.PlanHash, "operator", "review/conflict", timeout.Token);

        receipt.QuarantinedRows.Should().Be(3);
        (await data.CountAsync("fund_structure_tenant_quarantine")).Should().Be(3);
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

    private sealed class TestData(PostgresTestServer server, string fundSchema, string ledgerSchema) : IAsyncDisposable
    {
        public string ConnectionString => server.ConnectionString;
        public string FundSchema { get; } = fundSchema;
        public string LedgerSchema { get; } = ledgerSchema;
        public Guid FundId { get; } = Guid.NewGuid();
        public Guid SleeveId { get; } = Guid.NewGuid();
        public PostgresFundStructureStore Store => new(new() { ConnectionString = ConnectionString, Schema = FundSchema });
        public PostgresFundStructureTenantBackfillStore BackfillStore() => new(
            new() { ConnectionString = ConnectionString, Schema = FundSchema }, ConnectionString, LedgerSchema);
        public FundStructureTenantBackfillRunner Runner() => new(BackfillStore());

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
                    VALUES ('{Guid.NewGuid()}', '{data.FundId}', '{data.SleeveId}', 'Owns', now());
                    INSERT INTO {data.LedgerSchema}.fund_profile_tenancy (fund_profile_id, tenant_id, company_id)
                    VALUES ('retained-fund', 'tenant-a', 'company-b');
                    INSERT INTO {data.LedgerSchema}.ledger_books
                        (ledger_book_id, fund_profile_id, fund_structure_node_id, fund_structure_node_kind,
                         display_name, base_currency, created_at, updated_at, tenant_id)
                    VALUES ('{Guid.NewGuid()}', 'retained-fund', '{data.FundId}', 'Fund', 'Retained book', 'USD', now(), now(), 'tenant-a');
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
