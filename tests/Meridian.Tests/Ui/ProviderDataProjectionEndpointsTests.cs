using System.Net;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Identity.Auth;
using Meridian.ProviderSdk;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class ProviderDataProjectionEndpointsTests
{
    [Fact]
    public void CreateProjection_DeduplicatesLiveRowsAndRetainsAvailabilityAndLineage()
    {
        var service = new ProviderDataReadModelService(
            [new Requests()],
            [new News()],
            [new Calendar()],
            [new Instruments()],
            [new Availability()]);

        var projection = ProviderDataProjectionEndpoints.CreateProjection(service);

        projection.ScannerResults.Should().ContainSingle();
        projection.ScannerResults[0].Provenance.Key.Should().Be("scanner-msft");
        projection.ScannerResults[0].Provenance.Source!.ProviderConnectionId.Should().Be("ib-gateway-1");
        projection.ScannerResults[0].Availability.Entitlement.Should().Be("US equities");
        projection.ScannerResults[0].Availability.ConnectionState.Should().Be("Connected");
        projection.PnlStreams.Should().ContainSingle();
        projection.MarketRules.Should().ContainSingle();
        projection.News.Should().ContainSingle().Which.Provenance.Key.Should().Be("news-results");
        projection.Calendars.Should().ContainSingle().Which.Availability.IsAvailable.Should().BeTrue();
        projection.Instruments.Should().ContainSingle().Which.Provenance.Capability.Should().Be("instrument-discovery");
    }

    [Fact]
    public void CreateProjection_TenantScopedProviderExcludesOtherCompaniesAndOwnerlessRows()
    {
        var service = new ProviderDataReadModelService([new TenantScopedRequests()]);

        var alpha = ProviderDataProjectionEndpoints.CreateProjection(
            service,
            "tenant-shared",
            "company-alpha");
        var beta = ProviderDataProjectionEndpoints.CreateProjection(
            service,
            "tenant-shared",
            "company-beta");

        alpha.ScannerResults.Should().ContainSingle()
            .Which.Item.Symbol.Should().Be("ALPHA");
        beta.ScannerResults.Should().ContainSingle()
            .Which.Item.Symbol.Should().Be("BETA");
        alpha.ScannerResults.Should().NotContain(row => row.Item.Symbol == "OWNERLESS");
        beta.ScannerResults.Should().NotContain(row => row.Item.Symbol == "OWNERLESS");
    }

    [Fact]
    public async Task UnscopedProjectionAndWatch_ExcludeTenantScopedProviderCompatibilitySurface()
    {
        var provider = new TenantScopedRequests();
        var service = new ProviderDataReadModelService([provider]);

        ProviderDataProjectionEndpoints.CreateProjection(service).ScannerResults.Should().BeEmpty();
        await using var watch = service.WatchAsync().GetAsyncEnumerator();
        (await watch.MoveNextAsync()).Should().BeTrue();
        watch.Current.ScannerResults.Should().BeEmpty();
        (await watch.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5))).Should().BeFalse();

        provider.UnscopedReadCount.Should().Be(0);
        provider.UnscopedWatchCount.Should().Be(0);
    }

    [Fact]
    public async Task MapProviderDataProjection_MissingCompany_ReturnsForbiddenProblemDetails()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development"
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<IWorkstationTenantContextAccessor, HttpContextWorkstationTenantContextAccessor>();
        builder.Services.AddSingleton(new ProviderDataReadModelService([new TenantScopedRequests()]));

        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "provider-operator";
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.ViewTrades;
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "tenant-only";
            await next();
        });
        app.MapProviderDataProjectionEndpoints(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await app.StartAsync();

        var response = await app.GetTestClient().GetAsync(UiApiRoutes.ProviderDataProjection);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("type").GetString().Should().Be(ApiProblemTypes.Forbidden);
        problem.RootElement.GetProperty("title").GetString().Should().Be("Access Denied");
        problem.RootElement.GetProperty("detail").GetString()
            .Should().Be("A tenant- and company-scoped workstation request context is required.");
        problem.RootElement.GetProperty("instance").GetString().Should().Be(UiApiRoutes.ProviderDataProjection);
        problem.RootElement.TryGetProperty("traceId", out _).Should().BeTrue();
        problem.RootElement.TryGetProperty("timestamp", out _).Should().BeTrue();
    }

    private sealed class Requests : IProviderDataReadService
    {
        public IReadOnlyList<ProviderDataRequestReadModel> GetRequests() =>
        [
            new(42, "ib", "scanner", ProviderDataRequestStatus.Streaming, new DateTimeOffset(2026, 7, 22, 10, 0, 0, TimeSpan.Zero), Evidence("request-42"), ScannerResults: [new(1, "MSFT", "NASDAQ", null, null, null, null, null, Evidence("scanner-msft"))], Pnl: new("DU1", null, 1m, 2m, 3m, null, null, Evidence("pnl-du1")), MarketRuleIncrements: [new(0m, .01m, Evidence("rule-0"))]),
            new(42, "ib", "scanner", ProviderDataRequestStatus.Streaming, new DateTimeOffset(2026, 7, 22, 9, 0, 0, TimeSpan.Zero), Evidence("request-42"), ScannerResults: [new(1, "MSFT", "NASDAQ", null, null, null, null, null, Evidence("scanner-msft"))])
        ];
        public async IAsyncEnumerable<ProviderDataRequestReadModel> WatchAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { yield break; }
    }

    private sealed class TenantScopedRequests : ITenantScopedProviderDataReadService
    {
        private readonly IReadOnlyList<ProviderDataRequestReadModel> _all =
        [
            Request(101, "ALPHA"),
            Request(102, "BETA"),
            Request(103, "OWNERLESS")
        ];

        public int UnscopedReadCount { get; private set; }
        public int UnscopedWatchCount { get; private set; }

        public IReadOnlyList<ProviderDataRequestReadModel> GetRequests()
        {
            UnscopedReadCount++;
            return _all;
        }

        public IReadOnlyList<ProviderDataRequestReadModel> GetRequests(string tenantId, string companyId)
            => companyId switch
            {
                "company-alpha" => [_all[0]],
                "company-beta" => [_all[1]],
                _ => []
            };

        public async IAsyncEnumerable<ProviderDataRequestReadModel> WatchAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            UnscopedWatchCount++;
            yield break;
        }

        public async IAsyncEnumerable<ProviderDataRequestReadModel> WatchAsync(
            string tenantId,
            string companyId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield break;
        }

        private static ProviderDataRequestReadModel Request(int requestId, string symbol) => new(
            requestId,
            "ib",
            "scanner",
            ProviderDataRequestStatus.Completed,
            DateTimeOffset.UtcNow,
            Evidence($"request-{requestId}"),
            ScannerResults:
            [
                new ProviderScannerResult(
                    0,
                    symbol,
                    "NASDAQ",
                    null,
                    null,
                    null,
                    null,
                    null,
                    Evidence($"scanner-{symbol}"))
            ]);
    }
    private sealed class Availability : IProviderDataAvailabilityReadService { public IReadOnlyList<ProviderDataAvailability> GetAvailability() => [new("ib", true, "Connected", DateTimeOffset.UtcNow, "US equities", "gateway healthy")]; }
    private sealed class News : IProviderNewsReadService { public string ProviderFamily => "ib"; public IReadOnlyList<ProviderNewsItem> GetNews() => [new("n1", "Results", DateTimeOffset.UtcNow, "MSFT", null, null, Evidence("news-results"))]; public async IAsyncEnumerable<ProviderNewsItem> WatchNewsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { yield break; } }
    private sealed class Calendar : IProviderCalendarReadService { public string ProviderFamily => "ib"; public IReadOnlyList<ProviderCalendarEvent> GetCalendarEvents() => [new("c1", "NYSE", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), "halt", null, Evidence("calendar-nyse"))]; public async IAsyncEnumerable<ProviderCalendarEvent> WatchCalendarEventsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { yield break; } }
    private sealed class Instruments : IProviderInstrumentDiscoveryReadService { public string ProviderFamily => "ib"; public IReadOnlyList<ProviderInstrumentDiscoveryResult> GetInstruments() => [new("i1", "MSFT", "Microsoft", null, null, Evidence("instrument-msft"))]; public async IAsyncEnumerable<ProviderInstrumentDiscoveryResult> WatchInstrumentsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { yield break; } }

    private static ProviderDataProvenance Evidence(string stableKey) => new("ib", "ib-gateway-1", DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow, "US equities", "market-data", "real-time", "scanner", stableKey, "correlation-42", stableKey);
}
