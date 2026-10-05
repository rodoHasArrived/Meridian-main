using System.Net;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Identity.Auth;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// This compatibility test deliberately seeds process-startup state, so it must remain serialized
/// even when ordinary endpoint fixtures can run independently.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Endpoint")]
public sealed class EndpointTestFixtureProcessCompatibilityTests
{
    [Fact]
    public async Task InitializeAndDispose_AmbientStartupSettingsAndDisposedCatalog_AreIgnoredAndUnchanged()
    {
        var leanRoot = Path.Combine(Path.GetTempPath(), $"ambient-lean-{Guid.NewGuid():N}");
        var seededEnvironment = new Dictionary<string, string?>
        {
            ["MDC_AUTH_MODE"] = "required",
            ["MDC_API_KEY"] = "ambient-process-key",
            ["MDC_API_KEY_ROLE"] = "invalid-process-role",
            ["MDC_USERNAME"] = "ambient-operator",
            ["MDC_PASSWORD_HASH"] = "invalid-process-hash",
            ["MDC_USERS"] = "invalid-process-users",
            ["MDC_DISABLE_RATE_LIMIT"] = "false",
            ["MERIDIAN_USE_INMEMORY_GOVERNANCE"] = "false",
            ["DOTNET_ENVIRONMENT"] = "Production",
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
            ["LEAN_PATH"] = Path.Combine(leanRoot, "install"),
            ["LEAN_DATA_PATH"] = Path.Combine(leanRoot, "data"),
            ["LEAN_EXPORT_INTERVAL_SECONDS"] = "1",
            ["MERIDIAN_REPORTING_CONNECTION_STRING"] = "invalid-process-reporting-connection",
            ["MERIDIAN_DIRECT_LENDING_CONNECTION_STRING"] = "invalid-process-lending-connection",
            ["MERIDIAN_LEDGER_CONNECTION_STRING"] = "invalid-process-ledger-connection",
            ["MERIDIAN_DATABASE_URL"] = "invalid-process-database-url"
        };
        var originalEnvironment = seededEnvironment.Keys.ToDictionary(
            key => key, Environment.GetEnvironmentVariable);
        var originalCatalogProvider = ProviderCatalog.RuntimeCatalogProvider;
        var originalCatalogEntryProvider = ProviderCatalog.RuntimeCatalogEntryProvider;
        Func<IReadOnlyList<ProviderCatalogEntry>> disposedCatalog =
            static () => throw new ObjectDisposedException("previous-process-host");
        Func<string, ProviderCatalogEntry?> disposedEntry =
            static _ => throw new ObjectDisposedException("previous-process-host");
        var fixture = new EndpointTestFixture();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        try
        {
            foreach (var (key, value) in seededEnvironment)
                Environment.SetEnvironmentVariable(key, value);
            ProviderCatalog.InitializeFromRegistry(disposedCatalog, disposedEntry);

            await fixture.InitializeAsync().WaitAsync(timeout.Token);
            AssertAmbientStateUnchanged();
            using var client = fixture.CreatePermittedClient(UserPermission.ViewConfig, UserPermission.ViewDiagnostics);
            using var config = await client.GetAsync("/api/config", timeout.Token);
            using var catalog = await client.GetAsync("/api/providers/catalog", timeout.Token);
            using var comparison = await client.GetAsync("/api/providers/comparison", timeout.Token);
            config.StatusCode.Should().Be(HttpStatusCode.OK);
            catalog.StatusCode.Should().Be(HttpStatusCode.OK);
            comparison.StatusCode.Should().Be(HttpStatusCode.OK);

            await fixture.DisposeAsync().WaitAsync(timeout.Token);
            AssertAmbientStateUnchanged();
            Directory.Exists(leanRoot).Should().BeFalse();
        }
        finally
        {
            try
            {
                using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await fixture.DisposeAsync().WaitAsync(cleanupTimeout.Token);
            }
            finally
            {
                foreach (var (key, value) in originalEnvironment)
                    Environment.SetEnvironmentVariable(key, value);
                ProviderCatalog.RuntimeCatalogProvider = originalCatalogProvider;
                ProviderCatalog.RuntimeCatalogEntryProvider = originalCatalogEntryProvider;
            }
        }

        void AssertAmbientStateUnchanged()
        {
            foreach (var (key, value) in seededEnvironment)
                Environment.GetEnvironmentVariable(key).Should().Be(value, $"the fixture does not own {key}");
            ProviderCatalog.RuntimeCatalogProvider.Should().BeSameAs(disposedCatalog);
            ProviderCatalog.RuntimeCatalogEntryProvider.Should().BeSameAs(disposedEntry);
        }
    }
}
