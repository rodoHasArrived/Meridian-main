using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.RateLimiting;
using FluentAssertions;
using Meridian.Application.ProviderRouting;
using Meridian.Contracts.Api;
using Meridian.Core.Config;
using Meridian.DataIntegration.Credentials;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Ui;

public sealed class ProviderCredentialOwnershipEndpointsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-ownership-http", Guid.NewGuid().ToString("N"));
    private const UserPermission Permissions = UserPermission.ManageCredentials | UserPermission.ManageProviders | UserPermission.ModifyConfig;
    private Meridian.Application.UI.ConfigStore Config => new(Path.Combine(_root, "appsettings.json"));

    [Fact]
    public async Task OwnedConnection_AuthenticatedCreationMigrationBindingAndRollbackRoundTrip()
    {
        await using var app = await CreateAppAsync();
        var http = app.GetTestClient();
        var create = await http.PostAsJsonAsync("/api/provider-routing/connections", new CreateOwnedProviderConnectionRequest(
            new("owned", "alpaca", "Paper evidence", ExternalAccountId: "account-a"), "paper"));
        create.StatusCode.Should().Be(HttpStatusCode.OK);
        Config.LoadRequired().ProviderConnections!.Connections!.Single().TenantId.Should().Be("tenant-a");
        var preview = await http.GetFromJsonAsync<ProviderCredentialStoreStatus>(
            "/api/provider-routing/connections/owned/credentials/migration-preview?providerId=alpaca");
        var request = new ProviderCredentialMigrationRequest("alpaca", "migration-a", preview!.AuditMetadata["credentialGeneration"]);
        var migrate = await http.PostAsJsonAsync("/api/provider-routing/connections/owned/credentials/migrate", request);
        migrate.StatusCode.Should().Be(HttpStatusCode.OK);
        var receipt = await migrate.Content.ReadFromJsonAsync<ProviderCredentialMigrationReceipt>();
        receipt!.Actor.Should().Be("operator-a");
        receipt.Scope.TenantId.Should().Be("tenant-a");
        var bind = await http.PutAsJsonAsync("/api/provider-routing/connections/owned/runtime-credentials", new BindProviderRuntimeCredentialRequest("alpaca"));
        bind.StatusCode.Should().Be(HttpStatusCode.OK);
        Config.LoadRequired().ProviderConnections!.RuntimeCredentials.Should().ContainSingle();
        var rollback = await http.PostAsJsonAsync("/api/provider-routing/connections/owned/credentials/rollback", request);
        rollback.StatusCode.Should().Be(HttpStatusCode.OK);
        (await rollback.Content.ReadFromJsonAsync<ProviderCredentialMigrationReceipt>())!.RolledBack.Should().BeTrue();
        // A rollback never silently changes the host default: its now-empty scoped record blocks.
        (await app.Services.GetRequiredService<IProviderCredentialStore>().ReadForProviderAsync("alpaca"))!.Get("KeyId").Should().Be("legacy-key");
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task OwnershipCommands_RequireAllAdministrativeGrantsTenantAndActor(bool grants, bool tenant, bool actor)
    {
        await using var app = await CreateAppAsync(grants ? Permissions : UserPermission.ManageCredentials, tenant, actor);
        var response = await app.GetTestClient().PostAsJsonAsync("/api/provider-routing/connections",
            new CreateOwnedProviderConnectionRequest(new("owned", "alpaca", "Paper", ExternalAccountId: "account-a"), "paper"));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Config.LoadRequired().ProviderConnections.Should().BeNull();
    }

    [Fact]
    public async Task ForeignConnection_CannotBePreviewedBoundMigratedOrRolledBack()
    {
        await using var app = await CreateAppAsync();
        await new ProviderConnectionService(Config).UpsertForTenantAsync(new("foreign", "alpaca", "Foreign", ExternalAccountId: "account-b"), "tenant-b", "paper");
        var http = app.GetTestClient();
        (await http.GetAsync("/api/provider-routing/connections/foreign/credentials/migration-preview?providerId=alpaca")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await http.PutAsJsonAsync("/api/provider-routing/connections/foreign/runtime-credentials", new BindProviderRuntimeCredentialRequest("alpaca"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        foreach (var action in new[] { "migrate", "rollback" })
        {
            var response = await http.PostAsJsonAsync($"/api/provider-routing/connections/foreign/credentials/{action}",
                new ProviderCredentialMigrationRequest("alpaca", "migration-a", "generation"));
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task ScopedLifecycle_RechecksChangedOwnershipAndLegacyMutationCannotBypassIt()
    {
        await using var app = await CreateAppAsync();
        var connections = new ProviderConnectionService(Config);
        await connections.UpsertForTenantAsync(new("owned", "alpaca", "Paper", ExternalAccountId: "account-a"), "tenant-a", "paper");
        var lifecycle = app.Services.GetRequiredService<ProviderConnectionLifecycleService>();
        var selected = lifecycle.ForConnection("owned", "tenant-a", "alpaca");
        Func<Task> legacySave = () => lifecycle.SaveCredentialsAsync("alpaca", new(new Dictionary<string, string?> { ["KeyId"] = "forbidden" }));
        await legacySave.Should().ThrowAsync<UnauthorizedAccessException>();
        await connections.DeleteForTenantAsync("owned", "tenant-a");
        Func<Task> staleSave = () => selected.SaveCredentialsAsync("alpaca", new(new Dictionary<string, string?> { ["KeyId"] = "forbidden" }));
        await staleSave.Should().ThrowAsync<UnauthorizedAccessException>();
        (await app.Services.GetRequiredService<IProviderCredentialStore>().ReadForProviderAsync("alpaca"))!.Get("KeyId").Should().Be("legacy-key");
    }

    private async Task<WebApplication> CreateAppAsync(UserPermission permissions = Permissions, bool tenant = true, bool actor = true)
    {
        Directory.CreateDirectory(_root);
        await Config.SaveAsync(new AppConfig(DataRoot: _root));
        var store = new FileProviderCredentialStore(_root);
        await store.SaveAsync(new("alpaca", new Dictionary<string, string?> { ["KeyId"] = "legacy-key", ["SecretKey"] = "legacy-secret" }, "paper"));
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(Config);
        builder.Services.AddSingleton(new ConfigStore(Config.ConfigPath));
        builder.Services.AddSingleton<IProviderCredentialStore>(store);
        builder.Services.AddSingleton<ProviderConnectionService>();
        builder.Services.AddSingleton<ProviderRuntimeCredentialService>();
        builder.Services.AddSingleton<ProviderConnectionLifecycleService>();
        builder.Services.AddRateLimiter(options => options.AddPolicy(UiEndpoints.MutationRateLimitPolicy, _ => RateLimitPartition.GetNoLimiter("test")));
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            if (actor)
                context.Items[LoginSessionMiddleware.CurrentUserKey] = "operator-a";
            if (tenant)
            {
                context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "tenant-a";
                context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "tenant-a";
            }
            await next();
        });
        app.UseRateLimiter();
        app.MapProviderCredentialOwnershipEndpoints(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await app.StartAsync();
        return app;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}
