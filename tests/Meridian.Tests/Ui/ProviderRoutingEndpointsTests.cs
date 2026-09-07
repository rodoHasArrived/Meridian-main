using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using FluentAssertions;
using Meridian.Application.Config.Credentials;
using Meridian.DataIntegration.Credentials;
using Meridian.Application.ProviderRouting;
using Meridian.Contracts.Api;
using Meridian.Core.Config;
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
    public async Task TenantConnectionOwnership_ReloadsThroughSharedContractsAndSelectsOnlyItsCredentials()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "connection-ownership", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "appsettings.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { dataRoot = root }));
            var service = new ProviderConnectionService(new ApplicationConfigStore(path));
            var first = new CreateProviderConnectionRequest("connection-a", "alpaca", "First", ExternalAccountId: "account-a");
            var second = new CreateProviderConnectionRequest("connection-b", "alpaca", "Second", ExternalAccountId: "account-b");
            var created = await service.UpsertForTenantAsync(first, "tenant-a", "paper");
            await service.UpsertForTenantAsync(second, "tenant-b", "live");
            created.TenantId.Should().Be("tenant-a");
            created.CredentialEnvironment.Should().Be("paper");
            var reopened = new ProviderConnectionService(new ApplicationConfigStore(path));
            var scope = await reopened.GetCredentialScopeForTenantAsync("connection-a", "tenant-a");
            scope.Should().Be(new ProviderCredentialScope("tenant-a", "connection-a", "account-a", "paper"));
            (await reopened.GetCredentialScopeForTenantAsync("connection-a", "tenant-b")).Should().BeNull();

            var vault = new FileProviderCredentialStore(root);
            await vault.SaveScopedAsync(new ProviderCredentialSaveRequest("alpaca",
                new Dictionary<string, string?> { ["KeyId"] = "owned-key", ["SecretKey"] = "owned-secret" }, "paper"), scope!);
            var otherScope = await reopened.GetCredentialScopeForTenantAsync("connection-b", "tenant-b");
            (await vault.ReadScopedAsync("alpaca", otherScope!)).Should().BeNull();
            (await vault.ReadScopedAsync("alpaca", scope!))!.Get("KeyId").Should().Be("owned-key");
            var config = new ApplicationConfigStore(path).Load();
            var dto = JsonSerializer.Deserialize<ProviderConnectionsConfigDto>(JsonSerializer.Serialize(config.ProviderConnections), JsonOptions)!;
            dto.Connections!.Single(c => c.ConnectionId == "connection-a").TenantId.Should().Be("tenant-a");
            dto.Connections!.Single(c => c.ConnectionId == "connection-b").CredentialEnvironment.Should().Be("live");
            var response = Deserialize<ProviderConnectionDto>(JsonSerializer.Serialize(created));
            response.TenantId.Should().Be("tenant-a");
            response.CredentialEnvironment.Should().Be("paper");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task TenantConnectionOwnership_RejectsTakeoverReassignmentAndLegacyMutation()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "connection-ownership", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "appsettings.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { dataRoot = root }));
            var service = new ProviderConnectionService(new ApplicationConfigStore(path));
            var request = new CreateProviderConnectionRequest("owned", "alpaca", "Owned", ExternalAccountId: "account-a");
            await service.UpsertForTenantAsync(request, "tenant-a", "paper");
            var retained = await File.ReadAllTextAsync(path);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpsertForTenantAsync(request, "tenant-b", "paper"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpsertForTenantAsync(request with { ConnectionId = " owned " }, "tenant-b", "paper"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpsertForTenantAsync(request with { ExternalAccountId = "account-b" }, "tenant-a", "paper"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpsertForTenantAsync(request, "tenant-a", "live"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpsertForTenantAsync(request with { CredentialReference = "vault:alpaca/paper" }, "tenant-a", "paper"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpsertAsync(request));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync("owned"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteForTenantAsync("owned", "tenant-b"));
            (await File.ReadAllTextAsync(path)).Should().Be(retained);

            var legacy = request with { ConnectionId = "legacy" };
            await service.UpsertAsync(legacy);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpsertForTenantAsync(legacy, "tenant-a", "paper"));
            (await service.GetCredentialScopeForTenantAsync("legacy", "tenant-a")).Should().BeNull();
            (await service.DeleteForTenantAsync("owned", "tenant-a")).Should().BeTrue();
            (await service.GetConnectionAsync("legacy")).Should().NotBeNull();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

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
        preview.IsRoutable.Should().BeFalse();
        preview.SelectedConnectionId.Should().BeNull();
        preview.SelectedProviderFamilyId.Should().BeNull();
        preview.Candidates.Should().BeEmpty();
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

    [Theory]
    [InlineData(UiApiRoutes.ProviderRoutingConnections)]
    [InlineData(UiApiRoutes.ProviderRoutingBindings)]
    [InlineData(UiApiRoutes.ProviderRoutingTrustSnapshots)]
    public async Task RoutingDiscovery_ExcludesForeignAndUnassignedConnections(string route)
    {
        await using var app = await CreateAppAsync();
        var store = app.Services.GetRequiredService<ApplicationConfigStore>();
        var config = store.Load();
        config = config with
        {
            ProviderConnections = new ProviderConnectionsConfig(
            Connections: [
                new("owned", "yahoo", "Owned", TenantId: "tenant-test", ExternalAccountId: "account-owned", CredentialEnvironment: "paper"),
                new("foreign", "yahoo", "Foreign", TenantId: "tenant-other", ExternalAccountId: "account-foreign", CredentialEnvironment: "paper"),
                new("unassigned", "yahoo", "Unassigned")],
            Bindings: [
                new("owned-binding", ProviderCapabilityKind.HistoricalBars, "owned", FailoverConnectionIds: ["foreign", "unassigned"]),
                new("foreign-binding", ProviderCapabilityKind.HistoricalBars, "foreign"),
                new("unassigned-binding", ProviderCapabilityKind.HistoricalBars, "unassigned")])
        };
        await File.WriteAllTextAsync(store.ConfigPath, JsonSerializer.Serialize(config));
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "tenant-other");
        var response = await client.GetAsync(route + "?tenantId=tenant-other");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("owned").And.NotContain("foreign").And.NotContain("unassigned");
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetArrayLength().Should().Be(1);
        if (route == UiApiRoutes.ProviderRoutingTrustSnapshots)
            ((HealthyProviderConnectionHealthSource)app.Services.GetRequiredService<IProviderConnectionHealthSource>())
                .ConnectionIds.Should().Equal("owned");
    }

    [Fact]
    public async Task ProviderRoutingEndpoints_ReturnConnectionsBindingsAndTrustSnapshotsForOwnedSetupConnections()
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

        // Establish explicit retained ownership in this fixture; production never claims legacy connections implicitly.
        var store = app.Services.GetRequiredService<ApplicationConfigStore>();
        var config = store.Load();
        config = config with
        {
            ProviderConnections = config.ProviderConnections! with
            {
                Connections = config.ProviderConnections!.Connections!.Select(connection => connection with
                {
                    TenantId = "tenant-test",
                    ExternalAccountId = "yahoo-account",
                    CredentialEnvironment = "paper"
                }).ToArray()
            }
        };
        await File.WriteAllTextAsync(store.ConfigPath, JsonSerializer.Serialize(config));

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
    public async Task ConfigureProvider_WithNoPermissionContext_ReturnsUnauthorized()
    {
        await using var app = await CreateAppAsync(permissions: null);

        var response = await app.GetTestClient().PostAsync(UiApiRoutes.ProviderConfigure, JsonContent(new
        {
            kind = "polygon",
            displayName = "Polygon Local Setup",
            apiKey = "local-setup-key",
            capabilities = new[] { "backfill" }
        }));

        // The route now declares ManageProviders, and a declared route distinguishes the two
        // refusals the handler collapsed into one: no permissions snapshot at all is
        // unauthenticated (401), a snapshot without the permission is forbidden (403). A caller
        // with no permission context is the former. The request is refused either way.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
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


    [Fact]
    public async Task ConfigureProvider_UsesRegisteredHandlerWithoutChangingSetupService()
    {
        await using var app = await CreateAppAsync(registerServices: services =>
        {
            services.AddSingleton<IProviderSetupHandler>(new FakeProviderSetupHandler());
        });

        var response = await app.GetTestClient().PostAsync(UiApiRoutes.ProviderConfigure, JsonContent(new
        {
            kind = "fake-finnhub",
            displayName = "Fake Finnhub",
            apiKey = "fake-handler-key",
            environment = "paper",
            capabilities = new[] { "reference" }
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = Deserialize<ProviderSetupResult>(await response.Content.ReadAsStringAsync());
        result.Success.Should().BeTrue();
        result.ProviderId.Should().Be("finnhub");
        result.ConnectionId.Should().BeNull();
        result.Warnings.Should().Contain("Fake handler warning from DI.");

        var stored = await app.Services.GetRequiredService<IProviderCredentialStore>().ReadForProviderAsync("finnhub");
        stored.Should().NotBeNull();
        stored!.Get("ApiKey").Should().Be("fake-handler-key");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConfigureProvider_RequiresAndRetainsAuthenticatedActor(bool includeActor)
    {
        await using var app = await CreateAppAsync(includeActor: includeActor);
        var response = await app.GetTestClient().PostAsync(UiApiRoutes.ProviderConfigure, JsonContent(new
        {
            kind = "alpaca",
            displayName = "Actor Test",
            apiKey = "actor-key",
            apiSecret = "actor-secret",
            environment = "paper",
            capabilities = new[] { "streaming" },
            requestedBy = "forged-operator"
        }));
        var store = app.Services.GetRequiredService<IProviderCredentialStore>();
        if (!includeActor)
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            File.Exists(store.VaultPath).Should().BeFalse();
            return;
        }
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var auditPath = Path.Combine(Path.GetDirectoryName(store.VaultPath)!, "provider-credentials.audit.jsonl");
        var audit = JsonSerializer.Deserialize<JsonElement>((await File.ReadAllLinesAsync(auditPath)).Last());
        audit.GetProperty("actor").GetString().Should().Be("provider-routing-test-operator");
    }

    [Theory]
    [InlineData("tenant-test", "paper", true)]
    [InlineData("other-tenant", "paper", false)]
    [InlineData("tenant-test", "live", false)]
    public async Task ConfigureOwnedConnection_PreservesRoutingAndCannotChangeCredentialOwnership(string owner, string requestedEnvironment, bool allowed)
    {
        await using var app = await CreateAppAsync();
        var configStore = app.Services.GetRequiredService<ApplicationConfigStore>();
        await app.Services.GetRequiredService<ProviderConnectionService>().UpsertForTenantAsync(
            new CreateProviderConnectionRequest("existing", "alpaca", "Existing account", ExternalAccountId: "account-a"), owner, "paper");
        var retainedConfig = await File.ReadAllTextAsync(configStore.ConfigPath);
        var response = await app.GetTestClient().PostAsync(UiApiRoutes.ProviderConfigure + "?connectionId=existing", JsonContent(new
        {
            kind = "alpaca",
            displayName = "Must not create another connection",
            apiKey = "scoped-setup-key",
            apiSecret = "scoped-setup-secret",
            environment = requestedEnvironment,
            capabilities = new[] { "streaming" }
        }));
        var vault = (FileProviderCredentialStore)app.Services.GetRequiredService<IProviderCredentialStore>();
        (await File.ReadAllTextAsync(configStore.ConfigPath)).Should().Be(retainedConfig);
        if (allowed)
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var result = Deserialize<ProviderSetupResult>(await response.Content.ReadAsStringAsync());
            result.ConnectionId.Should().Be("existing");
            result.BindingIds.Should().BeEmpty();
            var stored = await vault.ReadScopedAsync("alpaca", new ProviderCredentialScope(owner, "existing", "account-a", "paper"));
            stored!.Get("KeyId").Should().Be("scoped-setup-key");
            stored.ExternalAccountId.Should().Be("account-a");
            stored.LastVerifiedAt.Should().BeNull();
            var audit = await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(vault.VaultPath)!, "provider-credentials.audit.jsonl"));
            audit.Should().Contain("provider-routing-test-operator").And.NotContain("scoped-setup-secret");
        }
        else
        {
            response.StatusCode.Should().Be(owner == "other-tenant" ? HttpStatusCode.Forbidden : HttpStatusCode.BadRequest);
            File.Exists(vault.VaultPath).Should().BeFalse();
        }
    }

    private static async Task<WebApplication> CreateAppAsync(
        UserPermission? permissions = UserPermission.ManageProviders | UserPermission.ManageCredentials,
        Action<IServiceCollection>? registerServices = null,
        bool includeActor = true)
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
        foreach (var handler in DefaultProviderSetupHandlers.Create())
        {
            builder.Services.AddSingleton(typeof(IProviderSetupHandler), handler);
        }
        builder.Services.AddSingleton<IProviderSetupRegistry, ProviderSetupRegistry>();
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
        registerServices?.Invoke(builder.Services);
        builder.Services.AddRateLimiter(options =>
        {
            options.AddPolicy(UiEndpoints.MutationRateLimitPolicy, _ =>
                RateLimitPartition.GetNoLimiter<string>("test"));
        });

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "tenant-test";
            context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "company-test";
            if (includeActor)
                context.Items[LoginSessionMiddleware.CurrentUserKey] = "provider-routing-test-operator";
            if (permissions is not null)
            {
                context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions.Value;
            }

            await next();
        });

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
        public List<string> ConnectionIds { get; } = [];
        public ValueTask<ProviderConnectionHealthSnapshot> GetHealthAsync(
            string connectionId,
            string providerFamilyId,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            ConnectionIds.Add(connectionId);
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

    private sealed class FakeProviderSetupHandler : IProviderSetupHandler
    {
        public ProviderSetupDescriptor Descriptor { get; } = new(
            "finnhub",
            ["fake-finnhub"],
            ProviderCredentialCatalog.BuildCredentialFields(ProviderCredentialCatalog.Find("finnhub")!),
            [],
            SupportsVerification: false,
            DefaultRoutingMode: ProviderConnectionMode.ReadOnly,
            EnableBindingsImmediately: false,
            CredentialOnly: true);

        public bool CanHandle(string providerIdOrAlias)
            => providerIdOrAlias.Equals("fake-finnhub", StringComparison.OrdinalIgnoreCase);

        public ProviderSetupValidationResult Validate(ProviderSetupContext context)
            => new(
                true,
                Credentials: new Dictionary<string, string?> { ["ApiKey"] = context.ApiKey },
                NormalizedEnvironment: "paper",
                Warnings: ["Fake handler warning from DI."]);

        public ProviderSetupExecutionResult BuildExecution(ProviderSetupContext context, ProviderCredentialStoreStatus credentialStatus)
            => new(null, ProviderConnectionMode.ReadOnly, EnableBindings: false, CredentialOnly: true, []);
    }

}
