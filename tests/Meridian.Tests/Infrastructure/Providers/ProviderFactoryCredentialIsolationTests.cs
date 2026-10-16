using System.Net;
using FluentAssertions;
using Meridian.Application.Services;
using Meridian.Core.Config;
using Meridian.Core.IO;
using Meridian.DataIntegration.Credentials;
using Meridian.Domain.Collectors;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Storage.Archival;
using Meridian.Tests.TestHelpers;
using Meridian.Tests.Ui;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

[Collection(AlpacaCredentialEnvironmentCollection.Name)]
public sealed partial class ProviderFactoryCredentialIsolationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-factory-isolation", Guid.NewGuid().ToString("N"));
    private static readonly ProviderCredentialScope Owner = new("tenant-a", "connection-a", "account-a", "paper");

    [Theory]
    [InlineData(false, "complete")]
    [InlineData(false, "missing-key")]
    [InlineData(false, "missing-secret")]
    [InlineData(false, "cleared")]
    [InlineData(false, "deleted")]
    [InlineData(true, "complete")]
    [InlineData(true, "missing-key")]
    [InlineData(true, "missing-secret")]
    [InlineData(true, "cleared")]
    [InlineData(true, "deleted")]
    public async Task FactoryCapabilities_UseOnlyTheSelectedRecord(bool scoped, string state)
    {
        using var environment = AmbientCredentials.Alpaca();
        var store = new FileProviderCredentialStore(_root, _ => null);
        await SaveAsync("owned-key", "owned-secret");
        if (scoped)
        {
            await store.SaveAsync(Request("unassigned-key", "unassigned-secret"));
            await store.SaveScopedAsync(Request("foreign-key", "foreign-secret"), new ProviderCredentialScope("tenant-b", Owner.ConnectionId, Owner.ExternalAccountId, Owner.Environment));
        }

        if (state == "deleted")
        {
            if (scoped)
                await store.DeleteScopedAsync("alpaca", Owner);
            else
                await store.DeleteAsync("alpaca");
        }
        else if (state != "complete")
        {
            await SaveAsync(state is "missing-key" or "cleared" ? "" : "owned-key",
                state is "missing-secret" or "cleared" ? "" : "owned-secret");
        }

        IProviderCredentialResolver resolver = scoped
            ? new StoredProviderCredentialResolver(store, new EnvironmentCredentialResolver(), Owner)
            : new StoredProviderCredentialResolver(store, new EnvironmentCredentialResolver());
        using var handler = new RecordingHandler();
        using var services = CreateServices(handler);
        var factory = new ProviderFactory(Config(), resolver, services: services);
        var complete = state == "complete";
        var historical = factory.CreateAlpacaBackfillProvider(null);
        var search = factory.CreateAlpacaSearchProvider(null);
        (historical is not null).Should().Be(complete);
        (search is not null).Should().Be(complete);
        var options = (AlpacaOptionsChainProvider)factory.CreateAlpacaOptionsProvider();
        options.IsCredentialsConfigured.Should().Be(complete);
        if (complete)
        {
            await using var stream = factory.CreateAlpacaStreamingClient();
        }
        else
        {
            var construct = () => factory.CreateAlpacaStreamingClient();
            construct.Should().Throw<ArgumentException>();
        }

        var corporateActions = ProviderCapabilityDescriptorCatalog.Descriptors.Single(d => d.ProviderId == "alpaca")
            .CorporateActionsFactory!(factory)!;
        await corporateActions.FetchAsync("AAPL", Guid.NewGuid());
        await using var brokerage = (AlpacaBrokerageGateway)factory.CreateAlpacaBrokerageGateway();
        if (complete)
        {
            var account = await brokerage.GetAccountInfoAsync();
            account.AccountId.Should().Be("owned-account");
            handler.Requests.Select(request => request.Uri.PathAndQuery).Should().Equal(
                "/v2/corporate-actions/announcements?ca_types=dividend&symbol=AAPL",
                "/v2/corporate-actions/announcements?ca_types=split&symbol=AAPL",
                "/v2/account");
            handler.Requests.Should().OnlyContain(request => request.Key == "owned-key" && request.Secret == "owned-secret");
            handler.Requests.Last().Uri.Host.Should().Be("paper-api.alpaca.markets",
                "ambient trading environment cannot change the selected factory options");
        }
        else
        {
            var readAccount = () => brokerage.GetAccountInfoAsync();
            await readAccount.Should().ThrowAsync<InvalidOperationException>();
            handler.Requests.Should().BeEmpty("incomplete or deleted records must never authenticate using ambient credentials");
        }
        (historical as IDisposable)?.Dispose();
        (search as IDisposable)?.Dispose();

        Task SaveAsync(string key, string secret) => scoped
            ? store.SaveScopedAsync(Request(key, secret), Owner)
            : store.SaveAsync(Request(key, secret));
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("connection")]
    [InlineData("account")]
    [InlineData("environment")]
    public async Task ScopedFactory_RotationAndDeletionDoNotSelectAnotherOwner(string dimension)
    {
        using var environment = AmbientCredentials.Alpaca();
        var other = new ProviderCredentialScope(
            dimension == "tenant" ? "tenant-b" : Owner.TenantId,
            dimension == "connection" ? "connection-b" : Owner.ConnectionId,
            dimension == "account" ? "account-b" : Owner.ExternalAccountId,
            dimension == "environment" ? "live" : Owner.Environment);
        var store = new FileProviderCredentialStore(_root, _ => null);
        await store.SaveScopedAsync(Request("owned-key", "owned-secret"), Owner);
        await store.SaveScopedAsync(Request("foreign-key", "foreign-secret") with { Environment = other.Environment }, other);
        var ownerFactory = new ProviderFactory(Config(), new StoredProviderCredentialResolver(store, new EnvironmentCredentialResolver(), Owner));
        var otherFactory = new ProviderFactory(Config(), new StoredProviderCredentialResolver(store, new EnvironmentCredentialResolver(), other));

        using var original = (IDisposable)ownerFactory.CreateAlpacaBackfillProvider(null)!;
        await store.SaveScopedAsync(Request("rotated-key", ""), Owner);
        ownerFactory.CreateAlpacaBackfillProvider(null).Should().BeNull();
        using var foreign = (IDisposable)otherFactory.CreateAlpacaBackfillProvider(null)!;
        foreign.Should().NotBeNull();
        await store.DeleteScopedAsync("alpaca", Owner);
        ownerFactory.CreateAlpacaBackfillProvider(null).Should().BeNull();
        using var retained = (IDisposable)otherFactory.CreateAlpacaBackfillProvider(null)!;
        retained.Should().NotBeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ModuleCredentialRecord_DoesNotBorrowMissingFields(bool partial)
    {
        using var environment = AmbientCredentials.Alpaca();
        var credentials = new Dictionary<string, string?>();
        if (partial)
            credentials["keyId"] = "module-key";
        var factory = new ProviderFactory(Config(), new EnvironmentCredentialResolver(),
            moduleContexts: new Dictionary<string, ProviderModuleContext>
            {
                ["alpaca"] = ProviderModuleContext.FromCredentials(credentials)
            });

        factory.CreateAlpacaBackfillProvider(null).Should().BeNull();
        factory.CreateAlpacaSearchProvider(null).Should().BeNull();
    }

    [Theory]
    [InlineData("alphavantage", "ALPHA_VANTAGE_API_KEY")]
    [InlineData("finnhub", "FINNHUB_API_KEY")]
    [InlineData("tiingo", "TIINGO_API_TOKEN")]
    [InlineData("twelvedata", "TWELVEDATA_API_KEY")]
    [InlineData("nasdaq", "NASDAQ_DATA_LINK_API_KEY")]
    public async Task CorporateActionFactory_MissingRecordDoesNotRereadEnvironment(string providerId, string credentialName)
    {
        using var environment = new AmbientCredentials(new Dictionary<string, string?> { [credentialName] = "ambient-secret" });
        using var handler = new RecordingHandler();
        using var services = CreateServices(handler);
        var resolver = new StoredProviderCredentialResolver(new FileProviderCredentialStore(_root, _ => null), new EnvironmentCredentialResolver());
        var factory = new ProviderFactory(Config(), resolver, services: services);
        var provider = ProviderCapabilityDescriptorCatalog.Descriptors.Single(d => d.ProviderId == providerId)
            .CorporateActionsFactory!(factory)!;

        await provider.FetchAsync("AAPL", Guid.NewGuid());

        if (providerId == "nasdaq")
            handler.Requests.Should().ContainSingle().Which.Uri.Query.Should().NotContain("api_key");
        else
            handler.Requests.Should().BeEmpty();
    }

    private static AppConfig Config() => new(Alpaca: new AlpacaOptions("configured-key", "configured-secret", UseSandbox: true));

    private static ProviderCredentialSaveRequest Request(string key, string secret) => new("alpaca",
        new Dictionary<string, string?> { ["KeyId"] = key, ["SecretKey"] = secret }, "paper");

    private static ServiceProvider CreateServices(RecordingHandler handler) => CreateServiceCollection(handler).BuildServiceProvider();

    private static IServiceCollection CreateServiceCollection(RecordingHandler handler)
    {
        var publisher = new TestMarketEventPublisher();
        return new ServiceCollection()
            .AddLogging()
            .AddSingleton<IHttpClientFactory>(new RecordingHttpClientFactory(handler))
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["APCA_API_KEY_ID"] = "configuration-alias-key",
                ["APCA_API_SECRET_KEY"] = "configuration-alias-secret"
            }).Build())
            .AddSingleton<IAtomicFileWriter, AtomicFileWriterAdapter>()
            .AddSingleton(new TradeDataCollector(publisher))
            .AddSingleton(new QuoteCollector(publisher));
    }

    private sealed class RecordingHttpClientFactory(RecordingHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed record RequestEvidence(Uri Uri, string? Key, string? Secret, string? BearerToken);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<RequestEvidence> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RequestEvidence(request.RequestUri!,
                request.Headers.TryGetValues("APCA-API-KEY-ID", out var key) ? key.Single() : null,
                request.Headers.TryGetValues("APCA-API-SECRET-KEY", out var secret) ? secret.Single() : null,
                request.Headers.Authorization?.Parameter));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(request.RequestUri!.AbsolutePath == "/v2/account" ? "{\"id\":\"owned-account\"}" : "{}",
                    System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class AmbientCredentials : IDisposable
    {
        private readonly Dictionary<string, string?> _previous;
        public AmbientCredentials(Dictionary<string, string?> values)
        {
            _previous = values.Keys.ToDictionary(key => key, Environment.GetEnvironmentVariable);
            foreach (var (key, value) in values)
                Environment.SetEnvironmentVariable(key, value);
        }

        public static AmbientCredentials Alpaca() => new(new Dictionary<string, string?>
        {
            ["ALPACA_KEY_ID"] = "ambient-key",
            ["ALPACA_SECRET_KEY"] = "ambient-secret",
            ["APCA_API_KEY_ID"] = "ambient-alias-key",
            ["APCA_API_SECRET_KEY"] = "ambient-alias-secret",
            ["ALPACA_TRADING_ENVIRONMENT"] = "live"
        });

        public void Dispose()
        {
            foreach (var (key, value) in _previous)
                Environment.SetEnvironmentVariable(key, value);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
