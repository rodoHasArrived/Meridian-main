using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using FluentAssertions;
using Meridian.Application.Config.Credentials;
using Meridian.DataIntegration.Credentials;
using Meridian.Application.ProviderRouting;
using Meridian.Contracts.Api;
using Meridian.Identity.Auth;
using Meridian.Contracts.Configuration;
using Meridian.ProviderSdk;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using ApplicationConfigStore = Meridian.Application.UI.ConfigStore;
using SharedConfigStore = Meridian.Ui.Shared.Services.ConfigStore;

namespace Meridian.Tests.Ui;

public sealed class ProviderRoutingEndpointsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task ConfigureProvider_StoresAlpacaCredentialsInEncryptedStoreAndLeavesConfigSecretFree()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();
        var credentialStore = (FileProviderCredentialStore)app.Services.GetRequiredService<IProviderCredentialStore>();
        var configStore = app.Services.GetRequiredService<ApplicationConfigStore>();
        var apiKey = $"alpaca-key-{Guid.NewGuid():N}";
        var apiSecret = $"alpaca-secret-{Guid.NewGuid():N}";

        var response = await client.PostAsync(UiApiRoutes.ProviderConfigure, JsonContent(new
        {
            kind = "alpaca",
            displayName = "Alpaca Paper Setup",
            apiKey,
            apiSecret,
            environment = "paper",
            capabilities = new[] { "streaming", "backfill" }
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var responseJson = await response.Content.ReadAsStringAsync();
        responseJson.Should().NotContain(apiKey);
        responseJson.Should().NotContain(apiSecret);

        var result = Deserialize<ProviderSetupResult>(responseJson);
        result.Success.Should().BeTrue();
        result.ProviderId.Should().Be("alpaca");
        result.ConnectionId.Should().Be(result.ProviderId);
        result.BindingIds.Should().BeEquivalentTo("alpaca-realtime-market-data", "alpaca-historical-bars");
        result.CredentialState.Should().Be(ProviderCredentialStateDto.Configured);
        result.CredentialSource.Should().Be(ProviderCredentialSourceDto.LocalEncryptedStore);
        result.CredentialReference.Should().Be("vault:alpaca/paper");
        result.Environment.Should().Be("paper");

        var stored = await credentialStore.ReadForProviderAsync("alpaca");
        stored.Should().NotBeNull();
        stored!.Get("KeyId").Should().Be(apiKey);
        stored.Get("SecretKey").Should().Be(apiSecret);

        var configJson = await File.ReadAllTextAsync(configStore.ConfigPath);
        configJson.Should().NotContain(apiKey);
        configJson.Should().NotContain(apiSecret);

        var dataSources = await client.GetAsync(UiApiRoutes.ConfigDataSources);
        dataSources.StatusCode.Should().Be(HttpStatusCode.OK);
        var dataSourcesJson = await dataSources.Content.ReadAsStringAsync();
        dataSourcesJson.Should().NotContain(apiKey);
        dataSourcesJson.Should().NotContain(apiSecret);

        var vaultJson = await File.ReadAllTextAsync(credentialStore.VaultPath);
        vaultJson.Should().NotContain(apiKey);
        vaultJson.Should().NotContain(apiSecret);
    }

    [Fact]
    public async Task ConfigureProvider_LiveAlpacaWithUnverifiedCredentials_DoesNotEnableLiveRouting()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        var response = await client.PostAsync(UiApiRoutes.ProviderConfigure, JsonContent(new
        {
            kind = "alpaca",
            displayName = "Alpaca Live Setup",
            apiKey = $"alpaca-live-key-{Guid.NewGuid():N}",
            apiSecret = $"alpaca-live-secret-{Guid.NewGuid():N}",
            environment = "live",
            capabilities = new[] { "streaming", "backfill" }
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var setup = await ReadAsync<ProviderSetupResult>(response);
        setup.Success.Should().BeTrue();
        setup.CredentialState.Should().Be(ProviderCredentialStateDto.Configured);
        setup.Warnings.Should().Contain(warning => warning.Contains("live provider routing remains disabled", StringComparison.OrdinalIgnoreCase));

        var connectionsResponse = await client.GetAsync(UiApiRoutes.ProviderRoutingConnections);
        connectionsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var connections = await ReadAsync<ProviderConnectionDto[]>(connectionsResponse);
        connections.Should().ContainSingle(connection =>
            connection.ConnectionId == "alpaca" &&
            !connection.Enabled &&
            connection.ConnectionMode == nameof(ProviderConnectionMode.ReadOnly));

        var bindingsResponse = await client.GetAsync(UiApiRoutes.ProviderRoutingBindings);
        bindingsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var bindings = await ReadAsync<ProviderBindingDto[]>(bindingsResponse);
        bindings.Where(binding => binding.ConnectionId == "alpaca").Should().OnlyContain(binding => !binding.Enabled);

        var previewResponse = await client.PostAsync(
            UiApiRoutes.ProviderRoutingPreview,
            JsonContent(new RoutePreviewRequest(Capability: "RealtimeMarketData", Symbol: "AAPL")));

        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var preview = await ReadAsync<RoutePreviewResponse>(previewResponse);
        preview.IsRoutable.Should().BeFalse();
    }

    [Fact]
    public async Task ConfigureProvider_MissingOrPartialCredentials_CreatesNoActiveBinding()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        var response = await client.PostAsync(UiApiRoutes.ProviderConfigure, JsonContent(new
        {
            kind = "alpaca",
            displayName = "Alpaca Partial Setup",
            apiKey = $"alpaca-partial-key-{Guid.NewGuid():N}",
            environment = "paper",
            capabilities = new[] { "streaming" }
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var setup = await ReadAsync<ProviderSetupResult>(response);
        setup.CredentialState.Should().Be(ProviderCredentialStateDto.Partial);
        setup.Warnings.Should().Contain(warning => warning.Contains("routing remains disabled", StringComparison.OrdinalIgnoreCase));

        var bindingsResponse = await client.GetAsync(UiApiRoutes.ProviderRoutingBindings);
        bindingsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var bindings = await ReadAsync<ProviderBindingDto[]>(bindingsResponse);
        bindings.Where(binding => binding.ConnectionId == "alpaca").Should().OnlyContain(binding => !binding.Enabled);

        var connectionsResponse = await client.GetAsync(UiApiRoutes.ProviderRoutingConnections);
        connectionsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var connections = await ReadAsync<ProviderConnectionDto[]>(connectionsResponse);
        connections.Should().ContainSingle(connection => connection.ConnectionId == "alpaca" && !connection.Enabled);
    }

    [Fact]
    public async Task ConfigureProvider_VerifiedCredentialsWithCertification_EnableIntendedLiveConnectionMode()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();
        var credentialStore = app.Services.GetRequiredService<IProviderCredentialStore>();
        var configStore = app.Services.GetRequiredService<ApplicationConfigStore>();

        await credentialStore.SaveAsync(new ProviderCredentialSaveRequest(
            "alpaca",
            new Dictionary<string, string?>
            {
                ["KeyId"] = $"alpaca-verified-key-{Guid.NewGuid():N}",
                ["SecretKey"] = $"alpaca-verified-secret-{Guid.NewGuid():N}"
            },
            Environment: "live",
            Actor: "provider-routing-endpoint-test"));
        await credentialStore.RecordVerificationAsync(new ProviderCredentialVerificationUpdate(
            "alpaca",
            Success: true,
            ExternalAccountId: "alpaca-live-account",
            Actor: "provider-routing-endpoint-test"));

        var cfg = configStore.Load();
        var section = ProviderRoutingConfigExtensions.GetSection(cfg);
        await configStore.SaveAsync(cfg with
        {
            ProviderConnections = section with
            {
                Certifications = new[]
                {
                    new ProviderCertificationConfig(
                        ConnectionId: "alpaca",
                        Status: "Passed",
                        LastRunAt: DateTimeOffset.UtcNow,
                        ExpiresAt: DateTimeOffset.UtcNow.AddDays(30),
                        ProductionReady: true,
                        Checks: new[] { "Credential verification passed." },
                        Notes: new[] { "Explicit test certification." })
                }
            }
        });

        var response = await client.PostAsync(UiApiRoutes.ProviderConfigure, JsonContent(new
        {
            kind = "alpaca",
            displayName = "Alpaca Certified Live Setup",
            environment = "live",
            capabilities = new[] { "streaming", "backfill" }
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var setup = await ReadAsync<ProviderSetupResult>(response);
        setup.CredentialState.Should().Be(ProviderCredentialStateDto.Verified);
        setup.Warnings.Should().NotContain(warning => warning.Contains("routing remains disabled", StringComparison.OrdinalIgnoreCase));

        var connectionsResponse = await client.GetAsync(UiApiRoutes.ProviderRoutingConnections);
        connectionsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var connections = await ReadAsync<ProviderConnectionDto[]>(connectionsResponse);
        connections.Should().ContainSingle(connection =>
            connection.ConnectionId == "alpaca" &&
            connection.Enabled &&
            connection.ConnectionMode == nameof(ProviderConnectionMode.Live));

        var bindingsResponse = await client.GetAsync(UiApiRoutes.ProviderRoutingBindings);
        bindingsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var bindings = await ReadAsync<ProviderBindingDto[]>(bindingsResponse);
        bindings.Where(binding => binding.ConnectionId == "alpaca").Should().OnlyContain(binding => binding.Enabled);
    }

    [Fact]
    public async Task ConfigureProvider_WithPolygonCredential_StoresCredentialAndRoutesReferenceData()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();
        var credentialStore = app.Services.GetRequiredService<IProviderCredentialStore>();
        var apiKey = $"polygon-key-{Guid.NewGuid():N}";

        var configureResponse = await client.PostAsync(UiApiRoutes.ProviderConfigure, JsonContent(new
        {
            kind = "polygon",
            displayName = "Polygon Reference Setup",
            apiKey,
            capabilities = new[] { "reference" }
        }));

        configureResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var setup = await ReadAsync<ProviderSetupResult>(configureResponse);
        setup.ProviderId.Should().Be("polygon");
        setup.BindingIds.Should().Contain("polygon-reference-data");
        setup.CredentialState.Should().Be(ProviderCredentialStateDto.Configured);

        var stored = await credentialStore.ReadForProviderAsync("polygon");
        stored.Should().NotBeNull();
        stored!.Get("ApiKey").Should().Be(apiKey);

        var previewResponse = await client.PostAsync(
            UiApiRoutes.ProviderRoutingPreview,
            JsonContent(new RoutePreviewRequest(Capability: "ReferenceData", Symbol: "SPY")));

        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var preview = await ReadAsync<RoutePreviewResponse>(previewResponse);
        preview.IsRoutable.Should().BeFalse("newly saved provider credentials are not routable until verification succeeds");
        preview.SelectedConnectionId.Should().BeNull();
        preview.SelectedProviderFamilyId.Should().BeNull();
    }

    [Fact]
    public async Task ConfigureProvider_WithPlaidCredential_StoresCredentialOnlyWithoutMarketDataRoute()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();
        var credentialStore = (FileProviderCredentialStore)app.Services.GetRequiredService<IProviderCredentialStore>();
        var configStore = app.Services.GetRequiredService<ApplicationConfigStore>();
        var clientId = $"plaid-client-{Guid.NewGuid():N}";
        var secret = $"plaid-secret-{Guid.NewGuid():N}";

        var response = await client.PostAsync(UiApiRoutes.ProviderConfigure, JsonContent(new
        {
            kind = "plaid",
            displayName = "Plaid Sandbox Setup",
            apiKey = clientId,
            apiSecret = secret,
            environment = "sandbox",
            capabilities = new[] { "banking", "identity", "investments" }
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var responseJson = await response.Content.ReadAsStringAsync();
        responseJson.Should().NotContain(clientId);
        responseJson.Should().NotContain(secret);

        var result = Deserialize<ProviderSetupResult>(responseJson);
        result.Success.Should().BeTrue();
        result.ProviderId.Should().Be("plaid");
        result.ConnectionId.Should().BeNull();
        result.BindingIds.Should().BeEmpty();
        result.CredentialState.Should().Be(ProviderCredentialStateDto.Configured);
        result.CredentialSource.Should().Be(ProviderCredentialSourceDto.LocalEncryptedStore);
        result.CredentialReference.Should().Be("vault:plaid/sandbox");
        result.Environment.Should().Be("sandbox");

        var stored = await credentialStore.ReadForProviderAsync("plaid");
        stored.Should().NotBeNull();
        stored!.Get("ClientId").Should().Be(clientId);
        stored.Get("Secret").Should().Be(secret);

        var configJson = await File.ReadAllTextAsync(configStore.ConfigPath);
        configJson.Should().NotContain(clientId);
        configJson.Should().NotContain(secret);
        configJson.Should().NotContain("Plaid Sandbox Setup");

        var connectionsResponse = await client.GetAsync(UiApiRoutes.ProviderRoutingConnections);
        connectionsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var connections = await ReadAsync<ProviderConnectionDto[]>(connectionsResponse);
        connections.Should().NotContain(connection => connection.ProviderFamilyId == "plaid");

        var bindingsResponse = await client.GetAsync(UiApiRoutes.ProviderRoutingBindings);
        bindingsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var bindings = await ReadAsync<ProviderBindingDto[]>(bindingsResponse);
        bindings.Should().NotContain(binding => binding.ConnectionId == "plaid");

        var vaultJson = await File.ReadAllTextAsync(credentialStore.VaultPath);
        vaultJson.Should().NotContain(clientId);
        vaultJson.Should().NotContain(secret);
    }

    [Fact]
    public async Task ProviderRoutingEndpoints_ReturnConnectionsBindingsAndTrustSnapshotsForSetupConnections()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        var configureResponse = await client.PostAsync(UiApiRoutes.ProviderConfigure, JsonContent(new
        {
            kind = "yahoo",
            displayName = "Yahoo Backfill Setup",
            capabilities = new[] { "backfill" }
        }));
        configureResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var connectionsResponse = await client.GetAsync(UiApiRoutes.ProviderRoutingConnections);
        connectionsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var connections = await ReadAsync<ProviderConnectionDto[]>(connectionsResponse);
        connections.Should().ContainSingle(connection =>
            connection.ConnectionId == "yahoo" &&
            connection.ProviderFamilyId == "yahoo" &&
            connection.ProductionReady == false);

        var bindingsResponse = await client.GetAsync(UiApiRoutes.ProviderRoutingBindings);
        bindingsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var bindings = await ReadAsync<ProviderBindingDto[]>(bindingsResponse);
        bindings.Should().ContainSingle(binding =>
            binding.ConnectionId == "yahoo" &&
            binding.Capability == nameof(ProviderCapabilityKind.HistoricalBars));

        var trustResponse = await client.GetAsync(UiApiRoutes.ProviderRoutingTrustSnapshots);
        trustResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var trust = await ReadAsync<ProviderTrustSnapshotDto[]>(trustResponse);
        trust.Should().ContainSingle(snapshot =>
            snapshot.ConnectionId == "yahoo" &&
            snapshot.ProviderFamilyId == "yahoo" &&
            snapshot.IsHealthy &&
            !snapshot.IsProductionReady);
    }

    [Fact]
    public async Task ConfigureProvider_WithoutSecrets_AllowsManageProvidersOnly()
    {
        await using var app = await CreateAppAsync(UserPermission.ManageProviders);

        var response = await app.GetTestClient().PostAsync(UiApiRoutes.ProviderConfigure, JsonContent(new
        {
            kind = "yahoo",
            displayName = "Yahoo No Secret Setup",
            capabilities = new[] { "backfill" }
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ConfigureProvider_WithSubmittedCredentials_RequiresManageCredentials()
    {
        await using var app = await CreateAppAsync(UserPermission.ManageProviders);

        var response = await app.GetTestClient().PostAsync(UiApiRoutes.ProviderConfigure, JsonContent(new
        {
            kind = "polygon",
            displayName = "Polygon Secret Setup",
            apiKey = "polygon-permission-key",
            capabilities = new[] { "backfill" }
        }));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ConfigureProvider_WithoutManageProviders_ReturnsForbidden()
    {
        await using var app = await CreateAppAsync(UserPermission.ViewTrades);

        var response = await app.GetTestClient().PostAsync(UiApiRoutes.ProviderConfigure, JsonContent(new
        {
            kind = "yahoo",
            displayName = "Yahoo Forbidden Setup",
            capabilities = new[] { "backfill" }
        }));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ConfigureProvider_WithNoPermissionContext_ReturnsForbidden()
    {
        await using var app = await CreateAppAsync(permissions: null);

        var response = await app.GetTestClient().PostAsync(UiApiRoutes.ProviderConfigure, JsonContent(new
        {
            kind = "polygon",
            displayName = "Polygon Local Setup",
            apiKey = "local-setup-key",
            capabilities = new[] { "backfill" }
        }));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DataSourceMutation_WithoutPermissionContext_ReturnsUnauthorized()
    {
        await using var app = await CreateAppAsync(permissions: null);

        var response = await app.GetTestClient().PostAsync(UiApiRoutes.ConfigDataSources, JsonContent(new
        {
            name = "No Session Provider",
            provider = "Yahoo",
            type = "Historical"
        }));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DataSourceMutation_WithoutManageProviders_ReturnsForbiddenAndDoesNotPersist()
    {
        await using var app = await CreateAppAsync(UserPermission.ViewTrades);
        var configStore = app.Services.GetRequiredService<ApplicationConfigStore>();

        var response = await app.GetTestClient().PostAsync(UiApiRoutes.ConfigDataSources, JsonContent(new
        {
            name = "Forbidden Provider",
            provider = "Yahoo",
            type = "Historical"
        }));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var configJson = await File.ReadAllTextAsync(configStore.ConfigPath);
        configJson.Should().NotContain("Forbidden Provider");
    }

    [Fact]
    public async Task DataSourceMutation_WithSubmittedCredentials_RequiresManageCredentials()
    {
        await using var app = await CreateAppAsync(UserPermission.ManageProviders);
        var configStore = app.Services.GetRequiredService<ApplicationConfigStore>();

        var response = await app.GetTestClient().PostAsync(UiApiRoutes.ConfigDataSources, JsonContent(new
        {
            name = "Polygon Secret Provider",
            provider = "Polygon",
            type = "Historical",
            polygon = new
            {
                apiKey = "polygon-secret-that-must-not-persist"
            }
        }));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var configJson = await File.ReadAllTextAsync(configStore.ConfigPath);
        configJson.Should().NotContain("polygon-secret-that-must-not-persist");
    }

    [Fact]
    public async Task DataSourceMutation_WithManageProvidersAndNoSecrets_Persists()
    {
        await using var app = await CreateAppAsync(UserPermission.ManageProviders);
        var configStore = app.Services.GetRequiredService<ApplicationConfigStore>();

        var response = await app.GetTestClient().PostAsync(UiApiRoutes.ConfigDataSources, JsonContent(new
        {
            name = "Yahoo Historical",
            provider = "Yahoo",
            type = "Historical",
            enabled = true
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var configJson = await File.ReadAllTextAsync(configStore.ConfigPath);
        configJson.Should().Contain("Yahoo Historical");
    }

    private static async Task<WebApplication> CreateAppAsync(
        UserPermission? permissions = UserPermission.ManageProviders | UserPermission.ManageCredentials)
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "provider-routing-endpoints", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "appsettings.json");
        var dataRoot = Path.Combine(root, "data");
        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(new { dataRoot }));

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(new ApplicationConfigStore(configPath));
        builder.Services.AddSingleton(new SharedConfigStore(configPath));
        builder.Services.AddSingleton<IProviderCredentialStore>(_ => new FileProviderCredentialStore(dataRoot));
        builder.Services.AddSingleton<ProviderSetupService>();
        builder.Services.AddSingleton<ProviderConnectionService>();
        builder.Services.AddSingleton<ProviderBindingService>();
        builder.Services.AddSingleton<KernelObservabilityService>();
        builder.Services.AddSingleton<IProviderConnectionHealthSource, HealthyProviderConnectionHealthSource>();
        builder.Services.AddSingleton<IProviderFamilyCatalogService, TestProviderFamilyCatalogService>();
        builder.Services.AddSingleton<ProviderRoutingService>();
        builder.Services.AddSingleton<IBestOfBreedProviderSelector, BestOfBreedProviderSelector>();
        builder.Services.AddSingleton<ProviderRouteExplainabilityService>();
        builder.Services.AddSingleton<ProviderTrustScoringService>();
        builder.Services.AddRateLimiter(options =>
        {
            options.AddPolicy(UiEndpoints.MutationRateLimitPolicy, _ =>
                RateLimitPartition.GetNoLimiter<string>("test"));
        });

        var app = builder.Build();
        if (permissions is not null)
        {
            app.Use(async (context, next) =>
            {
                context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions.Value;
                await next();
            });
        }

        app.UseRateLimiter();
        app.MapProviderEndpoints(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        app.MapProviderRoutingEndpoints(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await app.StartAsync();
        return app;
    }

    private static StringContent JsonContent(object payload)
        => new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return Deserialize<T>(json);
    }

    private static T Deserialize<T>(string json)
    {
        var result = JsonSerializer.Deserialize<T>(json, JsonOptions);
        result.Should().NotBeNull($"expected {typeof(T).Name}, got {json}");
        return result!;
    }

    private sealed class HealthyProviderConnectionHealthSource : IProviderConnectionHealthSource
    {
        public ValueTask<ProviderConnectionHealthSnapshot> GetHealthAsync(
            string connectionId,
            string providerFamilyId,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new ProviderConnectionHealthSnapshot(
                connectionId,
                providerFamilyId,
                IsHealthy: true,
                Status: "healthy",
                Score: 100,
                CheckedAt: DateTimeOffset.UtcNow));
        }
    }

    private sealed class TestProviderFamilyCatalogService : IProviderFamilyCatalogService
    {
        private static readonly IReadOnlyDictionary<string, IProviderFamilyAdapter> Families =
            new[]
            {
                "alpaca",
                "polygon",
                "yahoo",
                "ib",
                "synthetic"
            }.ToDictionary(
                static providerId => providerId,
                static providerId => (IProviderFamilyAdapter)new TestProviderFamilyAdapter(providerId),
                StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<IProviderFamilyAdapter> GetFamilies() => Families.Values.ToArray();

        public IProviderFamilyAdapter? GetFamily(string providerFamilyId)
            => Families.TryGetValue(ProviderCredentialCatalog.NormalizeProviderId(providerFamilyId), out var family)
                ? family
                : null;
    }

    private sealed class TestProviderFamilyAdapter : IProviderFamilyAdapter
    {
        private static readonly ProviderCapabilityDescriptor[] Descriptors =
        [
            new(ProviderCapabilityKind.RealtimeMarketData, "Test realtime market data"),
            new(ProviderCapabilityKind.HistoricalBars, "Test historical bars"),
            new(ProviderCapabilityKind.ReferenceData, "Test reference data")
        ];

        public TestProviderFamilyAdapter(string providerFamilyId)
        {
            ProviderFamilyId = providerFamilyId;
            DisplayName = providerFamilyId;
            Description = "Test provider family.";
        }

        public string ProviderFamilyId { get; }

        public string DisplayName { get; }

        public string Description { get; }

        public IReadOnlyList<ProviderCapabilityDescriptor> CapabilityDescriptors => Descriptors;

        public bool SupportsCapability(ProviderCapabilityKind capability)
            => Descriptors.Any(descriptor => descriptor.Kind == capability);

        public Task InitializeConnectionAsync(string connectionId, ProviderConnectionScope scope, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<ProviderConnectionTestResult> TestConnectionAsync(string connectionId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new ProviderConnectionTestResult(
                Success: true,
                Checks: ["Test provider is available."],
                TestedAt: DateTimeOffset.UtcNow,
                Status: "healthy"));
        }

        public ValueTask<object?> ResolveCapabilityAsync(ProviderCapabilityKind capability, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return ValueTask.FromResult<object?>(null);
        }
    }
}
