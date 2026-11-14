using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Core.Config;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Infrastructure.Adapters.TwelveData;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class BackfillProviderEditorTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(6)]
    public void Catalog_RejectsLimitsOutsideDeclaredTier(int limit)
    {
        var apply = () => BackfillProviderCatalog.Apply(new(), "polygon", new(true, 4, RateLimitPerMinute: limit));
        apply.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Catalog_PreservesSecretsAndArchiveSemantics()
    {
        var original = new BackfillProvidersConfig(Alpaca: new(SecretKey: "retained-secret", Adjustment: "raw"));
        var changed = BackfillProviderCatalog.Apply(original, "alpaca", new(true, 2, RateLimitPerMinute: 10));
        changed.Alpaca!.SecretKey.Should().Be("retained-secret");
        changed.Alpaca.Adjustment.Should().Be("raw");
        changed.Alpaca.RateLimitPerMinute.Should().Be(10);
    }

    [Fact]
    public void Catalog_StarterPresetFeedsRuntimePriorityAndLimit()
    {
        var cfg = BackfillProviderCatalog.Apply(new(), "polygon", new(true, 3, "starter"));
        using var http = new HttpClient();
        using var provider = new PolygonHistoricalDataProvider(apiKey: "synthetic-fixture", httpClient: http,
            runtimeOptions: BackfillProviderRuntimeOptions.FromConfig(cfg, "polygon"));
        provider.Priority.Should().Be(3);
        provider.MaxRequestsPerWindow.Should().Be(100);
        provider.RateLimitDelay.Should().Be(TimeSpan.FromMilliseconds(600));
        provider.GetRateLimitInfo().MaxRequests.Should().Be(100);
    }

    [Fact]
    public void Catalog_TwelveDataLowerLimitFeedsBothCapabilities()
    {
        var cfg = BackfillProviderCatalog.Apply(new(), "twelvedata", new(true, 7, RateLimitPerMinute: 4));
        var options = BackfillProviderRuntimeOptions.FromConfig(cfg, "twelvedata");
        using var http = new HttpClient();
        using var provider = new TwelveDataHistoricalDataProvider(apiKey: "synthetic-fixture", httpClient: http, runtimeOptions: options);
        using var search = new TwelveDataSymbolSearchProvider(apiKey: "synthetic-fixture", httpClient: http, runtimeOptions: options);
        provider.Priority.Should().Be(7);
        search.Priority.Should().Be(7);
        provider.MaxRequestsPerWindow.Should().Be(4);
        provider.RateLimitDelay.Should().Be(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task Put_RejectsMissingConfigurationPermission()
    {
        var (app, store, root) = await CreateApp(UserPermission.ManageProviders);
        await using (app)
        {
            var response = await app.GetTestClient().PutAsJsonAsync("/api/backfill/providers/polygon", new BackfillProviderEdit(true, 3));
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            store.Load().Backfill.Should().BeNull();
        }
        Directory.Delete(root, true);
    }

    [Fact]
    public async Task Put_RejectsOverCeilingWithoutWriting_ThenSavesSecretFreeOptions()
    {
        const string retainedSecret = "retained-provider-fixture-secret";
        var initial = new AppConfig(Backfill: new BackfillConfig(Providers:
            new BackfillProvidersConfig(Tiingo: new TiingoConfig(ApiToken: retainedSecret))));
        var (app, store, root) = await CreateApp(UserPermission.ManageProviders | UserPermission.ModifyConfig, initial);
        await using (app)
        {
            var client = app.GetTestClient();
            var original = await File.ReadAllTextAsync(store.ConfigPath);
            var secretWrite = await client.PutAsJsonAsync("/api/backfill/providers/polygon", new { enabled = true, priority = 3, tierId = "free", apiKey = "rejected-fixture-secret" });
            secretWrite.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await File.ReadAllTextAsync(store.ConfigPath)).Should().Be(original);
            var refused = await client.PutAsJsonAsync("/api/backfill/providers/polygon", new BackfillProviderEdit(true, 3, RateLimitPerMinute: 6));
            refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await File.ReadAllTextAsync(store.ConfigPath)).Should().Be(original);
            var response = await client.PutAsJsonAsync("/api/backfill/providers/polygon", new BackfillProviderEdit(true, 3, "starter", 20));
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            store.Load().Backfill!.Providers!.Polygon!.RateLimitPerMinute.Should().Be(20);
            store.Load().Backfill!.Providers!.Tiingo!.ApiToken.Should().Be(retainedSecret);
            var body = await response.Content.ReadAsStringAsync();
            body.Should().NotContain(retainedSecret).And.NotContain("rejected-fixture-secret");
            using var document = JsonDocument.Parse(body);
            document.RootElement.TryGetProperty("changeImpact", out _).Should().BeTrue();
            document.RootElement.GetProperty("options").EnumerateObject().Select(property => property.Name)
                .Should().NotContain(name => name.Equals("apiKey", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("apiToken", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("secretKey", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("keyId", StringComparison.OrdinalIgnoreCase));
        }
        Directory.Delete(root, true);
    }

    private static async Task<(WebApplication App, ConfigStore Store, string Root)> CreateApp(UserPermission permissions, AppConfig? initial = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-provider-editor-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "appsettings.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(initial ?? new AppConfig(), AppConfigJsonOptions.Write));
        var store = new ConfigStore(path);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(store);
        builder.Services.AddSingleton<Meridian.Ui.Shared.Services.BackfillCoordinator>();
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "synthetic-provider-operator";
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "synthetic-provider-tenant";
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            await next();
        });
        app.MapBackfillEndpoints(new(JsonSerializerDefaults.Web), new(JsonSerializerDefaults.Web));
        await app.StartAsync();
        return (app, store, root);
    }
}
