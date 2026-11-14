using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Subscriptions.Services;
using Meridian.Application.ProviderRouting;
using Meridian.Application.UI;
using Meridian.Core.Config;
using Meridian.Core.Subscriptions.Models;
using Meridian.DataIntegration.Credentials;
using Meridian.Identity.Auth;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Tests.TestHelpers;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class BondDiscoveryEndpointsTests : IDisposable
{
    private readonly List<string> _configRoots = [];
    private const string OwnerTenant = "bond-discovery-test-tenant";

    [Fact]
    public async Task Lookup_DeadlineRefusesUnavailableDataAndReleasesCredentialConfigurationLease()
    {
        var clock = new FakeTimeProvider();
        using var handler = new CancelableSearchHandler();
        using var http = new HttpClient(handler);
        using var provider = new AlpacaSymbolSearchProvider("synthetic-key", "synthetic-secret", http);
        using var search = new SymbolSearchService(new[] { provider }, null, new MetadataEnrichmentService());
        await using var app = await CreateAppAsync(UserPermission.ViewSecurityMaster, search, timeProvider: clock);
        var lookup = app.GetTestClient().GetAsync("/api/reference-data/bonds/discovery?query=Treasury");
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        clock.Advance(TimeSpan.FromSeconds(20));

        using var response = await lookup.WaitAsync(TimeSpan.FromSeconds(5));
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("available").GetBoolean().Should().BeFalse();
        body.GetProperty("failureReason").GetString().Should().Be("lookupTimedOut");
        body.GetProperty("candidates").GetArrayLength().Should().Be(0);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var lease = await app.Services.GetRequiredService<ConfigStore>().AcquireWriteLeaseAsync(timeout.Token);
    }

    [Fact]
    public async Task Lookup_CallerCancellationPropagatesAndReleasesCredentialConfigurationLease()
    {
        using var handler = new CancelableSearchHandler();
        using var http = new HttpClient(handler);
        using var provider = new AlpacaSymbolSearchProvider("synthetic-key", "synthetic-secret", http);
        using var search = new SymbolSearchService(new[] { provider }, null, new MetadataEnrichmentService());
        await using var app = await CreateAppAsync(UserPermission.ViewSecurityMaster, search);
        using var caller = new CancellationTokenSource();
        var lookup = app.GetTestClient().GetAsync("/api/reference-data/bonds/discovery?query=Treasury", caller.Token);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await caller.CancelAsync();

        Func<Task> awaitLookup = async () => { using var response = await lookup; };
        await awaitLookup.Should().ThrowAsync<OperationCanceledException>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var lease = await app.Services.GetRequiredService<ConfigStore>().AcquireWriteLeaseAsync(timeout.Token);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Lookup_BlankQueryRefusesBeforeAnyProviderProbe(string query)
    {
        var unrelated = Substitute.For<ISymbolSearchProvider>();
        unrelated.Name.Returns("finnhub");
        using var search = new SymbolSearchService(new[] { unrelated }, null, new MetadataEnrichmentService());
        await using var app = await CreateAppAsync(UserPermission.ViewSecurityMaster, search);

        using var response = await app.GetTestClient().GetAsync("/api/reference-data/bonds/discovery?query=" + Uri.EscapeDataString(query));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await unrelated.DidNotReceiveWithAnyArgs().IsAvailableAsync(default);
        await unrelated.DidNotReceiveWithAnyArgs().SearchAsync(default!, default, default);
    }

    [Theory]
    [InlineData("selected", OwnerTenant, HttpStatusCode.OK, 1)]
    [InlineData("selected", "foreign-tenant", HttpStatusCode.Forbidden, 0)]
    [InlineData("deleted", OwnerTenant, HttpStatusCode.ServiceUnavailable, 0)]
    [InlineData("mismatched", OwnerTenant, HttpStatusCode.ServiceUnavailable, 0)]
    [InlineData("missing", OwnerTenant, HttpStatusCode.ServiceUnavailable, 0)]
    [InlineData("pending", OwnerTenant, HttpStatusCode.ServiceUnavailable, 0)]
    [InlineData("missing-config", OwnerTenant, HttpStatusCode.ServiceUnavailable, 0)]
    [InlineData("invalid-config", OwnerTenant, HttpStatusCode.ServiceUnavailable, 0)]
    [InlineData("null-config", OwnerTenant, HttpStatusCode.ServiceUnavailable, 0)]
    [InlineData("noncanonical-environment", OwnerTenant, HttpStatusCode.ServiceUnavailable, 0)]
    public async Task Lookup_RequiresCurrentAppliedTenantOwnedBindingBeforeProviderAccess(
        string ownership, string tenant, HttpStatusCode expectedStatus, int expectedRequests)
    {
        using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
        using var http = new HttpClient(handler);
        using var provider = new AlpacaSymbolSearchProvider("synthetic-key", "synthetic-secret", http);
        using var search = new SymbolSearchService(new[] { provider }, null, new MetadataEnrichmentService());
        await using var app = await CreateAppAsync(UserPermission.ViewSecurityMaster, search, tenantId: tenant, ownership: ownership);

        using var response = await app.GetTestClient().GetAsync("/api/reference-data/bonds/discovery?query=Treasury");

        response.StatusCode.Should().Be(expectedStatus);
        handler.CallCount.Should().Be(expectedRequests);
        if (expectedStatus == HttpStatusCode.ServiceUnavailable)
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("available").GetBoolean().Should().BeFalse();
            body.GetProperty("failureReason").GetString().Should().Be("credentialsUnavailable");
            body.GetProperty("candidates").GetArrayLength().Should().Be(0);
        }
        if (ownership != "missing-config")
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var lease = await app.Services.GetRequiredService<ConfigStore>().AcquireWriteLeaseAsync(timeout.Token);
        }
    }

    [Theory]
    [InlineData(UserPermission.ViewSecurityMaster, false, "fixed_income")]
    [InlineData(UserPermission.ModifySecurityMaster, true, "fixed_income")]
    [InlineData(UserPermission.ViewSecurityMaster, false, "treasury")]
    [InlineData(UserPermission.ViewSecurityMaster, false, "corporate")]
    public async Task Lookup_UsesFixedIncomeProviderAndReturnsStagingPermission(UserPermission permission, bool canStage, string rawClass)
    {
        using var handler = new StubHttpMessageHandler(request =>
        {
            request.RequestUri!.Query.Should().Contain("asset_class=fixed_income");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new[]
                {
                    new { symbol = "912TEST", name = "Synthetic Treasury", @class = rawClass, tradable = true },
                    new { symbol = "WRONG", name = "Treasury stock", @class = "us_equity", tradable = true }
                }))
            };
        });
        using var http = new HttpClient(handler);
        using var provider = new AlpacaSymbolSearchProvider("synthetic-key", "synthetic-secret", http);
        using var search = new SymbolSearchService(new[] { provider }, null, new MetadataEnrichmentService());
        await using var app = await CreateAppAsync(permission, search);
        using var response = await app.GetTestClient().GetAsync("/api/reference-data/bonds/discovery?query=Treasury");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("canStage").GetBoolean().Should().Be(canStage);
        body.GetProperty("available").GetBoolean().Should().BeTrue();
        body.GetProperty("candidates").GetArrayLength().Should().Be(1);
        body.GetProperty("candidates")[0].GetProperty("symbol").GetString().Should().Be("912TEST");
        handler.CallCount.Should().Be(1, "the lookup itself establishes availability without an unrelated health probe");
    }

    [Fact]
    public async Task Lookup_WithoutReadPermissionRefusesBeforeProviderAccess()
    {
        await using var app = await CreateAppAsync(UserPermission.ViewTrades);
        using var response = await app.GetTestClient().GetAsync("/api/reference-data/bonds/discovery?query=Treasury");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Lookup_WithoutTenantScopeFailsClosed()
    {
        await using var app = await CreateAppAsync(UserPermission.ViewSecurityMaster, tenantScoped: false);
        using var response = await app.GetTestClient().GetAsync("/api/reference-data/bonds/discovery?query=Treasury");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Lookup_WithoutSearchServiceReturnsExplicitUnavailable()
    {
        await using var app = await CreateAppAsync(UserPermission.ViewSecurityMaster);
        using var response = await app.GetTestClient().GetAsync("/api/reference-data/bonds/discovery?query=Treasury");
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Lookup_WithUnavailableProviderHasNoCandidates()
    {
        var provider = Substitute.For<ISymbolSearchProvider>();
        provider.Name.Returns("alpaca");
        provider.DisplayName.Returns("Alpaca");
        provider.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(false);
        using var search = new SymbolSearchService(new[] { provider }, null, new MetadataEnrichmentService());
        await using var app = await CreateAppAsync(UserPermission.ViewSecurityMaster, search);
        using var response = await app.GetTestClient().GetAsync("/api/reference-data/bonds/discovery?query=Treasury");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("available").GetBoolean().Should().BeFalse();
        body.GetProperty("unavailableReason").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("candidates").GetArrayLength().Should().Be(0);
        await provider.DidNotReceiveWithAnyArgs().SearchAsync(default!, default, default);
    }

    [Theory]
    [InlineData("credentialsUnavailable", false, false, HttpStatusCode.OK)]
    [InlineData("credentialsUnavailable", true, false, HttpStatusCode.Unauthorized)]
    [InlineData("credentialsUnavailable", true, false, HttpStatusCode.Forbidden)]
    [InlineData("transportError", true, true, HttpStatusCode.OK)]
    [InlineData("transportError", true, false, HttpStatusCode.ServiceUnavailable)]
    [InlineData("incompleteResponse", true, false, HttpStatusCode.PartialContent)]
    [InlineData("incompleteResponse", true, false, HttpStatusCode.Accepted)]
    [InlineData("throttled", true, false, HttpStatusCode.TooManyRequests)]
    public async Task Lookup_SeparatesCredentialTransportAndIncompleteFailures(
        string failureReason, bool credentials, bool transportFailure, HttpStatusCode providerStatus)
    {
        using var handler = new StubHttpMessageHandler(_ => transportFailure
            ? throw new HttpRequestException("Synthetic transport failure")
            : new HttpResponseMessage(providerStatus) { Content = new StringContent("[]") });
        using var http = new HttpClient(handler);
        using var provider = new AlpacaSymbolSearchProvider(credentials ? "synthetic-key" : "", credentials ? "synthetic-secret" : "", http);
        using var search = new SymbolSearchService(new[] { provider }, null, new MetadataEnrichmentService());
        await using var app = await CreateAppAsync(UserPermission.ViewSecurityMaster, search);

        using var response = await app.GetTestClient().GetAsync("/api/reference-data/bonds/discovery?query=Treasury");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("available").GetBoolean().Should().BeFalse();
        body.GetProperty("failureReason").GetString().Should().Be(failureReason);
        body.GetProperty("candidates").GetArrayLength().Should().Be(0);
        handler.CallCount.Should().Be(credentials ? 1 : 0);
    }

    [Fact]
    public async Task Lookup_WithValidEmptyPopulationReportsAvailableNoMatches()
    {
        using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
        using var http = new HttpClient(handler);
        using var provider = new AlpacaSymbolSearchProvider("synthetic-key", "synthetic-secret", http);
        using var search = new SymbolSearchService(new[] { provider }, null, new MetadataEnrichmentService());
        await using var app = await CreateAppAsync(UserPermission.ViewSecurityMaster, search);

        using var response = await app.GetTestClient().GetAsync("/api/reference-data/bonds/discovery?query=Treasury");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("available").GetBoolean().Should().BeTrue();
        body.GetProperty("failureReason").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("candidates").GetArrayLength().Should().Be(0);
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Lookup_WithInterruptedBodyReportsIncompleteInsteadOfNoMatches()
    {
        using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new InterruptedSearchContent()
        });
        using var http = new HttpClient(handler);
        using var provider = new AlpacaSymbolSearchProvider("synthetic-key", "synthetic-secret", http);
        using var search = new SymbolSearchService(new[] { provider }, null, new MetadataEnrichmentService());
        await using var app = await CreateAppAsync(UserPermission.ViewSecurityMaster, search);

        using var response = await app.GetTestClient().GetAsync("/api/reference-data/bonds/discovery?query=Treasury");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("available").GetBoolean().Should().BeFalse();
        body.GetProperty("failureReason").GetString().Should().Be("incompleteResponse");
        body.GetProperty("candidates").GetArrayLength().Should().Be(0);
    }

    [Theory]
    [InlineData("FixedIncome")]
    [InlineData("fixed_income")]
    [InlineData("Bond")]
    public async Task Lookup_PreservesAuthoritativeBondAssetTypeAliases(string assetType)
    {
        using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
        using var http = new HttpClient(handler);
        using var provider = new AuthoritativeBondProvider(http, assetType);
        using var search = new SymbolSearchService(new[] { provider }, null, new MetadataEnrichmentService());
        await using var app = await CreateAppAsync(UserPermission.ViewSecurityMaster, search);

        using var response = await app.GetTestClient().GetAsync("/api/reference-data/bonds/discovery?query=Treasury");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("candidates").GetArrayLength().Should().Be(1);
        body.GetProperty("candidates")[0].GetProperty("symbol").GetString().Should().Be("912TEST");
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{\"message\":\"provider error\"}")]
    [InlineData("null")]
    [InlineData("[{\"symbol\":\"912TEST\"}]")]
    [InlineData("[{\"symbol\":\"912TEST\",\"class\":\"fixed_income\"}]")]
    [InlineData("[{\"symbol\":\"912TEST\",\"tradable\":true}]")]
    [InlineData("[{\"symbol\":\"912TEST\",\"class\":\"fixed_income\",\"tradable\":true}]")]
    [InlineData("[{\"symbol\":\"912TEST\",\"name\":\"\",\"class\":\"fixed_income\",\"tradable\":true}]")]
    [InlineData("[{\"symbol\":\"912TEST\",\"name\":\"Synthetic Treasury\",\"class\":\"unknown-class\",\"tradable\":true}]")]
    public async Task Lookup_WithRealAlpacaDecoderFailureReturnsUnavailable(string bondPayload)
    {
        var bondRequests = 0;
        using var handler = new StubHttpMessageHandler(request =>
        {
            var isBondLookup = request.RequestUri!.Query.Contains("asset_class=fixed_income", StringComparison.Ordinal);
            if (isBondLookup)
                bondRequests++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(bondPayload)
            };
        });
        using var http = new HttpClient(handler);
        using var provider = new AlpacaSymbolSearchProvider("synthetic-key", "synthetic-secret", http);
        using var search = new SymbolSearchService(new[] { provider }, null, new MetadataEnrichmentService());
        await using var app = await CreateAppAsync(UserPermission.ViewSecurityMaster, search);

        using var response = await app.GetTestClient().GetAsync("/api/reference-data/bonds/discovery?query=Treasury");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("available").GetBoolean().Should().BeFalse();
        body.GetProperty("failureReason").GetString().Should().Be("malformedPayload");
        body.GetProperty("candidates").GetArrayLength().Should().Be(0);
        bondRequests.Should().Be(1);
        handler.CallCount.Should().Be(1);
    }

    private async Task<WebApplication> CreateAppAsync(UserPermission permissions, SymbolSearchService? search = null,
        bool tenantScoped = true, string tenantId = OwnerTenant, string ownership = "selected", TimeProvider? timeProvider = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        if (search is not null)
            builder.Services.AddSingleton(search);
        if (timeProvider is not null)
            builder.Services.AddSingleton(timeProvider);
        if (ownership != "missing-config")
        {
            var root = Path.Combine(Path.GetTempPath(), "meridian-bond-discovery-ownership", Guid.NewGuid().ToString("N"));
            _configRoots.Add(root);
            Directory.CreateDirectory(root);
            var config = new ConfigStore(Path.Combine(root, "appsettings.json"));
            await config.SaveAsync(new AppConfig(DataRoot: root, ProviderConnections: new ProviderConnectionsConfig(
                Connections: [new ProviderConnectionConfig("bond-account-a", "alpaca", "Synthetic bond account A",
                    TenantId: OwnerTenant, ExternalAccountId: "account-a", CredentialEnvironment: "paper"),
                    new ProviderConnectionConfig("bond-account-b", "alpaca", "Synthetic bond account B",
                        TenantId: "foreign-tenant", ExternalAccountId: "account-b", CredentialEnvironment: "paper")])));
            var tracker = new ProviderRuntimeCredentialApplicationTracker();
            var runtime = new ProviderRuntimeCredentialService(config, Substitute.For<IProviderCredentialStore>(), applicationTracker: tracker);
            if (ownership != "missing")
            {
                await runtime.BindAsync("alpaca", "bond-account-a", OwnerTenant);
                if (ownership != "pending")
                    tracker.MarkApplied("alpaca");
                if (ownership is "deleted" or "mismatched")
                {
                    await config.UpdateAsync(current => current with
                    {
                        ProviderConnections = current.ProviderConnections! with
                        {
                            Connections = ownership == "deleted"
                                ? current.ProviderConnections.Connections!.Where(connection => connection.ConnectionId != "bond-account-a").ToArray()
                                : current.ProviderConnections.Connections!
                                    .Select(connection => connection.ConnectionId == "bond-account-a" ? connection with { TenantId = "different-owner" } : connection).ToArray()
                        }
                    });
                }
                if (ownership is "invalid-config" or "null-config")
                    await File.WriteAllTextAsync(config.ConfigPath, ownership == "null-config" ? "null" : "{");
                if (ownership == "noncanonical-environment")
                {
                    await config.UpdateAsync(current => current with
                    {
                        ProviderConnections = current.ProviderConnections! with
                        {
                            Connections = current.ProviderConnections.Connections!
                                .Select(connection => connection.ConnectionId == "bond-account-a" ? connection with { CredentialEnvironment = "unsupported-environment" } : connection).ToArray()
                        }
                    });
                    Action invalidScope = () => ProviderRuntimeCredentialService.RequireScope(
                        config.LoadRequired(), "alpaca", "bond-account-a", OwnerTenant);
                    invalidScope.Should().Throw<InvalidDataException>();
                }
            }
            builder.Services.AddSingleton(config);
            builder.Services.AddSingleton(runtime);
        }
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            if (tenantScoped)
                context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = tenantId;
            await next();
        });
        app.MapBondDiscoveryEndpoints(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await app.StartAsync();
        return app;
    }

    public void Dispose()
    {
        foreach (var root in _configRoots)
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
    }

    private sealed class InterruptedSearchContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => Task.FromException(new HttpIOException(HttpRequestError.ResponseEnded, "Synthetic interrupted provider body."));
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }

    private sealed class CancelableSearchHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("The synthetic infinite wait completed without cancellation.");
        }
    }

    private sealed class AuthoritativeBondProvider(HttpClient http, string assetType) : BaseSymbolSearchProvider("synthetic-key", http)
    {
        public override string Name => "alpaca";
        public override string DisplayName => "Synthetic authoritative bond provider";
        protected override string HttpClientName => "synthetic-search";
        protected override string BaseUrl => "https://example.test";
        protected override string ApiKeyEnvVar => "SYNTHETIC_BOND_KEY";
        protected override string BuildSearchUrl(string query, string? assetType, string? exchange) => BaseUrl;
        protected override string BuildDetailsUrl(string symbol) => BaseUrl;
        protected override IEnumerable<SymbolSearchResult> DeserializeSearchResults(string json, string query) => [];
        protected override IEnumerable<SymbolSearchResult> DeserializeAuthoritativeSearchResults(string json, string query)
            => [new("912TEST", "Synthetic Treasury", AssetType: assetType, Source: "alpaca"), new("WRONG", "Wrong asset", AssetType: "Stock", Source: "alpaca")];
        protected override IEnumerable<SymbolSearchResult> ApplyFilters(IEnumerable<SymbolSearchResult> results, string? assetType, string? exchange)
            => results; // Match Alpaca's native-filter path so the endpoint's alias predicate is exercised.
        protected override Task<SymbolDetails?> DeserializeDetailsAsync(string json, string symbol, CancellationToken ct)
            => Task.FromResult<SymbolDetails?>(null);
    }
}
