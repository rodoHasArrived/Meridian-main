using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.RateLimiting;
using FluentAssertions;
using Meridian.Application.Backfill;
using Meridian.Contracts.Api;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Services;
using Meridian.Core.Config;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.Infrastructure.Adapters.Core;
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
            preview.GetProperty("missingSessions").GetInt32().Should().Be(0,
                "an unavailable calendar cannot establish which market sessions are missing");
            preview.GetProperty("providerErrors").EnumerateArray().Select(error => error.GetString())
                .Should().Contain("session-calendar: MissingCoverageEvidence", "the host has no retained listing identity for this symbol");

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

    [Fact]
    public async Task Preview_UsesRetainedListingAndConfiguredCalendarToFindSharedGap()
    {
        var to = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(-1);
        var from = to.AddDays(-4);
        var closure = from.AddDays(1);
        var missing = from.AddDays(2);
        var effectiveFrom = new DateTimeOffset(from.AddYears(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var identity = new SecurityDetailDto(Guid.NewGuid(), "Equity", SecurityStatusDto.Active, "Fixture SPY", "USD",
            JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(new { }),
            [new SecurityIdentifierDto(SecurityIdentifierKind.ListingId, "SPY:ARCX", true, effectiveFrom)],
            [], 1, effectiveFrom, null);
        var identities = new Mock<ISecurityIdentityQueryService>();
        identities.Setup(service => service.GetByIdentifierAsync(SecurityIdentifierKind.Ticker, "SPY", null,
            It.IsAny<CancellationToken>(), It.IsAny<DateTimeOffset?>())).ReturnsAsync(identity);
        var calendar = new Mock<IOperationalTradingCalendar>();
        calendar.Setup(service => service.IsTradingDay(It.IsAny<DateOnly>(), "US"))
            .Returns((DateOnly date, string _) => date != closure);
        var (app, _, directory) = await CreateApp(UserPermission.ViewConfig | UserPermission.ModifyConfig, services =>
        {
            services.AddSingleton(identities.Object);
            services.AddSingleton(calendar.Object);
            services.AddSingleton<IEnumerable<IHistoricalDataProvider>>(new[]
            {
                Provider("stooq", _ => 100m, date => date != missing && date != closure).Object,
                Provider("tiingo", _ => 100m, date => date != missing && date != closure).Object
            });
        });
        try
        {
            var response = await app.GetTestClient().PostAsJsonAsync(UiApiRoutes.QualityCrossProviderPreview,
                new CrossProviderTolerancePreviewRequest("SPY", "stooq", "tiingo", 5));

            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            var preview = await response.Content.ReadFromJsonAsync<JsonElement>();
            preview.GetProperty("missingSessions").GetInt32().Should().Be(1, "the shared missing session counts, but the declared closure does not");
            preview.GetProperty("providerErrors").GetArrayLength().Should().Be(0);
            preview.GetProperty("flaggedDays").GetInt32().Should().Be(0);
            identities.Verify(service => service.GetByIdentifierAsync(SecurityIdentifierKind.Ticker, "SPY", null,
                It.IsAny<CancellationToken>(), It.IsAny<DateTimeOffset?>()), Times.Once);
            calendar.Verify(service => service.IsTradingDay(missing, "US"), Times.Once);
        }
        finally
        {
            await app.DisposeAsync();
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task BuildPreview_MatchingBarsOnConfirmedClosure_RetainsBothProviderErrors()
    {
        var day = new DateOnly(2024, 7, 4);
        var service = new CrossSourceBackfillReconciliationService(new[]
        {
            Provider("stooq", _ => 100m).Object,
            Provider("tiingo", _ => 100m).Object
        })
        {
            ResolveSessionCoverageAsync = (_, from, to, _) =>
                Task.FromResult<CrossSourceBackfillSessionCoverage?>(new("fixture-us-closure-v1", from, to, []))
        };
        var result = await service.ReconcileDailyAsync(new CrossSourceBackfillReconciliationRequest(
            "SPY", day, day, "stooq", ["tiingo"]));

        var preview = CrossProviderToleranceEndpoints.BuildPreview("SPY", day, day, "stooq", "tiingo",
            CrossProviderTolerancePolicy.Default.Resolve((string?)null), result);

        result.IsClean.Should().BeFalse();
        preview.FlaggedDays.Should().Be(0);
        preview.MissingSessions.Should().Be(0);
        preview.ProviderErrors.Should().Equal(
            "stooq: BaselineUnexpectedSession (2024-07-04)",
            "tiingo: ComparisonUnexpectedSession (2024-07-04)");
    }

    [Fact]
    public void BuildPreview_InformationalVolumeIsCountedSeparately()
    {
        var day = new DateOnly(2026, 11, 2);
        var result = new CrossSourceBackfillReconciliationResult("SPY", day, day, "stooq", ["tiingo"],
            [new CrossSourceBackfillDiscrepancy(day, "stooq", "tiingo", "VolumeDrift", 100m, 100m, 1000, 2000, 0m, 0.5m, "volume")],
            [], DateTimeOffset.UtcNow);

        var preview = CrossProviderToleranceEndpoints.BuildPreview("SPY", day, day, "stooq", "tiingo",
            new CrossProviderTolerancePolicy(VolumeMode: CrossProviderVolumeMode.Informational).Resolve((string?)null), result);

        preview.FlaggedDays.Should().Be(0);
        preview.InformationalDays.Should().Be(1);
    }

    private static Mock<IHistoricalDataProvider> Provider(string name, Func<DateOnly, decimal> close, Func<DateOnly, bool>? include = null)
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
                    if (include is not null && !include(date))
                        continue;
                    var c = close(date);
                    bars.Add(new HistoricalBar(symbol, date, c, c + 1, c - 1, c, 1000, name, 0));
                }

                return bars;
            });
        return mock;
    }

    private static async Task<(WebApplication App, ConfigStore Store, string Directory)> CreateApp(
        UserPermission permissions, Action<IServiceCollection>? configure = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "meridian-tolerance-endpoint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "appsettings.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new AppConfig(), AppConfigJsonOptions.Write));
        var store = new ConfigStore(path);

        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(-1);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(store);
        builder.Services.AddSingleton<IEnumerable<IHistoricalDataProvider>>(new[]
        {
            Provider("stooq", _ => 100m).Object,
            Provider("tiingo", date => date == yesterday ? 100.3m : 100m).Object
        });
        builder.Services.AddRateLimiter(options => options.AddPolicy(UiEndpoints.MutationRateLimitPolicy,
            _ => RateLimitPartition.GetNoLimiter("test")));
        configure?.Invoke(builder.Services);
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
