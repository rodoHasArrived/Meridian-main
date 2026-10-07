using System.Data;
using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Npgsql;

namespace Meridian.Tests.Storage;

[Trait("Category", "Integration")]
public sealed class ConsolidationSourcesPostgresTests
{
    [LedgerDatabaseFact]
    public async Task UnchangedAsOfSources_PostEvenWhenFutureDatedJournalWasAdded()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var (books, periods) = await CreateBooksAsync(database, ct);
        await database.JournalStore.AppendAsync(ConsolidationStorageFixture.Write(books[0], periods[0]), ct);
        var write = await ReviewedWriteAsync(database, books, periods, ct);
        var june = await database.JournalStore.SavePeriodAsync(ConsolidationStorageFixture.Period(books[1], 6), 0, ct: ct);
        await database.JournalStore.AppendAsync(ConsolidationStorageFixture.Write(books[1], june, new(2026, 6, 1)), ct);

        await database.JournalStore.AppendAsync(write, ct);

        (await database.JournalStore.GetByPeriodAsync(periods[2].PeriodId, ct)).Should().ContainSingle();
        (await database.JournalStore.VerifyLedgerEventAuditAsync(ct)).ChainedEvents.Should().Be(7);
    }

    [LedgerDatabaseFact]
    public async Task BackdatedSourceInEarlierPeriod_RequiresRenewedReviewWithoutPosting()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var (books, periods) = await CreateBooksAsync(database, ct);
        var write = await ReviewedWriteAsync(database, books, periods, ct);
        var april = await database.JournalStore.SavePeriodAsync(ConsolidationStorageFixture.Period(books[0], 4), 0, ct: ct);
        await database.JournalStore.AppendAsync(ConsolidationStorageFixture.Write(books[0], april, new(2026, 4, 20)), ct);

        var post = () => database.JournalStore.AppendAsync(write, ct);
        await post.Should().ThrowAsync<LedgerValidationException>().WithMessage("*renewed review*");

        (await database.JournalStore.GetByPeriodAsync(periods[2].PeriodId, ct)).Should().BeEmpty();
    }

    [LedgerDatabaseFact]
    public async Task EliminationBookChangedAfterReview_RequiresRenewedReview()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var (books, periods) = await CreateBooksAsync(database, ct);
        var write = await ReviewedWriteAsync(database, books, periods, ct);
        await database.JournalStore.AppendAsync(ConsolidationStorageFixture.Write(books[2], periods[2]), ct);

        var post = () => database.JournalStore.AppendAsync(write, ct);
        await post.Should().ThrowAsync<LedgerValidationException>().WithMessage("*renewed review*");

        (await database.JournalStore.GetByPeriodAsync(periods[2].PeriodId, ct)).Should().ContainSingle();
    }

    [LedgerDatabaseFact]
    public async Task ConcurrentSourceCommit_IsObservedAfterAuditLockBeforeEliminationAppend()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var (books, periods) = await CreateBooksAsync(database, ct);
        var elimination = await ReviewedWriteAsync(database, books, periods, ct);

        await using var sourceConnection = new NpgsqlConnection(database.Options.ConnectionString);
        await sourceConnection.OpenAsync(ct);
        await using var sourceTransaction = await sourceConnection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await database.JournalStore.AppendAsync(sourceConnection, sourceTransaction,
            ConsolidationStorageFixture.Write(books[0], periods[0]), ct);

        await using var eliminationConnection = new NpgsqlConnection(database.Options.ConnectionString);
        await eliminationConnection.OpenAsync(ct);
        await using var eliminationTransaction = await eliminationConnection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var posting = database.JournalStore.AppendAsync(eliminationConnection, eliminationTransaction, elimination, ct);
        await WaitUntilBlockedAsync(database, eliminationConnection.ProcessID, ct);
        await sourceTransaction.CommitAsync(ct);

        var complete = async () => await posting;
        await complete.Should().ThrowAsync<LedgerValidationException>().WithMessage("*renewed review*");
        await eliminationTransaction.RollbackAsync(ct);
        (await database.JournalStore.GetByPeriodAsync(periods[2].PeriodId, ct)).Should().BeEmpty();
    }

    private static async Task<(LedgerBookRecord[] Books, LedgerAccountingPeriod[] Periods)> CreateBooksAsync(
        LedgerPostgresTestDatabase database, CancellationToken ct)
    {
        var books = new[] { ConsolidationStorageFixture.Book("Entity A"), ConsolidationStorageFixture.Book("Entity B"), ConsolidationStorageFixture.Book("Eliminations") };
        var periods = new LedgerAccountingPeriod[books.Length];
        for (var index = 0; index < books.Length; index++)
        {
            await database.JournalStore.SaveLedgerBookAsync(books[index], ct);
            periods[index] = await database.JournalStore.SavePeriodAsync(ConsolidationStorageFixture.Period(books[index]), 0, ct: ct);
        }
        return (books, periods);
    }

    private static async Task<LedgerJournalEntryWrite> ReviewedWriteAsync(LedgerPostgresTestDatabase database,
        LedgerBookRecord[] books, LedgerAccountingPeriod[] periods, CancellationToken ct)
    {
        var versions = new List<ConsolidationBookVersionDto>();
        foreach (var book in books)
        {
            var records = await database.JournalStore.QueryAsync(new(LedgerBookId: book.LedgerBookId,
                EffectiveTo: ConsolidationStorageFixture.AsOf), ct);
            versions.Add(new(book.LedgerBookId, records.Count == 0 ? 0 : records.Max(item => item.GlobalSequence), records.Count));
        }
        return ConsolidationStorageFixture.WithEvidence(ConsolidationStorageFixture.Write(books[2], periods[2]),
            ConsolidationStorageFixture.Evidence(books[2], periods[2], versions));
    }

    private static async Task WaitUntilBlockedAsync(LedgerPostgresTestDatabase database, int processId, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "select cardinality(pg_blocking_pids(@pid)) > 0;";
        command.Parameters.AddWithValue("pid", processId);
        while (!(bool)(await command.ExecuteScalarAsync(ct))!)
            await Task.Delay(10, ct);
    }
}
