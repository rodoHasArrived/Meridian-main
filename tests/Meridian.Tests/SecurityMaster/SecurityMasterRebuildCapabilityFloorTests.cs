using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Application.SecurityMaster.Rebuild;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Projection rebuilds persist terms-carrying projections, so they ask the terms capability floor, with
/// its fail-closed semantics, before each write: a refused node persists nothing.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityMasterRebuildCapabilityFloorTests
{
    private readonly ISecurityMasterEventStore _events = Substitute.For<ISecurityMasterEventStore>();
    private readonly ISecurityMasterStore _store = Substitute.For<ISecurityMasterStore>();
    private readonly ISecurityMasterSnapshotStore _snapshots = Substitute.For<ISecurityMasterSnapshotStore>();
    private readonly SecurityMasterProjectionCache _cache = new();

    [Fact]
    public async Task WarmRebuild_RefusedByTheFloor_PersistsNothing()
    {
        _store.GetCheckpointAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((long?)null);
        _events.GetLatestSequenceAsync(Arg.Any<CancellationToken>()).Returns(42L);
        _store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<SecurityProjectionRecord>());

        var action = () => Orchestrator(RefusingVerifier()).RebuildAsync();

        await action.Should().ThrowAsync<SecurityMasterTermsCapabilityRefusedException>();
        await _store.DidNotReceive().PersistProjectionBatchAsync(Arg.Any<string>(), Arg.Any<long>(),
            Arg.Any<IReadOnlyList<SecurityProjectionRecord>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IncrementalReplay_RefusedByTheFloor_PersistsNoBatch()
    {
        var securityId = Guid.NewGuid();
        var projection = Projection(securityId);
        _cache.Upsert(Projection(Guid.NewGuid()));
        _store.GetCheckpointAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(5L);
        _events.GetLatestSequenceAsync(Arg.Any<CancellationToken>()).Returns(7L);
        _events.LoadSinceSequenceAsync(5L, 10, Arg.Any<CancellationToken>()).Returns(new[]
        {
            new SecurityMasterEventEnvelope(6, securityId, 3, "TermsAmended", DateTimeOffset.UtcNow, "codex", null, null,
                JsonSerializer.SerializeToElement(projection, Meridian.Core.Serialization.SecurityMasterJsonContext.Default.SecurityProjectionRecord),
                JsonSerializer.SerializeToElement(new { sourceSystem = "test" }))
        });
        _store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>()).Returns(projection);

        var action = () => Orchestrator(RefusingVerifier()).RebuildAsync();

        await action.Should().ThrowAsync<SecurityMasterTermsCapabilityRefusedException>();
        await _store.DidNotReceive().PersistProjectionBatchAsync(Arg.Any<string>(), Arg.Any<long>(),
            Arg.Any<IReadOnlyList<SecurityProjectionRecord>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScopedRebuild_RefusedByTheFloor_UpsertsNothing()
    {
        var seed = Projection(Guid.NewGuid());
        _store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { seed });

        var action = () => Orchestrator(RefusingVerifier()).RebuildAssetClassAsync("Equity");

        await action.Should().ThrowAsync<SecurityMasterTermsCapabilityRefusedException>();
        await _store.DidNotReceive().UpsertProjectionAsync(Arg.Any<SecurityProjectionRecord>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScopedRebuild_AcceptedByTheFloor_Upserts()
    {
        var seed = Projection(Guid.NewGuid());
        _store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { seed });
        var verifier = Substitute.For<ISecurityMasterTermsCapabilityVerifier>();

        await Orchestrator(verifier).RebuildAssetClassAsync("Equity");

        await verifier.Received(1).VerifyAsync(Arg.Any<CancellationToken>());
        await _store.Received(1).UpsertProjectionAsync(Arg.Any<SecurityProjectionRecord>(), Arg.Any<CancellationToken>());
    }

    private SecurityMasterRebuildOrchestrator Orchestrator(ISecurityMasterTermsCapabilityVerifier verifier)
    {
        var rebuilder = new SecurityMasterAggregateRebuilder(_events, _snapshots);
        return new SecurityMasterRebuildOrchestrator(
            _events,
            _store,
            _cache,
            rebuilder,
            new SecurityMasterProjectionService(_store, _cache, rebuilder, NullLogger<SecurityMasterProjectionService>.Instance),
            new SecurityMasterOptions { PreloadProjectionCache = true, ProjectionReplayBatchSize = 10 },
            NullLogger<SecurityMasterRebuildOrchestrator>.Instance,
            termsCapabilityVerifier: verifier);
    }

    internal static ISecurityMasterTermsCapabilityVerifier RefusingVerifier()
    {
        var verifier = Substitute.For<ISecurityMasterTermsCapabilityVerifier>();
        verifier.VerifyAsync(Arg.Any<CancellationToken>()).Returns<Task>(_ =>
        {
            SecurityMasterTermsCapabilityVerifier.EnsureCompatible(
                new SecurityMasterTermsCapabilityState(true, null), writesDeclaredTerms: true, NullLogger.Instance);
            return Task.CompletedTask;
        });
        return verifier;
    }

    private static SecurityProjectionRecord Projection(Guid securityId)
        => new(
            securityId,
            "Equity",
            SecurityStatusDto.Active,
            "Floor-gated security",
            "USD",
            "Ticker",
            "ACME",
            JsonSerializer.SerializeToElement(new { displayName = "Floor-gated security", currency = "USD" }),
            JsonSerializer.SerializeToElement(new { shareClass = "Common" }),
            JsonSerializer.SerializeToElement(new { sourceSystem = "test", asOf = DateTimeOffset.UtcNow, updatedBy = "codex" }),
            3,
            DateTimeOffset.UtcNow.AddDays(-1),
            null,
            new[] { new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "ACME", true, DateTimeOffset.UtcNow.AddDays(-1), null, null) },
            Array.Empty<SecurityAliasDto>());
}
