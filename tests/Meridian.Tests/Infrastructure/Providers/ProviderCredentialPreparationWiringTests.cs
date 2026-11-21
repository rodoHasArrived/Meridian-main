using FluentAssertions;
using Meridian.Core.Config;
using Meridian.Infrastructure;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// The asynchronous provider entry points must read credentials first and run the synchronous
/// construction inside that prepared scope, then leave it.
/// </summary>
public sealed class ProviderCredentialPreparationWiringTests
{
    /// <summary>Records when a prepared scope is active.</summary>
    private sealed class RecordingPreparation : IPreparedProviderCredentials
    {
        public int Prepared { get; private set; }

        public bool IsActive { get; private set; }

        public ValueTask<IPreparedProviderCredentials> PrepareAsync(CancellationToken ct)
        {
            Prepared++;
            return ValueTask.FromResult<IPreparedProviderCredentials>(this);
        }

        public IDisposable Activate()
        {
            IsActive = true;
            return new Scope(this);
        }

        private sealed class Scope(RecordingPreparation owner) : IDisposable
        {
            public void Dispose() => owner.IsActive = false;
        }
    }

    private sealed class RecordingResolver(RecordingPreparation preparation) : IProviderCredentialResolver
    {
        public List<bool> ActiveAtConstruction { get; } = [];

        public ICredentialContext CreateContext(Type providerType, IReadOnlyDictionary<string, string?>? configuredValues = null)
        {
            ActiveAtConstruction.Add(preparation.IsActive);
            return new EnvironmentCredentialResolver().CreateContext(providerType, configuredValues);
        }

        public ValueTask<IPreparedProviderCredentials> PrepareAsync(
            IReadOnlyCollection<Type>? providerTypes = null,
            CancellationToken ct = default)
            => preparation.PrepareAsync(ct);
    }

    [Fact]
    public async Task CreateStreamingClientAsync_ConstructsInsideThePreparedScopeAndLeavesIt()
    {
        var preparation = new RecordingPreparation();
        using var registry = new ProviderRegistry { CredentialPreparer = preparation.PrepareAsync };
        var client = Mock.Of<IMarketDataClient>();
        bool? activeAtConstruction = null;
        registry.RegisterStreamingFactory("synthetic", () =>
        {
            activeAtConstruction = preparation.IsActive;
            return client;
        });

        var created = await registry.CreateStreamingClientAsync("synthetic");

        created.Should().BeSameAs(client);
        preparation.Prepared.Should().Be(1);
        activeAtConstruction.Should().BeTrue("the client must be constructed with its credentials already read");
        preparation.IsActive.Should().BeFalse("the prepared read must not outlive the construction");
    }

    [Fact]
    public async Task CreateStreamingClientAsync_WithoutAPreparer_StillCreatesTheClient()
    {
        using var registry = new ProviderRegistry();
        var client = Mock.Of<IMarketDataClient>();
        registry.RegisterStreamingFactory("synthetic", () => client);

        (await registry.CreateStreamingClientAsync("synthetic")).Should().BeSameAs(client);
    }

    [Fact]
    public async Task CreateStreamingClientAsync_UnknownProvider_FailsClosedAndLeavesTheScope()
    {
        var preparation = new RecordingPreparation();
        using var registry = new ProviderRegistry { CredentialPreparer = preparation.PrepareAsync };

        var act = async () => await registry.CreateStreamingClientAsync("not-registered");

        await act.Should().ThrowAsync<InvalidOperationException>();
        preparation.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task ProviderFactory_AsyncCreation_PreparesOnceAndConstructsInsideTheScope()
    {
        var preparation = new RecordingPreparation();
        var resolver = new RecordingResolver(preparation);
        var factory = new ProviderFactory(new AppConfig(), resolver);

        await factory.CreateBackfillProvidersAsync();

        preparation.Prepared.Should().Be(1);
        resolver.ActiveAtConstruction.Should().NotBeEmpty("default configuration builds credential-gated providers");
        resolver.ActiveAtConstruction.Should().OnlyContain(active => active,
            "every credential context must be created inside the prepared scope");
        preparation.IsActive.Should().BeFalse();

        resolver.ActiveAtConstruction.Clear();
        await factory.CreateSymbolSearchProvidersAsync();
        preparation.Prepared.Should().Be(2);
        resolver.ActiveAtConstruction.Should().OnlyContain(active => active);
    }

    [Fact]
    public async Task ProviderFactory_CreateAndRegisterAllAsync_PreparesBeforeRegistering()
    {
        var preparation = new RecordingPreparation();
        var resolver = new RecordingResolver(preparation);
        var factory = new ProviderFactory(new AppConfig(), resolver);
        using var registry = new ProviderRegistry();

        var result = await factory.CreateAndRegisterAllAsync(registry);

        preparation.Prepared.Should().Be(1);
        resolver.ActiveAtConstruction.Should().OnlyContain(active => active);
        result.BackfillProviders.Should().BeEquivalentTo(
            registry.GetProviders<IHistoricalDataProvider>().Select(provider => ProviderIdentity.NormalizeId(provider.ProviderId)));
        preparation.IsActive.Should().BeFalse();
    }

    [Fact]
    public void ResolveRegisteredCapabilities_BuildsHeldCapabilitiesButNeverStreamingClients()
    {
        using var registry = new ProviderRegistry();
        var built = new List<string>();
        registry.RegisterStreamingFactory("synthetic", () =>
        {
            built.Add("streaming");
            return Mock.Of<IMarketDataClient>();
        });
        registry.RegisterCapabilityFactory("alpaca", typeof(IDisposable), () =>
        {
            built.Add("held");
            return Mock.Of<IDisposable>();
        });
        registry.RegisterCapabilityFactory("polygon", typeof(IDisposable),
            () => throw new InvalidOperationException("not configured"));

        var failures = registry.ResolveRegisteredCapabilities();

        built.Should().Equal("held");
        failures.Should().ContainSingle().Which.ProviderId.Should().Be("polygon");
    }

    [Fact]
    public async Task WarmProvidersAsync_BuildsTheRegistryAndItsHeldCapabilitiesInsideOnePreparedScope()
    {
        var preparation = new RecordingPreparation();
        var resolver = new RecordingResolver(preparation);
        bool? registryBuiltInScope = null;
        bool? capabilityBuiltInScope = null;
        var services = new ServiceCollection();
        services.AddSingleton(new ProviderFactory(new AppConfig(), resolver));
        services.AddSingleton(_ =>
        {
            registryBuiltInScope = preparation.IsActive;
            var registry = new ProviderRegistry();
            registry.RegisterCapabilityFactory("alpaca", typeof(IDisposable), () =>
            {
                capabilityBuiltInScope = preparation.IsActive;
                return Mock.Of<IDisposable>();
            });
            registry.RegisterCapabilityFactory("polygon", typeof(IDisposable),
                () => throw new InvalidOperationException("not configured"));
            return registry;
        });
        await using var provider = services.BuildServiceProvider();

        await provider.WarmProvidersAsync();

        preparation.Prepared.Should().Be(1, "the registry and every capability share one read");
        registryBuiltInScope.Should().BeTrue();
        capabilityBuiltInScope.Should().BeTrue();
        preparation.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task WarmProvidersAsync_NeverFailsTheHost()
    {
        // No provider services at all.
        await using (var empty = new ServiceCollection().BuildServiceProvider())
            await empty.WarmProvidersAsync();

        // A registry that cannot be built.
        var services = new ServiceCollection();
        services.AddSingleton(new ProviderFactory(new AppConfig(), new EnvironmentCredentialResolver()));
        services.AddSingleton<ProviderRegistry>(_ => throw new InvalidOperationException("composition failed"));
        await using var provider = services.BuildServiceProvider();

        var act = async () => await provider.WarmProvidersAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task WarmProvidersAsync_HonoursCancellation()
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await provider.WarmProvidersAsync(ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RoutingFamily_PrefersTheAsynchronousResolver()
    {
        var family = new Meridian.Application.ProviderRouting.MutableProviderFamilyAdapter("synthetic", "Synthetic", "test");
        var kind = Meridian.ProviderSdk.ProviderCapabilityKind.RealtimeMarketData;
        var descriptor = new Meridian.ProviderSdk.ProviderCapabilityDescriptor(kind, "streaming");
        var syncCalls = 0;
        family.RegisterCapability(descriptor,
            resolver: () => { syncCalls++; return "sync"; },
            asyncResolver: _ => ValueTask.FromResult<object?>("async"));
        // A later registration of the same kind supplies neither resolver.
        family.RegisterCapability(descriptor, resolver: () => "late",
            asyncResolver: _ => ValueTask.FromResult<object?>("late-async"));

        (await family.ResolveCapabilityAsync(kind)).Should().Be("async");
        syncCalls.Should().Be(0);
    }

    [Fact]
    public async Task BackfillCoordinator_PreviewAsync_ConstructsFactoryProvidersInsideAPreparedScope()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-coordinator-prepare", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new Meridian.Application.UI.ConfigStore(Path.Combine(root, "appsettings.json"));
            await store.SaveAsync(new AppConfig(DataRoot: Path.Combine(root, "data")));
            var preparation = new RecordingPreparation();
            var resolver = new RecordingResolver(preparation);
            using var registry = new ProviderRegistry();
            using var coordinator = new Meridian.Application.Backfill.BackfillCoordinator(
                store, registry, new ProviderFactory(new AppConfig(), resolver));

            await coordinator.PreviewAsync(new Meridian.Application.Backfill.BackfillRequest(
                "yahoo", ["SPY"], new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 2)));

            preparation.Prepared.Should().Be(1);
            resolver.ActiveAtConstruction.Should().NotBeEmpty();
            resolver.ActiveAtConstruction.Should().OnlyContain(active => active,
                "the empty registry makes the factory construct the providers, which must not read storage");
            preparation.IsActive.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
