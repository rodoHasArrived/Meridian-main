using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Tenancy;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
using Npgsql;

namespace Meridian.Tests.Storage;

public sealed class LegalEntityReadAuthorityPostgresTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task SessionEntitlement_IsReleasedAfterRollbackEvenWhenRequestWasCanceled()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var bookId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(database.Options.ConnectionString)
        { NoResetOnClose = true }.ConnectionString);
        await connection.OpenAsync();
        await using var entitlement = await CashDividendEntitlementLock.AcquireSessionSharedAsync(connection, bookId, CancellationToken.None);
        await using (var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable))
        {
            using var canceled = new CancellationTokenSource();
            await canceled.CancelAsync();
            await using var command = new NpgsqlCommand("select 1", connection, transaction);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => command.ExecuteScalarAsync(canceled.Token));
            await transaction.RollbackAsync(CancellationToken.None);
        }
        await entitlement.DisposeAsync();
        await using var competing = new NpgsqlConnection(database.Options.ConnectionString);
        await competing.OpenAsync();
        await using var competingTransaction = await competing.BeginTransactionAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await CashDividendEntitlementLock.AcquireAsync(competing, competingTransaction, bookId, true, timeout.Token);
        await competingTransaction.CommitAsync(timeout.Token);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task NativeEntitlementBeforeGraph_QueuedGraphWriterAndOrdinaryAppend_AllComplete()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var options = new FundStructureStoreOptions
        {
            ConnectionString = database.Options.ConnectionString,
            Schema = database.Options.SchemaName + "_graph_order"
        };
        try
        {
            await new FundStructureMigrationRunner(options).EnsureMigratedAsync(ct);
            var graph = new PostgresFundStructureStore(options);
            var lifecycle = new PausingGraphAuthority(graph);
            var store = new PostgresLedgerJournalStore(database.Options, postingAuthorities: () =>
                new ILedgerPostingAuthority[] { lifecycle }.Concat(LedgerPostgresTestDatabase.SyntheticFinancialPostingAuthorities()
                    .Where(authority => authority.ControlName != "entity-lifecycle")));
            var book = ConsolidationStorageFixture.Book("Native graph order");
            await database.JournalStore.SaveLedgerBookAsync(book, ct);
            var period = await database.JournalStore.SavePeriodAsync(ConsolidationStorageFixture.Period(book), 0, ct: ct);
            var nativeName = "native-graph-order-" + Guid.NewGuid().ToString("N");
            var writerName = "queued-graph-order-" + Guid.NewGuid().ToString("N");
            await using var native = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(options.ConnectionString)
            { ApplicationName = nativeName }.ConnectionString);
            await native.OpenAsync(ct);
            await using var nativeTransaction = await native.BeginTransactionAsync(ct);
            await using var writer = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(options.ConnectionString)
            { ApplicationName = writerName }.ConnectionString);
            await writer.OpenAsync(ct);
            await using var writerTransaction = await writer.BeginTransactionAsync(ct);
            await using var existingReader = await graph.AcquireConsolidationAuthorityLeaseAsync(ct);
            try
            {
                var ordinaryPosting = store.AppendAsync(ConsolidationStorageFixture.Write(book, period), ct);
                await lifecycle.BeforeGraph.Task.WaitAsync(ct);
                // The exact producer helper must wait for ordinary entitlement without owning
                // graph. Reversing those two native steps recreates the queued-writer cycle.
                var nativePosting = CompleteNativeAuthorityAsync();
                await WaitUntilBlockedAsync(options.ConnectionString, nativeName, ct);
                var graphMutation = MutateGraphAsync();
                await WaitUntilBlockedAsync(options.ConnectionString, writerName, ct);
                await existingReader.DisposeAsync();
                lifecycle.ContinueGraph.TrySetResult();
                await Task.WhenAll(ordinaryPosting, nativePosting, graphMutation).WaitAsync(TimeSpan.FromSeconds(10), ct);
                Assert.Single(await database.JournalStore.GetByPeriodAsync(period.PeriodId, ct));
            }
            finally
            {
                lifecycle.ContinueGraph.TrySetResult();
                await existingReader.DisposeAsync();
                await timeout.CancelAsync();
            }

            async Task CompleteNativeAuthorityAsync()
            {
                await using var heldGraph = await store.AcquireNativeBookStructureAuthorityAsync(native, nativeTransaction, book.LedgerBookId, ct);
                await using var command = new NpgsqlCommand($"select period_id from \"{database.Options.SchemaName}\".accounting_periods where period_id=@id for update",
                    native, nativeTransaction);
                command.Parameters.AddWithValue("id", period.PeriodId);
                await command.ExecuteScalarAsync(ct);
                await nativeTransaction.CommitAsync(ct);
            }
            async Task MutateGraphAsync()
            {
                await using var command = new NpgsqlCommand($"update {options.Schema}.legal_entity set name=name", writer, writerTransaction);
                await command.ExecuteNonQueryAsync(ct);
                await writerTransaction.CommitAsync(ct);
            }
        }
        finally
        {
            await using var cleanup = new NpgsqlConnection(options.ConnectionString);
            await cleanup.OpenAsync(CancellationToken.None);
            await using var command = new NpgsqlCommand($"drop schema if exists {options.Schema} cascade", cleanup);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task StrictAuthority_WaitsForNativeEntitlementBeforeTakingAnyPeriodRowLock()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var book = ConsolidationStorageFixture.Book("Entitlement order book");
        await new PostgresFundProfileTenancyRegistry(database.Options).BindAsync(book.FundProfileId, "tenant-a", ct: ct);
        await database.JournalStore.SaveLedgerBookAsync(book, ct);
        var period = await database.JournalStore.SavePeriodAsync(ConsolidationStorageFixture.Period(book), 0, ct: ct);
        var write = ConsolidationStorageFixture.Write(book, period);
        var store = new PostgresLedgerJournalStore(database.Options, new ConsolidationTransitionStoreTests.TenantAccessor("tenant-a"),
            TenantScopeEnforcementOptions.FailClosed, postingAuthorities: LedgerPostgresTestDatabase.SyntheticFinancialPostingAuthorities);
        await using var native = new NpgsqlConnection(database.Options.ConnectionString);
        await native.OpenAsync(ct);
        await using var nativeTransaction = await native.BeginTransactionAsync(ct);
        await CashDividendEntitlementLock.AcquireAsync(native, nativeTransaction, book.LedgerBookId, true, ct);
        var name = "strict-entitlement-order-" + Guid.NewGuid().ToString("N");
        await using var ordinary = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(database.Options.ConnectionString)
        { ApplicationName = name }.ConnectionString);
        await ordinary.OpenAsync(ct);
        await using var ordinaryTransaction = await ordinary.BeginTransactionAsync(ct);
        try
        {
            var acquiring = store.AcquirePostingAuthorityAsync(ordinary, ordinaryTransaction, [write], ct);
            await WaitUntilBlockedAsync(database.Options.ConnectionString, name, ct);
            // Native already owns entitlement. It must still be able to lock this period;
            // an earlier strict FOR SHARE from the waiting ordinary writer would deadlock.
            await using (var lockPeriod = new NpgsqlCommand($"select period_id from \"{database.Options.SchemaName}\".accounting_periods where period_id=@id for update",
                native, nativeTransaction))
            {
                lockPeriod.Parameters.AddWithValue("id", period.PeriodId);
                await lockPeriod.ExecuteScalarAsync(ct).WaitAsync(TimeSpan.FromSeconds(5), ct);
            }
            await nativeTransaction.CommitAsync(ct);
            await using var authority = await acquiring.WaitAsync(ct);
            await store.AppendAsync(ordinary, ordinaryTransaction, write, ct);
            await ordinaryTransaction.CommitAsync(ct);
            Assert.Single(await database.JournalStore.GetByPeriodAsync(period.PeriodId, ct));
        }
        finally { await timeout.CancelAsync(); }
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task LiveGraphProof_DoesNotReacquireShareBehindQueuedEntityWriter_AndExpiredProofIsRejected()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var options = new FundStructureStoreOptions
        {
            ConnectionString = database.Options.ConnectionString,
            Schema = database.Options.SchemaName + "_structure"
        };
        try
        {
            await new FundStructureMigrationRunner(options).EnsureMigratedAsync(ct);
            var graph = new PostgresFundStructureStore(options);
            var agreements = new PostgresIntercompanyAgreementStore(options);
            await using var graphLease = await graph.AcquireConsolidationAuthorityLeaseAsync(ct);
            var proof = Assert.IsAssignableFrom<ILegalEntityReadAuthorityLease>(graphLease).StructureAuthorityProof;
            var writerName = "legal-entity-queued-" + Guid.NewGuid().ToString("N");
            await using var writer = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(options.ConnectionString)
            { ApplicationName = writerName }.ConnectionString);
            await writer.OpenAsync(ct);
            await using var transaction = await writer.BeginTransactionAsync(ct);
            await using var command = new NpgsqlCommand($"update {options.Schema}.legal_entity set name=name", writer, transaction);
            var mutation = command.ExecuteNonQueryAsync(ct);
            try
            {
                await WaitUntilBlockedAsync(options.ConnectionString, writerName, ct);
                // This must complete while the queued writer still waits on the original graph
                // lease. A second legal_entity SHARE on another connection would queue forever.
                await using var retained = await agreements.AcquirePostingLeaseAsync("tenant-a", [], proof, ct)
                    .WaitAsync(TimeSpan.FromSeconds(5), ct);
                Assert.Empty(retained.Agreements);
                Assert.False(mutation.IsCompleted);
                Assert.True(await agreements.ExecuteAsync("tenant-a", Guid.NewGuid(), proof,
                    (state, _) => Task.FromResult(state.State is null), ct).WaitAsync(TimeSpan.FromSeconds(5), ct));
                var foreign = new PostgresIntercompanyAgreementStore(new FundStructureStoreOptions
                { ConnectionString = options.ConnectionString, Schema = options.Schema + "_other" });
                await Assert.ThrowsAsync<IntercompanyAgreementValidationException>(() =>
                    foreign.AcquirePostingLeaseAsync("tenant-a", [], proof, ct));
            }
            finally { await graphLease.DisposeAsync(); }
            await mutation.WaitAsync(ct);
            await transaction.CommitAsync(ct);
            Assert.False(proof.IsActive);
            await Assert.ThrowsAsync<IntercompanyAgreementValidationException>(() =>
                agreements.AcquirePostingLeaseAsync("tenant-a", [], proof, ct));
            await Assert.ThrowsAsync<IntercompanyAgreementValidationException>(() =>
                agreements.ExecuteAsync("tenant-a", Guid.NewGuid(), proof, (_, _) => Task.FromResult(true), ct));
        }
        finally
        {
            await using var cleanup = new NpgsqlConnection(options.ConnectionString);
            await cleanup.OpenAsync(CancellationToken.None);
            await using var command = new NpgsqlCommand($"drop schema if exists {options.Schema} cascade", cleanup);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private static async Task WaitUntilBlockedAsync(string connectionString, string applicationName, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("select exists(select 1 from pg_stat_activity where application_name=@name and cardinality(pg_blocking_pids(pid))>0)", connection);
        command.Parameters.AddWithValue("name", applicationName);
        while (!(bool)(await command.ExecuteScalarAsync(ct))!)
            await Task.Delay(10, ct);
    }

    // Isolates real graph/entitlement/period exclusion. Lifecycle eligibility itself is covered
    // by the native journal/spine/case Active/Dissolved roundtrip, not this scheduling fixture.
    private sealed class PausingGraphAuthority(PostgresFundStructureStore graph)
        : ILedgerPostingAuthority, ILedgerStructurePostingAuthority
    {
        public string ControlName => "entity-lifecycle";
        public TaskCompletionSource BeforeGraph { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ContinueGraph { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IAsyncDisposable> AcquireStructureAuthorityAsync(CancellationToken ct = default)
            => graph.AcquireConsolidationAuthorityLeaseAsync(ct);
        public async Task<IAsyncDisposable> AcquireAsync(IReadOnlyList<LedgerPostingAuthorityContext> contexts, CancellationToken ct = default)
        {
            BeforeGraph.TrySetResult();
            await ContinueGraph.Task.WaitAsync(ct);
            var lease = await AcquireStructureAuthorityAsync(ct);
            foreach (var context in contexts)
            {
                context.RetainStructureAuthority(lease);
                context.Decisions.Add(new(ControlName, "real-graph-order-fixture", true, "Actual graph fence retained for lock-order regression."));
            }
            return lease;
        }
    }
}
