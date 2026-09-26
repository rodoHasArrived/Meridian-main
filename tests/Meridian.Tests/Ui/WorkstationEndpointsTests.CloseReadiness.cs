using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    // This transport scenario supplies an explicit reviewed decision and uses the real
    // publication guard. Real contributor freshness and repair are covered by publication tests.
    private sealed class HttpTransitionCloseAuthority(CloseReadinessScopeDto retainedScope) : IFinancialOperationsCommandCenterReadService
    {
        public OperationsContinuityWorkflowDto? Workflow { get; set; }
        public List<(CloseReadinessScopeDto Scope, string? TenantId, string? CompanyId)> Requests { get; } = [];

        public Task<FinancialOperationsCommandCenterDto> GetCommandCenterAsync(string? fundProfileId = null,
            Guid? ledgerBookId = null, Guid? fundAccountId = null, string? periodId = null, string? entityId = null,
            CancellationToken ct = default, string? tenantId = null, string? companyId = null)
        {
            ct.ThrowIfCancellationRequested();
            var scope = new CloseReadinessScopeDto(fundProfileId, ledgerBookId, fundAccountId, entityId, periodId);
            Requests.Add((scope, tenantId, companyId));
            var ready = scope == retainedScope && tenantId == "tenant-test" && companyId == "tenant-test"
                && Workflow is { CloseReadiness.IsReadyToClose: true } workflow && workflow.LedgerBookId == ledgerBookId
                && workflow.FundAccountId == fundAccountId && workflow.PeriodId == periodId;
            var now = DateTimeOffset.UtcNow;
            var projection = new CloseReadinessProjectionDto(scope, now, ready ? "Ready" : "Blocked", ready, ready,
                [new("reviewed-http-fixture", "Controller", ready ? "Ready" : "ScopeMismatch", now, ["retained-http-close-review"])],
                ready ? [] : [new("close.fixture.scope", "reviewed-http-fixture", "ScopeMismatch", 1, "Critical", "Controller",
                    "The retained close review does not cover the requested subject.", ["retained-http-close-review"])]);
            return Task.FromResult(new FinancialOperationsCommandCenterDto(now, fundProfileId, ledgerBookId, fundAccountId,
                periodId, projection.Status, ready, "Explicit close review for the HTTP transition scenario.", 0, 0, 0, [], [],
                ActiveWorkflow: Workflow, CloseReadiness: projection));
        }
    }

    [Fact]
    public async Task CloseReadinessRoute_MissingScopeReturnsBlockingProjection()
    {
        var workflows = new Mock<IOperationsContinuityWorkflowService>(MockBehavior.Strict);
        await using var app = await CreateAppAsync(services => services.AddSingleton<IFinancialOperationsCommandCenterReadService>(
            new FinancialOperationsCommandCenterReadService(workflows.Object)), currentUserPermissions: UserPermission.AdminMaintenance);
        var response = await app.GetTestClient().GetAsync(UiApiRoutes.FinancialOperationsCommandCenter + "?fundProfileId=fund-alpha");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<FinancialOperationsCommandCenterDto>(ServerJsonOptions);
        payload!.IsReadyToComplete.Should().BeFalse();
        payload.CloseReadiness!.Blockers.Should().Contain(b => b.Code == "close.scope.required");
        workflows.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CloseReadinessRoute_UnregisteredAuthorityReturnsUnavailable()
    {
        await using var app = await CreateAppAsync(currentUserPermissions: UserPermission.AdminMaintenance);
        var response = await app.GetTestClient().GetAsync(UiApiRoutes.FinancialOperationsCommandCenter + "?fundProfileId=fund-alpha");
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task CloseReadinessRoute_UnauthorizedCallerCannotReadCloseEvidence()
    {
        await using var app = await CreateAppAsync(currentUserPermissions: UserPermission.ViewReporting);
        var response = await app.GetTestClient().GetAsync(UiApiRoutes.FinancialOperationsCommandCenter + "?fundProfileId=fund-alpha");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
