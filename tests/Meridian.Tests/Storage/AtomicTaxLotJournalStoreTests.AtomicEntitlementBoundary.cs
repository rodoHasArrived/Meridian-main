using System.Data;
using FluentAssertions;
using Npgsql;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task AtomicPostingWaitsForJournalHistoryBeforeRetainingSharedDividendAuthority()
        => AssertAtomicEntitlementBoundaryAsync(cancel: false);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task CancelledAtomicPostingAtJournalHistoryBoundaryLeavesNoReceiptAndCanRetry()
        => AssertAtomicEntitlementBoundaryAsync(cancel: true);

    private static async Task AssertAtomicEntitlementBoundaryAsync(bool cancel)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var ct = deadline.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var (command, _) = await PrepareReferencePostingAsync(database, disposal: false);
        await using var producerConnection = new NpgsqlConnection(database.Options.ConnectionString);
        await producerConnection.OpenAsync(ct);
        await using var producer = await producerConnection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await using (var gate = producerConnection.CreateCommand())
        {
            gate.Transaction = producer;
            gate.CommandText = $"lock table \"{database.Options.SchemaName}\".journal_entries in share row exclusive mode";
            await gate.ExecuteNonQueryAsync(ct);
        }

        var operation = database.RestartJournalStore().AppendAssetPostingAsync(command, cancellation.Token);
        var released = false;
        try
        {
            await using var observer = new NpgsqlConnection(database.Options.ConnectionString);
            await observer.OpenAsync(ct);
            while (true)
            {
                await using var waiting = observer.CreateCommand();
                waiting.CommandText = """
                    select exists(select 1 from pg_catalog.pg_stat_activity
                        where @producer = any(pg_catalog.pg_blocking_pids(pid))
                          and wait_event_type='Lock' and wait_event='relation')
                    """;
                waiting.Parameters.AddWithValue("producer", producerConnection.ProcessID);
                if (await waiting.ExecuteScalarAsync(ct) is true)
                    break;
                operation.IsCompleted.Should().BeFalse("the real atomic writer must reach the journal history table gate");
                await Task.Delay(20, ct);
            }

            // The producer already owns the table gate. This must finish while the atomic
            // transaction waits: taking session entitlement first would create a lock cycle.
            await database.JournalStore.LockCashDividendBookAsync(producerConnection, producer, command.LedgerBookId, ct)
                .WaitAsync(TimeSpan.FromSeconds(5), ct);
            operation.IsCompleted.Should().BeFalse();
            if (cancel)
            {
                await cancellation.CancelAsync();
                var cancelled = async () => await operation;
                await cancelled.Should().ThrowAsync<OperationCanceledException>();
            }
            await producer.RollbackAsync(ct);
            released = true;
            if (cancel)
                (await database.JournalStore.GetAtomicTaxLotPostingAsync(command.MutationBatchId, ct)).Should().BeNull();
            var result = cancel
                ? await database.RestartJournalStore().AppendAssetPostingAsync(command, ct)
                : await operation.WaitAsync(ct);
            result.IsExactReplay.Should().BeFalse();
            result.MutatedLots.Should().ContainSingle();
            (await database.RestartJournalStore().AppendAssetPostingAsync(command, ct)).IsExactReplay.Should().BeTrue();
        }
        finally
        {
            if (!operation.IsCompleted)
                await cancellation.CancelAsync();
            if (!released)
                await producer.RollbackAsync(CancellationToken.None);
            try
            { await operation; }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        }
    }
}
