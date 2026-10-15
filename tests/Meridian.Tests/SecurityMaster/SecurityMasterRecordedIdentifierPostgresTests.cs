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
            AliasValue = $"NEW-{id:N}", CreatedBy = "correcting.operator", CreatedAt = DateTimeOffset.UtcNow
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
        // Reproduce migration 037's baseline for an alias corrected before revision history existed.
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
            .Should().BeNull("a later correction cannot retroactively create knowledge");
        (await service.GetRecordedByIdentifierAsOfAsync(SecurityIdentifierKind.Ticker, baseline.AliasValue, null, cutoff))
            .Should().BeNull();
        (await service.GetRecordedByIdentifierAsOfAsync(SecurityIdentifierKind.Ticker, correctedValue, null,
            DateTimeOffset.UtcNow.AddMinutes(1))).Should().NotBeNull();
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
