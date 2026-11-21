using System.Data;
using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Npgsql;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task CouponHistory_OrdinaryExternalAppendExcludesTypedCouponUntilCommit()
        => AssertCouponHistoryWaitAsync(rollback: false);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public Task CouponHistory_RollbackReleasesOtherPeriodWithoutRetainingTheSourceJournal()
        => AssertCouponHistoryWaitAsync(rollback: true);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CouponHistory_TypedCouponExternalAppendExcludesOrdinaryWriterInAnotherPeriod()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var ct = timeout.Token;
        await using var fixture = await CouponSinkFixture.CreateAsync();
        var june = await fixture.Store.SavePeriodAsync(new(Guid.NewGuid(), fixture.BookId, 2026, 6,
            "June ordinary publication", new(2026, 6, 1), new(2026, 6, 30), "Open",
            DateTimeOffset.Parse("2026-06-01T00:00:00Z"), null, 0), 0, ct: ct);
        var coupon = fixture.Instruction(0, "history-sensitive-coupon", 45m, 50m, 3m, 2m);
        await using var connection = new NpgsqlConnection(fixture.Options.ConnectionString);
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var write = fixture.Write(coupon);
        await using var authority = await fixture.Store.AcquirePostingAuthorityAsync(connection, transaction, [write], ct);
        var completed = false;
        try
        {
            await fixture.Store.AppendAsync(connection, transaction, write, ct);
            var application = "coupon-sensitive-history-" + Guid.NewGuid().ToString("N");
            var competing = fixture.Restart(CouponHistoryOptions(fixture.Options, application));
            var waiting = competing.AppendAsync(CouponHistoryWrite(june.PeriodId, DateTimeOffset.Parse("2026-06-12T12:00:00Z"), fixture.BookId), ct);
            await WaitForCouponHistoryRelationLockAsync(fixture.Options.ConnectionString, application, ct);
            waiting.IsCompleted.Should().BeFalse();
            // Readers used by canonical opening validation remain available while publication waits.
            (await fixture.Store.GetByPeriodAsync(june.PeriodId, ct)).Should().BeEmpty();
            (await fixture.Store.GetConsumedCouponCashAsync(fixture.Scope, "bank-coupon", ct)).Should().Be(0m);
            await transaction.CommitAsync(ct);
            completed = true;
            await waiting;
            (await fixture.Store.GetByPeriodAsync(june.PeriodId, ct)).Should().ContainSingle();
            (await fixture.Store.GetCouponRecognitionRunAsync(fixture.Scope, fixture.Lots[0].TaxLotRecordId,
                coupon.Request.RunId, ct)).Should().NotBeNull();
            (await fixture.Store.GetConsumedCouponCashAsync(fixture.Scope, "bank-coupon", ct)).Should().Be(45m);
        }
        finally
        {
            if (!completed)
                await transaction.RollbackAsync(CancellationToken.None);
        }
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CouponHistory_OrdinaryWriterCanPublishWhileAnotherOrdinaryParticipantHoldsItsBoundary()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var period = await database.SavePeriodAsync(Guid.NewGuid(), "Open", ct);
        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync(ct);
        await using var participant = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await using (var boundary = connection.CreateCommand())
        {
            boundary.Transaction = participant;
            boundary.CommandText = $"lock table \"{database.Options.SchemaName}\".journal_entries in row exclusive mode";
            await boundary.ExecuteNonQueryAsync(ct);
        }
        // This participant has not acquired the ledger audit head. Independent ordinary writers
        // must coexist at the history boundary; consolidation's authority race tests the same seam.
        await database.JournalStore.AppendAsync(CouponHistoryWrite(period.PeriodId,
            DateTimeOffset.Parse("2026-05-12T12:00:00Z")), ct);
        (await database.JournalStore.GetByPeriodAsync(period.PeriodId, ct)).Should().ContainSingle();
        await participant.RollbackAsync(ct);
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
        await using var fixture = await CouponSinkFixture.CreateAsync();
        var first = await fixture.Store.SavePeriodAsync(new(Guid.NewGuid(), fixture.BookId, 2026, 6,
            "June prior ordinary publication", new(2026, 6, 1), new(2026, 6, 30), "Open",
            DateTimeOffset.Parse("2026-06-01T00:00:00Z"), null, 0), 0, ct: ct);
        var at = DateTimeOffset.Parse("2026-06-12T12:00:00Z");
        await using var connection = new NpgsqlConnection(fixture.Options.ConnectionString);
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var write = CouponHistoryWrite(first.PeriodId, at, fixture.BookId);
        await using var authority = await fixture.Store.AcquirePostingAuthorityAsync(connection, transaction, [write], ct);
        var completed = false;
        try
        {
            await fixture.Store.AppendAsync(connection, transaction, write, ct);
            var application = "coupon-ordinary-history-" + Guid.NewGuid().ToString("N");
            var competing = fixture.Restart(CouponHistoryOptions(fixture.Options, application));
            var instruction = fixture.Instruction(0, "typed-coupon-after-ordinary", 45m, 50m, 3m, 2m);
            var waiting = competing.AppendAsync(fixture.Write(instruction), ct);
            await WaitForCouponHistoryRelationLockAsync(fixture.Options.ConnectionString, application, ct);
            waiting.IsCompleted.Should().BeFalse("actual typed coupon validation must exclude ordinary publication across periods");
            (await competing.GetByPeriodAsync(first.PeriodId, ct)).Should().BeEmpty();
            (await fixture.Store.GetCouponRecognitionRunAsync(fixture.Scope, fixture.Lots[0].TaxLotRecordId,
                instruction.Request.RunId, ct)).Should().BeNull();
            (await fixture.Store.GetConsumedCouponCashAsync(fixture.Scope, "bank-coupon", ct)).Should().Be(0m);
            if (rollback)
                await transaction.RollbackAsync(ct);
            else
                await transaction.CommitAsync(ct);
            completed = true;
            await waiting;
            (await competing.GetByPeriodAsync(first.PeriodId, ct)).Count.Should().Be(rollback ? 0 : 1);
            (await fixture.Store.GetCouponRecognitionRunAsync(fixture.Scope, fixture.Lots[0].TaxLotRecordId,
                instruction.Request.RunId, ct)).Should().NotBeNull();
            (await fixture.Store.GetConsumedCouponCashAsync(fixture.Scope, "bank-coupon", ct)).Should().Be(45m);
        }
        finally
        {
            if (!completed)
                await transaction.RollbackAsync(CancellationToken.None);
        }
    }

    private static LedgerJournalEntryWrite CouponHistoryWrite(Guid periodId, DateTimeOffset at, Guid? bookId = null)
    {
        var id = Guid.NewGuid();
        const string description = "Synthetic prior coupon accounting";
        return new(new(id, at, description,
        [new(Guid.NewGuid(), id, at, new("Cash", LedgerAccountType.Asset), 1m, 0m, description),
         new(Guid.NewGuid(), id, at, new("CouponIncome", LedgerAccountType.Revenue), 0m, 1m, description)]),
            bookId ?? Guid.NewGuid(), periodId, LedgerBookId: bookId,
            AccountingBasis: bookId is null ? AccountingBasisKindDto.Primary : AccountingBasisKindDto.Gaap,
            AccountingPolicyId: bookId is null ? "legacy-v1" : "coupon-policy", AccountingPolicyVersion: bookId is null ? "legacy-v1" : "1");
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
