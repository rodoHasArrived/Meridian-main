using System.Net;
using FluentAssertions;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    /// <summary>
    /// Catalog-reachability gate for the Accounting workstream's ledger surface
    /// (adversarial-program-review-2026-08-25, §1): the routes the Accounting screen
    /// links to must be callable by the roles that own the accounting workspace, and
    /// the strategy-run ledger must stay a strategy artifact rather than becoming a
    /// back door into (or a stand-in for) the posted book. These tests fail when a
    /// route's permission set drifts disjoint from the accounting roles — the exact
    /// class of defect that previously shipped a Trial Balance screen FundAccountant
    /// and Controller could not open.
    /// </summary>
    private static readonly UserRole[] AccountingWorkspaceRoles =
    [
        UserRole.Accounting,
        UserRole.FundAccountant,
        UserRole.Controller
    ];

    /// <summary>
    /// Endpoint names (as declared via <c>WithName</c>) of the posted-journal reporting
    /// routes the Accounting workstream's ledger panel consumes.
    /// </summary>
    private static readonly string[] PostedLedgerReportEndpointNames =
    [
        "ListLedgerPeriods",
        "GetLedgerPeriodTrialBalance",
        "GetLedgerPeriodTrialBalanceReport",
        "GetLedgerPeriodPnlSummary"
    ];

    public static TheoryData<string> PostedLedgerReportEndpointNameData()
    {
        var data = new TheoryData<string>();
        foreach (var name in PostedLedgerReportEndpointNames)
        {
            data.Add(name);
        }

        return data;
    }

    private static Endpoint FindEndpointByName(IServiceProvider services, string endpointName)
    {
        var matches = services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .Where(candidate => string.Equals(
                candidate.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName,
                endpointName,
                StringComparison.Ordinal))
            .ToList();

        matches.Should().ContainSingle($"endpoint '{endpointName}' should be mapped exactly once");
        return matches[0];
    }

    [Theory]
    [MemberData(nameof(PostedLedgerReportEndpointNameData))]
    public async Task PostedLedgerReports_EveryAccountingWorkspaceRole_CanReachTheDeclaredPermissionGate(string endpointName)
    {
        await using var app = await CreateAppAsync(mapLedgerApi: true);

        var authorization = FindEndpointByName(app.Services, endpointName)
            .Metadata.GetMetadata<EndpointAuthorizationMetadata>();

        authorization.Should().NotBeNull(
            $"the posted-journal report route '{endpointName}' must declare its permission requirement");
        authorization!.RequireAll.Should().BeFalse();
        authorization.Permissions.Should().NotBeEmpty();

        foreach (var role in AccountingWorkspaceRoles)
        {
            var granted = RolePermissions.For(role);
            authorization.Permissions.Any(permission => (granted & permission) == permission)
                .Should().BeTrue(
                    $"role '{role}' owns the Accounting workspace, so it must hold at least one of " +
                    $"[{string.Join(", ", authorization.Permissions)}] required by '{endpointName}'");
        }
    }

    [Theory]
    [MemberData(nameof(PostedLedgerReportEndpointNameData))]
    public async Task PostedLedgerReports_ReadOnlyRole_StaysOutsideTheGovernedBook(string endpointName)
    {
        await using var app = await CreateAppAsync(mapLedgerApi: true);

        var authorization = FindEndpointByName(app.Services, endpointName)
            .Metadata.GetMetadata<EndpointAuthorizationMetadata>();

        authorization.Should().NotBeNull();
        var granted = RolePermissions.For(UserRole.ReadOnly);
        authorization!.Permissions.Any(permission => (granted & permission) == permission)
            .Should().BeFalse(
                "the minimal read-only role must not gain governed ledger reporting access " +
                "while the accounting roles are the intended audience");
    }

    [Fact]
    public async Task RunLedgerTrialBalance_StaysAStrategyRunArtifact()
    {
        await using var app = await CreateAppAsync(mapLedgerApi: true);

        var authorization = FindEndpointByName(app.Services, "GetRunLedgerTrialBalance")
            .Metadata.GetMetadata<EndpointAuthorizationMetadata>();

        authorization.Should().NotBeNull();
        authorization!.RequireAll.Should().BeFalse();
        // The run-scoped trial balance is a simulation artifact of a strategy run. It is
        // gated by strategy permissions on purpose; widening it to the accounting roles
        // (or pointing the Accounting screen back at it) would recreate the reviewed
        // defect where the Accounting workstream rendered a run ledger as the book.
        authorization.Permissions.Should().BeEquivalentTo(
            [UserPermission.ViewStrategies, UserPermission.ManageStrategies]);
    }

    [Theory]
    [InlineData(UserRole.Accounting)]
    [InlineData(UserRole.FundAccountant)]
    [InlineData(UserRole.Controller)]
    public async Task ListLedgerPeriods_WithAccountingWorkspaceRole_IsNotRejectedByAuthorization(UserRole role)
    {
        await using var app = await CreateAppAsync(
            currentUserPermissions: RolePermissions.For(role),
            currentUserRole: role,
            mapLedgerApi: true);
        var client = app.GetTestClient();

        var response = await client.GetAsync("/api/ledger/periods");

        // No ledger book service is registered in this composition, so a caller who
        // clears the permission gate observes 501; the gate itself must never answer
        // 401/403 for the roles that own the accounting workspace.
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(UserRole.FundAccountant)]
    [InlineData(UserRole.Controller)]
    public async Task LedgerPeriodTrialBalance_WithAccountingWorkspaceRole_IsNotRejectedByAuthorization(UserRole role)
    {
        await using var app = await CreateAppAsync(
            currentUserPermissions: RolePermissions.For(role),
            currentUserRole: role,
            mapLedgerApi: true);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            $"/api/ledger/periods/{Guid.NewGuid()}/trial-balance");

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task LedgerPeriodTrialBalance_WithReadOnlyRole_IsForbidden()
    {
        await using var app = await CreateAppAsync(
            currentUserPermissions: RolePermissions.For(UserRole.ReadOnly),
            currentUserRole: UserRole.ReadOnly,
            mapLedgerApi: true);
        var client = app.GetTestClient();

        var response = await client.GetAsync(
            $"/api/ledger/periods/{Guid.NewGuid()}/trial-balance");

        // The permission gate must decide before any service resolution: the same
        // composition that answers 501 to an accountant answers 403 here, proving the
        // authorization seam is actually evaluated.
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
