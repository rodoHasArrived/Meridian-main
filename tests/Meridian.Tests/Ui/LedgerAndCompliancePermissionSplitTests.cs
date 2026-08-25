using System.Text.Json;
using FluentAssertions;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Ui;

/// <summary>
/// Pins the permission split from adversarial-program-review-2026-08-25 §2, which found
/// <see cref="UserPermission.ManageDirectLending"/> serving as the de facto fund-accounting grant
/// (a fund with no private-credit book still had to grant "manage direct lending" to close its
/// month) and <see cref="UserPermission.ManageUsers"/> gating the compliance surface (a compliance
/// officer could only file an approval request by also holding user administration).
/// </summary>
public sealed class LedgerAndCompliancePermissionSplitTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Roles whose job is the governed book.</summary>
    public static TheoryData<UserRole> LedgerOperatingRoles() =>
        [UserRole.Accounting, UserRole.FundAccountant, UserRole.Controller];

    // ── Role model ───────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(LedgerOperatingRoles))]
    public void LedgerOperatingRoles_HoldTheLedgerReportPermissions(UserRole role)
    {
        var granted = RolePermissions.For(role);

        granted.Should().HaveFlag(UserPermission.ViewLedgerReports);
        granted.Should().HaveFlag(UserPermission.ManageLedgerReports);
    }

    [Fact]
    public void ComplianceRole_CanRunComplianceWithoutUserAdministration()
    {
        var granted = RolePermissions.For(UserRole.Compliance);

        granted.Should().HaveFlag(UserPermission.ManageCompliance);
        granted.Should().NotHaveFlag(
            UserPermission.ManageUsers,
            "the split exists so a compliance officer no longer needs user-administration rights");
    }

    [Fact]
    public void ReportingAnalyst_ReadsTheLedgerWithoutOperatingIt()
    {
        var granted = RolePermissions.For(UserRole.ReportingAnalyst);

        granted.Should().HaveFlag(UserPermission.ViewLedgerReports);
        granted.Should().NotHaveFlag(UserPermission.ManageLedgerReports);
    }

    /// <summary>
    /// Developer is defined as Admin minus user administration, so anything added to
    /// AdminPermissions is inherited silently. Compliance routes previously required ManageUsers,
    /// which Developer does not hold, so it was refused by all of them; inheriting ManageCompliance
    /// would have handed every built-in Developer account the authority to file and decide approval
    /// requests, extract the audit chain, and read access reviews.
    /// </summary>
    [Fact]
    public void DeveloperRole_DoesNotInheritComplianceAuthorityFromAdmin()
    {
        var developer = RolePermissions.For(UserRole.Developer);

        developer.Should().NotHaveFlag(
            UserPermission.ManageCompliance,
            "Developer inherits Admin minus ManageUsers, so a new admin grant must be subtracted explicitly");
        developer.Should().NotHaveFlag(UserPermission.ManageUsers);
        // The subtraction must not have cost Developer anything else it held.
        developer.Should().HaveFlag(UserPermission.AdminMaintenance);
    }

    [Fact]
    public void MinimalRoles_DoNotReceiveTheNewAuthority()
    {
        foreach (var role in new[] { UserRole.ReadOnly, UserRole.TradeDesk, UserRole.Analysis })
        {
            var granted = RolePermissions.For(role);
            granted.Should().NotHaveFlag(UserPermission.ViewLedgerReports, $"{role} does not own the book");
            granted.Should().NotHaveFlag(UserPermission.ManageLedgerReports, $"{role} does not own the book");
            granted.Should().NotHaveFlag(UserPermission.ManageCompliance, $"{role} is not a compliance officer");
        }
    }

    /// <summary>
    /// Compatibility guard: the split is additive on the ledger lane, so no built-in role that
    /// could reach the governed ledger before may lose that access. A future change that removes
    /// <see cref="UserPermission.ManageDirectLending"/> from a role without granting the ledger
    /// permissions in the same edit fails here.
    /// </summary>
    [Fact]
    public void NoBuiltInRole_LosesLedgerReadAccessToTheSplit()
    {
        const UserPermission legacyLedgerRead = UserPermission.AdminMaintenance | UserPermission.ManageDirectLending;
        const UserPermission currentLedgerRead = legacyLedgerRead
            | UserPermission.ViewLedgerReports
            | UserPermission.ManageLedgerReports;

        foreach (var role in Enum.GetValues<UserRole>())
        {
            var granted = RolePermissions.For(role);
            if ((granted & legacyLedgerRead) == 0)
            {
                continue;
            }

            (granted & currentLedgerRead).Should().NotBe(
                UserPermission.None,
                $"role '{role}' could read the governed ledger before the split and must still be able to");
        }
    }

    [Fact]
    public void NewPermissions_AreDistinctSingleFlags()
    {
        UserPermission[] added =
        [
            UserPermission.ViewLedgerReports,
            UserPermission.ManageLedgerReports,
            UserPermission.ManageCompliance
        ];

        foreach (var permission in added)
        {
            var value = (long)permission;
            (value & (value - 1)).Should().Be(0, $"{permission} must be a single flag");
        }

        added.Distinct().Should().HaveCount(added.Length, "the new flags must not collide");

        // Every existing flag must stay distinct from the new ones — a duplicated bit would
        // silently widen an unrelated permission.
        foreach (var existing in Enum.GetValues<UserPermission>().Except(added).Where(static p => p != UserPermission.None))
        {
            foreach (var permission in added)
            {
                ((long)existing).Should().NotBe((long)permission, $"{existing} must not share a bit with {permission}");
            }
        }
    }

    [Fact]
    public void PermissionCatalog_DescribesTheNewPermissions()
    {
        var catalog = RolePermissions.GetCatalog();

        foreach (var name in new[] { "ViewLedgerReports", "ManageLedgerReports", "ManageCompliance" })
        {
            var item = catalog.Permissions.SingleOrDefault(permission => permission.Name == name);
            item.Should().NotBeNull($"the operator-facing permission catalog must list {name}");
            item!.Group.Should().NotBe("Other", $"{name} needs a real group so the settings editor can present it");
            item.Description.Should().NotBe(name, $"{name} needs a human description, not its own identifier");
        }
    }

    // ── Endpoint declarations ────────────────────────────────────────────────

    private static async Task<WebApplication> CreateLedgerAndComplianceAppAsync(
        UserPermission permissions,
        IFundProfileTenantGuard? tenantGuard = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        if (tenantGuard is not null)
        {
            builder.Services.AddSingleton(tenantGuard);
        }

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "permission-split-user";
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "tenant-test";
            await next();
        });

        app.MapLedgerEndpoints(JsonOptions);
        app.MapComplianceEndpoints(JsonOptions);
        await app.StartAsync();
        return app;
    }

    private static IReadOnlyList<(string Method, string Pattern, EndpointAuthorizationMetadata Authorization)> DeclaredRoutes(
        WebApplication app,
        string pathPrefix)
        => app.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase) == true)
            .SelectMany(endpoint =>
                (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .Select(method => (
                    Method: method.ToUpperInvariant(),
                    Pattern: endpoint.RoutePattern.RawText!,
                    Authorization: endpoint.Metadata.GetMetadata<EndpointAuthorizationMetadata>()!)))
            .Where(route => route.Authorization is not null)
            .ToList();

    /// <summary>
    /// The one compliance route that is not compliance authority. It removes roles from the account
    /// named in the request body, and decides dormancy from the caller's own supplied timestamp
    /// rather than authoritative activity data, so reaching it is enough to strip every role from
    /// any account. It stays behind user administration.
    /// </summary>
    private const string AccessReviewRemediationRoute = "/api/compliance/access-reviews/run";

    [Fact]
    public async Task ComplianceRoutes_GateOnManageCompliance_AndNoLongerOnUserAdministration()
    {
        await using var app = await CreateLedgerAndComplianceAppAsync(UserPermission.ManageCompliance);

        var routes = DeclaredRoutes(app, "/api/compliance")
            .Where(route => !route.Pattern.Equals(AccessReviewRemediationRoute, StringComparison.OrdinalIgnoreCase))
            .ToList();
        routes.Should().NotBeEmpty("the compliance surface must be mapped");

        foreach (var route in routes)
        {
            route.Authorization.Permissions.Should().Contain(
                UserPermission.ManageCompliance,
                $"{route.Method} {route.Pattern} is compliance authority");
            route.Authorization.Permissions.Should().NotContain(
                UserPermission.ManageUsers,
                $"{route.Method} {route.Pattern} must not require user administration");
        }
    }

    /// <summary>
    /// Regression guard for the escalation the compliance split first introduced: moving this route
    /// to <see cref="UserPermission.ManageCompliance"/> let a compliance operator demote or disable
    /// an administrator by posting an old <c>LastUsedAtUtc</c> for their account.
    /// </summary>
    [Fact]
    public async Task AccessReviewRemediation_RequiresUserAdministration_NotMerelyCompliance()
    {
        await using var app = await CreateLedgerAndComplianceAppAsync(UserPermission.ManageCompliance);

        var remediation = DeclaredRoutes(app, "/api/compliance")
            .Where(route => route.Pattern.Equals(AccessReviewRemediationRoute, StringComparison.OrdinalIgnoreCase))
            .ToList();

        remediation.Should().NotBeEmpty($"{AccessReviewRemediationRoute} must stay mapped");
        foreach (var route in remediation)
        {
            route.Authorization.Permissions.Should().Contain(
                UserPermission.ManageUsers,
                "removing another account's roles is user administration whatever surface it sits on");
            route.Authorization.Permissions.Should().NotContain(
                UserPermission.ManageCompliance,
                "holding compliance authority alone must not reach a route that can demote an administrator");
        }

        RolePermissions.For(UserRole.Compliance).HasFlag(UserPermission.ManageUsers).Should().BeFalse(
            "the built-in Compliance role must not reach the remediation route");
    }

    /// <summary>
    /// The load-bearing structural assertion of the split: reading the trial balance must never
    /// confer the authority to post to it. Every mutating ledger route must exclude
    /// <see cref="UserPermission.ViewLedgerReports"/> from its declared set.
    /// </summary>
    [Fact]
    public async Task LedgerMutationRoutes_NeverAcceptTheReadOnlyLedgerPermission()
    {
        await using var app = await CreateLedgerAndComplianceAppAsync(UserPermission.ManageLedgerReports);

        var mutations = DeclaredRoutes(app, "/api/ledger")
            .Where(route => route.Method is "POST" or "PUT" or "PATCH" or "DELETE")
            .ToList();
        mutations.Should().NotBeEmpty("the ledger surface must map mutating routes");

        foreach (var route in mutations)
        {
            route.Authorization.Permissions.Should().NotContain(
                UserPermission.ViewLedgerReports,
                $"{route.Method} {route.Pattern} is a write: read authority must not satisfy it");
        }
    }

    [Fact]
    public async Task LedgerReadRoutes_AcceptTheLedgerReportPermissions()
    {
        await using var app = await CreateLedgerAndComplianceAppAsync(UserPermission.ViewLedgerReports);

        var reads = DeclaredRoutes(app, "/api/ledger")
            .Where(route => route.Method == "GET")
            .Where(IsLedgerReportSurface)
            .Where(route => route.Authorization.Permissions.Contains(UserPermission.ManageDirectLending))
            .ToList();
        reads.Should().NotBeEmpty("the governed ledger must map read routes");

        foreach (var route in reads)
        {
            route.Authorization.Permissions.Should().Contain(
                UserPermission.ViewLedgerReports,
                $"{route.Method} {route.Pattern} still treats direct lending as the fund-accounting grant");
        }
    }


    /// <summary>The private-capital surface is a different domain, not a ledger report.</summary>
    private const string PrivateCapitalPrefix = "/api/ledger/private-capital/";

    /// <summary>
    /// Whether a route under <c>/api/ledger</c> is part of what the ledger-report grant actually
    /// buys: the trial balance, P&amp;L, periods and posted entries. Two neighbours live under the
    /// same path prefix without belonging to it — the private-capital surface (investor-level
    /// detail) and the manual-journal workbench (pre-posting workflow, drafts, approver identities
    /// and the audit trail). Both keep the domain grants they had before the split.
    /// </summary>
    private static bool IsLedgerReportSurface(
        (string Method, string Pattern, EndpointAuthorizationMetadata Authorization) route)
        => !route.Pattern.StartsWith(PrivateCapitalPrefix, StringComparison.Ordinal)
           && !route.Pattern.Contains("manual-journal", StringComparison.OrdinalIgnoreCase)
           && !route.Pattern.Contains("journal-entry-workbench", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The ledger-report grant buys the trial balance, P&amp;L, periods and posted entries — not the
    /// private-capital surface. Those routes return investor-level balances and activity,
    /// allocation rules and inputs, evidence links and statement lineage, which is materially
    /// broader than what a Reporting Analyst is being granted. They stay behind the
    /// direct-lending grant they had before the split.
    /// </summary>
    [Fact]
    public async Task PrivateCapitalRoutes_AreNotReachableWithTheLedgerReportGrant()
    {
        await using var app = await CreateLedgerAndComplianceAppAsync(UserPermission.ViewLedgerReports);

        var privateCapital = DeclaredRoutes(app, PrivateCapitalPrefix).ToList();
        privateCapital.Should().NotBeEmpty("the private-capital surface must map routes");

        foreach (var route in privateCapital)
        {
            route.Authorization.Permissions.Should().NotContain(
                UserPermission.ViewLedgerReports,
                $"{route.Method} {route.Pattern} exposes investor-level private-capital detail, not a ledger report");
            route.Authorization.Permissions.Should().NotContain(
                UserPermission.ManageLedgerReports,
                $"{route.Method} {route.Pattern} exposes investor-level private-capital detail, not a ledger report");
            route.Authorization.Permissions.Should().Contain(
                UserPermission.ManageDirectLending,
                $"{route.Method} {route.Pattern} must keep its private-capital domain grant");
        }
    }


    /// <summary>
    /// The manual-journal workbench is pre-posting workflow, not a ledger report: unposted drafts
    /// with full lines, evidence links, preparer and approver identities, validation issues,
    /// lifecycle history and the accounting audit trail. The ledger-report grant buys the trial
    /// balance, P&amp;L, periods and posted entries, so it must not reach this route.
    /// </summary>
    [Fact]
    public async Task ManualJournalWorkbench_IsNotReachableWithTheLedgerReportGrant()
    {
        await using var app = await CreateLedgerAndComplianceAppAsync(UserPermission.ViewLedgerReports);

        var workbench = DeclaredRoutes(app, "/api/ledger")
            .Where(route => route.Pattern.Contains("journal-entry-workbench", StringComparison.OrdinalIgnoreCase))
            .Where(route => route.Method == "GET")
            .ToList();
        workbench.Should().NotBeEmpty("the manual-journal workbench must map a read route");

        foreach (var route in workbench)
        {
            route.Authorization.Permissions.Should().NotContain(
                UserPermission.ViewLedgerReports,
                $"{route.Method} {route.Pattern} exposes pre-posting workflow, not a ledger report");
            route.Authorization.Permissions.Should().NotContain(
                UserPermission.ManageLedgerReports,
                $"{route.Method} {route.Pattern} exposes pre-posting workflow, not a ledger report");
        }
    }

    // ── Live requests ────────────────────────────────────────────────────────

    [Fact]
    public async Task LedgerRead_WithLedgerReportPermissionOnly_IsNotRejectedByAuthorization()
    {
        // No direct-lending, no admin-maintenance: exactly the least-privilege fund accountant the
        // review said was impossible to deploy.
        await using var app = await CreateLedgerAndComplianceAppAsync(UserPermission.ViewLedgerReports);

        var response = await app.GetTestClient().GetAsync($"/api/ledger/periods/{Guid.NewGuid()}/trial-balance");

        // No ledger service is registered in this composition, so a caller who clears the gate sees
        // 501; the gate itself must not answer 401/403.
        response.StatusCode.Should().NotBe(System.Net.HttpStatusCode.Forbidden);
        response.StatusCode.Should().NotBe(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task LedgerRead_WithNoRelevantPermission_IsForbidden()
    {
        await using var app = await CreateLedgerAndComplianceAppAsync(UserPermission.ViewMarketData);

        var response = await app.GetTestClient().GetAsync($"/api/ledger/periods/{Guid.NewGuid()}/trial-balance");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Widening a fund-scoped read route means widening its tenant filter with it. The filter skips
    /// its ownership evaluation for callers holding none of the permissions it was given — so a route
    /// that accepts the new ledger grants while its filter still lists only the old ones lets a
    /// least-privilege caller read another tenant's fund with no ownership check at all.
    /// </summary>
    [Fact]
    public async Task ForeignFund_IsRefused_ForACallerHoldingOnlyTheNewLedgerGrant()
    {
        var guard = Substitute.For<IFundProfileTenantGuard>();
        guard.EvaluateAsync(Arg.Any<WorkstationTenantContext>(), "fund-other", Arg.Any<CancellationToken>())
            .Returns(FundProfileTenantDecision.Deny("owned by another tenant"));
        await using var app = await CreateLedgerAndComplianceAppAsync(UserPermission.ViewLedgerReports, guard);

        var response = await app.GetTestClient().GetAsync("/api/ledger/books?fundProfileId=fund-other");

        response.StatusCode.Should().Be(
            System.Net.HttpStatusCode.Forbidden,
            "the tenant filter must evaluate ownership for every permission its route accepts");
        await guard.Received().EvaluateAsync(
            Arg.Any<WorkstationTenantContext>(), "fund-other", Arg.Any<CancellationToken>());
    }
}
