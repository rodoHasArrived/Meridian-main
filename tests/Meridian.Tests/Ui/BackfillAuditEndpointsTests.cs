using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Backfill;
using Meridian.Contracts.Api;
using Meridian.Contracts.Configuration;
using Meridian.Core.Config;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using UiBackfillCoordinator = Meridian.Ui.Shared.Services.BackfillCoordinator;

namespace Meridian.Tests.Ui;

public sealed class BackfillAuditEndpointsTests
{
    [Fact]
    public async Task GetProviderConfigAudit_WithoutManageProviders_ReturnsForbidden()
    {
        await using var app = await CreateAppAsync(UserPermission.ViewConfig);

        var response = await app.GetTestClient().GetAsync(UiApiRoutes.BackfillProviderConfigAudit);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetProviderConfigAudit_WithoutAuditReader_FailsClosed()
    {
        await using var app = await CreateAppAsync(UserPermission.ManageProviders);

        var response = await app.GetTestClient().GetAsync(UiApiRoutes.BackfillProviderConfigAudit);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("type").GetString()
            .Should().Be(ApiProblemTypes.ServiceUnavailable);
    }

    [Fact]
    public async Task GetProviderConfigAudit_WithManageProviders_RedactsConfigurationValues()
    {
        const string secret = "backfill-audit-secret";
        await using var app = await CreateAppAsync(UserPermission.ManageProviders, new StubAuditReader(
        [
            new ProviderConfigAuditEntryDto
            {
                ProviderId = "alpaca",
                Action = "updated",
                PreviousValue = $"{{\"apiKey\":\"{secret}\"}}",
                NewValue = $"{{\"token\":\"{secret}\"}}"
            }
        ]));

        var response = await app.GetTestClient().GetAsync(UiApiRoutes.BackfillProviderConfigAudit);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(secret);

        var entries = await response.Content.ReadFromJsonAsync<ProviderConfigAuditEntryDto[]>();
        entries.Should().ContainSingle();
        entries![0].ProviderId.Should().Be("alpaca");
        entries[0].Action.Should().Be("updated");
        entries[0].PreviousValue.Should().BeNull();
        entries[0].NewValue.Should().BeNull();
    }

    private static async Task<WebApplication> CreateAppAsync(
        UserPermission permissions,
        IBackfillProviderConfigAuditReader? auditReader = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "backfill-audit-endpoints", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "appsettings.json");
        await File.WriteAllTextAsync(configPath, "{}");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(new ConfigStore(configPath));
        builder.Services.AddSingleton<UiBackfillCoordinator>();
        if (auditReader is not null)
        {
            builder.Services.AddSingleton(auditReader);
        }

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "backfill-audit-operator";
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "tenant-backfill-audit-tests";
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            await next();
        });
        app.MapBackfillEndpoints(new JsonSerializerOptions(JsonSerializerDefaults.Web), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        await app.StartAsync();
        return app;
    }

    private sealed class StubAuditReader(IReadOnlyList<ProviderConfigAuditEntryDto> entries) : IBackfillProviderConfigAuditReader
    {
        public IReadOnlyList<ProviderConfigAuditEntryDto> GetAuditLog(int maxEntries = 100) => entries.Take(maxEntries).ToArray();
    }
}
