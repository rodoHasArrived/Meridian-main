using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.RateLimiting;
using FluentAssertions;
using Meridian.Application.Backfill;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Features;
using Meridian.Contracts.Api;
using Meridian.Contracts.Catalog;
using Meridian.Contracts.Domain.Models;
using Meridian.Core.Config;
using Meridian.DataIntegration.Monitoring.DataQuality;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Services;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class CrossProviderToleranceEndpointTests
{
    private static readonly CrossProviderTolerancePolicy AssetClassPolicy = new(
        PriceTolerancePercent: 0.20,
        BackfillCloseTolerancePercent: 0.20,
        AssetClassOverrides: new Dictionary<string, CrossProviderToleranceOverride>
        {
            ["equity"] = new(PriceTolerancePercent: 0.10, BackfillCloseTolerancePercent: 0.10),
            ["bond"] = new(PriceTolerancePercent: 0.70, BackfillCloseTolerancePercent: 0.70),
            ["unknown"] = new(PriceTolerancePercent: 1.00, BackfillCloseTolerancePercent: 1.00)
        });

    [Fact]
    public void ApplyPolicy_RejectsLooseningWithoutReason()
    {
        var request = new CrossProviderTolerancePolicyRequest(new CrossProviderTolerancePolicy(PriceTolerancePercent: 1.0));

        var change = () => CrossProviderToleranceEndpoints.ApplyPolicy(new AppConfig(), request, "operator");

        change.Should().Throw<ArgumentException>().WithMessage("*recorded reason*");
    }

    [Fact]
    public void ApplyPolicy_TighteningNeedsNoReasonAndRecordsBeforeAfter()
    {
        var config = new AppConfig(DataRoot: "custom-root");
        var strict = new CrossProviderTolerancePolicy(PriceTolerancePercent: 0.05);

        var (next, audit) = CrossProviderToleranceEndpoints.ApplyPolicy(config,
            new CrossProviderTolerancePolicyRequest(strict), "desk");

        next.DataRoot.Should().Be("custom-root");
        next.DataQuality!.CrossProvider.Should().Be(strict);
        audit.Loosened.Should().BeFalse();
        audit.Before.Should().Be(CrossProviderTolerancePolicy.Default);
        next.DataQuality.CrossProviderAudit.Should().ContainSingle().Which.Should().Be(audit);
    }

    [Fact]
    public async Task Put_RejectsOutOfBoundsAndUnreasonedLoosening_ThenSavesWithAudit()
    {
        var (app, store, directory) = await CreateApp(UserPermission.ViewConfig | UserPermission.ModifyConfig);
        try
        {
            var client = app.GetTestClient();
            var before = await File.ReadAllTextAsync(store.ConfigPath);

            var outOfBounds = await client.PutAsJsonAsync(UiApiRoutes.QualityCrossProviderPolicy,
                new CrossProviderTolerancePolicyRequest(new CrossProviderTolerancePolicy(PriceTolerancePercent: 100), "disable alarms"));
            outOfBounds.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var unreasoned = await client.PutAsJsonAsync(UiApiRoutes.QualityCrossProviderPolicy,
                new CrossProviderTolerancePolicyRequest(new CrossProviderTolerancePolicy(PriceTolerancePercent: 2)));
            unreasoned.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await File.ReadAllTextAsync(store.ConfigPath)).Should().Be(before);

            var saved = await client.PutAsJsonAsync(UiApiRoutes.QualityCrossProviderPolicy,
                new CrossProviderTolerancePolicyRequest(new CrossProviderTolerancePolicy(PriceTolerancePercent: 2), "Illiquid preferreds"));
            saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());

            var policy = CrossProviderTolerancePolicy.FromConfig(store.Load());
            policy.PriceTolerancePercent.Should().Be(2);
            var audit = store.Load().DataQuality!.CrossProviderAudit!.Single();
            audit.Actor.Should().Be("tolerance-test-operator");
            audit.Reason.Should().Be("Illiquid preferreds");
            audit.Loosened.Should().BeTrue();

            var get = await client.GetFromJsonAsync<JsonElement>(UiApiRoutes.QualityCrossProviderPolicy);
            get.GetProperty("policy").GetProperty("priceTolerancePercent").GetDouble().Should().Be(2);
            get.GetProperty("bounds").GetProperty("maxPriceTolerancePercent").GetDouble().Should().Be(5);
            get.GetProperty("audit").GetArrayLength().Should().Be(1);
            get.GetProperty("previewProviders").EnumerateArray().Select(p => p.GetString())
                .Should().Equal("stooq", "tiingo");
        }
        finally
        {
            await app.DisposeAsync();
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Put_WithoutModifyConfig_IsForbidden()
    {
        var (app, _, directory) = await CreateApp(UserPermission.ViewConfig);
        try
        {
            var response = await app.GetTestClient().PutAsJsonAsync(UiApiRoutes.QualityCrossProviderPolicy,
                new CrossProviderTolerancePolicyRequest(CrossProviderTolerancePolicy.Default));

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            await app.DisposeAsync();
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Preview_CountsDaysTheDraftPolicyWouldFlag()
    {
        var (app, _, directory) = await CreateApp(UserPermission.ViewConfig | UserPermission.ModifyConfig);
        try
        {
            var client = app.GetTestClient();
            var request = new CrossProviderTolerancePreviewRequest("SPY", "stooq", "tiingo", 30,
                new CrossProviderTolerancePolicy(BackfillCloseTolerancePercent: 0.15));

            var response = await client.PostAsJsonAsync(UiApiRoutes.QualityCrossProviderPreview, request);

            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            var preview = await response.Content.ReadFromJsonAsync<JsonElement>();
            preview.GetProperty("flaggedDays").GetInt32().Should().Be(1, "only one session is 0.3% apart");
            preview.GetProperty("providerCallsUsed").GetInt32().Should().Be(2);

            var defaults = await client.PostAsJsonAsync(UiApiRoutes.QualityCrossProviderPreview,
                request with { Policy = null });
            (await defaults.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("flaggedDays").GetInt32().Should().Be(0);

            var sameProvider = await client.PostAsJsonAsync(UiApiRoutes.QualityCrossProviderPreview,
                request with { ComparisonProvider = "stooq" });
            sameProvider.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var tooLong = await client.PostAsJsonAsync(UiApiRoutes.QualityCrossProviderPreview, request with { Days = 90 });
            tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        finally
        {
            await app.DisposeAsync();
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("FIXTURE-STOCK", "equity", null, 0.10, true)]
    [InlineData("FIXTURE-BOND", "bond", " BoNd ", 0.70, false)]
    [InlineData("FIXTURE-UNREGISTERED", null, null, 0.20, true)]
    public async Task PreviewAndStreamingComparison_UseTheSameAssetClassPolicy(
        string symbol, string? expectedClass, string? assertion, double threshold, bool shouldFlag)
    {
        var (app, _, directory) = await CreateApp(UserPermission.ModifyConfig, AssetClassPolicy);
        try
        {
            using var response = await app.GetTestClient().PostAsJsonAsync(UiApiRoutes.QualityCrossProviderPreview,
                new CrossProviderTolerancePreviewRequest(symbol.ToLowerInvariant(), "stooq", "tiingo", 1, AssetClass: assertion));
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            var preview = await response.Content.ReadFromJsonAsync<CrossProviderTolerancePreviewResponse>();
            Assert.NotNull(preview);

            var comparison = app.Services.GetRequiredService<DataQualityMonitoringService>().CrossProvider;
            var resolution = comparison.ResolvePolicy(symbol);
            var now = DateTimeOffset.UtcNow;
            comparison.RecordTrade(symbol, "stooq", now, 100m, 1000);
            comparison.RecordTrade(symbol, "tiingo", now, 100.3m, 1000);
            var evidence = comparison.Compare(symbol, DateOnly.FromDateTime(now.UtcDateTime));

            preview.ResolvedAssetClass.Should().Be(expectedClass);
            preview.Tolerances.BackfillCloseTolerancePercent.Should().Be(threshold);
            preview.Tolerances.Should().Be(resolution.Tolerances);
            resolution.AssetClass.Should().Be(expectedClass);
            resolution.Tolerances.PriceTolerancePercent.Should().Be(threshold);
            preview.FlaggedDays.Should().Be(shouldFlag ? 1 : 0);
            if (shouldFlag)
                evidence.Discrepancies.Should().ContainSingle().Which.PolicyResolution.Should().Be(resolution);
            else
                evidence.Discrepancies.Should().BeEmpty();
        }
        finally
        {
            await app.DisposeAsync();
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("FIXTURE-STOCK", "'equity'")]
    [InlineData("FIXTURE-UNREGISTERED", "No asset class is resolved")]
    public async Task Preview_RejectsAnAssertedAssetClassMismatchWithoutProviderCalls(string symbol, string evidence)
    {
        var (app, _, directory) = await CreateApp(UserPermission.ModifyConfig, AssetClassPolicy);
        try
        {
            using var response = await app.GetTestClient().PostAsJsonAsync(UiApiRoutes.QualityCrossProviderPreview,
                new CrossProviderTolerancePreviewRequest(symbol, "stooq", "tiingo", 1, AssetClass: "bond"));

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            problem.GetProperty("errors").GetProperty("assetClass")[0].GetString().Should().Contain(evidence);
            foreach (var provider in app.Services.GetRequiredService<IEnumerable<IHistoricalDataProvider>>())
            {
                Mock.Get(provider).Verify(p => p.GetDailyBarsAsync(It.IsAny<string>(), It.IsAny<DateOnly?>(),
                    It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()), Times.Never);
            }
        }
        finally
        {
            await app.DisposeAsync();
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Preview_ResolvesTheDraftAgainstTheRegistryWithoutChangingTheLivePolicy()
    {
        var (app, _, directory) = await CreateApp(UserPermission.ModifyConfig, CrossProviderTolerancePolicy.Default);
        try
        {
            using var response = await app.GetTestClient().PostAsJsonAsync(UiApiRoutes.QualityCrossProviderPreview,
                new CrossProviderTolerancePreviewRequest("FIXTURE-STOCK", "stooq", "tiingo", 1, AssetClassPolicy));

            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            var preview = await response.Content.ReadFromJsonAsync<CrossProviderTolerancePreviewResponse>();
            Assert.NotNull(preview);
            preview.ResolvedAssetClass.Should().Be("equity");
            preview.Tolerances.BackfillCloseTolerancePercent.Should().Be(0.10);
            preview.FlaggedDays.Should().Be(1);
            app.Services.GetRequiredService<DataQualityMonitoringService>().CrossProvider.Policy
                .Should().Be(CrossProviderTolerancePolicy.Default);
        }
        finally
        {
            await app.DisposeAsync();
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void BuildPreview_InformationalVolumeIsCountedSeparately()
    {
        var day = new DateOnly(2026, 11, 2);
        var result = new CrossSourceBackfillReconciliationResult("SPY", day, day, "stooq", ["tiingo"],
            [new CrossSourceBackfillDiscrepancy(day, "stooq", "tiingo", "VolumeDrift", 100m, 100m, 1000, 2000, 0m, 0.5m, "volume")],
            [], DateTimeOffset.UtcNow);

        var preview = CrossProviderToleranceEndpoints.BuildPreview("SPY", day, day, "stooq", "tiingo",
            CrossProviderToleranceResolver.Resolve("SPY",
                new CrossProviderTolerancePolicy(VolumeMode: CrossProviderVolumeMode.Informational), null), result);

        preview.FlaggedDays.Should().Be(0);
        preview.InformationalDays.Should().Be(1);
    }

    private static Mock<IHistoricalDataProvider> Provider(string name, Func<DateOnly, DateOnly, decimal> close)
    {
        var mock = new Mock<IHistoricalDataProvider>();
        mock.Setup(p => p.Name).Returns(name);
        mock.Setup(p => p.IsSimulated).Returns(false);
        mock.Setup(p => p.GetDailyBarsAsync(It.IsAny<string>(), It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string symbol, DateOnly? from, DateOnly? to, CancellationToken _) =>
            {
                var bars = new List<HistoricalBar>();
                for (var date = from!.Value; date <= to!.Value; date = date.AddDays(1))
                {
                    var c = close(date, to.Value);
                    bars.Add(new HistoricalBar(symbol, date, c, c + 1, c - 1, c, 1000, name, 0));
                }

                return bars;
            });
        return mock;
    }

    private static async Task<(WebApplication App, ConfigStore Store, string Directory)> CreateApp(
        UserPermission permissions, CrossProviderTolerancePolicy? policy = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "meridian-tolerance-endpoint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "appsettings.json");
        var config = new AppConfig(DataRoot: directory)
        {
            DataQuality = policy is null ? null : new DataQualityConfig(policy)
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(config, AppConfigJsonOptions.Write));
        var store = new ConfigStore(path);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(store);
        if (policy is not null)
        {
            var registry = new SymbolRegistryService(directory);
            await registry.InitializeAsync();
            await registry.RegisterSymbolAsync(new SymbolRegistryEntry { Canonical = "FIXTURE-STOCK", AssetClass = "equity" });
            await registry.RegisterSymbolAsync(new SymbolRegistryEntry { Canonical = "FIXTURE-BOND", AssetClass = "bond" });
            builder.Services.AddSingleton<ISymbolRegistryService>(registry);
            new PipelineFeatureRegistration().Register(builder.Services, CompositionOptions.Default);
        }
        builder.Services.AddSingleton<IEnumerable<IHistoricalDataProvider>>(new[]
        {
            Provider("stooq", (_, _) => 100m).Object,
            Provider("tiingo", (date, end) => date == end ? 100.3m : 100m).Object
        });
        builder.Services.AddRateLimiter(options => options.AddPolicy(UiEndpoints.MutationRateLimitPolicy,
            _ => RateLimitPartition.GetNoLimiter("test")));
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "tolerance-test-tenant";
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "tolerance-test-operator";
            await next();
        });
        app.UseRateLimiter();
        app.MapCrossProviderToleranceEndpoints(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await app.StartAsync();
        return (app, store, directory);
    }
}
