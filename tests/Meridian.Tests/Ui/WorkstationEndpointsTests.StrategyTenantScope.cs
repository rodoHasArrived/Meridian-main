using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task MapWorkstationEndpoints_StrategyPayload_ShouldFilterForeignAndMalformedScopedRunsWhileKeepingLegacy()
    {
        await using var app = await CreateAppAsync(
            RegisterRunReadServices,
            currentUserCompanyId: "company-a",
            currentUserTenantId: "tenant-a");
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        var startedAt = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

        await store.RecordRunAsync(BuildScopedStrategyRun(
            "foreign-scoped-run",
            "covered-call-overwrite:foreign",
            startedAt.AddMinutes(4),
            "tenant-b",
            "company-b"));
        await store.RecordRunAsync(BuildScopedStrategyRun(
            "malformed-scoped-run",
            "covered-call-overwrite:malformed",
            startedAt.AddMinutes(3),
            "tenant-a",
            companyId: null));
        await store.RecordRunAsync(BuildScopedStrategyRun(
            "local-scoped-run",
            "covered-call-overwrite:local",
            startedAt.AddMinutes(2),
            "tenant-a",
            "company-a"));
        await store.RecordRunAsync(BuildRun(
            "legacy-unscoped-run",
            "legacy-strategy",
            "Legacy Strategy",
            RunType.Backtest,
            startedAt.AddMinutes(1)));

        var client = app.GetTestClient();
        using var strategy = await ReadJsonAsync(client, "/api/workstation/strategy");
        var runIds = strategy.RootElement
            .GetProperty("runs")
            .EnumerateArray()
            .Select(static run => run.GetProperty("id").GetString())
            .ToArray();

        runIds.Should().BeEquivalentTo(["local-scoped-run", "legacy-unscoped-run"]);
        strategy.RootElement.GetProperty("workspace").GetProperty("totalRuns").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_StrategyRunDrillIns_ShouldReturnNotFoundForForeignScopedRun()
    {
        // The sweep below spans two permission families: the run drill-ins require the strategy set,
        // while the two reconciliation reads require the reconciliation set (they return the same
        // ReconciliationRunDetail as GetReconciliationRun). The caller holds both so that a 403 can
        // never stand in for the 404 this test is actually asserting -- scope isolation, not
        // authorization.
        await using var app = await CreateAppAsync(
            RegisterRunReadServices,
            currentUserPermissions: UserPermission.ViewStrategies | UserPermission.ModifySecurityMaster,
            currentUserCompanyId: "company-a",
            currentUserTenantId: "tenant-a");
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        var foreign = BuildContinuityRun("foreign-run-detail") with
        {
            StrategyId = "covered-call-overwrite:foreign",
            ParameterSet = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["workstationTenantId"] = "tenant-b",
                ["workstationCompanyId"] = "company-b"
            }
        };
        await store.RecordRunAsync(foreign);
        var local = BuildContinuityRun("local-run-detail") with
        {
            StrategyId = "covered-call-overwrite:local",
            ParameterSet = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["workstationTenantId"] = "tenant-a",
                ["workstationCompanyId"] = "company-a"
            }
        };
        await store.RecordRunAsync(local);

        var client = app.GetTestClient();
        var routes = new[]
        {
            $"/api/workstation/runs/{foreign.RunId}/ledger",
            $"/api/workstation/runs/{foreign.RunId}/continuity",
            $"/api/workstation/runs/{foreign.RunId}/review-packet",
            $"/api/workstation/runs/{foreign.RunId}/equity-curve",
            $"/api/workstation/runs/{foreign.RunId}/fills",
            $"/api/workstation/runs/{foreign.RunId}/attribution",
            $"/api/workstation/runs/{foreign.RunId}/ledger/trial-balance",
            $"/api/workstation/runs/{foreign.RunId}/ledger/journal",
            $"/api/workstation/runs/{foreign.RunId}/reconciliation",
            $"/api/workstation/runs/{foreign.RunId}/reconciliation/history"
        };

        foreach (var route in routes)
        {
            var response = await client.GetAsync(route);
            response.StatusCode.Should().Be(HttpStatusCode.NotFound, route);
        }

        var localReviewPacket = await client.GetAsync($"/api/workstation/runs/{local.RunId}/review-packet");
        localReviewPacket.StatusCode.Should().Be(HttpStatusCode.OK);

        var compare = await client.PostAsJsonAsync(
            "/api/workstation/runs/compare",
            new RunComparisonRequest([local.RunId, foreign.RunId]),
            ServerJsonOptions);
        compare.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var diff = await client.PostAsJsonAsync(
            "/api/workstation/runs/diff",
            new RunDiffRequest(local.RunId, foreign.RunId),
            ServerJsonOptions);
        diff.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_RunReviewPacket_ShouldDeclareScopeAndStrategyReadPermissionMetadata()
    {
        await using var app = await CreateAppAsync(RegisterRunReadServices);
        var endpoint = app.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .Single(candidate => string.Equals(
                candidate.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName,
                "GetRunReviewPacket",
                StringComparison.Ordinal));

        endpoint.Metadata.GetMetadata<WorkstationTenantScopeMetadata>()
            .Should().NotBeNull();
        var authorization = endpoint.Metadata.GetMetadata<EndpointAuthorizationMetadata>();
        authorization.Should().NotBeNull();
        authorization!.RequireAll.Should().BeFalse();
        authorization.Permissions.Should().BeEquivalentTo(
            [UserPermission.ViewStrategies, UserPermission.ManageStrategies]);
    }

    [Theory]
    [InlineData(UserPermission.ViewStrategies)]
    [InlineData(UserPermission.ManageStrategies)]
    public async Task MapWorkstationEndpoints_RunReviewPacket_WithStrategyReadPermissionAndScope_ShouldReturnPacket(
        UserPermission permission)
    {
        await using var app = await CreateAppAsync(
            RegisterRunReadServices,
            currentUserPermissions: permission,
            currentUserCompanyId: "company-a",
            currentUserTenantId: "tenant-a");
        var run = BuildContinuityRun($"authorized-review-packet-{permission}") with
        {
            ParameterSet = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["workstationTenantId"] = "tenant-a",
                ["workstationCompanyId"] = "company-a"
            }
        };
        await app.Services.GetRequiredService<IStrategyRepository>().RecordRunAsync(run);

        var response = await app.GetTestClient().GetAsync(
            $"/api/workstation/runs/{run.RunId}/review-packet");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_RunReviewPacket_WithUnrelatedPermission_ShouldReturnForbidden()
    {
        await using var app = await CreateAppAsync(
            RegisterRunReadServices,
            currentUserPermissions: UserPermission.ViewReporting,
            currentUserCompanyId: "company-a",
            currentUserTenantId: "tenant-a");
        var run = BuildContinuityRun("unrelated-permission-review-packet") with
        {
            ParameterSet = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["workstationTenantId"] = "tenant-a",
                ["workstationCompanyId"] = "company-a"
            }
        };
        await app.Services.GetRequiredService<IStrategyRepository>().RecordRunAsync(run);

        var response = await app.GetTestClient().GetAsync(
            $"/api/workstation/runs/{run.RunId}/review-packet");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_RunReviewPacket_WithoutCompanyScope_ShouldReturnForbidden()
    {
        await using var app = await CreateAppAsync(
            RegisterRunReadServices,
            currentUserPermissions: UserPermission.ViewStrategies,
            currentUserCompanyId: null,
            currentUserTenantId: "tenant-a");
        var run = BuildContinuityRun("missing-company-review-packet") with
        {
            ParameterSet = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["workstationTenantId"] = "tenant-a",
                ["workstationCompanyId"] = "company-a"
            }
        };
        await app.Services.GetRequiredService<IStrategyRepository>().RecordRunAsync(run);

        var response = await app.GetTestClient().GetAsync(
            $"/api/workstation/runs/{run.RunId}/review-packet");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MapWorkstationEndpoints_PublicStrategyRunsRoute_ShouldApplySessionScopeAndPermission()
    {
        await using var app = await CreateAppAsync(
            RegisterRunReadServices,
            currentUserPermissions: UserPermission.ViewStrategies,
            currentUserCompanyId: "company-a",
            currentUserTenantId: "tenant-a");
        var store = app.Services.GetRequiredService<IStrategyRepository>();
        var startedAt = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);
        await store.RecordRunAsync(BuildScopedStrategyRun(
            "public-local-run",
            "scoped-strategy",
            startedAt,
            "tenant-a",
            "company-a"));
        await store.RecordRunAsync(BuildScopedStrategyRun(
            "public-foreign-run",
            "scoped-strategy",
            startedAt.AddMinutes(1),
            "tenant-b",
            "company-b"));

        var response = await app.GetTestClient().GetAsync("/api/strategies/scoped-strategy/runs");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var runs = await response.Content.ReadFromJsonAsync<StrategyRunSummary[]>(ServerJsonOptions);
        runs.Should().ContainSingle(static run => run.RunId == "public-local-run");
    }

    private static StrategyRunEntry BuildScopedStrategyRun(
        string runId,
        string strategyId,
        DateTimeOffset startedAt,
        string tenantId,
        string? companyId)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["workstationTenantId"] = tenantId
        };
        if (companyId is not null)
        {
            parameters["workstationCompanyId"] = companyId;
        }

        return BuildRun(
            runId,
            strategyId,
            "Covered Call",
            RunType.Backtest,
            startedAt) with
        {
            ParameterSet = parameters
        };
    }
}
