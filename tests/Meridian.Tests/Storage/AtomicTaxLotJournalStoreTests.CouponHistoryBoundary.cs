using System.Data;
using FluentAssertions;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Npgsql;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task CouponHistory_OrdinaryExternalAppendExcludesOtherPeriodsUntilCommit()
        => AssertCouponHistoryWaitAsync(rollback: false);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task CouponHistory_RollbackReleasesOtherPeriodWithoutRetainingTheSourceJournal()
        => AssertCouponHistoryWaitAsync(rollback: true);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CouponHistory_AtomicWriterWaitsForOrdinaryWriterInAnotherPeriod()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var seed = await PrepareFactFreeAcquisitionAsync(database, face: false);
        var acquisition = BuildAcquisitionCommand(seed.LedgerBookId, seed.Journal.PeriodId, seed.ExpectedPeriodVersion);
        var authority = await database.SeedLotReferenceAuthorityAsync(acquisition.LedgerBookId,
            acquisition.AcquisitionLot!.SecurityId, acquisition.AcquisitionLot.BookPositionId);
        acquisition = (acquisition with { ReferenceVersions = authority.Versions }).WithComputedFingerprint();
        var june = await database.JournalStore.SavePeriodAsync(new(Guid.NewGuid(), acquisition.LedgerBookId,
            2026, 6, "June history lock", new(2026, 6, 1), new(2026, 6, 30), "Open",
            DateTimeOffset.Parse("2026-06-01T00:00:00Z"), null, 0), 0, ct: ct);
        var ordinary = CouponHistoryWrite(june.PeriodId, DateTimeOffset.Parse("2026-06-12T12:00:00Z"))
            with
        { AggregateId = acquisition.LedgerBookId, LedgerBookId = acquisition.LedgerBookId };
        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await database.JournalStore.AppendAsync(connection, transaction, ordinary, ct);
        var application = "coupon-atomic-history-" + Guid.NewGuid().ToString("N");
        var atomicStore = new PostgresLedgerJournalStore(CouponHistoryOptions(database.Options, application),
            backfillSecurityMaster: () => authority.Securities, backfillPositions: () => authority.Positions);
        var waiting = atomicStore.AppendAssetPostingAsync(acquisition, ct);
        await WaitForCouponHistoryRelationLockAsync(database.Options.ConnectionString, application, ct);
        waiting.IsCompleted.Should().BeFalse();
        // Readers used by canonical opening validation remain available while publication waits.
        (await database.JournalStore.GetByPeriodAsync(june.PeriodId, ct)).Should().BeEmpty();
        await transaction.CommitAsync(ct);
        var posted = await waiting;
        posted.IsExactReplay.Should().BeFalse();
        (await database.JournalStore.GetByPeriodAsync(june.PeriodId, ct)).Should().ContainSingle();
        (await database.JournalStore.GetByPeriodAsync(acquisition.Journal.PeriodId, ct)).Should().ContainSingle();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CouponHistory_StaleExternalSerializableSnapshotCannotPostAfterReadCommittedJournal()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var first = await database.SavePeriodAsync(Guid.NewGuid(), "Open", ct);
        var second = await database.SavePeriodAsync(Guid.NewGuid(), "Open", ct);
        var at = DateTimeOffset.Parse("2026-05-12T12:00:00Z");
        await database.JournalStore.AppendAsync(CouponHistoryWrite(first.PeriodId, at), ct);
        await using var staleConnection = new NpgsqlConnection(database.Options.ConnectionString);
        await staleConnection.OpenAsync(ct);
        await using var stale = await staleConnection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await using (var read = staleConnection.CreateCommand())
        {
            read.Transaction = stale;
            read.CommandText = $"select count(*) from \"{database.Options.SchemaName}\".journal_entries";
            await read.ExecuteScalarAsync(ct);
        }
        await using (var freshConnection = new NpgsqlConnection(database.Options.ConnectionString))
        {
            await freshConnection.OpenAsync(ct);
            await using var fresh = await freshConnection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await database.JournalStore.AppendAsync(freshConnection, fresh,
                CouponHistoryWrite(first.PeriodId, at.AddMinutes(1)), ct);
            await fresh.CommitAsync(ct);
        }
        var rejected = CouponHistoryWrite(second.PeriodId, at.AddMinutes(2));
        var error = await Assert.ThrowsAsync<PostgresException>(() => database.JournalStore.AppendAsync(
            staleConnection, stale, rejected, ct));
        error.SqlState.Should().Be(PostgresErrorCodes.SerializationFailure);
        await stale.RollbackAsync(ct);
        (await database.JournalStore.GetByPeriodAsync(second.PeriodId, ct)).Should().BeEmpty();
        (await database.JournalStore.GetByPeriodAsync(first.PeriodId, ct)).Should().HaveCount(2);
    }

    private static async Task AssertCouponHistoryWaitAsync(bool rollback)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var first = await database.SavePeriodAsync(Guid.NewGuid(), "Open", ct);
        var second = await database.SavePeriodAsync(Guid.NewGuid(), "Open", ct);
        var at = DateTimeOffset.Parse("2026-05-12T12:00:00Z");
        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await database.JournalStore.AppendAsync(connection, transaction, CouponHistoryWrite(first.PeriodId, at), ct);
        var application = "coupon-ordinary-history-" + Guid.NewGuid().ToString("N");
        var competing = new PostgresLedgerJournalStore(CouponHistoryOptions(database.Options, application));
        var waiting = competing.AppendAsync(CouponHistoryWrite(second.PeriodId, at.AddMinutes(1)), ct);
        await WaitForCouponHistoryRelationLockAsync(database.Options.ConnectionString, application, ct);
        waiting.IsCompleted.Should().BeFalse("other periods share the complete-history publication boundary");
        (await competing.GetByPeriodAsync(first.PeriodId, ct)).Should().BeEmpty();
        if (rollback)
            await transaction.RollbackAsync(ct);
        else
            await transaction.CommitAsync(ct);
        await waiting;
        (await competing.GetByPeriodAsync(first.PeriodId, ct)).Count.Should().Be(rollback ? 0 : 1);
        (await competing.GetByPeriodAsync(second.PeriodId, ct)).Should().ContainSingle();
    }

    private static LedgerJournalEntryWrite CouponHistoryWrite(Guid periodId, DateTimeOffset at)
    {
        var id = Guid.NewGuid();
        const string description = "Synthetic prior coupon accounting";
        return new(new(id, at, description,
        [new(Guid.NewGuid(), id, at, new("Cash", LedgerAccountType.Asset), 1m, 0m, description),
         new(Guid.NewGuid(), id, at, new("CouponIncome", LedgerAccountType.Revenue), 0m, 1m, description)]),
            Guid.NewGuid(), periodId);
    }

    private static LedgerJournalStoreOptions CouponHistoryOptions(LedgerJournalStoreOptions source, string application)
        => new()
        {
            ConnectionString = new NpgsqlConnectionStringBuilder(source.ConnectionString) { ApplicationName = application }.ConnectionString,
            SchemaName = source.SchemaName
        };

    private static async Task WaitForCouponHistoryRelationLockAsync(string connectionString, string application,
        CancellationToken ct)
    {
        await using var observer = new NpgsqlConnection(connectionString);
        await observer.OpenAsync(ct);
        while (true)
        {
            await using var command = observer.CreateCommand();
            command.CommandText = "select exists(select 1 from pg_catalog.pg_stat_activity where application_name=@application and wait_event_type='Lock' and wait_event='relation')";
            command.Parameters.AddWithValue("application", application);
            if (await command.ExecuteScalarAsync(ct) is true)
                return;
            await Task.Delay(25, ct);
        }
    }
}
