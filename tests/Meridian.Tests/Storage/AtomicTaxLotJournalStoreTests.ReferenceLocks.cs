using FluentAssertions;
using Meridian.Storage.Ledger;
using Npgsql;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task AcquisitionReferenceAuthority_HoldsDatabaseLocksThroughCommit()
        => AssertPostingReferenceLocksAsync(disposal: false, cancel: false);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task DisposalReferenceAuthority_HoldsDatabaseLocksThroughCommit()
        => AssertPostingReferenceLocksAsync(disposal: true, cancel: false);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task AcquisitionReferenceAuthority_CancellationReleasesLocksWithoutWritesBeforeRetry()
        => AssertPostingReferenceLocksAsync(disposal: false, cancel: true);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task DisposalReferenceAuthority_CancellationReleasesLocksWithoutWritesBeforeRetry()
        => AssertPostingReferenceLocksAsync(disposal: true, cancel: true);

    private static async Task AssertPostingReferenceLocksAsync(bool disposal, bool cancel)
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var (command, authority) = await PrepareReferencePostingAsync(database, disposal);
        var before = (await database.JournalStore.VerifyLedgerEventAuditAsync()).ChainedEvents;
        await using var blocker = new NpgsqlConnection(database.Options.ConnectionString);
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await using var hold = blocker.CreateCommand();
        hold.Transaction = transaction;
        hold.CommandText = $"select chain_id from \"{database.Options.SchemaName}\".ledger_event_audit_head where chain_id = 1 for update";
        (await hold.ExecuteScalarAsync()).Should().Be(1);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var operation = database.JournalStore.AppendAssetPostingAsync(command, cancellation.Token);
        try
        {
            await using var observer = new NpgsqlConnection(database.Options.ConnectionString);
            await observer.OpenAsync(deadline.Token);
            await using var wait = observer.CreateCommand();
            wait.CommandText = "select exists(select 1 from pg_stat_activity where @blocker = any(pg_blocking_pids(pid)))";
            wait.Parameters.AddWithValue("blocker", blocker.ProcessID);
            while (!(bool)(await wait.ExecuteScalarAsync(deadline.Token))!)
            {
                if (operation.IsCompleted)
                {
                    await operation;
                    throw new InvalidOperationException("Posting completed without waiting for the retained audit head.");
                }
                await Task.Delay(20, deadline.Token);
            }
            await AssertPostingReferencesLockedAsync(authority, held: true, deadline.Token);
            if (cancel)
            {
                await cancellation.CancelAsync();
                var cancelled = async () => await operation;
                await cancelled.Should().ThrowAsync<OperationCanceledException>();
                await AssertPostingReferencesLockedAsync(authority, held: false, deadline.Token);
            }
        }
        finally
        {
            await transaction.RollbackAsync();
            try
            { await operation; }
            catch (OperationCanceledException) when (cancel) { }
        }
        await AssertPostingReferencesLockedAsync(authority, held: false, deadline.Token);
        if (cancel)
        {
            (await database.JournalStore.GetAtomicTaxLotPostingAsync(command.MutationBatchId)).Should().BeNull();
            (await database.JournalStore.VerifyLedgerEventAuditAsync()).ChainedEvents.Should().Be(before);
            (await database.RestartJournalStore().AppendAssetPostingAsync(command)).IsExactReplay.Should().BeFalse();
        }
        else
            (await operation).IsExactReplay.Should().BeFalse();
        (await new PostgresLedgerJournalStore(database.Options).AppendAssetPostingAsync(command)).IsExactReplay.Should().BeTrue();
    }

    private static async Task AssertPostingReferencesLockedAsync(LotReferenceAuthority authority, bool held, CancellationToken ct)
    {
        await using var observer = new NpgsqlConnection(authority.SecurityOptions.ConnectionString);
        await observer.OpenAsync(ct);
        foreach (var (schema, table, key, id) in new[]
        {
            (authority.SecurityOptions.Schema, "securities", "security_id", authority.Security.SecurityId),
            (authority.PositionOptions.Schema, "book_position_projections", "position_id", authority.Position.PositionId)
        })
        {
            await using var command = observer.CreateCommand();
            command.CommandText = $"select {key} from \"{schema}\".{table} where {key} = @id for update nowait";
            command.Parameters.AddWithValue("id", id);
            if (held)
            {
                var competingWrite = () => command.ExecuteScalarAsync(ct);
                (await competingWrite.Should().ThrowAsync<PostgresException>()).Which.SqlState
                    .Should().Be(PostgresErrorCodes.LockNotAvailable);
            }
            else
                (await command.ExecuteScalarAsync(ct)).Should().Be(id);
        }
    }
}
