using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.RateLimiting;
using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.ProviderRouting;
using Meridian.Core.Config;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using WorkstationConfigStore = Meridian.Ui.Shared.Services.ConfigStore;
using CoreConfigStore = Meridian.Application.UI.ConfigStore;

namespace Meridian.Tests.Ui;

public sealed class ProviderConfigurationCredentialGuardTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-config-credential-guard", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("production", "/api/config/alpaca", "alpaca-key")]
    [InlineData("packaged", "/api/config/alpaca", "alpaca-secret")]
    [InlineData("production", "/api/config/datasources", "alpaca-key")]
    [InlineData("packaged", "/api/config/data-sources", "polygon-key")]
    public async Task OwnedCompositions_RefuseSubmittedSecretsBeforeAnyConfigurationWrite(string posture, string route, string field)
    {
        await using var app = await CreateAppAsync(posture);
        var before = await File.ReadAllBytesAsync(ConfigPath);
        using var response = await app.GetTestClient().PostAsJsonAsync(route, Request(route, field));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var text = await response.Content.ReadAsStringAsync();
        text.Should().Contain("OwnedProviderCredentialsRequired").And.Contain("Manage owned credentials");
        text.Should().NotContain("synthetic-submitted-key").And.NotContain("synthetic-submitted-secret");
        (await File.ReadAllBytesAsync(ConfigPath)).Should().Equal(before);
    }

    [Theory]
    [InlineData("/api/config/alpaca")]
    [InlineData("/api/config/datasources")]
    [InlineData("/api/config/data-sources")]
    public async Task Development_RetainsExistingCredentialConfigurationCompatibility(string route)
    {
        await using var app = await CreateAppAsync("development");
        using var response = await app.GetTestClient().PostAsJsonAsync(route, Request(route, "alpaca-key"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await File.ReadAllTextAsync(ConfigPath)).Should().Contain("synthetic-submitted-key");
    }

    [Theory]
    [InlineData("/api/config/alpaca")]
    [InlineData("/api/config/datasources")]
    [InlineData("/api/config/data-sources")]
    public async Task OwnedCompositions_AllowNonSecretSettingsAndEnvironmentNameReferences(string route)
    {
        var config = new AppConfig(ProviderModules: new ProviderModulesConfig(new()
        {
            ["alpaca"] = new(Credentials: new() { ["keyId"] = "ALPACA_KEY_ID", ["secretKey"] = "ALPACA_SECRET_KEY" })
        }));
        await using var app = await CreateAppAsync("production", config);
        using var response = await app.GetTestClient().PostAsJsonAsync(route, Request(route, null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var retained = new CoreConfigStore(ConfigPath).LoadRequired();
        retained.ProviderModules!.Modules!["alpaca"].Credentials!["keyId"].Should().Be("ALPACA_KEY_ID");
        if (route.EndsWith("/alpaca", StringComparison.Ordinal))
            retained.Alpaca!.Feed.Should().Be("sip");
        else
            retained.DataSources!.Sources!.Single().Description.Should().Be("ApiKey is a configuration field name, not a submitted credential.");
    }

    [Theory]
    [InlineData("alpaca")]
    [InlineData("polygon")]
    [InlineData("backfill-alpaca")]
    [InlineData("backfill-nasdaq")]
    [InlineData("backfill-openfigi")]
    [InlineData("backfill-tiingo")]
    [InlineData("backfill-polygon")]
    [InlineData("backfill-alpha")]
    [InlineData("backfill-finnhub")]
    [InlineData("backfill-fred")]
    [InlineData("source-polygon")]
    [InlineData("extension-twelvedata")]
    [InlineData("extension-nyse")]
    [InlineData("extension-plaid")]
    [InlineData("extension-quickbooks")]
    [InlineData("extension-xero")]
    [InlineData("extension-netsuite")]
    [InlineData("extension-flat-environment")]
    [InlineData("extension-flat-path")]
    [InlineData("module-settings")]
    public async Task SecretFreePartialRequest_CannotRepublishLegacyCredentialsInAnotherSection(string section)
    {
        await using var app = await CreateAppAsync("production", WithLegacySecret(section));
        var before = await File.ReadAllBytesAsync(ConfigPath);
        using var response = await app.GetTestClient().PostAsJsonAsync("/api/config/datasource", new { dataSource = "Synthetic" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("synthetic-retained-secret");
        (await File.ReadAllBytesAsync(ConfigPath)).Should().Equal(before);
    }

    [Fact]
    public async Task LegacyExtensionSecrets_AreAbsentFromConfigurationResponsesAndGeneralPostCannotPersistThem()
    {
        await using var app = await CreateAppAsync("production", WithLegacySecret("extension-twelvedata"));
        var before = await File.ReadAllBytesAsync(ConfigPath);
        using var read = await app.GetTestClient().GetAsync("/api/config");
        read.StatusCode.Should().Be(HttpStatusCode.OK);
        (await read.Content.ReadAsStringAsync()).Should().NotContain("synthetic-retained-secret");
        using var post = await app.GetTestClient().PostAsJsonAsync("/api/config", new { twelveData = new { apiKey = "synthetic-submitted-key" } });
        post.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        (await File.ReadAllBytesAsync(ConfigPath)).Should().Equal(before);
    }

    [Fact]
    public async Task ReviewedPurge_CanReplaceLegacyPlaintextWithASecretFreeSnapshot()
    {
        await using var app = await CreateAppAsync("production", WithLegacySecret("backfill-tiingo"));
        var store = app.Services.GetRequiredService<WorkstationConfigStore>();
        await store.SaveAsync(new AppConfig(Backfill: new BackfillConfig(Enabled: true)));
        var retained = new CoreConfigStore(ConfigPath).LoadRequired();
        retained.Backfill!.Enabled.Should().BeTrue();
        (await File.ReadAllTextAsync(ConfigPath)).Should().NotContain("synthetic-retained-secret");
    }

    [Fact]
    public async Task CallerMutationWhileSaveWaits_CannotInjectCredentialsAfterSnapshotValidation()
    {
        await using var app = await CreateAppAsync("production");
        var candidate = new AppConfig { AdditionalSections = new() };
        var store = app.Services.GetRequiredService<WorkstationConfigStore>();
        var lease = await new CoreConfigStore(ConfigPath).AcquireWriteLeaseAsync();
        Task saving;
        try
        {
            saving = store.SaveAsync(candidate);
            saving.IsCompleted.Should().BeFalse();
            candidate.AdditionalSections["TwelveData"] = JsonSerializer.SerializeToElement(new { apiKey = "synthetic-late-secret" });
        }
        finally
        {
            lease.Dispose();
        }
        await saving;
        (await File.ReadAllTextAsync(ConfigPath)).Should().NotContain("synthetic-late-secret");
    }

    private string ConfigPath => Path.Combine(_root, "appsettings.json");

    private async Task<WebApplication> CreateAppAsync(string posture, AppConfig? config = null)
    {
        Directory.CreateDirectory(_root);
        await new CoreConfigStore(ConfigPath).SaveAsync(config ?? new AppConfig());
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var services = builder.Services;
        services.AddSingleton(new CompositionConfiguration(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["MDC_PACKAGED_BUILD"] = posture == "packaged" ? "true" : "false"
        }).Build()));
        services.AddProviderCredentialOwnershipPolicy();
        services.DeclareMeridianDeploymentPosture(posture == "production"
            ? MeridianDeploymentPosture.ProductionApi : MeridianDeploymentPosture.LocalWorkstation);
        services.AddSingleton(sp => new WorkstationConfigStore(ConfigPath, sp.GetRequiredService<ProviderCredentialOwnershipPolicy>()));
        services.AddRateLimiter(options => options.AddPolicy(Meridian.Ui.Shared.Endpoints.UiEndpoints.MutationRateLimitPolicy,
            _ => RateLimitPartition.GetNoLimiter("config-credential-fixture")));
        var app = builder.Build();
        app.UseRateLimiter();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.ViewConfig | UserPermission.ModifyConfig
                | UserPermission.ManageProviders | UserPermission.ManageCredentials;
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "config-credential-tenant";
            await next();
        });
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        app.MapConfigEndpoints(options);
        app.MapProviderEndpoints(options);
        await app.StartAsync();
        return app;
    }

    private static object Request(string route, string? field)
    {
        var alpaca = new
        {
            keyId = field == "alpaca-key" ? "synthetic-submitted-key" : null,
            secretKey = field == "alpaca-secret" ? "synthetic-submitted-secret" : null,
            feed = "sip",
            useSandbox = true
        };
        return route.EndsWith("/alpaca", StringComparison.Ordinal) ? alpaca : new
        {
            id = "synthetic-source",
            name = "Synthetic source",
            provider = "Alpaca",
            alpaca,
            polygon = new { apiKey = field == "polygon-key" ? "synthetic-submitted-key" : null },
            description = "ApiKey is a configuration field name, not a submitted credential."
        };
    }

    private static AppConfig WithLegacySecret(string section)
    {
        const string secret = "synthetic-retained-secret";
        return section switch
        {
            "alpaca" => new(Alpaca: new(KeyId: secret)),
            "polygon" => new(Polygon: new(ApiKey: secret)),
            "backfill-alpaca" => new(Backfill: new(Providers: new(Alpaca: new(SecretKey: secret)))),
            "backfill-nasdaq" => new(Backfill: new(Providers: new(Nasdaq: new(ApiKey: secret)))),
            "backfill-openfigi" => new(Backfill: new(Providers: new(OpenFigi: new(ApiKey: secret)))),
            "backfill-tiingo" => new(Backfill: new(Providers: new(Tiingo: new(ApiToken: secret)))),
            "backfill-polygon" => new(Backfill: new(Providers: new(Polygon: new(ApiKey: secret)))),
            "backfill-alpha" => new(Backfill: new(Providers: new(AlphaVantage: new(ApiKey: secret)))),
            "backfill-finnhub" => new(Backfill: new(Providers: new(Finnhub: new(ApiKey: secret)))),
            "backfill-fred" => new(Backfill: new(Providers: new(Fred: new(ApiKey: secret)))),
            "source-polygon" => new(DataSources: new(Sources: [new("legacy", "Legacy source", Polygon: new(ApiKey: secret))])),
            "module-settings" => new(ProviderModules: new(new() { ["alpaca"] = new(Settings: new() { ["SecretKey"] = secret }) })),
            _ => new AppConfig
            {
                AdditionalSections = section switch
                {
                    "extension-flat-environment" => new() { ["TWELVEDATA_API_KEY"] = JsonSerializer.SerializeToElement(secret) },
                    "extension-flat-path" => new() { ["Backfill:Providers:TwelveData:ApiKey"] = JsonSerializer.SerializeToElement(secret) },
                    _ => new()
                    {
                        [section["extension-".Length..]] = JsonSerializer.SerializeToElement(new Dictionary<string, string>
                        {
                            [section switch
                            {
                                "extension-nyse" => "ApiSecret",
                                "extension-plaid" => "Secret",
                                "extension-quickbooks" => "RefreshToken",
                                "extension-xero" or "extension-netsuite" => "ClientSecret",
                                _ => "ApiKey"
                            }] = secret
                        })
                    }
                }
            }
        };
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
