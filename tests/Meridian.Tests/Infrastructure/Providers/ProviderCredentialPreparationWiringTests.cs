using FluentAssertions;
using Meridian.Core.Config;
using Meridian.Infrastructure;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
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
}
