using System.Data;
using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Npgsql;

namespace Meridian.Tests.Storage;

[Trait("Category", "Integration")]
public sealed class LedgerEventAuditPostgresTests
{
    [LedgerDatabaseFact]
    public async Task PostReverseCloseReopen_RetainsActualActorsAndOneAuditPerCommittedFact()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var (book, period) = await CreatePeriodAsync(database, ct);
        var store = GovernedStore(database);
        var write = Write(book, period, "posting-controller");
        await store.AppendAsync(write, ct);
        var reversal = Write(book, period, "correction-controller", write.Entry.JournalEntryId);
        await store.AppendAsync(reversal, ct);
        var replay = () => GovernedStore(database).AppendAsync(write, ct);
        await replay.Should().ThrowAsync<PostgresException>();

        var soft = await store.SavePeriodAsync(period with { Status = "SoftClosed" }, period.Version,
            Close(period, "SoftClosed", "soft-close-controller"), ct);
        var hard = await store.SaveHardClosedPeriodAsync(soft with { Status = "HardClosed" }, soft.Version,
            Close(soft, "HardClosed", "hard-close-controller"), ct);
        await store.SavePeriodAsync(hard with { Status = "Open", ClosedAt = null }, hard.Version,
            Close(hard, "Open", "reopening-controller"), ct);

        var reloaded = GovernedStore(database);
        (await reloaded.VerifyLedgerEventAuditAsync(ct)).ChainedEvents.Should().Be(6);
        (await SqlScalarAsync(database, "select string_agg(action || ':' || actor, ',' order by chain_sequence) from {schema}.ledger_event_audit_events", ct))
            .Should().Be("PeriodCreated:period-creator,JournalPosted:posting-controller,JournalReversed:correction-controller,PeriodClosed:soft-close-controller,PeriodClosed:hard-close-controller,PeriodReopened:reopening-controller");
        var stalePeriodReplay = () => reloaded.SavePeriodAsync(hard with { Status = "Open" }, hard.Version,
            Close(hard, "Open", "reopening-controller"), ct);
        await stalePeriodReplay.Should().ThrowAsync<InvalidOperationException>().WithMessage("*version conflict*");
        (await reloaded.VerifyLedgerEventAuditAsync(ct)).ChainedEvents.Should().Be(6);
        (await reloaded.GetByPeriodAsync(period.PeriodId, ct)).Should().HaveCount(2);
    }

    [LedgerDatabaseFact]
    public async Task AuditInsertFailure_RollsBackJournalPeriodAndCloseEvent()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var (book, period) = await CreatePeriodAsync(database, ct);
        await SqlAsync(database, """
            create function {schema}.reject_audit() returns trigger language plpgsql as $$
            begin raise exception 'injected audit persistence failure'; end $$;
            create trigger reject_audit before insert on {schema}.ledger_event_audit_events
            for each row execute function {schema}.reject_audit();
            """, ct);
        var post = () => GovernedStore(database).AppendAsync(Write(book, period, "poster"), ct);
        await post.Should().ThrowAsync<PostgresException>().WithMessage("*injected audit persistence failure*");
        var close = () => database.JournalStore.SavePeriodAsync(period with { Status = "SoftClosed" }, period.Version,
            Close(period, "SoftClosed", "controller"), ct);
        await close.Should().ThrowAsync<PostgresException>().WithMessage("*injected audit persistence failure*");
        var create = () => database.SavePeriodAsync(Guid.NewGuid(), "Open", ct);
        await create.Should().ThrowAsync<PostgresException>().WithMessage("*injected audit persistence failure*");
        (await database.JournalStore.GetByPeriodAsync(period.PeriodId, ct)).Should().BeEmpty();
        (await database.JournalStore.GetPeriodAsync(period.PeriodId, ct))!.Status.Should().Be("Open");
        (await SqlScalarAsync(database, "select count(*) from {schema}.period_close_events", ct)).Should().Be(0L);
        (await database.JournalStore.VerifyLedgerEventAuditAsync(ct)).ChainedEvents.Should().Be(1);
    }

    [LedgerDatabaseFact]
    public async Task CallerTransactionRollback_RemovesBothJournalAndAudit()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var (book, period) = await CreatePeriodAsync(database, ct);
        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync(ct);
        await using (var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct))
        {
            await GovernedStore(database).AppendAsync(connection, transaction, Write(book, period, "poster"), ct);
            await transaction.RollbackAsync(ct);
        }
        (await database.JournalStore.GetByPeriodAsync(period.PeriodId, ct)).Should().BeEmpty();
        (await database.JournalStore.VerifyLedgerEventAuditAsync(ct)).ChainedEvents.Should().Be(1);
    }

    [LedgerDatabaseFact]
    public async Task ConcurrentIndependentStores_RetainOneUnforkedChain()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var (book, period) = await CreatePeriodAsync(database, ct);
        var writes = Enumerable.Range(0, 6).Select(i => Write(book, period, $"poster-{i}")).ToArray();
        await Task.WhenAll(writes.Select(async write =>
        {
            // SERIALIZABLE is an existing store contract: retry the complete transaction, never
            // an audit insert alone. Every retry retains the original journal/command identity.
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    await GovernedStore(database).AppendAsync(write, ct);
                    return;
                }
                catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.SerializationFailure && attempt < 10)
                {
                    await Task.Delay(10, ct);
                }
            }
        }));
        (await database.JournalStore.VerifyLedgerEventAuditAsync(ct)).ChainedEvents.Should().Be(7);
        (await database.JournalStore.GetByPeriodAsync(period.PeriodId, ct)).Should().HaveCount(6);
        (await SqlScalarAsync(database, "select count(distinct subject_id) from {schema}.ledger_event_audit_events where subject_kind = 'journal'", ct)).Should().Be(6L);
    }

    [LedgerDatabaseFact]
    public async Task TamperDeleteAndHeadRollback_FailClosedBeforeNextMutation()
    {
        string[] corruptions =
        [
            "update {schema}.ledger_event_audit_events set actor = 'forged' where chain_sequence = 1",
            "delete from {schema}.ledger_event_audit_events where chain_sequence = 1",
            "delete from {schema}.ledger_event_audit_events where chain_sequence = 2",
            "delete from {schema}.ledger_event_audit_head",
            "insert into {schema}.ledger_event_audit_genesis values ('journal', gen_random_uuid(), 1)",
            "update {schema}.ledger_event_audit_head set next_sequence = 1, last_hash = null",
            // Internally consistent tail rollback still leaves a retained, uncovered journal.
            "delete from {schema}.ledger_event_audit_events where chain_sequence = 2; update {schema}.ledger_event_audit_head set next_sequence = 2, last_hash = (select entry_hash from {schema}.ledger_event_audit_events where chain_sequence = 1)"
        ];
        foreach (var corruption in corruptions)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var ct = timeout.Token;
            await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
            var (book, period) = await CreatePeriodAsync(database, ct);
            await GovernedStore(database).AppendAsync(Write(book, period, "poster"), ct);
            await SqlAsync(database, corruption, ct);
            var verify = () => database.JournalStore.VerifyLedgerEventAuditAsync(ct);
            await verify.Should().ThrowAsync<LedgerValidationException>().WithMessage("*audit integrity failure*");
            var next = () => GovernedStore(database).AppendAsync(Write(book, period, "next-poster"), ct);
            await next.Should().ThrowAsync<LedgerValidationException>().WithMessage("*audit integrity failure*");
            (await database.JournalStore.GetByPeriodAsync(period.PeriodId, ct)).Should().ContainSingle();
        }
    }

    [LedgerDatabaseFact]
    public async Task CoveredFactMutationAndUnauditedPeriodWrite_AreDetected()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var (_, period) = await CreatePeriodAsync(database, ct);
        await SqlAsync(database, $"update {{schema}}.accounting_periods set label = 'unaudited change' where period_id = '{period.PeriodId:D}'", ct);
        var verify = () => database.JournalStore.VerifyLedgerEventAuditAsync(ct);
        await verify.Should().ThrowAsync<LedgerValidationException>().WithMessage("*covered*" );
    }

    [LedgerDatabaseFact]
    public async Task UpgradeDeclaresGenesis_RerunCannotExemptNewFactsOrRecreateMissingHead()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var (book, period) = await CreatePeriodAsync(database, ct);
        await GovernedStore(database).AppendAsync(Write(book, period, "legacy-poster"), ct);
        // Reproduce a pre-036 database shape in this disposable schema only.
        await SqlAsync(database, "drop table {schema}.ledger_event_audit_events, {schema}.ledger_event_audit_genesis, {schema}.ledger_event_audit_head", ct);
        await ReapplyAuditMigrationAsync(database, ct);
        var genesis = await database.JournalStore.VerifyLedgerEventAuditAsync(ct);
        genesis.ChainedEvents.Should().Be(0);
        genesis.UnprotectedGenesisFacts.Should().Be(2);
        await GovernedStore(database).AppendAsync(Write(book, period, "new-poster"), ct);
        await ReapplyAuditMigrationAsync(database, ct);
        var rerun = await database.JournalStore.VerifyLedgerEventAuditAsync(ct);
        rerun.ChainedEvents.Should().Be(1);
        rerun.UnprotectedGenesisFacts.Should().Be(2);
        rerun.GenesisAtUtc.Should().Be(genesis.GenesisAtUtc);
        await SqlAsync(database, "delete from {schema}.ledger_event_audit_head", ct);
        var migrate = () => ReapplyAuditMigrationAsync(database, ct);
        await migrate.Should().ThrowAsync<PostgresException>().WithMessage("*head is missing*");
    }

    [LedgerDatabaseFact]
    public async Task LegacyActorAbsenceAndAdditiveDatabaseColumns_DoNotInventAttribution()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var period = await database.SavePeriodAsync(Guid.NewGuid(), "Open", ct);
        await SqlAsync(database, "alter table {schema}.accounting_periods add column future_nullable_field text null", ct);
        (await database.JournalStore.VerifyLedgerEventAuditAsync(ct)).ChainedEvents.Should().Be(1);
        (await SqlScalarAsync(database, "select actor is null from {schema}.ledger_event_audit_events", ct)).Should().Be(true);
        period.PeriodId.Should().NotBeEmpty();
    }

    private static async Task<(LedgerBookRecord Book, LedgerAccountingPeriod Period)> CreatePeriodAsync(LedgerPostgresTestDatabase database, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var book = new LedgerBookRecord(Guid.NewGuid(), "fund-audit", Guid.NewGuid(), FundStructureNodeKindDto.Fund,
            "Audit fixture", "USD", now, now);
        await database.JournalStore.SaveLedgerBookAsync(book, ct);
        var period = new LedgerAccountingPeriod(Guid.NewGuid(), book.LedgerBookId, 2026, 5, "May 2026",
            new(2026, 5, 1), new(2026, 5, 31), "Open", now, null, 0) { MutationActor = "period-creator" };
        return (book, await database.JournalStore.SavePeriodAsync(period, 0, ct: ct));
    }

    private static PostgresLedgerJournalStore GovernedStore(LedgerPostgresTestDatabase database) => new(new LedgerJournalStoreOptions
    {
        ConnectionString = database.Options.ConnectionString, SchemaName = database.Options.SchemaName,
        RequireGovernedPostingCommand = true, RequireExpectedVersion = true
    });

    private static LedgerJournalEntryWrite Write(LedgerBookRecord book, LedgerAccountingPeriod period, string actor, Guid? reverses = null)
    {
        var id = Guid.NewGuid();
        var at = DateTimeOffset.Parse("2026-05-20T12:00:00Z");
        const string description = "Reviewed audit integration posting";
        var journal = new JournalEntry(id, at, description,
        [
            new LedgerEntry(Guid.NewGuid(), id, at, new LedgerAccount("Cash", LedgerAccountType.Asset), reverses is null ? 100m : 0m, reverses is null ? 0m : 100m, description),
            new LedgerEntry(Guid.NewGuid(), id, at, new LedgerAccount("Revenue", LedgerAccountType.Revenue), reverses is null ? 0m : 100m, reverses is null ? 100m : 0m, description)
        ]);
        var command = new AccountingPostingCommandDto(Guid.NewGuid(), book.LedgerBookId, period.PeriodId,
            new(2026, 5, 20), at, $"audit:{id:D}",
            Intent: reverses is null ? AccountingPostingIntentDto.Originating : AccountingPostingIntentDto.Reversal,
            SourceEventId: Guid.NewGuid(), SourceJournalEntryId: reverses, ExpectedVersion: period.Version,
            ApprovalState: AccountingPostingApprovalStateDto.Approved, ApprovalId: $"review:{id:D}",
            OperatorRationale: "Explicit independent controller approval for the retained balanced fixture.", LedgerBookId: book.LedgerBookId)
        {
            Actor = actor,
            BookContext = new AccountingBookContextDto(book.LedgerBookId, book.FundProfileId, book.FundStructureNodeId,
                book.FundStructureNodeKind, book.DisplayName, book.BaseCurrency, book.AccountingBasis,
                book.AccountingPolicyId, book.AccountingPolicyVersion, period.PeriodId)
        };
        return new(journal, book.LedgerBookId, period.PeriodId, PostingCommand: command, LedgerBookId: book.LedgerBookId);
    }

    private static PeriodCloseEventRecord Close(LedgerAccountingPeriod period, string target, string actor) =>
        new(Guid.NewGuid(), period.PeriodId, period.Status, target, actor, "Reviewed period transition", DateTimeOffset.UtcNow);

    private static async Task SqlAsync(LedgerPostgresTestDatabase database, string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql.Replace("{schema}", $"\"{database.Options.SchemaName}\"", StringComparison.Ordinal);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task ReapplyAuditMigrationAsync(LedgerPostgresTestDatabase database, CancellationToken ct)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Meridian.sln")))
            directory = directory.Parent;
        var root = directory?.FullName ?? throw new InvalidOperationException("Repository root is required.");
        var sql = await File.ReadAllTextAsync(Path.Combine(root, "src", "Meridian.Storage", "Ledger", "Migrations",
            "V_ledger_036__ledger_event_audit_chain.sql"), ct);
        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql.Replace("__SCHEMA__", $"\"{database.Options.SchemaName}\"", StringComparison.Ordinal);
        await command.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private static async Task<object?> SqlScalarAsync(LedgerPostgresTestDatabase database, string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql.Replace("{schema}", $"\"{database.Options.SchemaName}\"", StringComparison.Ordinal);
        return await command.ExecuteScalarAsync(ct);
    }
}
