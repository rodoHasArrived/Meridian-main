using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Alias writes keep the projection version and the cache accepts an equal version, so the
/// post-commit reread that refreshes this node's cache must be ordered across concurrent writes.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityMasterServiceAliasCacheRefreshTests
{
    [Fact]
    public async Task ConcurrentAliasWrites_LeaveTheCacheOnTheLatestReread()
    {
        var securityId = Guid.NewGuid();
        var store = Substitute.For<ISecurityMasterStore>();
        store.UpsertAliasAsync(Arg.Any<SecurityAliasDto>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<SecurityAliasDto>());

        // The first reread stalls until released and then returns the earlier state; the second
        // reread returns the later state. Ungated, the second write would refresh the cache first
        // and the stalled first reread would then overwrite it with the superseded alias.
        var releaseFirstReread = new TaskCompletionSource();
        var firstRereadStarted = new TaskCompletionSource();
        var rereads = 0;
        store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                if (Interlocked.Increment(ref rereads) == 1)
                {
                    firstRereadStarted.SetResult();
                    await releaseFirstReread.Task;
                    return (SecurityProjectionRecord?)Projection(securityId, "ACME.O");
                }

                return Projection(securityId, "ACME.OQ");
            });

        var cache = new SecurityMasterProjectionCache();
        using var service = new SecurityMasterService(
            Substitute.For<ISecurityMasterEventStore>(),
            Substitute.For<ISecurityMasterSnapshotStore>(),
            store,
            new SecurityMasterAggregateRebuilder(Substitute.For<ISecurityMasterEventStore>(), Substitute.For<ISecurityMasterSnapshotStore>()),
            new SecurityMasterOptions { ConnectionString = "Host=unused", Schema = "security_master" },
            NullLogger<SecurityMasterService>.Instance,
            projectionCache: cache);

        var first = service.UpsertAliasAsync(Request(securityId, "ACME.O"));
        await firstRereadStarted.Task;
        var second = service.UpsertAliasAsync(Request(securityId, "ACME.OQ"));
        await Task.Delay(50);
        releaseFirstReread.SetResult();
        await Task.WhenAll(first, second);

        cache.Get(securityId)!.Aliases.Should().ContainSingle().Which.AliasValue.Should().Be("ACME.OQ");
    }

    [Fact]
    public async Task CommittedAliasWrite_ReportsSuccessWhenTheCacheRereadFails()
    {
        // The alias is durable once the store returns; a failed or cancelled post-commit cache
        // refresh must not surface as a failed write.
        var securityId = Guid.NewGuid();
        var store = Substitute.For<ISecurityMasterStore>();
        store.UpsertAliasAsync(Arg.Any<SecurityAliasDto>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<SecurityAliasDto>());
        store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>())
            .Returns<SecurityProjectionRecord?>(_ => throw new InvalidOperationException("transient reread failure"));
        using var service = CreateService(store, new SecurityMasterProjectionCache());

        var persisted = await service.UpsertAliasAsync(Request(securityId, "ACME.OQ"));

        persisted.AliasValue.Should().Be("ACME.OQ");
    }

    [Fact]
    public async Task RemoteRefreshAndLocalAliasWrite_ShareTheCacheRefreshGate()
    {
        // A remote notification's reread holds the cache's refresh gate; a local alias write's
        // reread must wait for it, so the later local state is the one left in the cache.
        var securityId = Guid.NewGuid();
        var cache = new SecurityMasterProjectionCache();
        var store = Substitute.For<ISecurityMasterStore>();
        store.UpsertAliasAsync(Arg.Any<SecurityAliasDto>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<SecurityAliasDto>());
        store.GetProjectionAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(Projection(securityId, "ACME.OQ"));
        using var service = CreateService(store, cache);

        await cache.RefreshGate.WaitAsync();
        var write = service.UpsertAliasAsync(Request(securityId, "ACME.OQ"));
        await Task.Delay(50);
        write.IsCompleted.Should().BeFalse("the local reread waits for the in-flight remote refresh");
        cache.Upsert(Projection(securityId, "ACME.O"));
        cache.RefreshGate.Release();
        await write;

        cache.Get(securityId)!.Aliases.Should().ContainSingle().Which.AliasValue.Should().Be("ACME.OQ");
    }

    private static SecurityMasterService CreateService(ISecurityMasterStore store, SecurityMasterProjectionCache cache)
        => new(
            Substitute.For<ISecurityMasterEventStore>(),
            Substitute.For<ISecurityMasterSnapshotStore>(),
            store,
            new SecurityMasterAggregateRebuilder(Substitute.For<ISecurityMasterEventStore>(), Substitute.For<ISecurityMasterSnapshotStore>()),
            new SecurityMasterOptions { ConnectionString = "Host=unused", Schema = "security_master" },
            NullLogger<SecurityMasterService>.Instance,
            projectionCache: cache);

    private static UpsertSecurityAliasRequest Request(Guid securityId, string value)
        => new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), securityId, "Ticker", value, null,
            SecurityAliasScope.Operations, "tests", DateTimeOffset.UtcNow.AddDays(-1), null, null);

    private static SecurityProjectionRecord Projection(Guid securityId, string aliasValue)
    {
        var asOf = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return new SecurityProjectionRecord(
            securityId,
            "Equity",
            SecurityStatusDto.Active,
            "Acme",
            "USD",
            "Ticker",
            "ACME",
            JsonSerializer.SerializeToElement(new { displayName = "Acme", currency = "USD" }),
            JsonSerializer.SerializeToElement(new { schemaVersion = 1 }),
            JsonSerializer.SerializeToElement(new { sourceSystem = "tests", updatedBy = "tests", asOf = "2026-01-01T00:00:00Z" }),
            3,
            asOf,
            null,
            [],
            [new SecurityAliasDto(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), securityId, "Ticker", aliasValue, null,
                SecurityAliasScope.Operations, null, "tests", asOf, asOf, null, true)]);
    }
}
