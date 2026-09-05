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
