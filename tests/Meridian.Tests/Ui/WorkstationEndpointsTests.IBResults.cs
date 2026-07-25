using System.Net;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Identity.Auth;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;
using Meridian.ProviderSdk;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task MapWorkstationEndpoints_IBResults_ShouldRequireViewTradesPermission()
    {
        await using var app = await CreateAppAsync(
            currentUserPermissions: UserPermission.ViewMarketData | UserPermission.ViewAnalytics);
        var client = app.GetTestClient();

        var response = await client.GetAsync(UiApiRoutes.IBResults + "?family=pnl");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_IBResults_ShouldServeAuthorizedTenantWithMinimalServices()
    {
        var resultStore = new RecordingIBDurableResultStore();
        await using var app = await CreateAppAsync(
            services =>
            {
                services.AddSingleton<IWorkstationTenantContextAccessor>(
                    new StubWorkstationTenantContextAccessor("tenant-test", "company-test"));
                services.AddSingleton(new IBResultQueryService(resultStore));
            },
            currentUserPermissions: UserPermission.ViewTrades);
        var client = app.GetTestClient();

        var response = await client.GetAsync(UiApiRoutes.IBResults + "?family=pnl");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"provider\":\"interactive-brokers\"");
        resultStore.LastTenantId.Should().Be("tenant-test");
        resultStore.LastCompanyId.Should().Be("company-test");
    }

    [Fact]
    public async Task MapWorkstationEndpoints_IBResults_MissingCompany_ReturnsForbiddenProblemDetails()
    {
        await using var app = await CreateAppAsync(
            currentUserPermissions: UserPermission.ViewTrades,
            currentUserCompanyId: null,
            currentUserTenantId: "tenant-only");
        var client = app.GetTestClient();

        var response = await client.GetAsync(UiApiRoutes.IBResults);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("type").GetString().Should().Be(ApiProblemTypes.Forbidden);
        problem.RootElement.GetProperty("title").GetString().Should().Be("Access Denied");
        problem.RootElement.GetProperty("detail").GetString()
            .Should().Be("A tenant- and company-scoped workstation request context is required.");
        problem.RootElement.GetProperty("instance").GetString().Should().Be(UiApiRoutes.IBResults);
        problem.RootElement.TryGetProperty("traceId", out _).Should().BeTrue();
        problem.RootElement.TryGetProperty("timestamp", out _).Should().BeTrue();
    }

    private sealed class StubWorkstationTenantContextAccessor(
        string tenantId,
        string companyId) : IWorkstationTenantContextAccessor
    {
        private readonly WorkstationTenantContext _context = new(
            tenantId,
            companyId,
            "ops-user",
            null,
            UserPermission.ViewTrades);

        public bool TryGetCurrent(out WorkstationTenantContext context)
        {
            context = _context;
            return true;
        }

        public WorkstationTenantContext GetRequired() => _context;
    }

    private sealed class RecordingIBDurableResultStore : IBDurableResultStore
    {
        public string? LastTenantId { get; private set; }
        public string? LastCompanyId { get; private set; }

        public void Upsert(
            IBDataRequestOwnership ownership,
            string providerConnectionId,
            string requestCorrelationId,
            ProviderDataRequestReadModel request,
            IBDataLineage? lineage)
        {
        }

        public IReadOnlyList<IBDurableResult> Get(
            string tenantId,
            string companyId,
            string? capability = null,
            string? accountId = null,
            string? modelAccountId = null)
        {
            LastTenantId = tenantId;
            LastCompanyId = companyId;
            return [];
        }
    }
}
