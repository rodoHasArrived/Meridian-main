using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

public sealed class SecurityMasterRebuildOrchestratorTests
{
    [Fact]
    public async Task RebuildAsync_WhenCheckpointMissing_PersistsWarmSetAndSavesCheckpointInOneCall()
    {
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        var store = Substitute.For<ISecurityMasterStore>();
        var cache = new SecurityMasterProjectionCache();
        var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
        var rebuilder = new SecurityMasterAggregateRebuilder(eventStore, snapshotStore);
        var projectionService = new SecurityMasterProjectionService(store, cache, rebuilder, NullLogger<SecurityMasterProjectionService>.Instance);
        var options = new SecurityMasterOptions { PreloadProjectionCache = true, ProjectionReplayBatchSize = 10 };

        store.GetCheckpointAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((long?)null);
        eventStore.GetLatestSequenceAsync(Arg.Any<CancellationToken>()).Returns(42L);
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<SecurityProjectionRecord>());

        var orchestrator = new SecurityMasterRebuildOrchestrator(
            eventStore,
            store,
            cache,
            rebuilder,
            projectionService,
            options,
            NullLogger<SecurityMasterRebuildOrchestrator>.Instance);

        await orchestrator.RebuildAsync();

        await store.Received(1).LoadAllAsync(Arg.Any<CancellationToken>());
        await store.Received(1).PersistProjectionBatchAsync(
            "security_master_cache",
            42L,
            Arg.Is<IReadOnlyList<SecurityProjectionRecord>>(records => records.Count == 0),
            Arg.Any<CancellationToken>());
        await store.DidNotReceive().SaveCheckpointAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RebuildAsync_WhenCheckpointExistsButCacheIsCold_PersistsWarmSetInsteadOfIncrementalReplay()
    {
        var securityId = Guid.NewGuid();
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        var store = Substitute.For<ISecurityMasterStore>();
        var cache = new SecurityMasterProjectionCache();
        var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
        var rebuilder = new SecurityMasterAggregateRebuilder(eventStore, snapshotStore);
        var projectionService = new SecurityMasterProjectionService(store, cache, rebuilder, NullLogger<SecurityMasterProjectionService>.Instance);
        var options = new SecurityMasterOptions { PreloadProjectionCache = true, ProjectionReplayBatchSize = 10 };

        var projection = CreateProjection(securityId, "Tail Name", 3);

        store.GetCheckpointAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(5L);
        eventStore.GetLatestSequenceAsync(Arg.Any<CancellationToken>()).Returns(7L);
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { projection });
        snapshotStore.LoadAsync(securityId, Arg.Any<CancellationToken>())
            .Returns((SecuritySnapshotRecord?)null);

        var orchestrator = new SecurityMasterRebuildOrchestrator(
            eventStore,
            store,
            cache,
            rebuilder,
            projectionService,
            options,
            NullLogger<SecurityMasterRebuildOrchestrator>.Instance);

        await orchestrator.RebuildAsync();

        await store.Received(1).LoadAllAsync(Arg.Any<CancellationToken>());
        await eventStore.DidNotReceive().LoadSinceSequenceAsync(Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await eventStore.Received(1).LoadAsync(securityId, Arg.Any<CancellationToken>());
        cache.Count.Should().Be(1);
        await store.Received(1).PersistProjectionBatchAsync(
            "security_master_cache",
            7L,
            Arg.Is<IReadOnlyList<SecurityProjectionRecord>>(records =>
                records.Count == 1 &&
                records[0].SecurityId == securityId &&
                records[0].DisplayName == "Tail Name"),
            Arg.Any<CancellationToken>());
        await store.DidNotReceive().SaveCheckpointAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RebuildAsync_WhenCheckpointExistsAndCacheIsWarm_PersistsReplayBatchBeforeUpdatingCache()
    {
        var securityId = Guid.NewGuid();
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        var store = Substitute.For<ISecurityMasterStore>();
        var cache = new SecurityMasterProjectionCache();
        var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
        var rebuilder = new SecurityMasterAggregateRebuilder(eventStore, snapshotStore);
        var projectionService = new SecurityMasterProjectionService(store, cache, rebuilder, NullLogger<SecurityMasterProjectionService>.Instance);
        var options = new SecurityMasterOptions { PreloadProjectionCache = true, ProjectionReplayBatchSize = 10 };

        cache.Upsert(CreateProjection(Guid.NewGuid(), "Existing", 1));

        var projection = CreateProjection(securityId, "Tail Name", 3);

        store.GetCheckpointAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(5L);
        eventStore.GetLatestSequenceAsync(Arg.Any<CancellationToken>()).Returns(7L);
        eventStore.LoadSinceSequenceAsync(5L, 10, Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new SecurityMasterEventEnvelope(
                    6,
                    securityId,
                    3,
                    "TermsAmended",
                    DateTimeOffset.UtcNow,
                    "codex",
                    null,
                    null,
                    JsonSerializer.SerializeToElement(projection, Meridian.Core.Serialization.SecurityMasterJsonContext.Default.SecurityProjectionRecord),
                    JsonSerializer.SerializeToElement(new { sourceSystem = "test" }))
            });
        eventStore.LoadSinceSequenceAsync(6L, 10, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SecurityMasterEventEnvelope>());
        store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(projection);
        snapshotStore.LoadAsync(securityId, Arg.Any<CancellationToken>())
            .Returns((SecuritySnapshotRecord?)null);

        var orchestrator = new SecurityMasterRebuildOrchestrator(
            eventStore,
            store,
            cache,
            rebuilder,
            projectionService,
            options,
            NullLogger<SecurityMasterRebuildOrchestrator>.Instance);

        await orchestrator.RebuildAsync();

        Received.InOrder(() =>
        {
            eventStore.LoadSinceSequenceAsync(5L, 10, Arg.Any<CancellationToken>());
            store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>());
            store.PersistProjectionBatchAsync(
                "security_master_cache",
                6L,
                Arg.Is<IReadOnlyList<SecurityProjectionRecord>>(records => records.Count == 1 && records[0].SecurityId == securityId),
                Arg.Any<CancellationToken>());
        });

        cache.Get(securityId).Should().NotBeNull();
        await store.DidNotReceive().SaveCheckpointAsync(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    private static SecurityProjectionRecord CreateProjection(Guid securityId, string displayName, long version)
        => new(
            securityId,
            "Equity",
            SecurityStatusDto.Active,
            displayName,
            "USD",
            "Ticker",
            "ACME",
            JsonSerializer.SerializeToElement(new
            {
                displayName,
                currency = "USD"
            }),
            JsonSerializer.SerializeToElement(new
            {
                shareClass = "Common"
            }),
            JsonSerializer.SerializeToElement(new
            {
                sourceSystem = "test",
                asOf = DateTimeOffset.UtcNow,
                updatedBy = "codex"
            }),
            version,
            DateTimeOffset.UtcNow.AddDays(-1),
            null,
            new[]
            {
                new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "ACME", true, DateTimeOffset.UtcNow.AddDays(-1), null, null)
            },
            Array.Empty<SecurityAliasDto>());
}
