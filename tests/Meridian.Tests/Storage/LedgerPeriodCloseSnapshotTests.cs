using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Npgsql;

namespace Meridian.Tests.Storage;

public sealed class LedgerPeriodCloseSnapshotTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-05-31T23:59:59Z");

    [Fact]
    public void Capture_IsDeterministicAndRetainsCurrencySourceAndOpeningHistory()
    {
        var period = Period(Guid.NewGuid()) with { Status = "HardClosed", Version = 2, ClosedAt = At };
        var close = Close(period);
        var first = Entry(period, 100m);
        var second = Entry(period, 42m) with { GlobalSequence = 2 };
        var original = LedgerPeriodCloseSnapshot.Capture(period, close, [first, second]);
        var reordered = first with
        {
            Entry = new JournalEntry(first.Entry.JournalEntryId, first.Entry.Timestamp, first.Entry.Description,
                first.Entry.Lines.Reverse().ToArray(), first.Entry.Metadata with
                { Tags = new Dictionary<string, string> { ["z"] = "last", ["a"] = "first" } })
        };
        var replay = LedgerPeriodCloseSnapshot.Capture(period, close, [second, reordered]);

        replay.ContentHashSha256.Should().Be(original.ContentHashSha256);
        var restored = original.VerifyIntegrity();
        restored.Entries.Should().HaveCount(2);
        restored.Entries[0].Entry.Lines.Should().OnlyContain(line => line.Currency != null);
        restored.Entries[0].SourceEventId.Should().Be(first.SourceEventId);
        restored.Entries[0].Entry.Metadata.Tags.Should().ContainKey("a");
    }

    [Fact]
    public void Verify_RejectsChangedPayloadAndChangedScope()
    {
        var period = Period(Guid.NewGuid()) with { Status = "HardClosed", Version = 2, ClosedAt = At };
        var snapshot = LedgerPeriodCloseSnapshot.Capture(period, Close(period), [Entry(period, 100m)]);

        var content = () => (snapshot with { PayloadJson = snapshot.PayloadJson.Replace("Synthetic capital", "Changed capital") }).VerifyIntegrity();
        content.Should().Throw<InvalidDataException>().WithMessage("*hash*");
        var scope = () => (snapshot with { PeriodId = Guid.NewGuid() }).VerifyIntegrity();
        scope.Should().Throw<InvalidDataException>().WithMessage("*scope*");
        // Separate source records are allowed; only repeating the identical journal is refused.
        var entry = Entry(period, 100m);
        var repeated = () => LedgerPeriodCloseSnapshot.Capture(period, Close(period), [entry, entry]);
        repeated.Should().Throw<LedgerValidationException>().WithMessage("*duplicate*");
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task HardClose_RetainsAtomicSnapshot_ReplaysAfterRestart_AndPreservesItAfterReopen()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var store = database.JournalStore;
        var period = await CreateBookAndPeriodAsync(database);
        var entry = Entry(period, 100m);
        await store.AppendAsync(new LedgerJournalEntryWrite(entry.Entry, entry.AggregateId, period.PeriodId));
        var target = period with { Status = "HardClosed", ClosedAt = At };
        var close = Close(period);
        var saved = await store.SaveHardClosedPeriodAsync(target, period.Version, close);
        var restarted = database.RestartJournalStore();
        var snapshot = await restarted.GetCloseSnapshotAsync(period.PeriodId);
        snapshot.Should().NotBeNull();
        snapshot!.VerifyIntegrity().Entries.Should().ContainSingle();
        snapshot.PeriodVersion.Should().Be(saved.Version);
        (await restarted.SaveHardClosedPeriodAsync(target, period.Version, close)).Version.Should().Be(saved.Version);
        var altered = () => restarted.SaveHardClosedPeriodAsync(target, period.Version, close with { ClosedBy = "another-controller" });
        await altered.Should().ThrowAsync<InvalidOperationException>().WithMessage("*version conflict*");

        await restarted.SavePeriodAsync(saved with { Status = "Open", ClosedAt = null }, saved.Version);
        (await restarted.GetCloseSnapshotAsync(period.PeriodId, saved.Version))!.ContentHashSha256.Should().Be(snapshot.ContentHashSha256);
        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync();
        await using var mutation = connection.CreateCommand();
        mutation.CommandText = $"delete from \"{database.Options.SchemaName}\".period_close_snapshots";
        var remove = () => mutation.ExecuteNonQueryAsync();
        await remove.Should().ThrowAsync<PostgresException>().WithMessage("*immutable*");
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task SnapshotPersistenceFailure_RollsBackHardCloseAndRetainsRetry()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var period = await CreateBookAndPeriodAsync(database);
        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            create function "{database.Options.SchemaName}".reject_snapshot() returns trigger language plpgsql as $$
            begin raise exception 'injected snapshot write failure'; end $$;
            create trigger reject_snapshot before insert on "{database.Options.SchemaName}".period_close_snapshots
            for each row execute function "{database.Options.SchemaName}".reject_snapshot();
            """;
        await command.ExecuteNonQueryAsync();
        var target = period with { Status = "HardClosed", ClosedAt = At };
        var close = Close(period);
        var attempt = () => database.JournalStore.SaveHardClosedPeriodAsync(target, period.Version, close);
        await attempt.Should().ThrowAsync<PostgresException>().WithMessage("*injected snapshot write failure*");
        (await database.JournalStore.GetPeriodAsync(period.PeriodId))!.Status.Should().Be("Open");
        (await database.JournalStore.GetCloseSnapshotAsync(period.PeriodId)).Should().BeNull();
        command.CommandText = $"drop trigger reject_snapshot on \"{database.Options.SchemaName}\".period_close_snapshots;";
        await command.ExecuteNonQueryAsync();
        await attempt();
        (await database.JournalStore.GetCloseSnapshotAsync(period.PeriodId))!.VerifyIntegrity().CloseEvent.EventId.Should().Be(close.EventId);
    }

    private static async Task<LedgerAccountingPeriod> CreateBookAndPeriodAsync(LedgerPostgresTestDatabase database)
    {
        var bookId = Guid.NewGuid();
        await database.JournalStore.SaveLedgerBookAsync(new LedgerBookRecord(bookId, "synthetic-snapshot", Guid.NewGuid(),
            FundStructureNodeKindDto.Fund, "Synthetic snapshot book", "USD", At, At));
        return await database.JournalStore.SavePeriodAsync(Period(bookId), 0);
    }

    private static LedgerAccountingPeriod Period(Guid bookId) => new(Guid.NewGuid(), bookId, 2026, 5, "Synthetic May",
        new(2026, 5, 1), new(2026, 5, 31), "Open", At.AddDays(-30), null, 0);

    private static PeriodCloseEventRecord Close(LedgerAccountingPeriod period) => new(Guid.NewGuid(), period.PeriodId,
        "Open", "HardClosed", "synthetic-controller", "Reviewed synthetic close", At);

    private static LedgerJournalEntryRecord Entry(LedgerAccountingPeriod period, decimal amount)
    {
        var id = Guid.NewGuid();
        const string description = "Synthetic capital";
        var entry = new JournalEntry(id, At, description,
        [
            new LedgerEntry(Guid.NewGuid(), id, At, LedgerAccounts.Cash, amount, 0m, description,
                currency: new LedgerEntryCurrency("USD", "USD", amount, 0m, 1m)),
            new LedgerEntry(Guid.NewGuid(), id, At, LedgerAccounts.CapitalAccount, 0m, amount, description,
                currency: new LedgerEntryCurrency("USD", "USD", 0m, amount, 1m))
        ], new JournalEntryMetadata(EffectiveDate: new(2026, 5, 31),
            Tags: new Dictionary<string, string> { ["a"] = "first", ["z"] = "last" }));
        return new(entry, Guid.NewGuid(), period.PeriodId, null, null, 1, At, SourceEventId: Guid.NewGuid());
    }
}
