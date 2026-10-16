using FluentAssertions;
using Npgsql;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalPosting_LocksBookBeforeReferencesInUnscopedCompatibilityMode()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var command = fixture.Command();
        await using var blocker = new NpgsqlConnection(fixture.Options.ConnectionString);
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await using var holdBook = blocker.CreateCommand();
        holdBook.Transaction = transaction;
        holdBook.CommandText = $"select ledger_book_id from \"{fixture.Options.SchemaName}\".ledger_books where ledger_book_id = @id for update";
        holdBook.Parameters.AddWithValue("id", fixture.BookId);
        (await holdBook.ExecuteScalarAsync()).Should().Be(fixture.BookId);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var operation = fixture.Store.AppendAssetPostingAsync(command, deadline.Token);
        try
        {
            await using var observer = new NpgsqlConnection(fixture.Options.ConnectionString);
            await observer.OpenAsync(deadline.Token);
            await using var wait = observer.CreateCommand();
            wait.CommandText = "select exists(select 1 from pg_stat_activity where @blocker = any(pg_blocking_pids(pid)))";
            wait.Parameters.AddWithValue("blocker", blocker.ProcessID);
            while (!(bool)(await wait.ExecuteScalarAsync(deadline.Token))!)
            {
                if (operation.IsCompleted)
                {
                    await operation;
                    throw new InvalidOperationException("Posting completed without waiting for the locked ledger book.");
                }
                await Task.Delay(20, deadline.Token);
            }
            // A backfill holding the book must still be able to lock this security. Otherwise
            // posting holds the security while waiting for that book, completing a lock cycle.
            await using var reference = observer.CreateCommand();
            reference.CommandText = $"select security_id from \"{fixture.SecuritySchema}\".securities where security_id = @id for update nowait";
            reference.Parameters.AddWithValue("id", fixture.Security.SecurityId);
            (await reference.ExecuteScalarAsync(deadline.Token)).Should().Be(fixture.Security.SecurityId);
        }
        finally
        {
            await transaction.RollbackAsync();
            await operation;
        }
        (await operation).IsExactReplay.Should().BeFalse();
        (await fixture.Restart().AppendAssetPostingAsync(command)).IsExactReplay.Should().BeTrue();
    }
}
