using System.Net;
using System.Text.Json;
using Meridian.Contracts.FixedIncome;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class BondAnalyticsEndpointsTests
{
    [Fact]
    public async Task Analytics_BindsExplicitContextAndFractionalYield()
    {
        var service = Substitute.For<IBondAnalyticsService>();
        await using var app = await CreateAppAsync(service);
        var securityId = Guid.NewGuid();
        var response = await app.GetTestClient().GetAsync(
            $"/api/reference-data/bonds/{securityId}/analytics?asOf=2026-10-29T00:00:00Z&settlementDate=2026-10-30&knownAt=2026-10-28T00:00:00Z&yield=0.05&yieldConvention=NominalPeriodic");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await service.Received(1).CalculateAsync(Arg.Is<BondAnalyticsRequest>(request =>
            request.SecurityId == securityId && request.Yield == 0.05m && request.CleanPrice == null
            && request.YieldConvention == BondYieldConvention.NominalPeriodic
            && request.Context.SettlementDate == new DateOnly(2026, 10, 30)
            && request.Context.KnownAt == new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero)), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("cleanPrice=100&yield=0.05")]
    [InlineData("cleanPrice=100&yieldConvention=999")]
    [InlineData("cleanPrice=100&yieldConvention=0")]
    [InlineData("yieldConvention=NominalPeriodic")]
    public async Task Analytics_RejectsAmbiguousOrInvalidInputs(string query)
    {
        var service = Substitute.For<IBondAnalyticsService>();
        await using var app = await CreateAppAsync(service);
        var response = await app.GetTestClient().GetAsync(
            $"/api/reference-data/bonds/{Guid.NewGuid()}/analytics?asOf=2026-10-29T00:00:00Z&{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await service.DidNotReceiveWithAnyArgs().CalculateAsync(default!, default);
    }

    [Fact]
    public async Task Analytics_RefusesCallerWithoutSecurityMasterPermission()
    {
        var service = Substitute.For<IBondAnalyticsService>();
        await using var app = await CreateAppAsync(service, UserPermission.ViewMarketData);
        var response = await app.GetTestClient().GetAsync(
            $"/api/reference-data/bonds/{Guid.NewGuid()}/analytics?asOf=2026-10-29T00:00:00Z&cleanPrice=100");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await service.DidNotReceiveWithAnyArgs().CalculateAsync(default!, default);
    }

    private static async Task<WebApplication> CreateAppAsync(IBondAnalyticsService service,
        UserPermission permissions = UserPermission.ViewSecurityMaster)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(service);
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            await next();
        });
        app.MapBondAnalyticsEndpoints(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await app.StartAsync();
        return app;
    }
}
