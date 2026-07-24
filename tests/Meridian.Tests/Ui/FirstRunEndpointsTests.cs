using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Core.Config;
using Meridian.Identity.Auth;
using Meridian.Testing;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class FirstRunEndpointsTests
{
    [Fact]
    public async Task Complete_WhenUserLacksAdminMaintenancePermission_ReturnsForbidden()
    {
        await using var app = await CreateAppAsync(UserPermission.ViewStrategies);

        var response = await app.GetTestClient().PostAsJsonAsync("/api/workstation/first-run/complete", SampleRequest);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Complete_WhenUserHasAdminMaintenancePermission_CompletesOnboarding()
    {
        await using var app = await CreateAppAsync(UserPermission.AdminMaintenance);

        var response = await app.GetTestClient().PostAsJsonAsync("/api/workstation/first-run/complete", SampleRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static readonly CompleteFirstRunRequestDto SampleRequest = new(
        "monitor-investments", "personal-portfolio", "sample", true);

    private static async Task<WebApplication> CreateAppAsync(UserPermission permissions)
    {
        var artifacts = TestArtifactDirectory.Create(nameof(FirstRunEndpointsTests));
        var config = new ConfigStore(Path.Combine(artifacts.RootPath, "appsettings.json"));
        await config.SaveAsync(new AppConfig(DataRoot: artifacts.RootPath));

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development"
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(new FirstRunExperienceService(config));

        var app = builder.Build();
        app.Lifetime.ApplicationStopped.Register(artifacts.Dispose);
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "operator";
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            await next();
        });
        app.MapFirstRunEndpoints();
        await app.StartAsync();
        return app;
    }
}
