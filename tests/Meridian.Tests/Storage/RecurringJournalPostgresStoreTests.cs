using FluentAssertions;
using Meridian.FinancialOperations.FundAdministration;
using Meridian.Tests.FinancialOperations;
using Npgsql;
using static Meridian.Tests.FinancialOperations.RecurringJournalStoreTests;

namespace Meridian.Tests.Storage;

/// <summary>
/// The PostgreSQL recurring journal store retains the same document as the file store, but its lease
/// spans every process and host connected to the ledger database.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RecurringJournalPostgresStoreTests
{
    private static async Task<PostgresRecurringJournalStore> SeedAsync(LedgerPostgresTestDatabase database)
    {
        var store = new PostgresRecurringJournalStore(database.Options);
        await store.InitializeAsync();
        await using var session = await store.OpenSessionAsync();
        await session.RegisterTemplateAsync(Template(), "controller", 0, At);
        await session.RegisterScheduleAsync(Schedule(), Scope, [Evidence()], "controller", 0, At);
        return store;
    }

    private static string Table(LedgerPostgresTestDatabase database) => $"\"{database.Options.SchemaName}\".\"recurring_journal_state\"";

    private static async Task<object?> ScalarAsync(LedgerPostgresTestDatabase database, string sql)
    {
        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    [LedgerDatabaseFact]
    public async Task Restart_RetainsExactDefinitionsEvidenceAndDraftIdentity()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        await SeedAsync(database);
        RecurringOccurrenceRecord original;
        await using (var session = await new PostgresRecurringJournalStore(database.Options).OpenSessionAsync())
        {
            original = await session.ClaimAsync("schedule-a", Date, 1, 1, At);
            await session.SetOutcomeAsync(original.OccurrenceKey, RecurringOccurrenceState.Drafted, null, "2026-10", null, null, At);
        }

        await using var restarted = await new PostgresRecurringJournalStore(database.Options).OpenSessionAsync();
        var replay = await restarted.ClaimAsync("SCHEDULE-A", Date, 1, 1, At.AddHours(2));
        replay.DraftId.Should().Be(original.DraftId);
        replay.ScheduleDefinition.Should().BeEquivalentTo(original.ScheduleDefinition);
        replay.TemplateDefinition.Should().BeEquivalentTo(original.TemplateDefinition);
        replay.ScheduleDefinition.Evidence.Should().ContainSingle().Which.Should().BeEquivalentTo(Evidence());
        replay.TemplateDefinition.Template.Lines[0].Dimensions!.ExternalGlDimensions["desk"].Should().Be("operations");
        replay.State.Should().Be(RecurringOccurrenceState.Drafted);
        replay.History.Should().HaveCount(2);
        restarted.Occurrences.Should().ContainSingle();
    }

    [LedgerDatabaseFact]
    public async Task ConcurrentStoreInstances_SerializeClaimAndIntakeWithoutDuplicateEffect()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        await SeedAsync(database);
        var effects = 0;
        // Separate store instances and connections stand in for separate runner processes.
        var ids = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var session = await new PostgresRecurringJournalStore(database.Options).OpenSessionAsync();
            var claim = await session.ClaimAsync("schedule-a", Date, 1, 1, At);
            if (claim.State != RecurringOccurrenceState.Drafted)
            {
                Interlocked.Increment(ref effects);
                await Task.Delay(10);
                await session.SetOutcomeAsync(claim.OccurrenceKey, RecurringOccurrenceState.Drafted, null, "2026-10", null, null, At);
            }
            return claim.DraftId;
        }));

        effects.Should().Be(1);
        ids.Distinct().Should().ContainSingle();
        await using var final = await new PostgresRecurringJournalStore(database.Options).OpenSessionAsync();
        final.Occurrences.Should().ContainSingle().Which.State.Should().Be(RecurringOccurrenceState.Drafted);
    }

    [LedgerDatabaseFact]
    public async Task ChangedDefinition_BlocksOriginalClaimUntilExplicitRestoration()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var store = await SeedAsync(database);
        Guid originalId;
        await using (var session = await store.OpenSessionAsync())
        {
            originalId = (await session.ClaimAsync("schedule-a", Date, 1, 1, At)).DraftId;
            await session.RegisterScheduleAsync(Schedule(200), Scope, [Evidence()], "controller", 1, At.AddMinutes(1));
        }

        await using var restarted = await store.OpenSessionAsync();
        restarted.Occurrences.Single().State.Should().Be(RecurringOccurrenceState.DefinitionChanged);
        await restarted.ActivateDefinitionsAsync("schedule-a", 1, 1, "controller", "Restore approved definition.", At.AddMinutes(3));
        var recovered = await restarted.ClaimAsync("schedule-a", Date, 1, 1, At.AddMinutes(4));
        recovered.DraftId.Should().Be(originalId);
        recovered.State.Should().Be(RecurringOccurrenceState.Claimed);
        recovered.ScheduleDefinition.Schedule.Parameters["fee"].Should().Be(100m);
    }

    [LedgerDatabaseFact]
    public async Task UnprovisionedState_FailsClosedWithoutCreatingAnEmptyStore()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var open = async () => await new PostgresRecurringJournalStore(database.Options).OpenSessionAsync();
        await open.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not been provisioned*");
        (await ScalarAsync(database, $"select count(*) from {Table(database)};")).Should().Be(0L);
    }

    [LedgerDatabaseFact]
    public async Task Initialize_IsIdempotentAndNeverReplacesRetainedState()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var store = await SeedAsync(database);
        await store.InitializeAsync();
        await new PostgresRecurringJournalStore(database.Options).InitializeAsync();

        await using var session = await store.OpenSessionAsync();
        session.Templates.Should().ContainSingle();
        session.Schedules.Should().ContainSingle();
    }

    [LedgerDatabaseFact]
    public async Task RetainedState_CannotBeDeletedOrTruncated()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        await SeedAsync(database);
        var delete = () => ScalarAsync(database, $"delete from {Table(database)};");
        var truncate = () => ScalarAsync(database, $"truncate {Table(database)};");
        await delete.Should().ThrowAsync<PostgresException>().WithMessage("*cannot be deleted or truncated*");
        await truncate.Should().ThrowAsync<PostgresException>().WithMessage("*cannot be deleted or truncated*");

        await using var session = await new PostgresRecurringJournalStore(database.Options).OpenSessionAsync();
        session.Schedules.Should().ContainSingle();
    }

    [LedgerDatabaseFact]
    public async Task TamperedState_FailsClosedAndInitializeDoesNotOverwriteIt()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var store = await SeedAsync(database);
        // Valid JSON whose fee no longer matches the retained content hash.
        await ScalarAsync(database, $"update {Table(database)} set envelope_json = replace(envelope_json, '\"fee\":100', '\"fee\":900');");
        var tampered = (string)(await ScalarAsync(database, $"select envelope_json from {Table(database)};"))!;
        tampered.Should().Contain("\"fee\":900");

        var open = async () => await store.OpenSessionAsync();
        var initialize = () => store.InitializeAsync();
        await open.Should().ThrowAsync<InvalidOperationException>().WithMessage("*restore the retained store*");
        await initialize.Should().ThrowAsync<InvalidOperationException>();
        (await ScalarAsync(database, $"select envelope_json from {Table(database)};")).Should().Be(tampered);
    }

    [LedgerDatabaseFact]
    public async Task SessionThatLostItsLease_CannotOverwriteANewerCommit()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var store = await SeedAsync(database);
        await using var session = await store.OpenSessionAsync();
        // Another writer committed while this session's lease was lost (for example a dropped connection).
        await ScalarAsync(database, $"update {Table(database)} set revision = revision + 1;");

        var claim = async () => await session.ClaimAsync("schedule-a", Date, 1, 1, At);
        await claim.Should().ThrowAsync<InvalidOperationException>().WithMessage("*changed outside this session's lease*");
        // The session faults rather than retrying from its stale image.
        var retry = () => session.Occurrences;
        retry.Should().Throw<InvalidOperationException>().WithMessage("*uncertain*");
    }

    [LedgerDatabaseFact]
    public async Task Initialize_CarriesRetainedFileStateForwardSoDraftIdentityIsNotReissued()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var directory = Path.Combine(Path.GetTempPath(), "meridian-recurring-import", Guid.NewGuid().ToString("N"));
        try
        {
            var legacy = new FileRecurringJournalStore(directory);
            await legacy.InitializeAsync();
            RecurringOccurrenceRecord drafted;
            await using (var session = await legacy.OpenSessionAsync())
            {
                await session.RegisterTemplateAsync(Template(), "controller", 0, At);
                await session.RegisterScheduleAsync(Schedule(), Scope, [Evidence()], "controller", 0, At);
                drafted = await session.ClaimAsync("schedule-a", Date, 1, 1, At);
                drafted = await session.SetOutcomeAsync(drafted.OccurrenceKey, RecurringOccurrenceState.Drafted, null, "2026-10", null, null, At);
            }

            var store = new PostgresRecurringJournalStore(database.Options);
            await store.InitializeAsync(legacy);

            await using var imported = await store.OpenSessionAsync();
            var occurrence = imported.Occurrences.Should().ContainSingle().Which;
            occurrence.Should().BeEquivalentTo(drafted);
            // A runner replaying the same occurrence finds it already drafted rather than claiming anew.
            (await imported.ClaimAsync("schedule-a", Date, 1, 1, At.AddDays(1))).State.Should().Be(RecurringOccurrenceState.Drafted);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [LedgerDatabaseFact]
    public async Task Initialize_RefusesUnverifiableFileStateAndCreatesNoRow()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var directory = Path.Combine(Path.GetTempPath(), "meridian-recurring-import", Guid.NewGuid().ToString("N"));
        try
        {
            var legacy = new FileRecurringJournalStore(directory);
            await legacy.InitializeAsync();
            await File.WriteAllTextAsync(Path.Combine(directory, "recurring-journals.json"), "{bad-data");

            var initialize = () => new PostgresRecurringJournalStore(database.Options).InitializeAsync(legacy);
            await initialize.Should().ThrowAsync<InvalidOperationException>();
            (await ScalarAsync(database, $"select count(*) from {Table(database)};")).Should().Be(0L);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [LedgerDatabaseFact]
    public async Task Initialize_StartsEmptyWhenTheFileStoreWasNeverProvisioned()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var never = new FileRecurringJournalStore(Path.Combine(Path.GetTempPath(), "meridian-recurring-import", Guid.NewGuid().ToString("N")));

        var store = new PostgresRecurringJournalStore(database.Options);
        await store.InitializeAsync(never);

        await using var session = await store.OpenSessionAsync();
        session.Schedules.Should().BeEmpty();
        session.Occurrences.Should().BeEmpty();
    }

    [LedgerDatabaseFact]
    public async Task OpenSession_WaitsForTheCurrentLeaseHolder()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var store = await SeedAsync(database);
        var holder = await store.OpenSessionAsync();
        var waiter = new PostgresRecurringJournalStore(database.Options).OpenSessionAsync();

        await Task.Delay(200);
        waiter.IsCompleted.Should().BeFalse("a second runner must wait for the first runner's whole intake cycle");
        await holder.DisposeAsync();
        await using var acquired = await waiter.WaitAsync(TimeSpan.FromSeconds(10));
        acquired.Schedules.Should().ContainSingle();
    }

    [LedgerDatabaseFact]
    public async Task OpenSession_HonoursCancellationWhileWaitingForTheLease()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var store = await SeedAsync(database);
        await using var holder = await store.OpenSessionAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var open = async () => await new PostgresRecurringJournalStore(database.Options).OpenSessionAsync(cts.Token);
        await open.Should().ThrowAsync<OperationCanceledException>();
    }
}
