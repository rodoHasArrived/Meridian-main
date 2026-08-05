using System.Net;
using FluentAssertions;
using Meridian.Contracts.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Guards the endpoint-host restart scenario where a prior host leaves process-wide provider
/// catalog callbacks that can no longer resolve services from its disposed container.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class EndpointTestFixtureProviderCatalogLifetimeTests
{
    [Fact]
    public async Task ProviderComparison_PreviousHostCatalogWasDisposed_ReturnsCurrentFixtureResponse()
    {
        var originalCatalogProvider = ProviderCatalog.RuntimeCatalogProvider;
        var originalCatalogEntryProvider = ProviderCatalog.RuntimeCatalogEntryProvider;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        ProviderCatalog.InitializeFromRegistry(
            static () => throw new ObjectDisposedException("previous-host"),
            static _ => throw new ObjectDisposedException("previous-host"));

        var fixture = new EndpointTestFixture();
        try
        {
            await fixture.InitializeAsync().WaitAsync(timeout.Token);

            using var response = await fixture.Client.GetAsync(
                "/api/providers/comparison",
                timeout.Token);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

            var applicationLifetime = fixture.Services.GetRequiredService<IHostApplicationLifetime>();
            await fixture.DisposeAsync().WaitAsync(timeout.Token);

            applicationLifetime.ApplicationStopped.IsCancellationRequested.Should().BeTrue();
            ProviderCatalog.RuntimeCatalogProvider.Should().BeNull();
            ProviderCatalog.RuntimeCatalogEntryProvider.Should().BeNull();
            var readCatalog = () => ProviderCatalog.GetAll();
            readCatalog.Should().NotThrow();
        }
        finally
        {
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await fixture.DisposeAsync().WaitAsync(cleanupTimeout.Token);
            ProviderCatalog.RuntimeCatalogProvider = originalCatalogProvider;
            ProviderCatalog.RuntimeCatalogEntryProvider = originalCatalogEntryProvider;
        }
    }

    [Fact]
    public async Task ProviderComparison_InnerFixtureWasDisposed_OuterFixtureRemainsBound()
    {
        var originalCatalogProvider = ProviderCatalog.RuntimeCatalogProvider;
        var originalCatalogEntryProvider = ProviderCatalog.RuntimeCatalogEntryProvider;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var outer = new EndpointTestFixture();
        var inner = new EndpointTestFixture();

        try
        {
            await outer.InitializeAsync().WaitAsync(timeout.Token);
            var outerCatalogProvider = ProviderCatalog.RuntimeCatalogProvider;
            var outerCatalogEntryProvider = ProviderCatalog.RuntimeCatalogEntryProvider;
            await inner.InitializeAsync().WaitAsync(timeout.Token);
            await inner.DisposeAsync().WaitAsync(timeout.Token);

            ProviderCatalog.RuntimeCatalogProvider.Should().BeSameAs(outerCatalogProvider);
            ProviderCatalog.RuntimeCatalogEntryProvider.Should().BeSameAs(outerCatalogEntryProvider);

            using var response = await outer.Client.GetAsync(
                "/api/providers/comparison",
                timeout.Token);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        }
        finally
        {
            await inner.DisposeAsync().WaitAsync(timeout.Token);
            await outer.DisposeAsync().WaitAsync(timeout.Token);
            ProviderCatalog.RuntimeCatalogProvider.Should().BeNull();
            ProviderCatalog.RuntimeCatalogEntryProvider.Should().BeNull();
            ProviderCatalog.RuntimeCatalogProvider = originalCatalogProvider;
            ProviderCatalog.RuntimeCatalogEntryProvider = originalCatalogEntryProvider;
        }
    }

    [Fact]
    public async Task ProviderComparison_NewerNonFixtureOwnerWasInstalled_DisposeDoesNotClobberIt()
    {
        var originalCatalogProvider = ProviderCatalog.RuntimeCatalogProvider;
        var originalCatalogEntryProvider = ProviderCatalog.RuntimeCatalogEntryProvider;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var fixture = new EndpointTestFixture();
        ProviderCatalogEntry[] replacementEntries = [];
        Func<IReadOnlyList<ProviderCatalogEntry>> replacementCatalogProvider = () => replacementEntries;
        Func<string, ProviderCatalogEntry?> replacementCatalogEntryProvider = _ => null;

        try
        {
            await fixture.InitializeAsync().WaitAsync(timeout.Token);
            ProviderCatalog.InitializeFromRegistry(
                replacementCatalogProvider,
                replacementCatalogEntryProvider);

            await fixture.DisposeAsync().WaitAsync(timeout.Token);

            ProviderCatalog.RuntimeCatalogProvider.Should().BeSameAs(replacementCatalogProvider);
            ProviderCatalog.RuntimeCatalogEntryProvider.Should().BeSameAs(replacementCatalogEntryProvider);
        }
        finally
        {
            await fixture.DisposeAsync().WaitAsync(timeout.Token);
            ProviderCatalog.RuntimeCatalogProvider = originalCatalogProvider;
            ProviderCatalog.RuntimeCatalogEntryProvider = originalCatalogEntryProvider;
        }
    }

    [Fact]
    public async Task ProviderComparison_NonFixturePairReplacedOuter_InnerDoesNotResurrectOuter()
    {
        var originalCatalogProvider = ProviderCatalog.RuntimeCatalogProvider;
        var originalCatalogEntryProvider = ProviderCatalog.RuntimeCatalogEntryProvider;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var outer = new EndpointTestFixture();
        var inner = new EndpointTestFixture();
        Func<IReadOnlyList<ProviderCatalogEntry>> replacementCatalogProvider =
            static () => Array.Empty<ProviderCatalogEntry>();
        Func<string, ProviderCatalogEntry?> replacementCatalogEntryProvider = static _ => null;

        try
        {
            await outer.InitializeAsync().WaitAsync(timeout.Token);
            var detachedOuterCatalogProvider = ProviderCatalog.RuntimeCatalogProvider;
            var detachedOuterCatalogEntryProvider = ProviderCatalog.RuntimeCatalogEntryProvider;
            ProviderCatalog.InitializeFromRegistry(
                replacementCatalogProvider,
                replacementCatalogEntryProvider);

            await inner.InitializeAsync().WaitAsync(timeout.Token);
            await inner.DisposeAsync().WaitAsync(timeout.Token);

            ProviderCatalog.RuntimeCatalogProvider.Should().BeNull();
            ProviderCatalog.RuntimeCatalogEntryProvider.Should().BeNull();
            ProviderCatalog.RuntimeCatalogProvider.Should().NotBeSameAs(detachedOuterCatalogProvider);
            ProviderCatalog.RuntimeCatalogEntryProvider.Should().NotBeSameAs(detachedOuterCatalogEntryProvider);
        }
        finally
        {
            await inner.DisposeAsync().WaitAsync(timeout.Token);
            await outer.DisposeAsync().WaitAsync(timeout.Token);
            ProviderCatalog.RuntimeCatalogProvider = originalCatalogProvider;
            ProviderCatalog.RuntimeCatalogEntryProvider = originalCatalogEntryProvider;
        }
    }

    [Fact]
    public async Task ProviderComparison_InnerFixtureInitializationFails_OuterFixtureIsRestored()
    {
        var originalCatalogProvider = ProviderCatalog.RuntimeCatalogProvider;
        var originalCatalogEntryProvider = ProviderCatalog.RuntimeCatalogEntryProvider;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var outer = new EndpointTestFixture();
        var failingInner = new EndpointTestFixture(
            static () => throw new InvalidOperationException("synthetic initialization failure"));

        try
        {
            await outer.InitializeAsync().WaitAsync(timeout.Token);
            var outerCatalogProvider = ProviderCatalog.RuntimeCatalogProvider;
            var outerCatalogEntryProvider = ProviderCatalog.RuntimeCatalogEntryProvider;

            var initialize = () => failingInner.InitializeAsync().WaitAsync(timeout.Token);
            await initialize.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("synthetic initialization failure");

            ProviderCatalog.RuntimeCatalogProvider.Should().BeSameAs(outerCatalogProvider);
            ProviderCatalog.RuntimeCatalogEntryProvider.Should().BeSameAs(outerCatalogEntryProvider);

            using var response = await outer.Client.GetAsync(
                "/api/providers/comparison",
                timeout.Token);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        }
        finally
        {
            await failingInner.DisposeAsync().WaitAsync(timeout.Token);
            await outer.DisposeAsync().WaitAsync(timeout.Token);
            ProviderCatalog.RuntimeCatalogProvider.Should().BeNull();
            ProviderCatalog.RuntimeCatalogEntryProvider.Should().BeNull();
            ProviderCatalog.RuntimeCatalogProvider = originalCatalogProvider;
            ProviderCatalog.RuntimeCatalogEntryProvider = originalCatalogEntryProvider;
        }
    }

    [Fact]
    public async Task InitializeAndDispose_SeededLeanEnvironment_IsClearedThenRestored()
    {
        var originalLeanPath = Environment.GetEnvironmentVariable("LEAN_PATH");
        var originalLeanDataPath = Environment.GetEnvironmentVariable("LEAN_DATA_PATH");
        var originalInterval = Environment.GetEnvironmentVariable("LEAN_EXPORT_INTERVAL_SECONDS");
        var originalReportingConnection =
            Environment.GetEnvironmentVariable("MERIDIAN_REPORTING_CONNECTION_STRING");
        var originalDirectLendingConnection =
            Environment.GetEnvironmentVariable("MERIDIAN_DIRECT_LENDING_CONNECTION_STRING");
        var seededLeanPath = Path.Combine(Path.GetTempPath(), $"lean-install-{Guid.NewGuid():N}");
        var seededDataPath = Path.Combine(Path.GetTempPath(), $"lean-data-{Guid.NewGuid():N}");
        var fixture = new EndpointTestFixture();

        try
        {
            Environment.SetEnvironmentVariable("LEAN_PATH", seededLeanPath);
            Environment.SetEnvironmentVariable("LEAN_DATA_PATH", seededDataPath);
            Environment.SetEnvironmentVariable("LEAN_EXPORT_INTERVAL_SECONDS", "1");

            await fixture.InitializeAsync();

            Environment.GetEnvironmentVariable("LEAN_PATH").Should().BeNull();
            Environment.GetEnvironmentVariable("LEAN_DATA_PATH").Should().BeNull();
            Environment.GetEnvironmentVariable("LEAN_EXPORT_INTERVAL_SECONDS").Should().BeNull();
            Environment.GetEnvironmentVariable("MERIDIAN_REPORTING_CONNECTION_STRING")
                .Should().Be(" ");

            await fixture.DisposeAsync();

            Environment.GetEnvironmentVariable("LEAN_PATH").Should().Be(seededLeanPath);
            Environment.GetEnvironmentVariable("LEAN_DATA_PATH").Should().Be(seededDataPath);
            Environment.GetEnvironmentVariable("LEAN_EXPORT_INTERVAL_SECONDS").Should().Be("1");
            Environment.GetEnvironmentVariable("MERIDIAN_REPORTING_CONNECTION_STRING")
                .Should().Be(originalReportingConnection);
            Environment.GetEnvironmentVariable("MERIDIAN_DIRECT_LENDING_CONNECTION_STRING")
                .Should().Be(originalDirectLendingConnection);
            Directory.Exists(seededDataPath).Should().BeFalse();
        }
        finally
        {
            await fixture.DisposeAsync();
            Environment.SetEnvironmentVariable("LEAN_PATH", originalLeanPath);
            Environment.SetEnvironmentVariable("LEAN_DATA_PATH", originalLeanDataPath);
            Environment.SetEnvironmentVariable("LEAN_EXPORT_INTERVAL_SECONDS", originalInterval);
            Environment.SetEnvironmentVariable(
                "MERIDIAN_REPORTING_CONNECTION_STRING",
                originalReportingConnection);
            Environment.SetEnvironmentVariable(
                "MERIDIAN_DIRECT_LENDING_CONNECTION_STRING",
                originalDirectLendingConnection);
        }
    }

    [Fact]
    public async Task Dispose_HostedServiceStopThrowsAfterHostStops_CleansOwnedStateAndReportsError()
    {
        var originalCatalogProvider = ProviderCatalog.RuntimeCatalogProvider;
        var originalCatalogEntryProvider = ProviderCatalog.RuntimeCatalogEntryProvider;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var fixture = new EndpointTestFixture(static services =>
            services.AddSingleton<IHostedService, ThrowingStopHostedService>());

        try
        {
            await fixture.InitializeAsync().WaitAsync(timeout.Token);
            var applicationLifetime = fixture.Services.GetRequiredService<IHostApplicationLifetime>();
            var fixtureRoot = Path.GetDirectoryName(fixture.DataRoot)!;

            var dispose = () => fixture.DisposeAsync().WaitAsync(timeout.Token);
            await dispose.Should().ThrowAsync<AggregateException>()
                .WithMessage("Endpoint test fixture cleanup failed.*");

            applicationLifetime.ApplicationStopped.IsCancellationRequested.Should().BeTrue();
            ProviderCatalog.RuntimeCatalogProvider.Should().BeNull();
            ProviderCatalog.RuntimeCatalogEntryProvider.Should().BeNull();
            Directory.Exists(fixtureRoot).Should().BeFalse();
        }
        finally
        {
            await fixture.DisposeAsync().WaitAsync(timeout.Token);
            ProviderCatalog.RuntimeCatalogProvider = originalCatalogProvider;
            ProviderCatalog.RuntimeCatalogEntryProvider = originalCatalogEntryProvider;
        }
    }

    private sealed class ThrowingStopHostedService : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException("synthetic hosted-service stop failure"));
    }
}
