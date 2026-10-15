using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Catalog;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Meridian.Storage.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Unit")]
public sealed class SecurityMasterCanonicalSymbolSeedReconciliationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sm-seed-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task FirstSeedAfterRestart_WithdrawsDeletedSecurityMasterCanonical()
    {
        var id = Guid.NewGuid();
        var originalRegistry = new CanonicalSymbolRegistry(new SymbolRegistryService(_directory));
        await originalRegistry.ReconcileSecurityMasterAsync([id], [new CanonicalSymbolDefinition
        {
            Canonical = "DELETEDSM", SecurityId = id
        }]);
        var retainedStore = new SymbolRegistryService(_directory);
        await retainedStore.InitializeAsync();
        var restartedRegistry = new CanonicalSymbolRegistry(retainedStore);
        restartedRegistry.ResolveToCanonical("DELETEDSM").Should().Be("DELETEDSM");
        var seed = CreateSeed(new SecurityMasterProjectionCache(), restartedRegistry, authoritative: true);

        await seed.SeedAsync();

        restartedRegistry.ResolveToCanonical("DELETEDSM").Should().BeNull();
        seed.TryGetSecurityId("DELETEDSM").Should().BeNull();
        var reloadedStore = new SymbolRegistryService(_directory);
        await reloadedStore.InitializeAsync();
        new CanonicalSymbolRegistry(reloadedStore).ResolveToCanonical("DELETEDSM").Should().BeNull();
    }

    [Fact]
    public async Task DeletedSecurity_WithdrawsProvenMappingsAndPreservesIndependentAuthority()
    {
        var id = Guid.NewGuid();
        var unknownId = Guid.NewGuid();
        var registry = new CanonicalSymbolRegistry(new SymbolRegistryService(_directory));
        await registry.RegisterAsync(new CanonicalSymbolDefinition
        {
            Canonical = "MANUAL",
            SecurityId = id,
            AliasDefinitions = [new("DELETEDALIAS", SymbolMappingSources.SecurityMaster)]
        });
        await registry.SetProviderSymbolAsync("MANUAL", "polygon", "DELETEDPROVIDER", SymbolMappingSources.SecurityMaster, false);
        await registry.SetProviderSymbolAsync("MANUAL", "ib", "OPERATOR", SymbolMappingSources.Operator, true);
        await registry.RegisterAsync(new CanonicalSymbolDefinition
        {
            Canonical = "UNKNOWN",
            SecurityId = unknownId,
            Aliases = ["KEEPUNKNOWN"]
        });
        var seed = CreateSeed(new SecurityMasterProjectionCache(), registry, authoritative: true);

        await seed.SeedAsync();

        registry.ResolveToCanonical("DELETEDALIAS").Should().BeNull();
        registry.GetProviderSymbol("MANUAL", "polygon").Should().BeNull();
        registry.ResolveToCanonical("MANUAL").Should().Be("MANUAL");
        registry.GetProviderSymbol("MANUAL", "ib").Should().Be("OPERATOR");
        registry.ResolveToCanonical("KEEPUNKNOWN").Should().Be("UNKNOWN");
    }

    [Fact]
    public async Task DuplicateCanonicalTicker_RefusesPublicationAndRetainsPreviousReverseLookup()
    {
        var id = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var cache = new SecurityMasterProjectionCache();
        cache.Upsert(Projection(id, "GOOD"));
        var registry = Substitute.For<ICanonicalSymbolRegistry>();
        var seed = CreateSeed(cache, registry);
        await seed.SeedAsync();
        registry.ClearReceivedCalls();
        cache.Upsert(Projection(id, " DUPLICATE ", 2));
        cache.Upsert(Projection(otherId, "duplicate"));

        await seed.Invoking(service => service.SeedAsync()).Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*multiple active Security Master IDs*");

        await registry.DidNotReceive().RegisterBatchAsync(Arg.Any<IEnumerable<CanonicalSymbolDefinition>>(), Arg.Any<CancellationToken>());
        seed.TryGetSecurityId("GOOD").Should().Be(id);
        seed.TryGetSecurityId("DUPLICATE").Should().BeNull();
    }

    [Fact]
    public async Task PrimaryTickerWhitespace_MatchesCanonicalRegistryNormalization()
    {
        var id = Guid.NewGuid();
        var cache = new SecurityMasterProjectionCache();
        cache.Upsert(Projection(id, " NORMALIZED "));
        var registry = new CanonicalSymbolRegistry(new SymbolRegistryService(_directory));
        var seed = CreateSeed(cache, registry);

        await seed.SeedAsync();

        registry.ResolveToCanonical("NORMALIZED").Should().Be("NORMALIZED");
        seed.TryGetSecurityId("NORMALIZED").Should().Be(id);
        seed.TryGetSecurityId(" normalized ").Should().Be(id);
    }

    [Fact]
    public async Task StandardIdentifiers_OnlyCurrentWindowsResolveAndHistoryKeepsItsDates()
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var cache = new SecurityMasterProjectionCache();
        cache.Upsert(Projection(id, "WINDOWTEST") with
        {
            Identifiers =
            [
                new(SecurityIdentifierKind.Isin, "US0000000001", false, now.AddDays(-10), now.AddDays(-1)),
                new(SecurityIdentifierKind.Isin, "US0000000002", false, now.AddDays(-1)),
                new(SecurityIdentifierKind.Isin, "US0000000003", false, now.AddDays(1)),
                new(SecurityIdentifierKind.Cusip, "123456789", false, now.AddDays(-10), now.AddDays(-1)),
                new(SecurityIdentifierKind.Figi, "BBG000FUTURE", false, now.AddDays(1)),
                new(SecurityIdentifierKind.Sedol, "1234567", false, now.AddDays(-1))
            ]
        });
        var registry = new CanonicalSymbolRegistry(new SymbolRegistryService(_directory));
        var seed = CreateSeed(cache, registry);

        await seed.SeedAsync();

        registry.ResolveToCanonical("US0000000001").Should().BeNull();
        registry.ResolveToCanonical("US0000000002").Should().Be("WINDOWTEST");
        registry.ResolveToCanonical("US0000000003").Should().BeNull();
        registry.ResolveToCanonical("123456789").Should().BeNull();
        registry.ResolveToCanonical("BBG000FUTURE").Should().BeNull();
        registry.ResolveToCanonical("1234567").Should().Be("WINDOWTEST");
        var definition = registry.GetDefinition("WINDOWTEST")!;
        definition.Isin.Should().Be("US0000000002");
        definition.Cusip.Should().BeNull();
        definition.Figi.Should().BeNull();
        definition.AliasDefinitions.Should().Contain(alias => alias.Alias == "US0000000001"
            && !alias.IsActive && alias.ValidFrom == now.AddDays(-10) && alias.ValidTo == now.AddDays(-1));
        definition.AliasDefinitions.Should().Contain(alias => alias.Alias == "US0000000003"
            && !alias.IsActive && alias.ValidFrom == now.AddDays(1));
    }

    [Fact]
    public async Task RepeatedFutureIdentifierSeed_PreservesMatchingIndependentTypedField()
    {
        var id = Guid.NewGuid();
        const string isin = "US0000000004";
        var registry = new CanonicalSymbolRegistry(new SymbolRegistryService(_directory));
        await registry.RegisterAsync(new CanonicalSymbolDefinition
        {
            Canonical = "INDEPENDENT",
            SecurityId = id,
            Isin = isin
        });
        var cache = new SecurityMasterProjectionCache();
        cache.Upsert(Projection(id, "INDEPENDENT") with
        {
            Identifiers = [new(SecurityIdentifierKind.Isin, isin, false, DateTimeOffset.UtcNow.AddDays(1))]
        });
        var seed = CreateSeed(cache, registry);

        await seed.SeedAsync();
        await seed.SeedAsync();

        registry.GetDefinition("INDEPENDENT")!.Isin.Should().Be(isin);
        registry.ResolveToCanonical(isin).Should().Be("INDEPENDENT");
        registry.GetDefinition("INDEPENDENT")!.AliasDefinitions.Should().Contain(alias => alias.Alias == isin
            && !alias.IsActive && alias.Source == SymbolMappingSources.SecurityMaster);
    }

    [Fact]
    public async Task ExpiredPreviouslySeededIdentifier_WithdrawsItsOwnedTypedField()
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        const string isin = "US0000000005";
        var cache = new SecurityMasterProjectionCache();
        cache.Upsert(Projection(id, "EXPIREDOWNED") with
        {
            Identifiers = [new(SecurityIdentifierKind.Isin, isin, false, now.AddDays(-10))]
        });
        var registry = new CanonicalSymbolRegistry(new SymbolRegistryService(_directory));
        var seed = CreateSeed(cache, registry);
        await seed.SeedAsync();
        registry.GetDefinition("EXPIREDOWNED")!.Isin.Should().Be(isin);
        cache.Upsert(Projection(id, "EXPIREDOWNED", 2) with
        {
            Identifiers = [new(SecurityIdentifierKind.Isin, isin, false, now.AddDays(-10), now.AddDays(-1))]
        });

        await seed.SeedAsync();
        await seed.SeedAsync();

        registry.GetDefinition("EXPIREDOWNED")!.Isin.Should().BeNull();
        registry.ResolveToCanonical(isin).Should().BeNull();
        registry.GetDefinition("EXPIREDOWNED")!.AliasDefinitions.Should().Contain(alias => alias.Alias == isin
            && !alias.IsActive && alias.Source == SymbolMappingSources.SecurityMaster);
    }

    [Fact]
    public async Task PreloadedCacheWithIncompleteMembership_SeedsFromTheDurableStore()
    {
        var durableId = Guid.NewGuid();
        var localId = Guid.NewGuid();
        var registry = new CanonicalSymbolRegistry(new SymbolRegistryService(_directory));
        await registry.ReconcileSecurityMasterAsync([durableId],
            [new CanonicalSymbolDefinition { Canonical = "PREEXISTING", SecurityId = durableId }]);
        var cache = new SecurityMasterProjectionCache();
        cache.Upsert(Projection(localId, "LOCAL"));
        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(new[]
        {
            Projection(durableId, "PREEXISTING"), Projection(localId, "LOCAL")
        });
        var seed = new SecurityMasterCanonicalSymbolSeedService(cache, registry,
            NullLogger<SecurityMasterCanonicalSymbolSeedService>.Instance, store,
            new SecurityMasterOptions { PreloadProjectionCache = true });

        await seed.SeedAsync();

        seed.TryGetSecurityId("PREEXISTING").Should().Be(durableId);
        seed.TryGetSecurityId("LOCAL").Should().Be(localId);
        registry.ResolveToCanonical("PREEXISTING").Should().Be("PREEXISTING");
        cache.Snapshot().Should().ContainSingle().Which.SecurityId.Should().Be(localId);
    }

    private static SecurityProjectionRecord Projection(Guid id, string ticker, long version = 1)
        => SecurityMasterProjectionChangeNotificationTests.CreateProjection(id, ticker, version)
            with
        { PrimaryIdentifierValue = ticker };

    private static SecurityMasterCanonicalSymbolSeedService CreateSeed(SecurityMasterProjectionCache cache,
        ICanonicalSymbolRegistry registry, bool authoritative = false)
    {
        ISecurityMasterStore? store = null;
        if (authoritative)
        {
            store = Substitute.For<ISecurityMasterStore>();
            store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<SecurityProjectionRecord>());
        }
        return new(cache, registry, NullLogger<SecurityMasterCanonicalSymbolSeedService>.Instance, store);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
