using Meridian.Storage.Ledger;
using Meridian.Contracts.Ledger;
using Npgsql;

namespace Meridian.Tests.Storage;

public sealed class ConsolidationTransitionPostgresTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task ApprovedReceipt_IsIdempotentTenantScopedAndDatabaseImmutable()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var preview = ConsolidationTransitionStoreTests.Preview();
        await SeedBooksAsync(database, preview);
        var store = Store(database);
        await store.RetainPreviewAsync(preview);
        var receipt = ConsolidationTransitionStoreTests.Receipt(preview);
        var callbacks = 0;
        var retained = await store.AppendValidatedAsync(receipt, _ => { callbacks++; return Task.CompletedTask; });
        Assert.Equal(receipt.ContentHash, retained.ContentHash);
        await store.AppendValidatedAsync(receipt, _ => { callbacks++; return Task.CompletedTask; });
        Assert.Equal(1, callbacks);
        Assert.Equal(receipt.ContentHash, (await store.GetAsync(receipt.TransitionId))!.ContentHash);
        Assert.Single(await store.ListAsync(preview.Request.OrganizationId, preview.Request.OwnershipRootId));
        Assert.Null(await Store(database, "tenant-b").GetAsync(receipt.TransitionId));

        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync();
        foreach (var table in new[] { "consolidation_transition_receipts", "consolidation_transition_previews" })
        foreach (var mutation in new[] { $"update \"{database.Options.SchemaName}\".{table} set tenant_id='rewritten'",
                     $"delete from \"{database.Options.SchemaName}\".{table}", $"truncate \"{database.Options.SchemaName}\".{table}" })
        {
            await using var command = new NpgsqlCommand(mutation, connection);
            await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        }
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task Approval_WaitsForSharedRouteLeaseAndLedgerAppendBarrierBeforeRevalidating()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var preview = ConsolidationTransitionStoreTests.Preview();
        await SeedBooksAsync(database, preview);
        var store = Store(database);
        await using (var reader = await store.AcquireAuthorityLeaseAsync())
        {
            // Preview retention must not upgrade the calculator's shared active-route fence.
            await store.RetainPreviewAsync(preview);
            using var canceled = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.AppendAsync(ConsolidationTransitionStoreTests.Receipt(preview), canceled.Token));
        }
        await using var blockerConnection = new NpgsqlConnection(database.Options.ConnectionString);
        await blockerConnection.OpenAsync();
        await using var blocker = await blockerConnection.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand($"select chain_id from \"{database.Options.SchemaName}\".ledger_event_audit_head where chain_id=1 for update", blockerConnection, blocker))
            await command.ExecuteScalarAsync();
        var called = false;
        using (var canceled = new CancellationTokenSource(TimeSpan.FromSeconds(1)))
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.AppendValidatedAsync(ConsolidationTransitionStoreTests.Receipt(preview),
                _ => { called = true; return Task.CompletedTask; }, canceled.Token));
        Assert.False(called);
        Assert.Null(await store.GetAsync(preview.Request.TransitionId));
        await blocker.CommitAsync();
        await store.AppendValidatedAsync(ConsolidationTransitionStoreTests.Receipt(preview), async ct =>
        {
            await using var nested = await store.AcquireAuthorityLeaseAsync(ct);
            Assert.Empty(await store.ListAsync(preview.Request.OrganizationId, preview.Request.OwnershipRootId, ct));
        });
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task ConcurrentApprovals_CannotForkOneRootVersion()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var first = ConsolidationTransitionStoreTests.Preview();
        var second = ConsolidationTransitionStoreTests.Preview(first.Request with { TransitionId = Guid.NewGuid(), NewEliminationBookId = Guid.NewGuid() });
        await SeedBooksAsync(database, first);
        await SeedBooksAsync(database, second);
        var store = Store(database);
        await store.RetainPreviewAsync(first);
        await store.RetainPreviewAsync(second);
        var outcomes = await Task.WhenAll(TryAppend(first), TryAppend(second));
        Assert.Single(outcomes.Where(value => value is null));
        Assert.Single(outcomes.OfType<InvalidOperationException>());
        Assert.Single(await store.ListAsync(first.Request.OrganizationId, first.Request.OwnershipRootId));

        async Task<Exception?> TryAppend(ConsolidationTransitionPreviewDto value)
        {
            try { await store.AppendAsync(ConsolidationTransitionStoreTests.Receipt(value)); return null; }
            catch (Exception exception) { return exception; }
        }
    }

    private static ConsolidationTransitionPostgresStore Store(LedgerPostgresTestDatabase database, string tenant = "tenant-a")
        => new(database.Options, new ConsolidationTransitionStoreTests.TenantAccessor(tenant));

    internal static async Task SeedBooksAsync(LedgerPostgresTestDatabase database, ConsolidationTransitionPreviewDto preview)
    {
        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            insert into "{database.Options.SchemaName}".fund_profile_tenancy(fund_profile_id,tenant_id,company_id)
            values('transition-fixture','tenant-a','company-a') on conflict do nothing;
            insert into "{database.Options.SchemaName}".ledger_books(ledger_book_id,fund_profile_id,fund_structure_node_id,
                fund_structure_node_kind,display_name,base_currency,created_at,updated_at,tenant_id,
                accounting_basis,accounting_policy_id,accounting_policy_version)
            values(@old,'transition-fixture',@root,'Fund','Historical elimination book '||@old::text,'USD',now(),now(),'tenant-a','Primary','consolidation-v2','w10-v2'),
                (@new,'transition-fixture',@root,'Fund','Fresh elimination book '||@new::text,'USD',now(),now(),'tenant-a','Primary','consolidation-v2','w10-v2') on conflict do nothing;
            """;
        command.Parameters.AddWithValue("old", preview.Request.PreviousEliminationBookId);
        command.Parameters.AddWithValue("new", preview.Request.NewEliminationBookId);
        command.Parameters.AddWithValue("root", preview.Request.OwnershipRootId);
        await command.ExecuteNonQueryAsync();
    }
}
