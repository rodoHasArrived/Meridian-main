using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Integration")]
public sealed class SecurityMasterRecordedIdentifierPostgresTests(SecurityMasterDatabaseFixture fixture)
    : IClassFixture<SecurityMasterDatabaseFixture>
{
    [SecurityMasterDatabaseFact]
    public async Task RecordedLookup_ResolvesPriorAliasRevision_WithoutUsingCorrectedCurrentRow()
    {
        var (service, store, id, recordedAt) = await CreateRecordedSecurityAsync();
        var original = new SecurityAliasDto(Guid.NewGuid(), id, "Ticker", $"OLD-{id:N}", "Vendor",
            SecurityAliasScope.Operations, "original", "first.operator", recordedAt, recordedAt, null, true);
        await store.UpsertAliasAsync(original);
        await store.UpsertAliasAsync(original with
        {
            AliasValue = $"NEW-{id:N}",
            CreatedBy = "correcting.operator",
            CreatedAt = DateTimeOffset.UtcNow
        });
        var cutoff = recordedAt.AddDays(1);

        var historical = await service.GetRecordedByIdentifierAsOfAsync(SecurityIdentifierKind.Ticker,
            original.AliasValue.ToLowerInvariant(), " vendor ", cutoff);
        historical.Should().NotBeNull();
        historical!.SecurityId.Should().Be(id);
        historical.Aliases.Should().ContainSingle().Which.AliasValue.Should().Be(original.AliasValue);
        (await service.GetRecordedByIdentifierAsOfAsync(SecurityIdentifierKind.Ticker, $"NEW-{id:N}", "Vendor", cutoff))
            .Should().BeNull("the corrected alias was recorded after the cutoff");
        (await service.GetRecordedByIdentifierAsOfAsync(SecurityIdentifierKind.Ticker, $"NEW-{id:N}", "Vendor",
            DateTimeOffset.UtcNow.AddMinutes(1))).Should().NotBeNull();
    }

    [SecurityMasterDatabaseFact]
    public async Task RecordedLookup_DoesNotBackdateMigrationBaselineOrSubsequentCorrection()
    {
        var (service, store, id, recordedAt) = await CreateRecordedSecurityAsync();
        var baseline = new SecurityAliasDto(Guid.NewGuid(), id, "Ticker", $"BASE-{id:N}", null,
            SecurityAliasScope.Operations, "pre-migration current value", "original.operator", recordedAt, recordedAt, null, true);
        // Reproduce migration 038's baseline for an alias corrected before revision history existed.
        // Its original creation facts are known; the earlier values of its material fields are not.
        await using (var connection = new NpgsqlConnection(fixture.Options.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            var schema = $"\"{fixture.Options.Schema.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
            command.CommandText = $"""
                insert into {schema}.security_aliases
                    (alias_id, security_id, alias_kind, alias_value, normalized_alias_value,
                     scope, reason, created_by, created_at, valid_from, is_enabled)
                values (@alias_id, @security_id, 'Ticker', @alias_value, @alias_value,
                        'Operations', @reason, @actor, @created_at, @created_at, true);
                insert into {schema}.security_alias_revisions
                    (alias_id, revision, security_id, alias_kind, alias_value, normalized_alias_value,
                     scope, reason, valid_from, is_enabled, recorded_by, recorded_at, history_available_from)
                values (@alias_id, 1, @security_id, 'Ticker', @alias_value, @alias_value,
                        'Operations', @reason, @created_at, true, @actor, @created_at, now());
                """;
            command.Parameters.AddWithValue("alias_id", baseline.AliasId);
            command.Parameters.AddWithValue("security_id", id);
            command.Parameters.AddWithValue("alias_value", baseline.AliasValue);
            command.Parameters.AddWithValue("reason", baseline.Reason!);
            command.Parameters.AddWithValue("actor", baseline.CreatedBy);
            command.Parameters.AddWithValue("created_at", baseline.CreatedAt.UtcDateTime);
            await command.ExecuteNonQueryAsync();
        }

        var cutoff = recordedAt.AddDays(1);
        (await store.GetAliasesRecordedAsOfAsync(id, cutoff)).Should().BeEmpty("pre-migration alias values are unavailable");
        (await service.GetRecordedByIdentifierAsOfAsync(SecurityIdentifierKind.Ticker, baseline.AliasValue, null, cutoff))
            .Should().BeNull();
        var correctedValue = $"CORRECTED-{id:N}";
        await store.UpsertAliasAsync(baseline with { AliasValue = correctedValue, CreatedBy = "correcting.operator" });
        (await service.GetRecordedByIdentifierAsOfAsync(SecurityIdentifierKind.Ticker, correctedValue, null, cutoff))
            .Should().BeNull("this correction's stored revision timestamp exceeds the cutoff");
        (await service.GetRecordedByIdentifierAsOfAsync(SecurityIdentifierKind.Ticker, baseline.AliasValue, null, cutoff))
            .Should().BeNull();
        (await service.GetRecordedByIdentifierAsOfAsync(SecurityIdentifierKind.Ticker, correctedValue, null,
            DateTimeOffset.UtcNow.AddMinutes(1))).Should().NotBeNull();
    }

    [SecurityMasterDatabaseFact]
    public Task StoredInitialAliasTimestampEligibility_CanChangeAfterALockDelayedCommit_ForTheSameCutoff()
        => AssertAliasTimestampEligibilityAfterLockWaitAsync(correction: false);

    [SecurityMasterDatabaseFact]
    public Task StoredCorrectedAliasTimestampEligibility_CanChangeAfterALockDelayedCommit_ForTheSameCutoff()
        => AssertAliasTimestampEligibilityAfterLockWaitAsync(correction: true);

    private async Task AssertAliasTimestampEligibilityAfterLockWaitAsync(bool correction)
    {
        var (service, store, id, recordedAt) = await CreateRecordedSecurityAsync();
        var alias = new SecurityAliasDto(Guid.NewGuid(), id, "Ticker", $"PENDING-{id:N}", null,
            SecurityAliasScope.Operations, null, "request.operator", await DatabaseTimestampAsync(), recordedAt, null, true);
        if (correction)
            await store.UpsertAliasAsync(alias with { AliasValue = $"ORIGINAL-{id:N}", CreatedAt = recordedAt });

        await using var blocker = new NpgsqlConnection(fixture.Options.ConnectionString);
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await LockAsync(blocker, transaction, id, lockEventStream: false);
        var options = WriterOptions($"alias-timestamp-{id:N}");
        var pending = new PostgresSecurityMasterStore(options).UpsertAliasAsync(alias);
        var released = false;
        try
        {
            var cutoff = await CutoffWhileWriterIsBlockedAsync(options);
            (await service.GetRecordedByIdentifierAsOfAsync(SecurityIdentifierKind.Ticker, alias.AliasValue, null, cutoff))
                .Should().BeNull("the pending transaction has not made its alias row visible yet");

            await transaction.CommitAsync();
            released = true;
            await pending.WaitAsync(TimeSpan.FromSeconds(10));

            var eligible = await service.GetRecordedByIdentifierAsOfAsync(SecurityIdentifierKind.Ticker, alias.AliasValue, null, cutoff);
            eligible.Should().NotBeNull("the stored creation/transaction-start timestamp precedes the cutoff even though commit followed it");
            eligible!.SecurityId.Should().Be(id);
            (await store.GetAliasesRecordedAsOfAsync(id, cutoff)).Should().Contain(aliasRow => aliasRow.AliasValue == alias.AliasValue);
        }
        finally
        {
            if (!released)
                await transaction.RollbackAsync();
            await pending.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [SecurityMasterDatabaseFact]
    public async Task StoredEventTimestampEligibility_CanChangeAfterALockDelayedCommit_ForTheSameCutoff()
    {
        var (service, store, id, _) = await CreateRecordedSecurityAsync();
        var current = (await store.GetProjectionAsync(id))!;
        var eventTimestamp = await DatabaseTimestampAsync();
        var ticker = $"PENDING-EVENT-{id:N}";
        var changed = current with
        {
            Version = 2,
            PrimaryIdentifierValue = ticker,
            Identifiers = [new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, ticker, true, current.EffectiveFrom, null, null)]
        };
        var envelope = new SecurityMasterEventEnvelope(null, id, 2, "TermsAmended", eventTimestamp, "test", null, null,
            JsonSerializer.SerializeToElement(changed, Meridian.Core.Serialization.SecurityMasterJsonContext.Default.SecurityProjectionRecord),
            JsonSerializer.SerializeToElement(new { }));
        await using var blocker = new NpgsqlConnection(fixture.Options.ConnectionString);
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await LockAsync(blocker, transaction, id, lockEventStream: true);
        var options = WriterOptions($"event-timestamp-{id:N}");
        var pending = new PostgresSecurityMasterEventStore(options).AppendAsync(id, 1, [envelope]);
        var released = false;
        try
        {
            var cutoff = await CutoffWhileWriterIsBlockedAsync(options);
            cutoff.Should().BeOnOrAfter(eventTimestamp);
            (await service.GetRecordedByIdentifierAsOfAsync(SecurityIdentifierKind.Ticker, ticker, null, cutoff))
                .Should().BeNull("the event append is still waiting on its stream lock");

            await transaction.CommitAsync();
            released = true;
            await pending.WaitAsync(TimeSpan.FromSeconds(10));

            var eligible = await service.GetRecordedByIdentifierAsOfAsync(SecurityIdentifierKind.Ticker, ticker, null, cutoff);
            eligible.Should().NotBeNull("stored EventTimestamp eligibility does not establish commit visibility at that cutoff");
            eligible!.Version.Should().Be(2);
            (await service.GetRecordedByIdAsOfAsync(id, cutoff))!.Version.Should().Be(2);
        }
        finally
        {
            if (!released)
                await transaction.RollbackAsync();
            await pending.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private SecurityMasterOptions WriterOptions(string applicationName)
        => new()
        {
            ConnectionString = new NpgsqlConnectionStringBuilder(fixture.Options.ConnectionString)
            { ApplicationName = applicationName }.ConnectionString,
            Schema = fixture.Options.Schema,
            ProjectionCacheNotificationsEnabled = false
        };

    private async Task LockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id, bool lockEventStream)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var schema = $"\"{fixture.Options.Schema.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        command.CommandText = lockEventStream
            ? "select pg_advisory_xact_lock(hashtext(@security_id::text));"
            : $"select security_id from {schema}.securities where security_id = @security_id for update;";
        command.Parameters.AddWithValue("security_id", id);
        await command.ExecuteScalarAsync();
    }

    private async Task<DateTimeOffset> CutoffWhileWriterIsBlockedAsync(SecurityMasterOptions options)
    {
        await using var observer = new NpgsqlConnection(fixture.Options.ConnectionString);
        await observer.OpenAsync();
        await using var command = observer.CreateCommand();
        command.CommandText = "select exists(select 1 from pg_stat_activity where application_name = @application_name and wait_event_type = 'Lock');";
        var applicationName = new NpgsqlConnectionStringBuilder(options.ConnectionString).ApplicationName
            ?? throw new InvalidOperationException("The timestamp-contract writer requires an application name.");
        command.Parameters.AddWithValue("application_name", applicationName);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (await command.ExecuteScalarAsync() is not true)
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("The timestamp-contract writer did not enter its expected lock wait.");
            await Task.Delay(10);
        }
        command.CommandText = "select clock_timestamp();";
        return new DateTimeOffset((DateTime)(await command.ExecuteScalarAsync())!);
    }

    private async Task<DateTimeOffset> DatabaseTimestampAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select clock_timestamp();";
        return new DateTimeOffset((DateTime)(await command.ExecuteScalarAsync())!);
    }

    private async Task<(SecurityMasterQueryService Service, PostgresSecurityMasterStore Store, Guid Id, DateTimeOffset RecordedAt)>
        CreateRecordedSecurityAsync()
    {
        var store = new PostgresSecurityMasterStore(fixture.Options);
        var eventStore = new PostgresSecurityMasterEventStore(fixture.Options, NullLogger<PostgresSecurityMasterEventStore>.Instance);
        var snapshots = new PostgresSecurityMasterSnapshotStore(fixture.Options);
        var id = Guid.NewGuid();
        var recordedAt = DateTimeOffset.UtcNow.AddDays(-30);
        var projection = SecurityAssetTermsSchemaRoundTripTests.LegacyProjection("Equity",
            JsonSerializer.SerializeToElement(new { schemaVersion = 1, classification = "Common", shareClass = "A" })) with
        {
            SecurityId = id,
            PrimaryIdentifierValue = $"PRIMARY-{id:N}",
            Identifiers = [new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, $"PRIMARY-{id:N}", true, recordedAt, null, null)],
            Version = 1
        };
        await store.UpsertProjectionAsync(projection);
        await eventStore.AppendAsync(id, 0, [new SecurityMasterEventEnvelope(null, id, 1, "SecurityCreated", recordedAt,
            "test", null, null, JsonSerializer.SerializeToElement(projection,
                Meridian.Core.Serialization.SecurityMasterJsonContext.Default.SecurityProjectionRecord),
            JsonSerializer.SerializeToElement(new { }))]);
        return (new SecurityMasterQueryService(eventStore, store, new SecurityMasterAggregateRebuilder(eventStore, snapshots)),
            store, id, recordedAt);
    }
}
