using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

public sealed class SecurityMasterProjectionServiceSnapshotTests
{
    [Fact]
    public async Task WarmAsync_RebuildsCacheFromSnapshotAndTailEvents()
    {
        var securityId = Guid.NewGuid();
        var store = Substitute.For<ISecurityMasterStore>();
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
        var cache = new SecurityMasterProjectionCache();
        var rebuilder = new SecurityMasterAggregateRebuilder(eventStore, snapshotStore);

        var staleProjection = CreateProjection(securityId, "Projection Name", 1);
        var snapshotProjection = CreateProjection(securityId, "Snapshot Name", 2);
        var tailProjection = CreateProjection(securityId, "Tail Name", 3);

        store.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { staleProjection });

        snapshotStore.LoadAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new SecuritySnapshotRecord(
                securityId,
                2,
                DateTimeOffset.UtcNow,
                JsonSerializer.SerializeToElement(snapshotProjection, Meridian.Core.Serialization.SecurityMasterJsonContext.Default.SecurityProjectionRecord)));

        eventStore.LoadAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new SecurityMasterEventEnvelope(
                    null,
                    securityId,
                    3,
                    "TermsAmended",
                    DateTimeOffset.UtcNow,
                    "codex",
                    null,
                    null,
                    JsonSerializer.SerializeToElement(tailProjection, Meridian.Core.Serialization.SecurityMasterJsonContext.Default.SecurityProjectionRecord),
                    JsonSerializer.SerializeToElement(new { sourceSystem = "test" }))
            });

        var service = new SecurityMasterProjectionService(
            store,
            cache,
            rebuilder,
            NullLogger<SecurityMasterProjectionService>.Instance);

        await service.WarmAsync();

        var cached = cache.Get(securityId);
        cached.Should().NotBeNull();
        cached!.DisplayName.Should().Be("Tail Name");
        cached.Version.Should().Be(3);
    }

    [Fact]
    public async Task WarmAsync_ReplacesExistingCacheEntries()
    {
        var existingSecurityId = Guid.NewGuid();
        var replacementSecurityId = Guid.NewGuid();

        var store = Substitute.For<ISecurityMasterStore>();
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
        var cache = new SecurityMasterProjectionCache();
        var rebuilder = new SecurityMasterAggregateRebuilder(eventStore, snapshotStore);

        cache.Upsert(CreateProjection(existingSecurityId, "Stale", 1));

        var rebuiltProjection = CreateProjection(replacementSecurityId, "Fresh", 2);

        store.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { rebuiltProjection });

        snapshotStore.LoadAsync(replacementSecurityId, Arg.Any<CancellationToken>())
            .Returns((SecuritySnapshotRecord?)null);
        eventStore.LoadAsync(replacementSecurityId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SecurityMasterEventEnvelope>());

        var service = new SecurityMasterProjectionService(
            store,
            cache,
            rebuilder,
            NullLogger<SecurityMasterProjectionService>.Instance);

        await service.WarmAsync();

        cache.Count.Should().Be(1);
        cache.Get(existingSecurityId).Should().BeNull();
        cache.Get(replacementSecurityId)!.DisplayName.Should().Be("Fresh");
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
                currency = "USD",
                exchange = "XNYS",
                lotSize = 1,
                tickSize = 0.01m
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
