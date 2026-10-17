using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Ledger;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;
using Moq;
using Npgsql;

namespace Meridian.Tests.Storage;

public sealed partial class OpenLotBackfillPostgresTests
{
    [LedgerDatabaseFact]
    public Task Review_HoldsReferenceLocksUntilTheAcceptedReviewCommits()
        => AssertBackfillReferenceLocksAsync(apply: false);

    [LedgerDatabaseFact]
    public Task Apply_HoldsReferenceLocksUntilCommitAndExactReceiptSurvivesLaterDrift()
        => AssertBackfillReferenceLocksAsync(apply: true);

    [LedgerDatabaseFact]
    public async Task ReviewAndApply_RejectMissingNonTransactionalAndDifferentDatabaseAuthorityWithoutWrites()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var legacy = await SeedAsync(database);
        var facts = OpenLotBackfillReconciliationTests.Facts(legacy);
        var authority = await BackfillAuthority.CreateAsync(database, facts);
        var store = authority.Restart();
        var exception = (await store.SurveyAsync(legacy.LedgerBookId)).Single();
        var evidence = await store.RetainEvidenceAsync(Retention(facts));
        var otherDatabase = new NpgsqlConnectionStringBuilder(database.Options.ConnectionString)
        { Database = "backfill_authority_in_a_different_database" }.ConnectionString;
        var invalid = new (PostgresLedgerJournalStore Store, string Message)[]
        {
            (new(database.Options), "*Authoritative PostgreSQL*"),
            (new(database.Options, backfillSecurityMaster: () => Mock.Of<ISecurityMasterStore>(),
                backfillPositions: () => Mock.Of<IInstrumentPositionProjectionStore>()), "*Authoritative PostgreSQL*"),
            (new(database.Options, backfillSecurityMaster: () => new PostgresSecurityMasterStore(new()
                { ConnectionString = otherDatabase, Schema = authority.SecurityOptions.Schema }),
                backfillPositions: () => authority.Positions), "*Security Master in the same PostgreSQL database*"),
            (new(database.Options, backfillSecurityMaster: () => authority.Securities,
                backfillPositions: () => new PostgresAssetOperationsProjectionStore(new()
                { ConnectionString = otherDatabase, Schema = authority.PositionOptions.Schema })),
                "*book positions in the same PostgreSQL database*")
        };
        foreach (var item in invalid)
        {
            var review = () => item.Store.ReviewEvidenceAsync(Review(evidence));
            await review.Should().ThrowAsync<LedgerValidationException>().WithMessage(item.Message);
            (await store.GetEvidenceAsync(legacy.LedgerBookId, evidence.EvidenceRecordId))!.Version.Should().Be(1);
        }
        var rejected = await store.RetainEvidenceAsync(Retention(facts));
        (await invalid[0].Store.ReviewEvidenceAsync(Review(rejected) with { Accepted = false }))
            .ReviewStatus.Should().Be("Rejected", "an operator can still reject evidence while reference authority is unavailable");
        await store.ReviewEvidenceAsync(Review(evidence));
        var request = Apply(legacy, exception, evidence);
        foreach (var item in invalid)
        {
            var apply = () => item.Store.ApplyAsync(request);
            await apply.Should().ThrowAsync<LedgerValidationException>().WithMessage(item.Message);
            (await store.GetTaxLotsByIdsAsync(legacy.LedgerBookId, [legacy.TaxLotRecordId]))
                .Should().ContainSingle().Which.Should().BeEquivalentTo(legacy);
            (await store.ListExceptionsAsync(legacy.LedgerBookId)).Should().ContainSingle();
        }
        (await store.ApplyAsync(request)).ResultingLotVersion.Should().Be(legacy.Version + 1,
            "rejected authority must neither change the lot nor reserve the idempotency key");
    }

    [LedgerDatabaseFact]
    public async Task CancelledApply_ReleasesReferenceLocksWithoutEnrichmentAndCanRetry()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var legacy = await SeedAsync(database);
        var facts = OpenLotBackfillReconciliationTests.Facts(legacy);
        var authority = await BackfillAuthority.CreateAsync(database, facts);
        var store = authority.Restart();
        var exception = (await store.SurveyAsync(legacy.LedgerBookId)).Single();
        var evidence = await store.RetainEvidenceAsync(Retention(facts));
        await store.ReviewEvidenceAsync(Review(evidence));
        var request = Apply(legacy, exception, evidence);
        await using var blocker = new NpgsqlConnection(database.Options.ConnectionString);
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await LockBackfillLotAsync(blocker, transaction, database.Options.SchemaName, legacy.TaxLotRecordId);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var operation = store.ApplyAsync(request, cancellation.Token);
        try
        {
            await WaitForBackfillLotWaitAsync(database, blocker.ProcessID, operation, deadline.Token);
            await AssertBackfillReferenceLocksAsync(authority, held: true, deadline.Token);
            await cancellation.CancelAsync();
            var cancelled = async () => await operation;
            await cancelled.Should().ThrowAsync<OperationCanceledException>();
            await AssertBackfillReferenceLocksAsync(authority, held: false, deadline.Token);
        }
        finally
        {
            await transaction.RollbackAsync();
            await cancellation.CancelAsync();
            try
            { await operation; }
            catch (OperationCanceledException) { }
        }
        (await store.GetTaxLotsByIdsAsync(legacy.LedgerBookId, [legacy.TaxLotRecordId]))
            .Should().ContainSingle().Which.Should().BeEquivalentTo(legacy);
        (await store.ListExceptionsAsync(legacy.LedgerBookId)).Should().ContainSingle();
        (await store.ApplyAsync(request)).ResultingLotVersion.Should().Be(legacy.Version + 1);
    }

    private static async Task AssertBackfillReferenceLocksAsync(bool apply)
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var legacy = await SeedAsync(database);
        var facts = OpenLotBackfillReconciliationTests.Facts(legacy);
        var authority = await BackfillAuthority.CreateAsync(database, facts);
        var store = authority.Restart();
        var exception = (await store.SurveyAsync(legacy.LedgerBookId)).Single();
        var evidence = await store.RetainEvidenceAsync(Retention(facts));
        if (apply)
            await store.ReviewEvidenceAsync(Review(evidence));
        var request = Apply(legacy, exception, evidence);
        await using var blocker = new NpgsqlConnection(database.Options.ConnectionString);
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await LockBackfillLotAsync(blocker, transaction, database.Options.SchemaName, legacy.TaxLotRecordId);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task operation = apply ? store.ApplyAsync(request, deadline.Token)
            : store.ReviewEvidenceAsync(Review(evidence), deadline.Token);
        try
        {
            await WaitForBackfillLotWaitAsync(database, blocker.ProcessID, operation, deadline.Token);
            await AssertBackfillReferenceLocksAsync(authority, held: true, deadline.Token);
        }
        finally
        {
            await transaction.RollbackAsync();
            await operation;
        }
        await AssertBackfillReferenceLocksAsync(authority, held: false, deadline.Token);
        await authority.Securities.UpsertProjectionAsync(authority.Security with { Version = authority.Security.Version + 1 });
        await authority.Positions.UpsertAsync(authority.Role, authority.Position with { Version = authority.Position.Version + 1 },
            null, authority.Position.Version, new("reviewer", "evidence://backfill/later-position",
                "Reviewed later position version", DateTimeOffset.Parse("2026-10-13T00:00:00Z")));
        if (apply)
        {
            var original = await (Task<OpenLotBackfillReceiptDto>)operation;
            // A committed receipt is authority for replay, even after restart without reference providers.
            var restarted = new PostgresLedgerJournalStore(database.Options);
            (await restarted.ApplyAsync(request)).Should().BeEquivalentTo(original);
            var collision = () => restarted.ApplyAsync(request with { Actor = "different-operator" });
            await collision.Should().ThrowAsync<LedgerValidationException>().WithMessage("*idempotency key collides*");
        }
        else
        {
            var stale = () => authority.Restart().ApplyAsync(request);
            await stale.Should().ThrowAsync<LedgerValidationException>().WithMessage("*Security Master*stale*");
            (await store.GetTaxLotsByIdsAsync(legacy.LedgerBookId, [legacy.TaxLotRecordId]))
                .Should().ContainSingle().Which.Should().BeEquivalentTo(legacy);
            (await store.ListExceptionsAsync(legacy.LedgerBookId)).Should().ContainSingle();
        }
    }

    private static async Task LockBackfillLotAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string schema, Guid lotId)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select tax_lot_record_id from \"{schema}\".tax_lots where tax_lot_record_id = @lot for update";
        command.Parameters.AddWithValue("lot", lotId);
        (await command.ExecuteScalarAsync()).Should().Be(lotId);
    }

    private static async Task WaitForBackfillLotWaitAsync(LedgerPostgresTestDatabase database,
        int blockerPid, Task operation, CancellationToken ct)
    {
        await using var observer = new NpgsqlConnection(database.Options.ConnectionString);
        await observer.OpenAsync(ct);
        await using var command = observer.CreateCommand();
        command.CommandText = "select exists(select 1 from pg_stat_activity where @blocker = any(pg_blocking_pids(pid)))";
        command.Parameters.AddWithValue("blocker", blockerPid);
        while (!(bool)(await command.ExecuteScalarAsync(ct))!)
        {
            if (operation.IsCompleted)
            {
                await operation;
                throw new InvalidOperationException("Backfill completed without waiting for the locked lot.");
            }
            await Task.Delay(20, ct);
        }
    }

    private static async Task AssertBackfillReferenceLocksAsync(BackfillAuthority authority, bool held, CancellationToken ct)
    {
        await using var observer = new NpgsqlConnection(authority.Database.Options.ConnectionString);
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
