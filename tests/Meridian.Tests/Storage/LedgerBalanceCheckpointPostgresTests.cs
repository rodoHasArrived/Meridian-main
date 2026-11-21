using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Npgsql;

namespace Meridian.Tests.Storage;

[Trait("Category", "Integration")]
public sealed partial class LedgerBalanceCheckpointPostgresTests
{
    private static readonly DateTimeOffset MayEnd = DateTimeOffset.Parse("2026-05-31T23:59:59.999999Z");

    [LedgerDatabaseFact]
    public async Task RestartedCheckpoint_PreservesKnownZeroAndCaseInsensitiveEntityAndAccountScope()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var (book, period) = await CreateBookAndPeriod(database);
        await Append(database, book, period, 100m, "broker-a", "Entity-A");
        await Append(database, book, period, -100m, "broker-a", "Entity-A");
        await Append(database, book, period, 70m, "broker-a", "Entity-B");
        await Append(database, book, period, 50m, "broker-b", "Entity-A");
        await HardClose(database, period);

        var restarted = database.RestartJournalStore();
        var scoped = await restarted.ReadTrialBalanceAsOfAsync(book.LedgerBookId, MayEnd,
            "BROKER-A", new LedgerLineDimensionSet(EntityId: "ENTITY-A"));

        scoped.Should().HaveCount(2, "a known zero is retained accounting evidence, not a missing account");
        scoped.Should().ContainKey(Cash("broker-a")).WhoseValue.Should().Be(0m);
        scoped.Should().ContainKey(Capital("broker-a")).WhoseValue.Should().Be(0m);
        var all = await restarted.ReadTrialBalanceAsOfAsync(book.LedgerBookId, MayEnd);
        all[Cash("broker-a")].Should().Be(70m);
        all[Cash("broker-b")].Should().Be(50m);
        (await restarted.ReadTrialBalanceAsOfAsync(book.LedgerBookId, MayEnd, "missing-owner"))
            .Should().BeEmpty();
        (await Scalar(database, "select count(*) from {schema}.ledger_balance_checkpoints"))
            .Should().Be(1, "restart must exercise a retained close checkpoint");
    }

    [LedgerDatabaseFact]
    public async Task PrepostedFutureJournal_BelowCheckpointSequence_IsExcludedAtCloseAndIncludedWhenEffective()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var (book, may) = await CreateBookAndPeriod(database);
        var june = await CreatePeriod(database, book, 6);
        await Append(database, book, may, 100m);
        await Append(database, book, june, 75m, occurredAt: DateTimeOffset.Parse("2026-06-15T12:00:00Z"));
        await HardClose(database, may);

        var restarted = database.RestartJournalStore();
        (await restarted.ReadTrialBalanceAsOfAsync(book.LedgerBookId, MayEnd))[Cash("broker-a")]
            .Should().Be(100m);
        (await restarted.ReadTrialBalanceAsOfAsync(book.LedgerBookId, MayEnd.AddDays(30)))[Cash("broker-a")]
            .Should().Be(175m, "future-effective rows below the sequence watermark still belong to a later as-of view");
        var beforeCheckpoint = await Scalar(database,
            "select count(*) from {schema}.journal_entries j join {schema}.accounting_periods p on p.period_id=j.period_id "
            + "cross join {schema}.ledger_balance_checkpoints c "
            + "where p.ledger_book_id=c.ledger_book_id and j.global_sequence<=c.source_sequence");
        beforeCheckpoint.Should().Be(2);
    }

    [LedgerDatabaseFact]
    public async Task ReopenedPeriod_BackdatedNewSequence_IsAddedToTheRetainedCloseCheckpoint()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var (book, period) = await CreateBookAndPeriod(database);
        await Append(database, book, period, 100m);
        var closed = await HardClose(database, period);
        var reopened = await database.JournalStore.SavePeriodAsync(closed with { Status = "Open", ClosedAt = null },
            closed.Version, new PeriodCloseEventRecord(Guid.NewGuid(), closed.PeriodId, "HardClosed", "Open",
                "synthetic-controller", "Reviewed backdated correction", DateTimeOffset.UtcNow));
        await Append(database, book, reopened, 25m, occurredAt: DateTimeOffset.Parse("2026-05-20T12:00:00Z"));

        var restarted = database.RestartJournalStore();
        var balances = await restarted.ReadTrialBalanceAsOfAsync(book.LedgerBookId, MayEnd);

        balances[Cash("broker-a")].Should().Be(125m);
        balances[Capital("broker-a")].Should().Be(125m);
        (await Scalar(database, "select count(*) from {schema}.ledger_balance_checkpoints")).Should().Be(1);
        (await restarted.QueryAsync(new LedgerJournalEntryQuery(LedgerBookId: book.LedgerBookId)))
            .Should().HaveCount(2, "the checkpoint supplements immutable journals rather than replacing them");
    }

    [LedgerDatabaseFact]
    public async Task MutatedCheckpointBodyOrDigest_FailsClosedInsteadOfReplayingAroundCorruption()
    {
        foreach (var mutateBody in new[] { true, false })
        {
            await using var database = await LedgerPostgresTestDatabase.CreateAsync();
            var (book, period) = await CreateBookAndPeriod(database);
            await Append(database, book, period, 100m);
            await HardClose(database, period);
            var table = mutateBody ? "ledger_balance_checkpoint_rows" : "ledger_balance_checkpoints";
            var mutation = mutateBody ? "debit=debit+1" : "content_hash=repeat('0',64)";
            await Execute(database, $"alter table {{schema}}.{table} disable trigger user; "
                + $"update {{schema}}.{table} set {mutation}; "
                + $"alter table {{schema}}.{table} enable trigger user;");

            var read = async () => await database.RestartJournalStore()
                .ReadTrialBalanceAsOfAsync(book.LedgerBookId, MayEnd);

            await read.Should().ThrowAsync<LedgerValidationException>().WithMessage("*checkpoint*");
        }
    }

    [LedgerDatabaseFact]
    public async Task CloseEventWriteFailure_RollsBackCheckpointPeriodStatusAndCloseEventTogether()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var (book, period) = await CreateBookAndPeriod(database);
        await Append(database, book, period, 100m);
        var soft = await SoftClose(database, period);
        await Execute(database, """
            create function {schema}.reject_checkpoint_close_for_test() returns trigger language plpgsql as $$
            begin
                if new.new_status='HardClosed' then raise exception 'synthetic close-event storage failure'; end if;
                return new;
            end; $$;
            create trigger reject_checkpoint_close_for_test before insert on {schema}.period_close_events
            for each row execute function {schema}.reject_checkpoint_close_for_test();
            """);

        var close = async () => await database.JournalStore.SaveHardClosedPeriodAsync(
            soft with { Status = "HardClosed" }, soft.Version, CloseEvent(soft, "HardClosed"));

        await close.Should().ThrowAsync<PostgresException>();
        var retained = await database.RestartJournalStore().GetPeriodAsync(period.PeriodId);
        retained!.Status.Should().Be("SoftClosed");
        retained.Version.Should().Be(soft.Version);
        (await Scalar(database, "select count(*) from {schema}.ledger_balance_checkpoints")).Should().Be(0);
        (await Scalar(database, "select count(*) from {schema}.period_close_events where new_status='HardClosed'"))
            .Should().Be(0);
        (await database.JournalStore.GetByPeriodAsync(period.PeriodId)).Should().ContainSingle();
    }

    [LedgerDatabaseFact]
    public async Task PopulatedPeriod_CannotMoveItsBookAndChangeTheCheckpointPopulation()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var (book, period) = await CreateBookAndPeriod(database);
        var otherBook = book with { LedgerBookId = Guid.NewGuid(), FundStructureNodeId = Guid.NewGuid() };
        await database.JournalStore.SaveLedgerBookAsync(otherBook);
        await Append(database, book, period, 100m);

        var move = async () => await database.JournalStore.SavePeriodAsync(
            period with { LedgerBookId = otherBook.LedgerBookId }, period.Version);

        await move.Should().ThrowAsync<LedgerValidationException>();
        var retained = await database.JournalStore.GetPeriodAsync(period.PeriodId);
        retained!.LedgerBookId.Should().Be(book.LedgerBookId);
        retained.Version.Should().Be(period.Version);
    }

    private static LedgerAccount Cash(string owner) => new("Cash", LedgerAccountType.Asset, FinancialAccountId: owner);
    private static LedgerAccount Capital(string owner) => new("Capital Account", LedgerAccountType.Equity, FinancialAccountId: owner);

    private static async Task<(LedgerBookRecord Book, LedgerAccountingPeriod Period)> CreateBookAndPeriod(
        LedgerPostgresTestDatabase database)
    {
        var now = DateTimeOffset.Parse("2026-05-01T00:00:00Z");
        var book = new LedgerBookRecord(Guid.NewGuid(), "synthetic-checkpoint-fund", Guid.NewGuid(),
            FundStructureNodeKindDto.Fund, "Checkpoint fixture", "USD", now, now);
        await database.JournalStore.SaveLedgerBookAsync(book);
        return (book, await CreatePeriod(database, book, 5));
    }

    private static Task<LedgerAccountingPeriod> CreatePeriod(LedgerPostgresTestDatabase database, LedgerBookRecord book, int month,
        PostgresLedgerJournalStore? store = null)
        => (store ?? database.JournalStore).SavePeriodAsync(new LedgerAccountingPeriod(Guid.NewGuid(), book.LedgerBookId,
            2026, month, $"2026-{month:00}", new DateOnly(2026, month, 1),
            new DateOnly(2026, month, DateTime.DaysInMonth(2026, month)), "Open",
            DateTimeOffset.Parse($"2026-{month:00}-01T00:00:00Z"), null, 0), expectedVersion: 0);

    private static Task<LedgerAccountingPeriod> SoftClose(LedgerPostgresTestDatabase database, LedgerAccountingPeriod period)
        => database.JournalStore.SavePeriodAsync(period with { Status = "SoftClosed", ClosedAt = MayEnd },
            period.Version, CloseEvent(period, "SoftClosed"));

    private static async Task<LedgerAccountingPeriod> HardClose(LedgerPostgresTestDatabase database, LedgerAccountingPeriod period)
    {
        var soft = await SoftClose(database, period);
        return await database.JournalStore.SaveHardClosedPeriodAsync(soft with { Status = "HardClosed" },
            soft.Version, CloseEvent(soft, "HardClosed"));
    }

    private static PeriodCloseEventRecord CloseEvent(LedgerAccountingPeriod period, string status)
        => new(Guid.NewGuid(), period.PeriodId, period.Status, status, "synthetic-controller",
            "Retained balanced close evidence", DateTimeOffset.UtcNow);

    private static Task Append(LedgerPostgresTestDatabase database, LedgerBookRecord book, LedgerAccountingPeriod period,
        decimal amount, string owner = "broker-a", string entity = "Entity-A", DateTimeOffset? occurredAt = null,
        PostgresLedgerJournalStore? store = null)
    {
        var id = Guid.NewGuid();
        var at = occurredAt ?? DateTimeOffset.Parse("2026-05-15T12:00:00Z");
        var dimensions = new LedgerLineDimensionSet(EntityId: entity);
        const string description = "Synthetic capital movement";
        var journal = new JournalEntry(id, at, description,
        [
            new LedgerEntry(Guid.NewGuid(), id, at, Cash(owner), Math.Max(amount, 0m), Math.Max(-amount, 0m), description, dimensions),
            new LedgerEntry(Guid.NewGuid(), id, at, Capital(owner), Math.Max(-amount, 0m), Math.Max(amount, 0m), description, dimensions)
        ]);
        return (store ?? database.JournalStore).AppendAsync(new LedgerJournalEntryWrite(journal, book.LedgerBookId,
            period.PeriodId, LedgerBookId: book.LedgerBookId));
    }

    private static async Task<long> Scalar(LedgerPostgresTestDatabase database, string sql)
    {
        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql.Replace("{schema}", $"\"{database.Options.SchemaName}\"");
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task Execute(LedgerPostgresTestDatabase database, string sql)
    {
        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql.Replace("{schema}", $"\"{database.Options.SchemaName}\"");
        await command.ExecuteNonQueryAsync();
    }
}
