using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.RateLimiting;
using FluentAssertions;
using Meridian.Application.Composition.Startup;
using Meridian.Application.UI;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.Lifecycle;
using Meridian.Core.Config;
using Meridian.Identity.Auth;
using Meridian.Infrastructure;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Serilog;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class ProviderActivationEndpointTests
{
    [Fact]
    public async Task SingleSourceToggleReportsNoRuntimeEffectAndPreservesRunningEvidence()
    {
        using var fixture = new Fixture(supervised: false);
        var store = fixture.Store;
        var source = new DataSourceConfig("polygon-stream", "Polygon", DataSourceKind.Polygon);
        await File.WriteAllTextAsync(store.ConfigPath, JsonSerializer.Serialize(
            new AppConfig(DataSources: new DataSourcesConfig(Sources: [source])), AppConfigJsonOptions.Write));
        using var applier = ConfigurationRuntimeAppliers.Attach(store.ConfigPath, new SingleSourceProviderConfigApplier());
        var clientMock = new Mock<IMarketDataClient> { CallBase = true };
        clientMock.SetupGet(client => client.IsEnabled).Returns(true);
        using var activation = new StreamingProviderActivation(store.ConfigPath,
            new Dictionary<string, IMarketDataClient> { ["ibkr"] = clientMock.Object }, [], null, null);
        activation.MarkConnected();
        await using var app = await fixture.CreateAppAsync(UserPermission.ViewConfig);
        using var client = app.GetTestClient();

        await store.UpdateAsync(config => config with
        {
            DataSources = config.DataSources! with { Sources = [source with { Enabled = false }] }
        });
        var status = await client.GetFromJsonAsync<ProviderActivationResponse>("/api/providers/activation");

        status!.Providers.Should().Contain(entry => entry.ProviderId == "ibkr" &&
            entry.State == ProviderActivationStates.Active && entry.ActiveSinceUtc.HasValue);
        status.Providers.Should().Contain(entry => entry.ProviderId == "polygon-stream" &&
            entry.State == ProviderActivationStates.Disabled && entry.Reason!.Contains("single-source"));
        status.PendingPaths.Should().NotContain(path => path.EndsWith(".Enabled", StringComparison.OrdinalIgnoreCase));
        status.CanRestart.Should().BeFalse();
    }

    [Theory]
    [InlineData(UserPermission.AdminMaintenance, false, HttpStatusCode.Conflict)]
    [InlineData(UserPermission.ManageProviders, true, HttpStatusCode.Forbidden)]
    [InlineData(UserPermission.AdminMaintenance, true, HttpStatusCode.Accepted)]
    public async Task RestartRequiresMaintenancePermissionAndAnAcknowledgedSupervisor(
        UserPermission permissions, bool supervised, HttpStatusCode expected)
    {
        using var fixture = new Fixture(supervised);
        await using var app = await fixture.CreateAppAsync(permissions);
        using var client = app.GetTestClient();

        using var response = await client.PostAsync("/api/providers/restart", null);

        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
        if (expected == HttpStatusCode.Accepted)
        {
            var accepted = await response.Content.ReadFromJsonAsync<LifecycleShutdownAcceptedDto>();
            accepted!.Accepted.Should().BeTrue();
            response.Headers.Location!.OriginalString.Should().Be(accepted.OperationUri);
            fixture.Lifecycle.ActiveShutdownOperation!.Reason.Should().Be(LifecycleShutdownReason.Restart);
            fixture.Lifecycle.ActiveShutdownOperation.RequestedBy.Should().Be("provider-operator");
        }
        else
        {
            fixture.Lifecycle.ActiveShutdownOperation.Should().BeNull();
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "meridian-provider-activation", Guid.NewGuid().ToString("N"));
        private readonly RuntimeSupervisorConnection _supervisor = new();

        public Fixture(bool supervised)
        {
            Directory.CreateDirectory(_directory);
            Store = new ConfigStore(Path.Combine(_directory, "appsettings.json"));
            File.WriteAllText(Store.ConfigPath, "{}");
            Lifecycle = ApplicationLifecycleCoordinator.Create(Log.Logger);
            Lifecycle.TransitionTo(RuntimeLifecycleState.Ready, "ready");
            if (supervised)
                _supervisor.MarkConfirmed();
        }

        public ConfigStore Store { get; }
        public ApplicationLifecycleCoordinator Lifecycle { get; }

        public async Task<WebApplication> CreateAppAsync(UserPermission permissions)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(Store);
            builder.Services.AddSingleton<IRuntimeLifecycleControlPlane>(Lifecycle);
            builder.Services.AddSingleton(_supervisor);
            builder.Services.AddRateLimiter(options =>
                options.AddPolicy(UiEndpoints.MutationRateLimitPolicy, _ => RateLimitPartition.GetNoLimiter("test")));
            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                context.Items[LoginSessionMiddleware.CurrentUserKey] = "provider-operator";
                context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "provider-tenant";
                context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
                await next();
            });
            app.UseRateLimiter();
            app.MapProviderModuleEndpoints(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            await app.StartAsync();
            return app;
        }

        public void Dispose()
        {
            Lifecycle.Dispose();
            Directory.Delete(_directory, true);
        }
    }
}
