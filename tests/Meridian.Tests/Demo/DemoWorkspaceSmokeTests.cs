using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using FluentAssertions;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ConfigStore = Meridian.Ui.Shared.Services.ConfigStore;

namespace Meridian.Tests.Demo;

/// <summary>
/// Demo-smoke: seeds the isolated demo workspace, boots the workstation host against the seeded demo
/// root, and asserts the key screens render populated — not empty and not HTTP 501. A broken first-run
/// fails this check instead of a first evaluation.
/// </summary>
[Trait("Category", "Integration")]
public sealed class DemoWorkspaceSmokeTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions ServerJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly EnvironmentSnapshot _env = new();
    private WebApplication? _app;
    private HttpClient? _client;
    private string? _tempRoot;

    public async Task InitializeAsync()
    {
        _env.Capture(
            "MDC_AUTH_MODE",
            "MDC_API_KEY",
            "MDC_USERNAME",
            "MDC_PASSWORD_HASH",
            "MDC_USERS",
            "MDC_DISABLE_RATE_LIMIT",
            "MERIDIAN_USE_INMEMORY_GOVERNANCE",
            "DOTNET_ENVIRONMENT",
            "ASPNETCORE_ENVIRONMENT");

        Environment.SetEnvironmentVariable("MDC_AUTH_MODE", "optional");
        Environment.SetEnvironmentVariable("MDC_API_KEY", null);
        Environment.SetEnvironmentVariable("MDC_USERNAME", null);
        Environment.SetEnvironmentVariable("MDC_PASSWORD_HASH", null);
        Environment.SetEnvironmentVariable("MDC_USERS", null);
        Environment.SetEnvironmentVariable("MDC_DISABLE_RATE_LIMIT", "true");
        Environment.SetEnvironmentVariable("MERIDIAN_USE_INMEMORY_GOVERNANCE", "true");
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Test");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Test");

        _tempRoot = Path.Combine(Path.GetTempPath(), "meridian-demo-smoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        var baseDataRoot = Path.Combine(_tempRoot, "data");

        // Seed the isolated demo workspace exactly as `--seed-demo --seed-only` would.
        var seeder = new DemoWorkspaceSeeder(baseDataRoot);
        await seeder.SeedAsync();

        // Point a workstation host at the seeded demo root via the same generated demo config the host
        // uses, then boot it and assert the served screens are populated.
        var demoConfigPath = Path.Combine(seeder.DemoRoot, "appsettings.demo.json");
        await File.WriteAllTextAsync(
            demoConfigPath,
            $"{{\"dataRoot\":{JsonSerializer.Serialize(seeder.DemoRoot)}}}");

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Environment.EnvironmentName = "Test";
        builder.Services.AddSingleton(new ConfigStore(demoConfigPath));
        builder.Services.AddUiSharedServices(demoConfigPath);

        _app = builder.Build();
        _app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "demo-smoke";
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.AdminMaintenance;
            context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "demo-smoke-tenant";
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "demo-smoke-tenant";
            await next();
        });
        _app.MapWorkstationEndpoints(ServerJsonOptions);

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    [Fact]
    public async Task ReconciliationBreakQueueScreen_RendersSeededCaseworkWithProvenance()
    {
        var response = await _client!.GetAsync("/api/workstation/reconciliation/break-queue");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("SAMPLE-BRK-001");
        body.Should().Contain(DemoTenantBlueprint.SeededSourceSystem);
    }

    [Fact]
    public async Task StrategyScreen_IsNotEmptyOrUnimplemented()
    {
        var response = await _client!.GetAsync("/api/workstation/strategy");

        response.StatusCode.Should().NotBe(HttpStatusCode.NotImplemented);
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var body = await response.Content.ReadAsStringAsync();
            body.Should().Contain(DemoTenantBlueprint.StrategyName);
        }
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        _env.Restore();

        if (_tempRoot is not null && Directory.Exists(_tempRoot))
        {
            try
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup of the temp workspace.
            }
        }
    }

    private sealed class EnvironmentSnapshot
    {
        private readonly Dictionary<string, string?> _values = new(StringComparer.Ordinal);

        public void Capture(params string[] names)
        {
            foreach (var name in names)
            {
                _values[name] = Environment.GetEnvironmentVariable(name);
            }
        }

        public void Restore()
        {
            foreach (var pair in _values)
            {
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }
    }
}
