using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Identity.Auth;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.ProviderSdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Exercises overlapping hosts through their real authentication, configuration, and provider routes.
/// No test in this class reads or changes process-wide configuration or provider callbacks.
/// </summary>
[Trait("Category", "Integration")]
public sealed class EndpointTestFixtureProviderCatalogLifetimeTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConcurrentFixtures_DifferentAuthDataAndProviders_RemainIsolatedInEitherDisposalOrder(
        bool disposeFirstFixtureFirst)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var first = CreateFixture("first-key", "first-provider", "required");
        var second = CreateFixture("second-key", "second-provider", "optional");

        try
        {
            await Task.WhenAll(
                Task.Run(first.InitializeAsync, timeout.Token),
                Task.Run(second.InitializeAsync, timeout.Token)).WaitAsync(timeout.Token);

            Path.IsPathFullyQualified(first.DataRoot).Should().BeTrue();
            Path.IsPathFullyQualified(second.DataRoot).Should().BeTrue();
            first.DataRoot.Should().NotBe(second.DataRoot);
            first.Services.GetRequiredService<IProviderCatalog>()
                .Should().NotBeSameAs(second.Services.GetRequiredService<IProviderCatalog>());

            using var firstClient = CreateApiClient(first, "first-key");
            using var secondClient = CreateApiClient(second, "second-key");
            await Task.WhenAll(
                AddSymbolAsync(firstClient, "FIRSTONLY", timeout.Token),
                AddSymbolAsync(secondClient, "SECONDONLY", timeout.Token));

            await Task.WhenAll(
                AssertOwnedStateAsync(first, firstClient, "first-provider", "second-provider", "FIRSTONLY", "SECONDONLY", timeout.Token),
                AssertOwnedStateAsync(second, secondClient, "second-provider", "first-provider", "SECONDONLY", "FIRSTONLY", timeout.Token),
                AssertKeyRejectedAsync(first, "second-key", timeout.Token),
                AssertKeyRejectedAsync(second, "first-key", timeout.Token));

            // Rotation is local too: an already-running sibling keeps accepting its original key.
            first.Configuration["MDC_API_KEY"] = "rotated-first-key";
            firstClient.DefaultRequestHeaders.Remove("X-Api-Key");
            firstClient.DefaultRequestHeaders.Add("X-Api-Key", "rotated-first-key");
            await Task.WhenAll(
                AssertKeyRejectedAsync(first, "first-key", timeout.Token),
                AssertOwnedStateAsync(first, firstClient, "first-provider", "second-provider", "FIRSTONLY", "SECONDONLY", timeout.Token),
                AssertOwnedStateAsync(second, secondClient, "second-provider", "first-provider", "SECONDONLY", "FIRSTONLY", timeout.Token));

            var disposed = disposeFirstFixtureFirst ? first : second;
            var survivor = disposeFirstFixtureFirst ? second : first;
            var survivorClient = disposeFirstFixtureFirst ? secondClient : firstClient;
            var stopped = disposed.Services.GetRequiredService<IHostApplicationLifetime>();
            var disposedRoot = Path.GetDirectoryName(disposed.DataRoot)!;
            await disposed.DisposeAsync().WaitAsync(timeout.Token);

            stopped.ApplicationStopped.IsCancellationRequested.Should().BeTrue();
            Directory.Exists(disposedRoot).Should().BeFalse();
            Directory.Exists(Path.GetDirectoryName(survivor.DataRoot)!).Should().BeTrue();
            await AssertOwnedStateAsync(
                survivor,
                survivorClient,
                disposeFirstFixtureFirst ? "second-provider" : "first-provider",
                disposeFirstFixtureFirst ? "first-provider" : "second-provider",
                disposeFirstFixtureFirst ? "SECONDONLY" : "FIRSTONLY",
                disposeFirstFixtureFirst ? "FIRSTONLY" : "SECONDONLY",
                timeout.Token);
            await AssertKeyRejectedAsync(
                survivor,
                disposeFirstFixtureFirst ? "rotated-first-key" : "second-key",
                timeout.Token);
        }
        finally
        {
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await Task.WhenAll(first.DisposeAsync(), second.DisposeAsync()).WaitAsync(cleanupTimeout.Token);
        }
    }

    [Fact]
    public async Task ConcurrentFixtures_OptionalAndRequiredAuthentication_DoNotShareTheirPosture()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var optional = new EndpointTestFixture();
        var required = CreateFixture("required-key", "required-provider", "required");

        try
        {
            await Task.WhenAll(
                Task.Run(optional.InitializeAsync, timeout.Token),
                Task.Run(required.InitializeAsync, timeout.Token)).WaitAsync(timeout.Token);
            using var optionalClient = optional.CreatePermittedClient(UserPermission.ViewConfig);
            using var requiredAnonymousClient = required.CreatePermittedClient(UserPermission.ViewConfig);
            using var requiredKeyClient = CreateApiClient(required, "required-key");

            using var optionalResponse = await optionalClient.GetAsync("/api/config", timeout.Token);
            using var requiredResponse = await requiredAnonymousClient.GetAsync("/api/config", timeout.Token);
            using var keyResponse = await requiredKeyClient.GetAsync("/api/config", timeout.Token);
            optionalResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            requiredResponse.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
                "required session authentication without configured users must fail closed before test permission injection");
            keyResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            await required.DisposeAsync().WaitAsync(timeout.Token);
            using var afterDisposal = await optionalClient.GetAsync("/api/config", timeout.Token);
            afterDisposal.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await Task.WhenAll(optional.DisposeAsync(), required.DisposeAsync()).WaitAsync(cleanupTimeout.Token);
        }
    }

    [Fact]
    public async Task Initialize_SiblingFails_CleansFailedFixtureAndLeavesRunningHostUsable()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var running = CreateFixture("survivor-key", "survivor-provider", "required");
        var failing = new EndpointTestFixture(
            static () => throw new InvalidOperationException("synthetic initialization failure"));

        try
        {
            await running.InitializeAsync().WaitAsync(timeout.Token);
            var initialize = () => failing.InitializeAsync().WaitAsync(timeout.Token);
            await initialize.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("synthetic initialization failure");
            Directory.Exists(Path.GetDirectoryName(failing.DataRoot)!).Should().BeFalse();

            using var client = CreateApiClient(running, "survivor-key");
            using var config = await client.GetAsync("/api/config", timeout.Token);
            config.StatusCode.Should().Be(HttpStatusCode.OK);
            using var providerClient = CreateProviderReadClient(running);
            using var response = await providerClient.GetAsync("/api/providers/catalog/survivor-provider", timeout.Token);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            using var comparison = await providerClient.GetAsync("/api/providers/comparison", timeout.Token);
            comparison.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await Task.WhenAll(failing.DisposeAsync(), running.DisposeAsync()).WaitAsync(cleanupTimeout.Token);
        }
    }

    [Fact]
    public async Task Dispose_HostedServiceStopThrowsAfterHostStops_CleansOwnedStateAndReportsError()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var fixture = new EndpointTestFixture(static services =>
            services.AddSingleton<IHostedService, ThrowingStopHostedService>());

        try
        {
            await fixture.InitializeAsync().WaitAsync(timeout.Token);
            var applicationLifetime = fixture.Services.GetRequiredService<IHostApplicationLifetime>();
            var fixtureRoot = Path.GetDirectoryName(fixture.DataRoot)!;

            var dispose = () => fixture.DisposeAsync().WaitAsync(timeout.Token);
            var failure = await dispose.Should().ThrowAsync<AggregateException>()
                .WithMessage("Endpoint test fixture cleanup failed.*");
            failure.Which.Flatten().InnerExceptions.Should().Contain(error =>
                error is InvalidOperationException && error.Message == "synthetic hosted-service stop failure");

            applicationLifetime.ApplicationStopped.IsCancellationRequested.Should().BeTrue();
            Directory.Exists(fixtureRoot).Should().BeFalse();
        }
        finally
        {
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await fixture.DisposeAsync().WaitAsync(cleanupTimeout.Token);
        }
    }

    private static EndpointTestFixture CreateFixture(string apiKey, string providerId, string authMode)
    {
        return new EndpointTestFixture(
            new Dictionary<string, string?>
            {
                ["MDC_AUTH_MODE"] = authMode,
                ["MDC_API_KEY"] = apiKey,
                ["MDC_API_KEY_ROLE"] = "Admin"
            },
            services =>
            {
                services.RemoveAll<ProviderRegistry>();
                services.AddSingleton<ProviderRegistry>(_ =>
                {
                    var registry = new ProviderRegistry();
                    registry.Register(new FixtureProvider(providerId));
                    return registry;
                });
                services.RemoveAll<IProviderCatalog>();
                services.AddSingleton<IProviderCatalog>(provider =>
                {
                    var registry = provider.GetRequiredService<ProviderRegistry>();
                    return new RuntimeProviderCatalog(registry.GetProviderCatalog, registry.GetProviderCatalogEntry);
                });
            });
    }

    private static HttpClient CreateApiClient(EndpointTestFixture fixture, string key)
    {
        var client = fixture.CreateNoRedirectClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", key);
        return client;
    }

    private static HttpClient CreateProviderReadClient(EndpointTestFixture fixture)
    {
        // Provider routes require a tenant-scoped session; API-key principals deliberately have no
        // tenant. Keep key-only authentication assertions on /api/config and model the workstation
        // session separately. The key lets required authentication reach the fixture's session stub.
        var client = fixture.CreateSessionClient(UserPermission.ViewDiagnostics);
        client.DefaultRequestHeaders.Add("X-Api-Key", fixture.Configuration["MDC_API_KEY"]);
        return client;
    }

    private static async Task AddSymbolAsync(HttpClient client, string symbol, CancellationToken ct)
    {
        using var response = await client.PostAsJsonAsync("/api/config/symbols", new { Symbol = symbol }, ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task AssertKeyRejectedAsync(EndpointTestFixture fixture, string key, CancellationToken ct)
    {
        using var client = CreateApiClient(fixture, key);
        using var response = await client.GetAsync("/api/config", ct);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task AssertOwnedStateAsync(
        EndpointTestFixture fixture,
        HttpClient client,
        string ownProvider,
        string otherProvider,
        string ownSymbol,
        string otherSymbol,
        CancellationToken ct)
    {
        using var configResponse = await client.GetAsync("/api/config", ct);
        configResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var config = JsonDocument.Parse(await configResponse.Content.ReadAsStringAsync(ct));
        config.RootElement.GetProperty("dataRoot").GetString().Should().Be(fixture.DataRoot);
        var symbols = config.RootElement.GetProperty("symbols").EnumerateArray()
            .Select(symbol => symbol.GetProperty("symbol").GetString()).ToArray();
        symbols.Should().Contain(ownSymbol).And.NotContain(otherSymbol);

        using var providerClient = CreateProviderReadClient(fixture);
        using var catalogResponse = await providerClient.GetAsync("/api/providers/catalog", ct);
        catalogResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var catalog = JsonDocument.Parse(await catalogResponse.Content.ReadAsStringAsync(ct));
        catalog.RootElement.GetProperty("providers").EnumerateArray()
            .Select(provider => provider.GetProperty("providerId").GetString()).Should().Equal(ownProvider);
        using var ownResponse = await providerClient.GetAsync($"/api/providers/catalog/{ownProvider}", ct);
        using var otherResponse = await providerClient.GetAsync($"/api/providers/catalog/{otherProvider}", ct);
        ownResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        otherResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var comparison = await providerClient.GetAsync("/api/providers/comparison", ct);
        comparison.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private sealed class ThrowingStopHostedService : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException("synthetic hosted-service stop failure"));
    }

    private sealed class FixtureProvider(string providerId) : IProviderMetadata
    {
        public string ProviderId => providerId;
        public string ProviderDisplayName => providerId;
        public string ProviderDescription => "Metadata owned by one endpoint fixture";
        public int ProviderPriority => 100;
        public ProviderCapabilities ProviderCapabilities => ProviderCapabilities.None;
    }
}
