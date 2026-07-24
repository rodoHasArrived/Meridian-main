using System.Net;
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
        await using var app = await CreateAppAsync(
            services =>
            {
                services.AddSingleton<IWorkstationTenantContextAccessor>(
                    new StubWorkstationTenantContextAccessor("tenant-test"));
                services.AddSingleton(new IBResultQueryService(new EmptyIBDurableResultStore()));
            },
            currentUserPermissions: UserPermission.ViewTrades);
        var client = app.GetTestClient();

        var response = await client.GetAsync(UiApiRoutes.IBResults + "?family=pnl");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"provider\":\"interactive-brokers\"");
    }

    private sealed class StubWorkstationTenantContextAccessor(string tenantId) : IWorkstationTenantContextAccessor
    {
        private readonly WorkstationTenantContext _context = new(
            tenantId,
            tenantId,
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

    private sealed class EmptyIBDurableResultStore : IBDurableResultStore
    {
        public void Upsert(
            string tenantId,
            ProviderDataRequestReadModel request,
            IBDataLineage? lineage)
        {
        }

        public IReadOnlyList<IBDurableResult> Get(
            string tenantId,
            string? capability = null,
            string? accountId = null,
            string? modelAccountId = null)
            => [];
    }
}
