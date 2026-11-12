using System.Diagnostics;
using Meridian.Contracts.FundStructure;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Npgsql;

namespace Meridian.Tests.Storage;

[Trait("Category", "Integration")]
public sealed class CashDividendEntitlementConcurrencyTests
{
    [LedgerDatabaseFact]
    public Task ExplicitBookAppend_WithDifferentAggregate_WaitsBeforePeriodLock()
        => CheckBookBarrierAsync(explicitBook: true, callerOwned: false);

    [LedgerDatabaseFact]
    public Task PeriodDerivedBookAppend_WithDifferentAggregate_WaitsBeforePeriodLock()
        => CheckBookBarrierAsync(explicitBook: false, callerOwned: false);

    [LedgerDatabaseFact]
    public Task CallerOwnedExplicitBookAppend_WithDifferentAggregate_WaitsBeforePeriodLock()
        => CheckBookBarrierAsync(explicitBook: true, callerOwned: true);

    [LedgerDatabaseFact]
    public Task CallerOwnedPeriodDerivedBookAppend_WithDifferentAggregate_WaitsBeforePeriodLock()
        => CheckBookBarrierAsync(explicitBook: false, callerOwned: true);

    [LedgerDatabaseFact]
    public Task CallerOwnedPeriodDerivedAppend_RefusesBookRebindingWhileWaiting()
        => CheckBookBarrierAsync(explicitBook: false, callerOwned: true, rebindPeriod: true);

    private static async Task CheckBookBarrierAsync(bool explicitBook, bool callerOwned, bool rebindPeriod = false)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var at = new DateTimeOffset(2026, 5, 15, 0, 0, 0, TimeSpan.Zero);
        var book = new LedgerBookRecord(Guid.NewGuid(), "entitlement-concurrency", Guid.NewGuid(),
            FundStructureNodeKindDto.Entity, "Synthetic complete entitlement book", "USD", at, at);
        await database.JournalStore.SaveLedgerBookAsync(book, ct);
        var period = await database.JournalStore.SavePeriodAsync(new(Guid.NewGuid(), book.LedgerBookId, 2026, 5,
            "Entitlement concurrency May", new(2026, 5, 1), new(2026, 5, 31), "Open", at, null, 0), 0, ct: ct);
        var id = Guid.NewGuid();
        var aggregate = Guid.NewGuid();
        const string description = "Ordinary independently scoped posting";
        var write = new LedgerJournalEntryWrite(new(id, at, description,
        [
            new(Guid.NewGuid(), id, at, LedgerAccounts.Cash, 100m, 0m, description),
            new(Guid.NewGuid(), id, at, LedgerAccounts.CapitalAccount, 0m, 100m, description)
        ]), aggregate, period.PeriodId, LedgerBookId: explicitBook ? book.LedgerBookId : null);
        var application = "entitlement-ordinary-" + Guid.NewGuid().ToString("N");
        var ordinaryOptions = new LedgerJournalStoreOptions
        {
            ConnectionString = new NpgsqlConnectionStringBuilder(database.Options.ConnectionString)
            { ApplicationName = application, Pooling = false }.ConnectionString,
            SchemaName = database.Options.SchemaName
        };
        var ordinaryStore = new PostgresLedgerJournalStore(ordinaryOptions);
        await using var nativeConnection = new NpgsqlConnection(database.Options.ConnectionString);
        await nativeConnection.OpenAsync(ct);
        await using var nativeTransaction = await nativeConnection.BeginTransactionAsync(ct);
        await database.JournalStore.LockCashDividendBookAsync(nativeConnection, nativeTransaction, book.LedgerBookId, ct);
        var append = AppendOrdinaryAsync();
        var committedNative = false;
        try
        {
            // Observe the real server wait instead of relying on a delay. Before the fix the
            // ordinary writer held the period and waited on the native book's transaction ID.
            var wait = await WaitForOrdinaryLockAsync(database.Options.ConnectionString, application, append, ct);
            Assert.Equal("advisory", wait);
            Assert.False(append.IsCompleted);

            // A wrong declared book must fail before waiting on any entitlement/book row.
            var wrongBook = () => database.JournalStore.AppendAsync(write with { LedgerBookId = Guid.NewGuid() }, ct);
            await Assert.ThrowsAsync<LedgerValidationException>(() => wrongBook().WaitAsync(TimeSpan.FromSeconds(2), ct));

            if (rebindPeriod)
            {
                var replacement = book with
                {
                    LedgerBookId = Guid.NewGuid(),
                    FundStructureNodeId = Guid.NewGuid(),
                    DisplayName = "Synthetic rebound book"
                };
                await database.JournalStore.SaveLedgerBookAsync(replacement, ct);
                await database.JournalStore.SavePeriodAsync(period with { LedgerBookId = replacement.LedgerBookId }, period.Version, ct: ct);
            }
            else
            {
                await using var periodLock = nativeConnection.CreateCommand();
                periodLock.Transaction = nativeTransaction;
                periodLock.CommandText = $"select period_id from \"{database.Options.SchemaName}\".accounting_periods where period_id=@period for update nowait";
                periodLock.Parameters.AddWithValue("period", period.PeriodId);
                Assert.Equal(period.PeriodId, (Guid)(await periodLock.ExecuteScalarAsync(ct))!);
            }
            await nativeTransaction.CommitAsync(ct);
            committedNative = true;
            if (rebindPeriod)
            {
                var error = await Assert.ThrowsAsync<LedgerValidationException>(() => append.WaitAsync(TimeSpan.FromSeconds(5), ct));
                Assert.Contains("book changed", error.Message, StringComparison.OrdinalIgnoreCase);
                Assert.Empty(await database.JournalStore.GetByPeriodAsync(period.PeriodId, ct));
            }
            else
            {
                await append.WaitAsync(TimeSpan.FromSeconds(5), ct);
                var retained = Assert.Single(await database.JournalStore.GetByPeriodAsync(period.PeriodId, ct));
                Assert.Equal(aggregate, retained.AggregateId);
                Assert.Equal(id, retained.Entry.JournalEntryId);
                Assert.Equal(book.LedgerBookId, (await database.JournalStore.GetPeriodAsync(period.PeriodId, ct))!.LedgerBookId);
            }
        }
        finally
        {
            if (!committedNative)
                await nativeTransaction.RollbackAsync(CancellationToken.None);
            try
            { await append.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch { /* Keep the primary assertion; release blocked test writers during cleanup. */ }
        }

        async Task AppendOrdinaryAsync()
        {
            if (!callerOwned)
            {
                await ordinaryStore.AppendAsync(write, ct);
                return;
            }
            await using var connection = new NpgsqlConnection(ordinaryOptions.ConnectionString);
            await connection.OpenAsync(ct);
            await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
            await ordinaryStore.AppendAsync(connection, transaction, write, ct);
            await transaction.CommitAsync(ct);
        }
    }

    private static async Task<string> WaitForOrdinaryLockAsync(string connectionString, string application,
        Task append, CancellationToken ct)
    {
        var elapsed = Stopwatch.StartNew();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        while (elapsed.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (append.IsCompleted)
            {
                await append;
                throw new InvalidOperationException("The ordinary append completed while the native book barrier was held.");
            }
            await using var command = connection.CreateCommand();
            command.CommandText = "select wait_event from pg_stat_activity where application_name=@application and state='active' and wait_event_type='Lock'";
            command.Parameters.AddWithValue("application", application);
            if (await command.ExecuteScalarAsync(ct) is string wait)
                return wait;
            await Task.Delay(25, ct);
        }
        throw new TimeoutException("The ordinary append did not reach its deterministic PostgreSQL lock wait.");
    }
}
