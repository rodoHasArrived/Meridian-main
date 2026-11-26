using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>HTTP-level checks for the cash ladder prior-period comparison (UX Phase E follow-up).</summary>
public sealed class PeriodComparisonEndpointTests
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private sealed class HistoricalCash : IPortfolioCashBalanceProvider, IPortfolioCashBalanceHistoryProvider
    {
        public Task<IReadOnlyList<PortfolioCashBalanceDto>> GetCashBalancesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PortfolioCashBalanceDto>>([new("cash", "Cash", 1_250m, "USD", "Ledger", null)]);

        public Task<IReadOnlyList<PortfolioCashBalanceDto>> GetCashBalancesAsOfAsync(DateOnly asOf, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PortfolioCashBalanceDto>>([new("cash", "Cash", 1_000m, "USD", "Ledger", null)]);
    }

    private static async Task<WebApplication> CreateAppAsync(UserPermission permissions, Action<IServiceCollection>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        configure?.Invoke(builder.Services);
        var app = builder.Build();
        // The host composes the endpoints without the session middleware, so stamp the permissions
        // snapshot the authorization filter reads.
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            await next();
        });
        app.MapPortfolioCashLadderEndpoints(JsonOpts);
        await app.StartAsync();
        return app;
    }

    private static void RegisterLedgerBackedLadder(IServiceCollection services)
    {
        var holdings = Substitute.For<IPortfolioHoldingsSource>();
        holdings.GetHoldingsAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<PortfolioHoldingDto>());
        services.AddSingleton<IPortfolioCashLadderQueryService>(new PortfolioCashLadderReadService(
            securityMasterQueryService: Substitute.For<ISecurityMasterQueryService>(),
            assetOperationsQueryService: Substitute.For<IAssetOperationsQueryService>(),
            holdingsSource: holdings,
            cashBalanceProvider: new HistoricalCash()));
    }

    [Fact]
    public async Task CashLadderComparison_ReturnsTheComparison()
    {
        await using var app = await CreateAppAsync(UserPermission.ViewTrades, RegisterLedgerBackedLadder);

        var response = await app.GetTestClient().GetAsync("/api/portfolio/cash-ladder/comparison?horizonDays=28&bucketDays=7");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<PeriodComparisonDto>(JsonOpts);
        payload!.Status.Should().Be(PeriodComparisonStatus.Ready);
        payload.Lines.Should().Contain(line => line.Key == "opening-cash" && line.Current == 1_250m && line.Prior == 1_000m);
    }

    [Fact]
    public async Task CashLadderComparison_RejectsAMalformedPriorDate()
    {
        await using var app = await CreateAppAsync(UserPermission.ViewTrades, RegisterLedgerBackedLadder);

        var response = await app.GetTestClient().GetAsync("/api/portfolio/cash-ladder/comparison?baselineAsOf=31/10/2026");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CashLadderComparison_IsNotImplementedForAnotherLadderService()
    {
        await using var app = await CreateAppAsync(UserPermission.ViewTrades,
            services => services.AddSingleton(Substitute.For<IPortfolioCashLadderQueryService>()));

        var response = await app.GetTestClient().GetAsync("/api/portfolio/cash-ladder/comparison");

        response.StatusCode.Should().Be(HttpStatusCode.NotImplemented);
    }

    [Fact]
    public async Task CashLadderComparison_WithoutViewTrades_IsForbidden()
    {
        await using var app = await CreateAppAsync(UserPermission.ViewStrategies, RegisterLedgerBackedLadder);

        var response = await app.GetTestClient().GetAsync("/api/portfolio/cash-ladder/comparison");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
