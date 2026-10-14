using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Catalog;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Unit")]
public sealed class SecurityMasterCanonicalSymbolSeedFallbackTests
{
    [Fact]
    public async Task RegistryWithoutReconciliation_IsSeededThroughTheBaseContract()
    {
        // A host may supply an ICanonicalSymbolRegistry that does not implement the Security
        // Master reconciliation writer; seeding must fall back to RegisterBatchAsync rather than
        // throw, so the ticker-to-security lookup is still populated.
        var securityId = Guid.NewGuid();
        var cache = new SecurityMasterProjectionCache();
        cache.Upsert(SecurityMasterProjectionChangeNotificationTests.CreateProjection(securityId, "Fallback", 1) with
        {
            PrimaryIdentifierValue = "FALLBK"
        });
        var registry = Substitute.For<ICanonicalSymbolRegistry>();
        registry.RegisterBatchAsync(Arg.Any<IEnumerable<CanonicalSymbolDefinition>>(), Arg.Any<CancellationToken>())
            .Returns(1);
        var seed = new SecurityMasterCanonicalSymbolSeedService(
            cache, registry, NullLogger<SecurityMasterCanonicalSymbolSeedService>.Instance);

        await seed.SeedAsync();

        await registry.Received(1).RegisterBatchAsync(
            Arg.Is<IEnumerable<CanonicalSymbolDefinition>>(definitions => definitions.Any(d => d.Canonical == "FALLBK")),
            Arg.Any<CancellationToken>());
        seed.TryGetSecurityId("FALLBK").Should().Be(securityId);
    }
    [Fact]
    public async Task LazyRegistrySeed_DiscoversDurableMembershipWithoutWarmingCache()
    {
        var securityId = Guid.NewGuid();
        var cache = new SecurityMasterProjectionCache();
        var store = Substitute.For<ISecurityMasterStore>();
        var record = SecurityMasterProjectionChangeNotificationTests.CreateProjection(securityId, "Missed creation", 1)
            with { PrimaryIdentifierValue = "MISSED" };
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { record });
        var registry = Substitute.For<ICanonicalSymbolRegistry>();
        registry.RegisterBatchAsync(Arg.Any<IEnumerable<CanonicalSymbolDefinition>>(), Arg.Any<CancellationToken>()).Returns(1);
        var seed = new SecurityMasterCanonicalSymbolSeedService(cache, registry,
            NullLogger<SecurityMasterCanonicalSymbolSeedService>.Instance, store,
            new SecurityMasterOptions { PreloadProjectionCache = false });

        await seed.SeedAsync();

        seed.TryGetSecurityId("MISSED").Should().Be(securityId);
        cache.Snapshot().Should().BeEmpty();
        await store.Received(1).LoadAllAsync(Arg.Any<CancellationToken>());
    }

}
