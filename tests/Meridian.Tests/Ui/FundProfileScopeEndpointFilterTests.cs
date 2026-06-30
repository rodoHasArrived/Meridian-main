using System.Net;
using FluentAssertions;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Meridian.Tests.Ui;

/// <summary>
/// The fund-profile tenant scope endpoint filter (SEC-005 slice 3) gates fund-scoped read routes: a
/// request whose <c>fundProfileId</c> query value the guard reports as owned by another tenant is refused
/// with 403, while a blank scope, an allowed fund, and an unavailable guard all pass through (fail open).
/// </summary>
public sealed class FundProfileScopeEndpointFilterTests
{
    [Fact]
    public async Task ForeignFundProfile_IsRejectedWith403()
    {
        var guard = Substitute.For<IFundProfileTenantGuard>();
        guard.EvaluateAsync(Arg.Any<WorkstationTenantContext>(), "fund-other", Arg.Any<CancellationToken>())
            .Returns(FundProfileTenantDecision.Deny("owned by another tenant"));
        await using var app = await CreateAppAsync(guard);
        var client = app.GetTestClient();

        using var response = await client.GetAsync("/probe?fundProfileId=fund-other");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OwnFundProfile_IsAllowed()
    {
        var guard = Substitute.For<IFundProfileTenantGuard>();
        guard.EvaluateAsync(Arg.Any<WorkstationTenantContext>(), "fund-mine", Arg.Any<CancellationToken>())
            .Returns(FundProfileTenantDecision.Allow("own fund"));
        await using var app = await CreateAppAsync(guard);
        var client = app.GetTestClient();

        using var response = await client.GetAsync("/probe?fundProfileId=fund-mine");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task BlankFundProfile_SkipsGuard_AndIsAllowed()
    {
        var guard = Substitute.For<IFundProfileTenantGuard>();
        guard.EvaluateAsync(Arg.Any<WorkstationTenantContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(FundProfileTenantDecision.Deny("should not be consulted"));
        await using var app = await CreateAppAsync(guard);
        var client = app.GetTestClient();

        using var response = await client.GetAsync("/probe");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await guard.DidNotReceive().EvaluateAsync(
            Arg.Any<WorkstationTenantContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GuardUnavailable_FailsOpen_AndIsAllowed()
    {
        // No IFundProfileTenantGuard registered: the route still serves (the single-company-per-deployment
        // boundary remains the control) so a fund-scoped read is never blocked by a missing registry.
        await using var app = await CreateAppAsync(guard: null);
        var client = app.GetTestClient();

        using var response = await client.GetAsync("/probe?fundProfileId=fund-other");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<WebApplication> CreateAppAsync(IFundProfileTenantGuard? guard)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        if (guard is not null)
        {
            builder.Services.AddSingleton(guard);
        }

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "tenant-test";
            await next();
        });

        app.MapGet("/probe", () => Results.Ok(new { ok = true }))
            .RequireFundProfileTenantScope();

        await app.StartAsync();
        return app;
    }
}
