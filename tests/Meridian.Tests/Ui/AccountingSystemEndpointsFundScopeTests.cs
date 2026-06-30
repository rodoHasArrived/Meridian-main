using System.Net;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Tenancy;
using Meridian.FinancialOperations.AccountingSystem;
using Meridian.Identity.Auth;
using Meridian.ProviderSdk.AccountingSystem;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Meridian.Tests.Ui;

/// <summary>
/// SEC-005 slice 3b: the accounting-system fund-scoped read routes carry the
/// <c>RequireFundProfileTenantScope()</c> filter, so a <c>fundProfileId</c> query value the guard reports
/// as owned by another tenant is refused with 403 before the route handler (and its fund-partitioned data
/// load) runs. This proves the gate is actually wired onto the route: with the filter absent, the authorized
/// caller would reach the handler and get 200 (the registered accounting service returns an empty list)
/// rather than the filter's 403. A minimal <see cref="AccountingSystemIntegrationService"/> is registered so
/// Minimal-API argument binding (which resolves the handler's service parameter before the filter runs)
/// succeeds and the request actually exercises the filter.
/// </summary>
public sealed class AccountingSystemEndpointsFundScopeTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Fact]
    public async Task MappingProfiles_ForeignFund_IsRejectedWith403_BeforeHandler()
    {
        var guard = Substitute.For<IFundProfileTenantGuard>();
        guard.EvaluateAsync(Arg.Any<WorkstationTenantContext>(), "fund-other", Arg.Any<CancellationToken>())
            .Returns(FundProfileTenantDecision.Deny("owned by another tenant"));
        await using var app = await CreateAppAsync(guard, UserPermission.AdminMaintenance);
        var client = app.GetTestClient();

        using var response = await client.GetAsync("/api/accounting-system/mapping-profiles?fundProfileId=fund-other");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await guard.Received().EvaluateAsync(
            Arg.Any<WorkstationTenantContext>(), "fund-other", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MappingProfiles_CallerWithoutAccountingPermission_GetsNoOwnershipOracle()
    {
        // The filter is given the route's read permissions, so a caller lacking them must not have ownership
        // evaluated (a foreign-fund 403 here vs. the handler's own 403 would distinguish cross-tenant
        // ownership). The caller is still refused — by the route's own gate — but the guard is never consulted.
        var guard = Substitute.For<IFundProfileTenantGuard>();
        guard.EvaluateAsync(Arg.Any<WorkstationTenantContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(FundProfileTenantDecision.Deny("owned by another tenant"));
        await using var app = await CreateAppAsync(guard, UserPermission.ViewSecurityMaster);
        var client = app.GetTestClient();

        using var response = await client.GetAsync("/api/accounting-system/mapping-profiles?fundProfileId=fund-other");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await guard.DidNotReceive().EvaluateAsync(
            Arg.Any<WorkstationTenantContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    private static async Task<WebApplication> CreateAppAsync(
        IFundProfileTenantGuard guard, UserPermission callerPermission)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(guard);
        // The mapping-profiles handler takes an AccountingSystemIntegrationService; Minimal-API argument
        // binding resolves it before the endpoint filter runs, so it must be registered for the request to
        // reach (and exercise) the fund-scope filter. A providers-less instance is enough — the filter
        // short-circuits a foreign fund before the handler ever calls it.
        builder.Services.AddSingleton(new AccountingSystemIntegrationService(Array.Empty<IAccountingSystemProvider>()));

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "tenant-test";
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = callerPermission;
            await next();
        });
        app.MapAccountingSystemEndpoints(Json);

        await app.StartAsync();
        return app;
    }
}
