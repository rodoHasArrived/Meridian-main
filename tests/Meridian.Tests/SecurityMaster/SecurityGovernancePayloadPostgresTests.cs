using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Npgsql;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// PostgreSQL coverage for versioned governance payloads: rows written the way the pre-contract
/// writers wrote them (no explicit payload_version) read unchanged through the typed reader after
/// a jsonb round trip, and new writes record their contract version explicitly.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SecurityGovernancePayloadPostgresTests(SecurityMasterDatabaseFixture fixture)
    : IClassFixture<SecurityMasterDatabaseFixture>
{
    [SecurityMasterDatabaseFact]
    public async Task RetainedVersionOneRows_ReadUnchangedThroughTheTypedReader()
    {
        var marker = $"fixture-{Guid.NewGuid():N}";
        var fixtures = SecurityGovernancePayloadContractTests.LoadVersionOneFixtures();
        await using (var connection = new NpgsqlConnection(fixture.Options.ConnectionString))
        {
            await connection.OpenAsync();
            foreach (var captured in fixtures)
            {
                // Exactly the pre-contract insert: payload_version is left to the column default.
                await using var insert = new NpgsqlCommand(
                    $"""
                    insert into {fixture.Options.Schema}.security_governance_events
                        (security_id, stream, event_type, actor, occurred_at, payload)
                    values (null, @stream, @event_type, @actor, now(), @payload::jsonb);
                    """,
                    connection);
                insert.Parameters.AddWithValue("stream", captured.Stream);
                insert.Parameters.AddWithValue("event_type", captured.EventType);
                insert.Parameters.AddWithValue("actor", marker);
                insert.Parameters.AddWithValue("payload", captured.Payload);
                await insert.ExecuteNonQueryAsync();
            }
        }

        var log = new PostgresSecurityGovernanceEventLog(fixture.Options);
        foreach (var captured in fixtures)
        {
            var stored = (await log.GetForStreamAsync(captured.Stream, limit: 5000))
                .Single(item => item.Actor == marker && item.EventType == captured.EventType);
            stored.PayloadVersion.Should().Be(1, "retained rows carry migration 040's default");

            var contract = SecurityGovernancePayloadContracts.Resolve(stored.Stream, stored.EventType);
            var read = contract.Read(stored.PayloadVersion, stored.Payload);
            JsonNode.DeepEquals(JsonNode.Parse(contract.Serialize(read)), JsonNode.Parse(captured.Payload))
                .Should().BeTrue($"{captured.Stream}/{captured.EventType} must read the retained values unchanged");
            stored.TryReadPayload<ISecurityGovernancePayload>(out var typed).Should().BeTrue();
            typed.Should().BeOfType(contract.PayloadType);
        }
    }

    [SecurityMasterDatabaseFact]
    public async Task NewWrites_RecordTheContractVersionExplicitly()
    {
        var curves = new PostgresSecurityCurveStore(fixture.Options);
        var events = new PostgresSecurityGovernanceEventLog(fixture.Options);
        var key = $"EXPLICIT-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";

        // Move the column default so that a write relying on it would be visible.
        await ExecuteAsync($"alter table {fixture.Options.Schema}.security_governance_events alter column payload_version set default 99;");
        try
        {
            var definition = await curves.DefineAsync(new DefineSecurityCurveRequest(
                key, SecurityCurveKind.Discount, "USD", null, null, "ACT/360", SecurityCurveInterpolation.Linear, "test", null, "curve.steward"));

            var recorded = (await events.GetForStreamAsync(SecurityGovernanceStreams.Curves, limit: 5000))
                .Single(item => item.EventType == PostgresSecurityCurveStore.DefinedEvent
                                && item.Payload.GetProperty("curveId").GetGuid() == definition.CurveId);
            recorded.PayloadVersion.Should().Be(SecurityGovernancePayloadContracts.InitialPayloadVersion);
            var payload = recorded.ReadPayload<CurveDefinedPayload>();
            payload.CurveKey.Should().Be(key);
            payload.Kind.Should().Be("Discount");
            payload.DayCount.Should().Be("Actual360");
        }
        finally
        {
            await ExecuteAsync($"alter table {fixture.Options.Schema}.security_governance_events alter column payload_version set default 1;");
        }
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
