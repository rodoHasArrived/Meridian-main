using System.Net;
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
/// The fund-profile tenant scope endpoint filter (SEC-005 slice 3, tightened for W9-GOV-008 criterion
/// 2) gates fund-scoped read routes.
/// </summary>
/// <remarks>
/// Under the deployment-boundary posture a request whose <c>fundProfileId</c> the guard reports as
/// owned by another tenant is refused with 403, while a blank scope, an allowed fund, and an
/// unavailable guard all pass. Under fail-closed each of those last three becomes a refusal, because
/// each is a scope that could not be resolved — and the criterion is categorical about rejecting one
/// rather than defaulting it. Both postures are pinned here; a suite that only covered the tightened
/// one would not notice single-company deployments breaking.
/// </remarks>
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
    public async Task MultipleFundProfiles_DeniesWhenAnyValueIsForeign()
    {
        // Query-parameter pollution: ?fundProfileId=fund-other&fundProfileId=fund-mine. The handler's
        // string parameter would bind to a single value, so the filter must evaluate EVERY supplied value
        // (not the comma-joined StringValues) and refuse when any is positively foreign.
        var guard = Substitute.For<IFundProfileTenantGuard>();
        guard.EvaluateAsync(Arg.Any<WorkstationTenantContext>(), "fund-other", Arg.Any<CancellationToken>())
            .Returns(FundProfileTenantDecision.Deny("owned by another tenant"));
        guard.EvaluateAsync(Arg.Any<WorkstationTenantContext>(), "fund-mine", Arg.Any<CancellationToken>())
            .Returns(FundProfileTenantDecision.Allow("own fund"));
        await using var app = await CreateAppAsync(guard);
        var client = app.GetTestClient();

        using var response = await client.GetAsync("/probe?fundProfileId=fund-other&fundProfileId=fund-mine");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
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

    [Fact]
    public async Task WithoutReadPermission_SkipsOwnershipEvaluation_NoOracle()
    {
        // The filter runs before the route's own permission check. A caller lacking read permission must
        // NOT receive the ownership verdict (a foreign-fund 403 here vs an own-fund fall-through would leak
        // cross-tenant ownership); the ownership check is skipped so the route's own gate decides uniformly.
        var guard = Substitute.For<IFundProfileTenantGuard>();
        guard.EvaluateAsync(Arg.Any<WorkstationTenantContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(FundProfileTenantDecision.Deny("owned by another tenant"));
        await using var app = await CreateAppAsync(
            guard,
            callerPermission: UserPermission.ViewSecurityMaster,
            requiredReadPermissions: [UserPermission.AdminMaintenance, UserPermission.ManageDirectLending]);
        var client = app.GetTestClient();

        using var response = await client.GetAsync("/probe?fundProfileId=fund-other");

        // The probe has no permission gate of its own, so falling through yields 200 — proving the filter
        // did not evaluate (and therefore did not leak) ownership for an unauthorized caller.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await guard.DidNotReceive().EvaluateAsync(
            Arg.Any<WorkstationTenantContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithReadPermission_StillEvaluatesOwnership()
    {
        var guard = Substitute.For<IFundProfileTenantGuard>();
        guard.EvaluateAsync(Arg.Any<WorkstationTenantContext>(), "fund-other", Arg.Any<CancellationToken>())
            .Returns(FundProfileTenantDecision.Deny("owned by another tenant"));
        await using var app = await CreateAppAsync(
            guard,
            callerPermission: UserPermission.AdminMaintenance,
            requiredReadPermissions: [UserPermission.AdminMaintenance, UserPermission.ManageDirectLending]);
        var client = app.GetTestClient();

        using var response = await client.GetAsync("/probe?fundProfileId=fund-other");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Fail-closed posture (W9-GOV-008 criterion 2) ──────────────────────────

    [Fact]
    public async Task FailClosed_RefusesACallerWithNoResolvableTenantScope()
    {
        var guard = AllowingGuard();
        await using var app = await CreateAppAsync(
            guard,
            tenantScope: TenantScopeEnforcementOptions.FailClosed,
            tenancyRegistry: RegistryOwning("fund-mine", "tenant-test"),
            callerTenantId: null);
        var client = app.GetTestClient();

        using var response = await client.GetAsync("/probe?fundProfileId=fund-mine");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task FailClosed_RefusesWhenTheGuardIsUnavailable()
    {
        // A gate that cannot reach its authority has not decided the caller is entitled; it has failed
        // to ask. This is the case most easily left fail-open by accident.
        await using var app = await CreateAppAsync(
            guard: null,
            tenantScope: TenantScopeEnforcementOptions.FailClosed,
            tenancyRegistry: RegistryOwning("fund-mine", "tenant-test"));
        var client = app.GetTestClient();

        using var response = await client.GetAsync("/probe?fundProfileId=fund-mine");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task FailClosed_RefusesASuppliedButBlankFundProfile()
    {
        await using var app = await CreateAppAsync(
            AllowingGuard(),
            tenantScope: TenantScopeEnforcementOptions.FailClosed,
            tenancyRegistry: RegistryOwning("fund-mine", "tenant-test"));
        var client = app.GetTestClient();

        // A supplied-but-blank scope is an unresolvable scope, not an absent one — the boundary
        // posture skips it, and that skip is exactly what the criterion calls defaulting.
        using var response = await client.GetAsync("/probe?fundProfileId=");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task FailClosed_RefusesAFundTheRegistryAttributesToNobody()
    {
        // EvaluateAsync allows an unattributed fund by contract. Fail-closed needs positive ownership,
        // so a fund nobody has claimed must not be served on the strength of nobody having claimed it.
        await using var app = await CreateAppAsync(
            AllowingGuard(),
            tenantScope: TenantScopeEnforcementOptions.FailClosed,
            tenancyRegistry: RegistryOwning("some-other-fund", "tenant-test"));
        var client = app.GetTestClient();

        using var response = await client.GetAsync("/probe?fundProfileId=fund-unregistered");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task FailClosed_RefusesWhenTheRegistryIsUnavailable()
    {
        await using var app = await CreateAppAsync(
            AllowingGuard(),
            tenantScope: TenantScopeEnforcementOptions.FailClosed,
            tenancyRegistry: null);
        var client = app.GetTestClient();

        using var response = await client.GetAsync("/probe?fundProfileId=fund-mine");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task FailClosed_AllowsAFundTheRegistryAttributesToTheCaller()
    {
        // The other direction, and the one that matters for not breaking legitimate operators: a
        // properly attributed fund read by its owner still succeeds under the tightened posture.
        await using var app = await CreateAppAsync(
            AllowingGuard(),
            tenantScope: TenantScopeEnforcementOptions.FailClosed,
            tenancyRegistry: RegistryOwning("fund-mine", "TENANT-TEST"));
        var client = app.GetTestClient();

        using var response = await client.GetAsync("/probe?fundProfileId=fund-mine");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task FailClosed_LeavesARouteWithNoFundScopeToItsOwnGate()
    {
        // No fundProfileId supplied means there is no fund scope to resolve. Whether the route may be
        // reached without one belongs to the route's own tenant gate, not to this filter.
        await using var app = await CreateAppAsync(
            AllowingGuard(),
            tenantScope: TenantScopeEnforcementOptions.FailClosed,
            tenancyRegistry: RegistryOwning("fund-mine", "tenant-test"));
        var client = app.GetTestClient();

        using var response = await client.GetAsync("/probe");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static IFundProfileTenantGuard AllowingGuard()
    {
        var guard = Substitute.For<IFundProfileTenantGuard>();
        guard.EvaluateAsync(Arg.Any<WorkstationTenantContext>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(FundProfileTenantDecision.Allow("not positively foreign"));
        return guard;
    }

    private static IFundProfileTenancyRegistry RegistryOwning(string fundProfileId, string tenantId)
    {
        var registry = Substitute.For<IFundProfileTenancyRegistry>();
        registry.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult<FundProfileOwnership?>(
                string.Equals(callInfo.ArgAt<string>(0), fundProfileId, StringComparison.OrdinalIgnoreCase)
                    ? new FundProfileOwnership(fundProfileId, tenantId, null)
                    : null));
        return registry;
    }

    private static async Task<WebApplication> CreateAppAsync(
        IFundProfileTenantGuard? guard,
        UserPermission? callerPermission = null,
        UserPermission[]? requiredReadPermissions = null,
        TenantScopeEnforcementOptions? tenantScope = null,
        IFundProfileTenancyRegistry? tenancyRegistry = null,
        string? callerTenantId = "tenant-test")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        if (guard is not null)
        {
            builder.Services.AddSingleton(guard);
        }

        if (tenantScope is not null)
        {
            builder.Services.AddSingleton(tenantScope);
        }

        if (tenancyRegistry is not null)
        {
            builder.Services.AddSingleton(tenancyRegistry);
        }

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (callerTenantId is not null)
            {
                context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = callerTenantId;
            }

            if (callerPermission is not null)
            {
                context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = callerPermission.Value;
            }

            await next();
        });

        var probe = app.MapGet("/probe", () => Results.Ok(new { ok = true }));
        if (requiredReadPermissions is { Length: > 0 })
        {
            probe.RequireFundProfileTenantScope(requiredReadPermissions);
        }
        else
        {
            probe.RequireFundProfileTenantScope();
        }

        await app.StartAsync();
        return app;
    }
}
