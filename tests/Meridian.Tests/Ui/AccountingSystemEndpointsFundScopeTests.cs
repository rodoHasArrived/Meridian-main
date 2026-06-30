using System.Net;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Tenancy;
using Meridian.Identity.Auth;
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
/// load) runs. This proves the gate is actually wired onto the route: with the filter absent, an authorized
/// caller would fall through to the handler (whose backing service is unregistered here) instead of a 403.
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
