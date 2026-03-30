using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

[Collection(nameof(SecurityMasterDatabaseCollection))]
public sealed class SecurityMasterSnapshotStoreTests
{
    private readonly SecurityMasterDatabaseFixture _fixture;

    public SecurityMasterSnapshotStoreTests(SecurityMasterDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [SecurityMasterDatabaseFact]
    public async Task SaveAndLoadSnapshot_AllowsTailReplayRebuild()
    {
        var securityId = Guid.NewGuid();
        var snapshotStore = new PostgresSecurityMasterSnapshotStore(_fixture.Options);
        var eventStore = new PostgresSecurityMasterEventStore(_fixture.Options);

        var snapshotPayload = JsonSerializer.SerializeToElement(new
        {
            securityId,
            assetClass = "Equity",
            status = "Active",
            version = 2,
            displayName = "Snapshot Name"
        });

        await snapshotStore.SaveAsync(new SecuritySnapshotRecord(
            securityId,
            2,
            DateTimeOffset.UtcNow,
            snapshotPayload));

        var tailEvent = new SecurityMasterEventEnvelope(
            GlobalSequence: null,
            SecurityId: securityId,
            StreamVersion: 3,
            EventType: "TermsAmended",
            EventTimestamp: DateTimeOffset.UtcNow,
            Actor: "codex",
            CorrelationId: null,
            CausationId: null,
            Payload: JsonSerializer.SerializeToElement(new
            {
                securityId,
                assetClass = "Equity",
                status = "Inactive",
                version = 3,
                displayName = "Tail Event Name"
            }),
            Metadata: JsonSerializer.SerializeToElement(new
            {
                sourceSystem = "test",
                schemaVersion = 1
            }));

        await eventStore.AppendAsync(securityId, expectedVersion: 0, [tailEvent]);

        var loadedSnapshot = await snapshotStore.LoadAsync(securityId);
        loadedSnapshot.Should().NotBeNull();
        loadedSnapshot!.Version.Should().Be(2);
        loadedSnapshot.Payload.GetProperty("displayName").GetString().Should().Be("Snapshot Name");

        var replay = await eventStore.LoadSinceSequenceAsync(0, 20);
        replay.Should().ContainSingle(evt => evt.SecurityId == securityId && evt.StreamVersion == 3);
        replay.Single(evt => evt.SecurityId == securityId).Payload.GetProperty("displayName").GetString().Should().Be("Tail Event Name");
    }
}
