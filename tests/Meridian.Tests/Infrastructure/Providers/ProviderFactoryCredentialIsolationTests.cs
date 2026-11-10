using System.Collections.Concurrent;
using System.Net;
using FluentAssertions;
using Meridian.Application.Services;
using Meridian.Core.Config;
using Meridian.Core.IO;
using Meridian.DataIntegration.Credentials;
using Meridian.Domain.Collectors;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Tests.TestHelpers;
using Meridian.Tests.Ui;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

[Collection(AlpacaCredentialEnvironmentCollection.Name)]
public sealed class ProviderFactoryCredentialIsolationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-factory-isolation", Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, string?> _environment = new(StringComparer.Ordinal);

    [Theory]
    [InlineData("tenant")]
    [InlineData("connection")]
    [InlineData("account")]
    [InlineData("environment")]
    public async Task ScopedConstruction_RotationAndDeletionNeverSelectAnotherOwnersCredentials(string dimension)
    {
        SetEnvironment("ALPACA_KEY_ID", "environment-owner-key");
        SetEnvironment("ALPACA_SECRET_KEY", "environment-owner-secret");
        SetEnvironment("APCA_API_KEY_ID", "alias-owner-key");
        SetEnvironment("APCA_API_SECRET_KEY", "alias-owner-secret");
        SetEnvironment("ALPACA_TRADING_ENVIRONMENT", "live");
        var first = new ProviderCredentialScope("tenant-a", "connection-a", "account-a", "paper");
        var second = new ProviderCredentialScope(
            dimension == "tenant" ? "tenant-b" : first.TenantId,
            dimension == "connection" ? "connection-b" : first.ConnectionId,
            dimension == "account" ? "account-b" : first.ExternalAccountId,
            dimension == "environment" ? "live" : first.Environment);
        var store = new FileProviderCredentialStore(_root);
        await store.SaveAsync(Request("global", "global-secret", "live"));
        await store.SaveScopedAsync(Request("first", "first-secret", first.Environment), first);
        await store.SaveScopedAsync(Request("second", "second-secret", second.Environment), second);
        using var handler = new CredentialCaptureHandler();
        using var services = CreateServices(handler);
        var factory = CreateFactory(services, new StoredProviderCredentialResolver(store, new EnvironmentCredentialResolver(), first));
        var other = CreateFactory(services, new StoredProviderCredentialResolver(store, new EnvironmentCredentialResolver(), second));

        await AssertConfiguredAsync(factory, handler, "first", "first-secret", "paper");
        await AssertConfiguredAsync(other, handler, "second", "second-secret", second.Environment);

        await store.SaveScopedAsync(Request("rotated", "", first.Environment), first);
        await AssertUnavailableAsync(factory, handler);
        await AssertConfiguredAsync(other, handler, "second", "second-secret", second.Environment);

        await store.SaveScopedAsync(Request("rotated", "rotated-secret", first.Environment), first);
        await AssertConfiguredAsync(factory, handler, "rotated", "rotated-secret", "paper");
        await store.DeleteScopedAsync("alpaca", first);
        await AssertUnavailableAsync(factory, handler);
        await AssertConfiguredAsync(other, handler, "second", "second-secret", second.Environment);
    }

    [Fact]
    public async Task StoredConstruction_PartialRecordAndDeletionCannotRefillFromEnvironmentConfigOrModule()
    {
        SetEnvironment("ALPACA_KEY_ID", "environment-key");
        SetEnvironment("ALPACA_SECRET_KEY", "environment-secret");
        var store = new FileProviderCredentialStore(_root);
        await store.SaveAsync(Request("stored", "", "paper"));
        using var handler = new CredentialCaptureHandler();
        using var services = CreateServices(handler);
        var factory = CreateFactory(services, new StoredProviderCredentialResolver(store, new EnvironmentCredentialResolver()));

        await AssertUnavailableAsync(factory, handler);
        await store.SaveAsync(Request("stored", "stored-secret", "paper"));
        await AssertConfiguredAsync(factory, handler, "stored", "stored-secret", "paper");
        await store.DeleteAsync("alpaca");
        await AssertUnavailableAsync(factory, handler);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicRegistration_SelectedSidecarRecordCannotFillMissingFieldsFromMappedEnvironment(bool emptyRecord)
    {
        SetEnvironment("FACTORY_MODULE_KEY", "mapped-key");
        SetEnvironment("FACTORY_MODULE_SECRET", "mapped-secret");
        var config = new AppConfig(ProviderModules: new ProviderModulesConfig(Modules: new()
        {
            ["alpaca"] = new(Credentials: new()
            {
                ["keyId"] = "FACTORY_MODULE_KEY",
                ["secretKey"] = "FACTORY_MODULE_SECRET"
            })
        }));
        var services = new ServiceCollection();
        services.AddProviderServices(config, new EnvironmentCredentialResolver(), sidecars:
            new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["alpaca"] = emptyRecord ? new Dictionary<string, string>() : new Dictionary<string, string> { ["keyId"] = "sidecar-key" }
            });
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<ProviderFactory>();

        factory.CreateAlpacaBackfillProvider(null).Should().BeNull();
        factory.CreateAlpacaSearchProvider(null).Should().BeNull();
    }

    [Theory]
    [InlineData(null, "live")]
    [InlineData("paper", "paper")]
    public async Task ModuleConstruction_PreservesExplicitOrLegacyTradingEnvironment(string? selectedEnvironment, string expectedEnvironment)
    {
        SetEnvironment("ALPACA_TRADING_ENVIRONMENT", "live");
        using var handler = new CredentialCaptureHandler();
        using var services = CreateServices(handler);
        var context = ProviderModuleContext.FromCredentials(new Dictionary<string, string?>
        {
            ["keyId"] = "module-key",
            ["secretKey"] = "module-secret"
        }) with
        { Settings = new Dictionary<string, string?> { ["environment"] = selectedEnvironment } };
        var factory = new ProviderFactory(new AppConfig(), new EnvironmentCredentialResolver(), services: services,
            moduleContexts: new Dictionary<string, ProviderModuleContext> { ["alpaca"] = context });

        await AssertConfiguredAsync(factory, handler, "module-key", "module-secret", expectedEnvironment);
    }

    [Theory]
    [InlineData("alphavantage", "ALPHA_VANTAGE_API_KEY")]
    [InlineData("finnhub", "FINNHUB_API_KEY")]
    [InlineData("tiingo", "TIINGO_API_TOKEN")]
    [InlineData("twelvedata", "TWELVEDATA_API_KEY")]
    [InlineData("nasdaq", "NASDAQ_DATA_LINK_API_KEY")]
    public async Task CorporateActionConstruction_DeletedRecordCannotResolveEnvironmentInsideAdapter(string providerId, string variable)
    {
        SetEnvironment(variable, "unselected-environment-credential");
        var store = new FileProviderCredentialStore(_root);
        await store.DeleteAsync(providerId);
        using var handler = new CredentialCaptureHandler();
        using var services = CreateServices(handler);
        var factory = CreateFactory(services, new StoredProviderCredentialResolver(store, new EnvironmentCredentialResolver()));
        var descriptor = ProviderCapabilityDescriptorCatalog.Descriptors.Single(candidate => candidate.ProviderId == providerId);
        var adapter = descriptor.CorporateActionsFactory!(factory)!;

        (await adapter.FetchAsync("AAPL", Guid.NewGuid())).Should().BeEmpty();

        // Nasdaq explicitly supports anonymous requests; the other providers require credentials.
        if (providerId == "nasdaq")
            handler.Requests.Should().ContainSingle().Which.Uri.Query.Should().NotContain("api_key");
        else
            handler.Requests.Should().BeEmpty();
    }

    private static async Task AssertConfiguredAsync(ProviderFactory factory, CredentialCaptureHandler handler,
        string key, string secret, string environment)
    {
        using var historical = factory.CreateAlpacaBackfillProvider(null) as AlpacaHistoricalDataProvider;
        historical.Should().NotBeNull();
        using var search = factory.CreateAlpacaSearchProvider(null) as AlpacaSymbolSearchProvider;
        search.Should().NotBeNull();
        await using var streaming = factory.CreateAlpacaStreamingClient();
        ((AlpacaOptionsChainProvider)factory.CreateAlpacaOptionsProvider()).IsCredentialsConfigured.Should().BeTrue();

        handler.Requests.Clear();
        var corporate = factory.CreateCorporateActionProvider<AlpacaCorporateActionProvider, AlpacaHistoricalDataProvider>();
        await corporate.FetchAsync("AAPL", Guid.NewGuid());
        await using var brokerage = factory.CreateAlpacaBrokerageGateway();
        await brokerage.GetAccountInfoAsync();
        handler.Requests.Should().HaveCount(3).And.OnlyContain(request => request.Key == key && request.Secret == secret);
        handler.Requests.Single(request => request.Uri.AbsolutePath == "/v2/account").Uri.Host.Should()
            .Be(environment == "live" ? "api.alpaca.markets" : "paper-api.alpaca.markets");
    }

    private static async Task AssertUnavailableAsync(ProviderFactory factory, CredentialCaptureHandler handler)
    {
        handler.Requests.Clear();
        factory.CreateAlpacaBackfillProvider(null).Should().BeNull();
        factory.CreateAlpacaSearchProvider(null).Should().BeNull();
        var constructStreaming = () => factory.CreateAlpacaStreamingClient();
        constructStreaming.Should().Throw<ArgumentException>();
        ((AlpacaOptionsChainProvider)factory.CreateAlpacaOptionsProvider()).IsCredentialsConfigured.Should().BeFalse();
        var corporate = factory.CreateCorporateActionProvider<AlpacaCorporateActionProvider, AlpacaHistoricalDataProvider>();
        (await corporate.FetchAsync("AAPL", Guid.NewGuid())).Should().BeEmpty();
        await using var brokerage = factory.CreateAlpacaBrokerageGateway();
        var query = () => brokerage.GetAccountInfoAsync();
        await query.Should().ThrowAsync<InvalidOperationException>();
        handler.Requests.Should().BeEmpty();
    }

    private static ProviderFactory CreateFactory(IServiceProvider services, IProviderCredentialResolver resolver)
        => new(new AppConfig(Alpaca: new AlpacaOptions("config-key", "config-secret")), resolver, services: services,
            moduleContexts: new Dictionary<string, ProviderModuleContext>
            {
                ["alpaca"] = ProviderModuleContext.FromCredentials(new Dictionary<string, string?>
                {
                    ["keyId"] = "module-key",
                    ["secretKey"] = "module-secret"
                })
            });

    private static ServiceProvider CreateServices(CredentialCaptureHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddEnvironmentVariables().Build());
        services.AddSingleton<IHttpClientFactory>(new CaptureClientFactory(handler));
        services.AddSingleton(Mock.Of<IAtomicFileWriter>());
        var publisher = new TestMarketEventPublisher();
        services.AddSingleton(new TradeDataCollector(publisher));
        services.AddSingleton(new QuoteCollector(publisher));
        return services.BuildServiceProvider();
    }

    private static ProviderCredentialSaveRequest Request(string key, string secret, string environment)
        => new("alpaca", new Dictionary<string, string?> { ["KeyId"] = key, ["SecretKey"] = secret }, environment);

    private void SetEnvironment(string name, string value)
    {
        _environment.TryAdd(name, Environment.GetEnvironmentVariable(name));
        Environment.SetEnvironmentVariable(name, value);
    }

    public void Dispose()
    {
        foreach (var (name, value) in _environment)
            Environment.SetEnvironmentVariable(name, value);
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class CaptureClientFactory(CredentialCaptureHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed record CapturedRequest(Uri Uri, string? Key, string? Secret);

    private sealed class CredentialCaptureHandler : HttpMessageHandler
    {
        public ConcurrentQueue<CapturedRequest> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Enqueue(new CapturedRequest(request.RequestUri!,
                request.Headers.TryGetValues("APCA-API-KEY-ID", out var key) ? key.Single() : null,
                request.Headers.TryGetValues("APCA-API-SECRET-KEY", out var secret) ? secret.Single() : null));
            var response = request.RequestUri!.AbsolutePath == "/v2/account"
                ? "{\"id\":\"account\",\"cash\":\"100\",\"equity\":\"100\",\"buying_power\":\"100\"}"
                : request.RequestUri.Host == "data.nasdaq.com" ? "{\"dataset\":{\"column_names\":[],\"data\":[]}}" : "[]";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response) });
        }
    }
}
