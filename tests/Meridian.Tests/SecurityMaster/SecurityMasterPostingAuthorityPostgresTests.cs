using System.Data;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Npgsql;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Integration")]
public sealed class SecurityMasterPostingAuthorityPostgresTests : IClassFixture<SecurityMasterDatabaseFixture>
{
    private static readonly DateTimeOffset EffectiveFrom = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly SecurityMasterDatabaseFixture _fixture;

    public SecurityMasterPostingAuthorityPostgresTests(SecurityMasterDatabaseFixture fixture) => _fixture = fixture;

    [SecurityMasterDatabaseFact]
    public Task PostingAuthority_BlocksSecurityTermsUpdate() => AssertMutationBlockedAsync(
        "update {schema}.securities set common_terms = common_terms || '{\"currency\":\"EUR\"}'::jsonb where security_id = @id;");

    [SecurityMasterDatabaseFact]
    public Task PostingAuthority_BlocksExistingAliasUpdate() => AssertMutationBlockedAsync(
        "update {schema}.security_aliases set reason = 'concurrent correction' where security_id = @id;");

    [SecurityMasterDatabaseFact]
    public Task PostingAuthority_BlocksExistingAliasDelete() => AssertMutationBlockedAsync(
        "delete from {schema}.security_aliases where security_id = @id;");

    [SecurityMasterDatabaseFact]
    public Task PostingAuthority_BlocksAliasInsert() => AssertMutationBlockedAsync(
        """
        insert into {schema}.security_aliases
            (alias_id, security_id, alias_kind, alias_value, normalized_alias_value, scope,
             created_by, created_at, valid_from, is_enabled)
        values (@child_id, @id, 'Ticker', 'NEW', 'NEW', 'Operations', 'writer', @effective, @effective, true);
        """);

    [SecurityMasterDatabaseFact]
    public Task PostingAuthority_BlocksExistingIdentifierUpdate() => AssertMutationBlockedAsync(
        "update {schema}.security_identifiers set provider = 'changed-provider' where security_id = @id;");

    [SecurityMasterDatabaseFact]
    public Task PostingAuthority_BlocksExistingIdentifierDelete() => AssertMutationBlockedAsync(
        "delete from {schema}.security_identifiers where security_id = @id;");

    [SecurityMasterDatabaseFact]
    public Task PostingAuthority_BlocksIdentifierInsert() => AssertMutationBlockedAsync(
        """
        insert into {schema}.security_identifiers
            (security_id, identifier_kind, identifier_value, normalized_identifier_value, is_primary,
             valid_from, source, manual_override)
        values (@id, 'Ric', 'NEW.O', 'NEW.O', false, @effective, 'writer', false);
        """);

    [SecurityMasterDatabaseFact]
    public async Task PostingAuthority_BlocksStandaloneAliasInsertReplayAndCorrectionUntilCommit()
    {
        foreach (var mode in new[] { "insert", "replay", "correction" })
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var ct = timeout.Token;
            var store = new PostgresSecurityMasterStore(_fixture.Options);
            var security = await SeedAsync(store, ct);
            var alias = mode switch
            {
                "insert" => Alias(security.SecurityId),
                "replay" => security.Aliases.Single(),
                _ => security.Aliases.Single() with { Reason = "concurrent correction" }
            };
            var writerName = $"alias-authority-{Guid.NewGuid():N}";
            var writerOptions = new SecurityMasterOptions
            {
                ConnectionString = new NpgsqlConnectionStringBuilder(_fixture.Options.ConnectionString)
                {
                    ApplicationName = writerName,
                    Pooling = false
                }.ConnectionString,
                Schema = _fixture.Options.Schema,
                PreloadProjectionCache = false
            };
            await using var posting = await OpenAsync(ct);
            await using var transaction = await posting.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var locked = await store.LockForLotPostingAsync(posting, transaction, security.SecurityId, ct);
            var write = new PostgresSecurityMasterStore(writerOptions).UpsertAliasAsync(alias, ct);
            try
            {
                await WaitUntilBlockedAsync(write, posting.ProcessID, writerName, ct);
                var unchanged = await store.GetProjectionAsync(security.SecurityId, ct);
                OpenLotAmortization.SecurityHash(unchanged!).Should().Be(OpenLotAmortization.SecurityHash(locked!));
            }
            finally
            {
                await transaction.CommitAsync(CancellationToken.None);
            }

            (await write).Should().BeEquivalentTo(alias);
            var after = await store.GetProjectionAsync(security.SecurityId, ct);
            after!.Aliases.Should().HaveCount(mode == "insert" ? 2 : 1);
            after.Version.Should().Be(security.Version, "alias writes must preserve the event-stream version");
            if (mode == "correction")
            {
                after.Aliases.Single().Reason.Should().Be("concurrent correction");
            }
        }
    }

    [SecurityMasterDatabaseFact]
    public async Task PostingAuthority_RefusesAliasCommittedAfterSerializableSnapshot()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var ct = timeout.Token;
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var security = await SeedAsync(store, ct);
        await using var posting = await OpenAsync(ct);
        await using var transaction = await posting.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await using (var snapshot = posting.CreateCommand())
        {
            snapshot.Transaction = transaction;
            snapshot.CommandText = $"select version from {Schema}.securities where security_id = @id;";
            snapshot.Parameters.AddWithValue("id", security.SecurityId);
            (await snapshot.ExecuteScalarAsync(ct)).Should().Be(security.Version);
        }

        // The ledger establishes its snapshot before reaching Security Master. A child insertion
        // committed in this gap must refuse posting, even though the stream version is unchanged.
        await store.UpsertAliasAsync(Alias(security.SecurityId), ct);
        var act = () => store.LockForLotPostingAsync(posting, transaction, security.SecurityId, ct);
        var refusal = await act.Should().ThrowAsync<PostgresException>();
        refusal.Which.SqlState.Should().Be(PostgresErrorCodes.SerializationFailure);
        await transaction.RollbackAsync(ct);

        await using var retry = await posting.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var fresh = await store.LockForLotPostingAsync(posting, retry, security.SecurityId, ct);
        fresh!.Aliases.Should().HaveCount(2);
        fresh.Version.Should().Be(security.Version);
        OpenLotAmortization.SecurityHash(fresh).Should().NotBe(OpenLotAmortization.SecurityHash(security));
        await retry.CommitAsync(ct);
    }

    [SecurityMasterDatabaseFact]
    public async Task PostingAuthority_RefusesAliasCorrectionCommittedAfterSerializableSnapshot()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var ct = timeout.Token;
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var security = await SeedAsync(store, ct);
        await using var posting = await OpenAsync(ct);
        await using var transaction = await posting.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await using (var snapshot = posting.CreateCommand())
        {
            snapshot.Transaction = transaction;
            snapshot.CommandText = $"select version from {Schema}.securities where security_id = @id;";
            snapshot.Parameters.AddWithValue("id", security.SecurityId);
            await snapshot.ExecuteScalarAsync(ct);
        }

        // A correction appends a revision and rewrites the current alias row. Committed after the
        // posting snapshot, it must refuse posting through the parent lock exactly as an insert does.
        var corrected = security.Aliases.Single() with { Reason = "corrected after snapshot" };
        await store.UpsertAliasAsync(corrected, ct);
        var act = () => store.LockForLotPostingAsync(posting, transaction, security.SecurityId, ct);
        var refusal = await act.Should().ThrowAsync<PostgresException>();
        refusal.Which.SqlState.Should().Be(PostgresErrorCodes.SerializationFailure);
        await transaction.RollbackAsync(ct);

        await using var retry = await posting.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var fresh = await store.LockForLotPostingAsync(posting, retry, security.SecurityId, ct);
        fresh!.Aliases.Single().Reason.Should().Be("corrected after snapshot");
        fresh.Version.Should().Be(security.Version);
        OpenLotAmortization.SecurityHash(fresh).Should().NotBe(OpenLotAmortization.SecurityHash(security));
        await retry.CommitAsync(ct);
    }

    [SecurityMasterDatabaseFact]
    public async Task StandaloneAliasRepointConflict_RollsBackParentTouchAndRetainsProjection()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var ct = timeout.Token;
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var security = await SeedAsync(store, ct);
        var other = await SeedAsync(store, ct);
        await using var posting = await OpenAsync(ct);
        await using var transaction = await posting.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await using (var snapshot = posting.CreateCommand())
        {
            snapshot.Transaction = transaction;
            snapshot.CommandText = $"select version from {Schema}.securities where security_id in (@id, @other);";
            snapshot.Parameters.AddWithValue("id", security.SecurityId);
            snapshot.Parameters.AddWithValue("other", other.SecurityId);
            await snapshot.ExecuteScalarAsync(ct);
        }

        // An alias ID stays bound to its security; re-pointing it is refused and its parent touch
        // on the target security rolls back with it.
        var alias = security.Aliases.Single() with { SecurityId = other.SecurityId };
        var conflict = () => store.UpsertAliasAsync(alias, ct);
        await conflict.Should().ThrowAsync<SecurityAliasHistoryConflictException>();

        var retainedOther = await store.LockForLotPostingAsync(posting, transaction, other.SecurityId, ct);
        OpenLotAmortization.SecurityHash(retainedOther!).Should().Be(OpenLotAmortization.SecurityHash(other));
        var retained = await store.LockForLotPostingAsync(posting, transaction, security.SecurityId, ct);
        OpenLotAmortization.SecurityHash(retained!).Should().Be(OpenLotAmortization.SecurityHash(security));
        await transaction.CommitAsync(ct);
    }

    private async Task AssertMutationBlockedAsync(string sql)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var ct = timeout.Token;
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        var security = await SeedAsync(store, ct);
        await using var posting = await OpenAsync(ct);
        await using var transaction = await posting.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var locked = await store.LockForLotPostingAsync(posting, transaction, security.SecurityId, ct);
        locked.Should().NotBeNull();
        var retainedHash = OpenLotAmortization.SecurityHash(locked!);

        await using var writer = await OpenAsync(ct);
        await using var mutation = writer.CreateCommand();
        mutation.CommandText = sql.Replace("{schema}", Schema, StringComparison.Ordinal);
        mutation.Parameters.AddWithValue("id", security.SecurityId);
        mutation.Parameters.AddWithValue("child_id", Guid.NewGuid());
        mutation.Parameters.AddWithValue("effective", EffectiveFrom.UtcDateTime);
        var write = mutation.ExecuteNonQueryAsync(ct);
        try
        {
            await WaitUntilBlockedAsync(write, posting.ProcessID, writer.ProcessID, ct);
            var unchanged = await store.GetProjectionAsync(security.SecurityId, ct);
            OpenLotAmortization.SecurityHash(unchanged!).Should().Be(retainedHash);
        }
        finally
        {
            await transaction.CommitAsync(CancellationToken.None);
        }

        (await write).Should().Be(1);
        var changed = await store.GetProjectionAsync(security.SecurityId, ct);
        OpenLotAmortization.SecurityHash(changed!).Should().NotBe(retainedHash);
    }

    private async Task WaitUntilBlockedAsync(Task writer, int postingPid, object writerIdentity, CancellationToken ct)
    {
        await using var observer = await OpenAsync(ct);
        await using var query = observer.CreateCommand();
        query.CommandText = writerIdentity is int
            ? "select @posting = any(pg_blocking_pids(@writer));"
            : "select exists (select 1 from pg_stat_activity where application_name = @writer and @posting = any(pg_blocking_pids(pid)));";
        query.Parameters.AddWithValue("posting", postingPid);
        query.Parameters.AddWithValue("writer", writerIdentity);
        while (!(bool)(await query.ExecuteScalarAsync(ct))!)
        {
            if (writer.IsCompleted)
            {
                await writer;
                Assert.Fail("The concurrent reference write completed before the posting transaction released its authority locks.");
            }

            // Poll an observed server lock dependency; elapsed time never counts as blocking proof.
            await Task.Delay(10, ct);
        }
        writer.IsCompleted.Should().BeFalse();
    }

    private async Task<SecurityProjectionRecord> SeedAsync(PostgresSecurityMasterStore store, CancellationToken ct)
    {
        var securityId = Guid.NewGuid();
        var ticker = $"LOCK-{securityId:N}";
        var security = new SecurityProjectionRecord(
            securityId, "Bond", SecurityStatusDto.Active, "Posting authority bond", "USD", "Ticker", ticker,
            JsonSerializer.SerializeToElement(new { currency = "USD" }),
            JsonSerializer.SerializeToElement(new { couponRate = 0.05m, maturityDate = "2030-01-01" }),
            JsonSerializer.SerializeToElement(new { sourceSystem = "posting-authority-test" }),
            1, EffectiveFrom, null,
            [new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, ticker, true, EffectiveFrom)],
            [Alias(securityId)]);
        await store.UpsertProjectionAsync(security, ct);
        return (await store.GetProjectionAsync(securityId, ct))!;
    }

    private static SecurityAliasDto Alias(Guid securityId) => new(
        Guid.NewGuid(), securityId, "Ticker", $"ALIAS-{Guid.NewGuid():N}", null,
        SecurityAliasScope.Operations, "original registration", "test", EffectiveFrom, EffectiveFrom, null, true);

    private string Schema => $"\"{_fixture.Options.Schema}\"";

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new NpgsqlConnection(_fixture.Options.ConnectionString);
        await connection.OpenAsync(ct);
        return connection;
    }
}
