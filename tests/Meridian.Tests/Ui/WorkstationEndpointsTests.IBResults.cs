using System.Net;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.TestHost;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task MapWorkstationEndpoints_IBResults_ShouldRequireViewTradesPermission()
    {
        await using var app = await CreateAppAsync(
            currentUserPermissions: UserPermission.ViewMarketData | UserPermission.ViewAnalytics);
        var client = app.GetTestClient();

        var response = await client.GetAsync(UiApiRoutes.IBResults + "?family=pnl");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
