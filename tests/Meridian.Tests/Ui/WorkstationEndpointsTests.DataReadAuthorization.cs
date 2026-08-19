using System.Net;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Theory]
    [InlineData(UiApiRoutes.WorkstationData)]
    [InlineData(UiApiRoutes.WorkstationDataOperations)]
    [InlineData(UiApiRoutes.WorkstationDataReplacementCost)]
    public async Task MapWorkstationEndpoints_DataWorkspaceReads_WithoutDataReadPermission_ShouldReturnForbidden(string route)
    {
        await using var app = await CreateAppAsync(currentUserPermissions: UserPermission.ModifySecurityMaster);
        var response = await app.GetTestClient().GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(UiApiRoutes.WorkstationData)]
    [InlineData(UiApiRoutes.WorkstationDataOperations)]
    [InlineData(UiApiRoutes.WorkstationDataReplacementCost)]
    public async Task MapWorkstationEndpoints_DataWorkspaceReads_WithHistoricalDataReadPermission_ShouldReachHandler(string route)
    {
        await using var app = await CreateAppAsync(currentUserPermissions: UserPermission.ViewHistoricalData);
        var response = await app.GetTestClient().GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Theory]
    [InlineData(UserPermission.ViewHistoricalData)]
    [InlineData(UserPermission.ViewDiagnostics)]
    [InlineData(UserPermission.ManageStorage)]
    public async Task MapWorkstationEndpoints_DataWorkspaceRead_WithAnyDataReadPermission_ShouldReachHandler(UserPermission permission)
    {
        await using var app = await CreateAppAsync(currentUserPermissions: permission);
        var response = await app.GetTestClient().GetAsync(UiApiRoutes.WorkstationData);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }
}
