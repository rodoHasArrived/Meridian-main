using System.Net;
using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Features;
using Meridian.Application.Services;
using Meridian.Application.ProviderRouting;
using Meridian.Application.Subscriptions.Services;
using Meridian.Core.Subscriptions.Models;
using Meridian.Application.UI;
using Meridian.Contracts.Catalog;
using Meridian.Contracts.SecurityMaster;
using Meridian.Core.Config;
using Meridian.DataIntegration.Credentials;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Core.SymbolResolution;
using Meridian.Infrastructure.Adapters.OpenFigi;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Infrastructure.Http;
using Meridian.Tests.Ui;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Meridian.Tests.Infrastructure.Providers;

[Collection(AlpacaCredentialEnvironmentCollection.Name)]
public sealed class HostedProviderCredentialSelectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hosted-provider-credentials", Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, string?> _environment = new();

    public HostedProviderCredentialSelectionTests()
    {
        foreach (var name in new[] { "OPENFIGI_API_KEY", "POLYGON_API_KEY", "MERIDIAN_POLYGON_API_KEY" })
        {
            _environment[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, "environment-other-owner");
        }
    }

    private static readonly ProviderCredentialScope Selected = new("tenant-a", "selected", "account-a", "production");
    private static readonly ProviderCredentialScope Other = new("tenant-b", "other", "account-b", "production");

    [Fact]
    public async Task HostedOpenFigi_BothClientsRereadSelectedRecordAndNeverFallbackAfterDeletion()
    {
        Directory.CreateDirectory(_root);
        var store = await SetupStoreAsync("openfigi");
        var configStore = new ConfigStore(Path.Combine(_root, "appsettings.json"));
        await configStore.SaveAsync(new AppConfig(DataRoot: _root,
            Backfill: new BackfillConfig(Providers: new BackfillProvidersConfig(OpenFigi: new OpenFigiConfig(ApiKey: "config-other-owner")))));
        var factory = new RecordingFactory("[]");
        var services = new ServiceCollection();
        services.AddSingleton(configStore);
        services.AddSingleton<IProviderCredentialResolver>(new StoredProviderCredentialResolver(store, new EnvironmentCredentialResolver(), Selected));
        services.AddSingleton<IHttpClientFactory>(factory);
        new SymbolManagementFeatureRegistration().Register(services, CompositionOptions.Minimal);
        services.AddSingleton(Mock.Of<ICanonicalSymbolRegistry>());
        using var provider = services.BuildServiceProvider();
        HttpClientFactoryProvider.Initialize(provider);
        var client = provider.GetRequiredService<OpenFigiClient>();
        var resolver = provider.GetRequiredService<ISymbolResolver>();

        await client.LookupByTickerAsync("AAPL");
        await resolver.ResolveAsync("AAPL");
        await store.SaveScopedAsync(Credential("openfigi", "rotated"), Selected);
        await client.LookupByTickerAsync("MSFT");
        await resolver.ResolveAsync("MSFT");
        factory.Keys.Should().Equal("selected", "selected", "rotated", "rotated");
        factory.RetryOwnership.Should().OnlyContain(owned => owned);

        await store.DeleteScopedAsync("openfigi", Selected);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.LookupByTickerAsync("TSLA"));
        (await resolver.ResolveAsync("TSLA")).Should().BeNull();
        factory.Keys.Should().HaveCount(4, "neither legacy, configured, nor another owner's key can replace a deleted selected credential");
    }

    [Fact]
    public async Task HostedSymbolSearch_UsesOnlyFactorySelectedOwnerAndCannotReconstructFromAmbientKeysAfterRemoval()
    {
        Directory.CreateDirectory(_root);
        var config = new ConfigStore(Path.Combine(_root, "appsettings.json"));
        await config.SaveAsync(new AppConfig(DataRoot: _root, Alpaca: new AlpacaOptions(
            KeyId: "config-other-owner", SecretKey: "config-other-secret")));
        var first = new ProviderCredentialScope("tenant-a", "selected", "account-a", "paper");
        var other = new ProviderCredentialScope("tenant-b", "other", "account-b", "paper");
        var store = new FileProviderCredentialStore(_root, name => name switch
        {
            "MDC_PROVIDER_ALLOW_ENV_FALLBACK" => "true",
            "ALPACA_KEY_ID" => "environment-other-owner",
            "ALPACA_SECRET_KEY" => "environment-other-secret",
            _ => null
        });
        foreach (var scope in new[] { first, other })
        {
            await new ProviderConnectionService(config).UpsertForTenantAsync(new(scope.ConnectionId, "alpaca", "Synthetic owner",
                ExternalAccountId: scope.ExternalAccountId), scope.TenantId, scope.Environment);
            await store.SaveScopedAsync(new("alpaca", new Dictionary<string, string?>
            {
                ["KeyId"] = scope.ExternalAccountId,
                ["SecretKey"] = scope.ExternalAccountId + "-secret"
            }, scope.Environment), scope);
        }
        await store.SaveAsync(new("alpaca", new Dictionary<string, string?>
        {
            ["KeyId"] = "legacy-other-owner",
            ["SecretKey"] = "legacy-other-secret"
        }, "paper"));
        await new ProviderRuntimeCredentialService(config, store).BindAsync("alpaca", first.ConnectionId, first.TenantId);
        var resolver = new StoredProviderCredentialResolver(store, new EnvironmentCredentialResolver(), config, new(true));
        var factory = new ProviderFactory(config.LoadRequired(), resolver);
        var httpFactory = new RecordingFactory("[]");
        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddSingleton<IProviderCredentialResolver>(resolver);
        services.AddSingleton<IHttpClientFactory>(httpFactory);
        var registry = new ProviderRegistry();
        services.AddSingleton(registry);
        new SymbolManagementFeatureRegistration().Register(services, CompositionOptions.Minimal);
        using var provider = services.BuildServiceProvider();
        HttpClientFactoryProvider.Initialize(provider);
        var search = factory.CreateAlpacaSearchProvider(null);
        search.Should().NotBeNull();
        registry.RegisterSymbolSearch("alpaca", search!);
        await provider.GetRequiredService<SymbolSearchService>().SearchAsync(new("BOND", Provider: "alpaca", IncludeFigi: false));
        httpFactory.Keys.Should().NotBeEmpty().And.OnlyContain(key => key == "account-a");
        var dispatched = httpFactory.Keys.Count;

        // Model stopped-instance reconstruction after vault maintenance. Missing selected data is
        // authoritative despite retained legacy, config, environment and the other tenant's keys.
        await store.DeleteScopedAsync("alpaca", first);
        factory.CreateAlpacaSearchProvider(null).Should().BeNull();
        await new ProviderConnectionService(config).DeleteForTenantAsync(first.ConnectionId, first.TenantId);
        Action reconstruct = () => factory.CreateAlpacaSearchProvider(null);
        reconstruct.Should().Throw<UnauthorizedAccessException>();
        httpFactory.Keys.Should().HaveCount(dispatched);
        (await store.ReadScopedAsync("alpaca", other))!.Get("KeyId").Should().Be("account-b");
    }

    [Fact]
    public async Task Polygon_RechecksSelectedCredentialForEveryPageAndEndpoint()
    {
        var store = await SetupStoreAsync("polygon");
        var factory = new RecordingFactory("{\"results\":[]}")
        {
            FirstJson = "{\"results\":[],\"next_url\":\"/v3/reference/dividends?cursor=second\"}",
            AfterSend = count => count == 1 ? store.SaveScopedAsync(Credential("polygon", "rotated"), Selected) : Task.CompletedTask
        };
        var services = PolygonServices(factory, store, new RateLimiter(1000, TimeSpan.FromMinutes(1)));
        using var provider = services.BuildServiceProvider();
        var fetcher = ActivatorUtilities.CreateInstance<PolygonCorporateActionFetcher>(provider);
        await fetcher.FetchAndPersistAsync("AAPL", Guid.NewGuid(), CancellationToken.None);
        factory.Keys.Should().Equal("selected", "rotated", "rotated");
        factory.RetryOwnership.Should().OnlyContain(owned => owned);

        await store.DeleteScopedAsync("polygon", Selected);
        var failure = await Assert.ThrowsAsync<PolygonCorporateActionFetchException>(
            () => fetcher.FetchAndPersistAsync("AAPL", Guid.NewGuid(), CancellationToken.None));
        failure.FailureKind.Should().Be(PolygonCorporateActionFailureKind.NotConfigured);
        factory.Keys.Should().HaveCount(3);
    }

    [Fact]
    public async Task Polygon_DeletionWhileWaitingForQuotaStopsTheQueuedSend()
    {
        var store = await SetupStoreAsync("polygon");
        var clock = new RequestBudgetTimeProvider();
        using var limiter = new RateLimiter(1000, TimeSpan.FromMinutes(1), timeProvider: clock);
        limiter.ApplyCooldown(TimeSpan.FromMinutes(1));
        var factory = new RecordingFactory("{\"results\":[]}");
        using var provider = PolygonServices(factory, store, limiter).BuildServiceProvider();
        var fetcher = ActivatorUtilities.CreateInstance<PolygonCorporateActionFetcher>(provider);
        var pending = fetcher.FetchAndPersistAsync("AAPL", Guid.NewGuid(), CancellationToken.None);
        pending.IsCompleted.Should().BeFalse();
        await store.DeleteScopedAsync("polygon", Selected);
        clock.Advance(TimeSpan.FromMinutes(1));

        var failure = await Assert.ThrowsAsync<PolygonCorporateActionFetchException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        failure.FailureKind.Should().Be(PolygonCorporateActionFailureKind.NotConfigured);
        factory.Keys.Should().BeEmpty();
    }

    private ServiceCollection PolygonServices(RecordingFactory factory, FileProviderCredentialStore store, RateLimiter limiter)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHttpClientFactory>(factory);
        services.AddSingleton(Mock.Of<ISecurityMasterQueryService>());
        services.AddSingleton(Mock.Of<ISecurityMasterCorporateActionCommandService>());
        services.AddSingleton(limiter);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Polygon:ApiKey"] = "config-other-owner" }).Build());
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<PolygonCorporateActionFetcher>>(NullLogger<PolygonCorporateActionFetcher>.Instance);
        services.AddSingleton<IProviderCredentialResolver>(new StoredProviderCredentialResolver(store, new EnvironmentCredentialResolver(), Selected));
        return services;
    }

    private async Task<FileProviderCredentialStore> SetupStoreAsync(string provider)
    {
        var store = new FileProviderCredentialStore(_root);
        await store.SaveAsync(Credential(provider, "legacy-other-owner"));
        await store.SaveScopedAsync(Credential(provider, "another-owner"), Other);
        await store.SaveScopedAsync(Credential(provider, "selected"), Selected);
        return store;
    }

    private static ProviderCredentialSaveRequest Credential(string provider, string key)
        => new(provider, new Dictionary<string, string?> { ["ApiKey"] = key }, "production");

    private sealed class RecordingFactory(string json) : IHttpClientFactory
    {
        public List<string?> Keys { get; } = [];
        public List<bool> RetryOwnership { get; } = [];
        public string? FirstJson { get; init; }
        public Func<int, Task>? AfterSend { get; init; }
        public HttpClient CreateClient(string name) => new(new Handler(this, json));

        private sealed class Handler(RecordingFactory factory, string json) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                var key = request.Headers.TryGetValues("X-OPENFIGI-APIKEY", out var values) ? values.Single()
                    : request.Headers.TryGetValues("APCA-API-KEY-ID", out var alpacaKeys) ? alpacaKeys.Single()
                    : Uri.UnescapeDataString(request.RequestUri!.Query.Split('&').Single(part => part.TrimStart('?').StartsWith("apiKey=", StringComparison.Ordinal)).Split('=')[1]);
                factory.Keys.Add(key);
                factory.RetryOwnership.Add(request.Options.TryGetValue(SharedResiliencePolicies.ProviderOwnsRetries, out var owned) && owned);
                var count = factory.Keys.Count;
                if (factory.AfterSend is not null)
                    await factory.AfterSend(count);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(count == 1 ? factory.FirstJson ?? json : json)
                };
            }
        }
    }

    public void Dispose()
    {
        HttpClientFactoryProvider.Reset();
        foreach (var (name, value) in _environment)
            Environment.SetEnvironmentVariable(name, value);
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
